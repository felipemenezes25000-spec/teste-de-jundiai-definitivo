using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class ImmunizationAdvancedEndpoints
{
    public static IEndpointRouteBuilder MapImmunizationAdvancedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/immunization/v2/schedule", (Guid? citizenId, ImmunizationAdvancedStore store, DemoStore demo) =>
            Results.Ok(store.Schedule(citizenId, demo)));

        endpoints.MapPost("/api/immunization/v2/screen", (ImmunizationScreeningRequest request, ImmunizationAdvancedStore store, DemoStore demo) =>
            Results.Ok(store.Screen(request, demo)));

        endpoints.MapPost("/api/immunization/v2/administer", (
            ImmunizationAdvancedAdministerRequest request,
            ImmunizationAdvancedStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var result = store.Administer(request, demo);
            evidence.Append(new CreateEvidenceEventRequest(
                request.Professional,
                "immunization.v2.administer",
                $"immunization:{result.Event.Id}",
                "POC-B09",
                $"rule={result.RuleId};lot={result.Event.Lot};dose={result.Event.Dose}",
                "immunization"));
            demo.AuditExternal(request.Professional, "immunization.v2.administer", $"citizen:{result.Event.CitizenId}", $"{result.Event.Vaccine} · {result.Event.Dose}");
            return Results.Created("/api/immunization/history", result);
        });

        endpoints.MapGet("/api/immunization/v2/adverse-events", (Guid? citizenId, ImmunizationAdvancedStore store) =>
            Results.Ok(store.AdverseEvents(citizenId)));
        endpoints.MapPost("/api/immunization/v2/adverse-events", (
            RecordAdverseEventRequest request,
            ImmunizationAdvancedStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var item = store.RecordAdverseEvent(request, demo);
            evidence.Append(new CreateEvidenceEventRequest(request.ReportedBy, "immunization.adverse-event", $"adverse-event:{item.Id}", "POC-B09", $"severity={item.Severity}", "patient-safety"));
            return Results.Created($"/api/immunization/v2/adverse-events/{item.Id}", item);
        });

        endpoints.MapPost("/api/immunization/v2/adverse-events/{id:guid}/review", (Guid id, ReviewAdverseEventRequest request, ImmunizationAdvancedStore store) =>
            Results.Ok(store.ReviewAdverseEvent(id, request)));

        endpoints.MapGet("/api/immunization/v2/coverage", (ImmunizationAdvancedStore store, DemoStore demo) => Results.Ok(store.Coverage(demo)));
        endpoints.MapGet("/api/immunization/v2/readiness", (ImmunizationAdvancedStore store, DemoStore demo) => Results.Ok(new
        {
            scheduleRules = store.Rules().Count,
            administered = demo.Immunizations().Count,
            adverseEvents = store.AdverseEvents(null).Count,
            capabilities = new[]
            {
                "calendário parametrizado POC", "situação vacinal por cidadão", "dose atrasada", "screening pré-vacinal",
                "contraindicação/adiamento", "aplicação com lote", "evento adverso e revisão", "cobertura por grupo",
                "estoque integrado", "audit/evidence hooks"
            },
            external = new[] { "RNDS/SI-PNI credentials", "official national schedule/version governance", "production pharmacovigilance integration" },
            disclaimer = "Regras vacinais da POC são demonstrativas e não substituem calendário oficial vigente nem avaliação profissional."
        }));
        return endpoints;
    }
}

public sealed class ImmunizationAdvancedStore
{
    private readonly ConcurrentDictionary<string, ImmunizationScheduleRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, ImmunizationScreening> _screenings = new();
    private readonly ConcurrentDictionary<Guid, ImmunizationAdverseEvent> _adverseEvents = new();

    public ImmunizationAdvancedStore()
    {
        AddRule("DEMO-INFLUENZA-ANNUAL", "Influenza demonstrativa", "Dose anual", 6, 130, 365, "annual-demo");
        AddRule("DEMO-COVID-BOOSTER", "Covid-19 demonstrativa", "Reforço", 12, 130, 180, "booster-demo");
        AddRule("DEMO-TETANUS", "dT demonstrativa", "Reforço", 7, 130, 3650, "decennial-demo");
        AddRule("DEMO-MMR", "Tríplice viral demonstrativa", "Dose POC", 1, 59, null, "age-demo");
    }

    public IReadOnlyList<ImmunizationScheduleRule> Rules() => _rules.Values.OrderBy(x => x.Vaccine).ToList();
    public IReadOnlyList<ImmunizationAdverseEvent> AdverseEvents(Guid? citizenId) => _adverseEvents.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public IReadOnlyList<CitizenImmunizationSchedule> Schedule(Guid? citizenId, DemoStore demo)
    {
        var citizens = citizenId is { } id
            ? demo.Citizen(id) is { } selectedCitizen ? new[] { selectedCitizen } : Array.Empty<Citizen>()
            : demo.Citizens().ToArray();
        var now = DateTimeOffset.UtcNow;
        var result = new List<CitizenImmunizationSchedule>();
        foreach (var currentCitizen in citizens)
        {
            var age = Age(currentCitizen.BirthDate, DateOnly.FromDateTime(DateTime.Today));
            var history = demo.Immunizations(currentCitizen.Id);
            var items = new List<ImmunizationScheduleItem>();
            foreach (var rule in _rules.Values.Where(x => age >= x.MinAge && age <= x.MaxAge))
            {
                var latest = history
                    .Where(x => Normalize(x.Vaccine).Contains(Normalize(rule.Vaccine).Split(' ')[0], StringComparison.Ordinal))
                    .OrderByDescending(x => x.AppliedAt)
                    .FirstOrDefault();
                DateTimeOffset? dueAt = latest is null
                    ? now.AddDays(-Math.Min(30, age + 1))
                    : rule.IntervalDays is { } interval ? latest.AppliedAt.AddDays(interval) : null;
                var status = latest is null ? "due" : dueAt is { } due && due <= now ? "overdue" : "up_to_date";
                items.Add(new ImmunizationScheduleItem(rule.Id, rule.Vaccine, rule.DoseLabel, status, dueAt, latest?.AppliedAt, latest?.Lot, rule.PolicyVersion));
            }
            result.Add(new CitizenImmunizationSchedule(currentCitizen.Id, currentCitizen.Name, age, items.OrderByDescending(x => x.Status == "overdue").ThenBy(x => x.Vaccine).ToList(), now));
        }
        return result;
    }

    public ImmunizationScreening Screen(ImmunizationScreeningRequest request, DemoStore demo)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (!_rules.TryGetValue(request.RuleId.Trim(), out var rule)) throw new ArgumentException("Regra vacinal não cadastrada.");
        var reasons = new List<string>();
        var disposition = "eligible";
        if (request.SevereAllergicReactionToPreviousDose)
        {
            disposition = "contraindicated";
            reasons.Add("Reação alérgica grave prévia informada: exige avaliação profissional e não pode prosseguir automaticamente.");
        }
        if (request.AcuteFebrileIllness)
        {
            if (disposition != "contraindicated") disposition = "defer";
            reasons.Add("Condição febril aguda informada: avaliação profissional antes da aplicação.");
        }
        if (request.Pregnant && rule.Id == "DEMO-MMR")
        {
            disposition = "contraindicated";
            reasons.Add("Regra demonstrativa bloqueia esta vacina no cenário informado de gestação.");
        }
        if (request.Immunosuppressed && rule.Id == "DEMO-MMR")
        {
            disposition = "clinical_review";
            reasons.Add("Imunossupressão informada: revisão clínica obrigatória.");
        }
        if (reasons.Count == 0) reasons.Add("Nenhum bloqueio demonstrativo identificado; decisão final permanece profissional.");
        var item = new ImmunizationScreening(Guid.NewGuid(), citizen.Id, citizen.Name, rule.Id, rule.Vaccine, disposition, reasons, request.Actor.Trim(), DateTimeOffset.UtcNow);
        _screenings[item.Id] = item;
        return item;
    }

    public AdvancedImmunizationResult Administer(ImmunizationAdvancedAdministerRequest request, DemoStore demo)
    {
        if (!_screenings.TryGetValue(request.ScreeningId, out var screening)) throw new KeyNotFoundException();
        if (screening.CitizenId != request.CitizenId) throw new InvalidOperationException("Screening pertence a outro cidadão.");
        if (screening.Disposition is "contraindicated" or "defer") throw new InvalidOperationException("Screening atual não permite aplicação. Registre nova avaliação profissional quando aplicável.");
        if (screening.Disposition == "clinical_review" && !request.ClinicalReviewApproved)
            throw new InvalidOperationException("Revisão clínica obrigatória não foi confirmada.");
        if (!_rules.TryGetValue(screening.RuleId, out var rule)) throw new InvalidOperationException("Regra vacinal não encontrada.");
        var evt = demo.AdministerVaccine(new AdministerVaccineRequest(
            request.CitizenId,
            request.VaccineLotId,
            rule.DoseLabel,
            request.Route,
            request.Site,
            request.Professional,
            request.ProfessionalCouncil));
        return new AdvancedImmunizationResult(screening.Id, rule.Id, evt, request.ClinicalReviewApproved, DateTimeOffset.UtcNow);
    }

    public ImmunizationAdverseEvent RecordAdverseEvent(RecordAdverseEventRequest request, DemoStore demo)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("Descrição do evento é obrigatória.");
        var severity = NormalizeSeverity(request.Severity);
        var item = new ImmunizationAdverseEvent(
            Guid.NewGuid(), citizen.Id, citizen.Name, request.ImmunizationEventId, request.Vaccine?.Trim(), request.Lot?.Trim(),
            severity, request.Description.Trim(), request.StartedAt, "open", request.ReportedBy.Trim(), null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _adverseEvents[item.Id] = item;
        return item;
    }

    public ImmunizationAdverseEvent ReviewAdverseEvent(Guid id, ReviewAdverseEventRequest request)
    {
        if (!_adverseEvents.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Reviewer) || string.IsNullOrWhiteSpace(request.Assessment)) throw new ArgumentException("Revisor e avaliação são obrigatórios.");
        var status = (request.Status ?? "reviewed").Trim().ToLowerInvariant();
        if (status is not ("reviewed" or "closed" or "escalated")) throw new ArgumentException("Status de revisão inválido.");
        var updated = current with { Status = status, Reviewer = request.Reviewer.Trim(), ReviewAssessment = request.Assessment.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        _adverseEvents[id] = updated;
        return updated;
    }

    public object Coverage(DemoStore demo)
    {
        var citizens = demo.Citizens();
        var history = demo.Immunizations();
        var groups = citizens.GroupBy(c => Age(c.BirthDate, DateOnly.FromDateTime(DateTime.Today)) switch
        {
            < 12 => "0-11",
            < 18 => "12-17",
            < 60 => "18-59",
            _ => "60+"
        }).Select(g => new
        {
            group = g.Key,
            population = g.Count(),
            vaccinatedCitizens = g.Count(c => history.Any(h => h.CitizenId == c.Id)),
            coveragePercent = g.Count() == 0 ? 0 : Math.Round(100m * g.Count(c => history.Any(h => h.CitizenId == c.Id)) / g.Count(), 1)
        }).OrderBy(x => x.group).ToArray();
        return new { groups, totalPopulation = citizens.Count, totalEvents = history.Count, generatedAt = DateTimeOffset.UtcNow };
    }

    private void AddRule(string id, string vaccine, string doseLabel, int minAge, int maxAge, int? intervalDays, string policyVersion) =>
        _rules[id] = new ImmunizationScheduleRule(id, vaccine, doseLabel, minAge, maxAge, intervalDays, policyVersion);
    private static int Age(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return Math.Max(age, 0);
    }
    private static string Normalize(string value) => new string(value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    private static string NormalizeSeverity(string? value) => (value ?? "mild").Trim().ToLowerInvariant() switch
    {
        "mild" or "leve" => "mild",
        "moderate" or "moderada" => "moderate",
        "severe" or "grave" => "severe",
        "serious" or "serio" or "sério" => "serious",
        _ => throw new ArgumentException("Severidade inválida.")
    };
}

public sealed record ImmunizationScheduleRule(string Id, string Vaccine, string DoseLabel, int MinAge, int MaxAge, int? IntervalDays, string PolicyVersion);
public sealed record ImmunizationScheduleItem(string RuleId, string Vaccine, string DoseLabel, string Status, DateTimeOffset? DueAt, DateTimeOffset? LastDoseAt, string? LastLot, string PolicyVersion);
public sealed record CitizenImmunizationSchedule(Guid CitizenId, string CitizenName, int Age, IReadOnlyList<ImmunizationScheduleItem> Items, DateTimeOffset GeneratedAt);
public sealed record ImmunizationScreeningRequest(Guid CitizenId, string RuleId, bool SevereAllergicReactionToPreviousDose, bool AcuteFebrileIllness, bool Pregnant, bool Immunosuppressed, string Actor);
public sealed record ImmunizationScreening(Guid Id, Guid CitizenId, string CitizenName, string RuleId, string Vaccine, string Disposition, IReadOnlyList<string> Reasons, string Actor, DateTimeOffset ScreenedAt);
public sealed record ImmunizationAdvancedAdministerRequest(Guid ScreeningId, Guid CitizenId, Guid VaccineLotId, string Route, string Site, string Professional, string ProfessionalCouncil, bool ClinicalReviewApproved);
public sealed record AdvancedImmunizationResult(Guid ScreeningId, string RuleId, ImmunizationEvent Event, bool ClinicalReviewApproved, DateTimeOffset RecordedAt);
public sealed record RecordAdverseEventRequest(Guid CitizenId, Guid? ImmunizationEventId, string? Vaccine, string? Lot, string? Severity, string Description, DateTimeOffset StartedAt, string ReportedBy);
public sealed record ReviewAdverseEventRequest(string? Status, string Reviewer, string Assessment);
public sealed record ImmunizationAdverseEvent(Guid Id, Guid CitizenId, string CitizenName, Guid? ImmunizationEventId, string? Vaccine, string? Lot, string Severity, string Description, DateTimeOffset StartedAt, string Status, string ReportedBy, string? Reviewer, string? ReviewAssessment, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);