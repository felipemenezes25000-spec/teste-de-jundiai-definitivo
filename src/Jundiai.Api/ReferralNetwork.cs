using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class ReferralNetworkEndpoints
{
    public static IEndpointRouteBuilder MapReferralNetworkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/referrals", (Guid? citizenId, ReferralNetworkStore store) => Results.Ok(store.Referrals(citizenId)));
        endpoints.MapPost("/api/referrals", (
            CreateReferralRequest request,
            ReferralNetworkStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var referral = store.Create(request, demo);
            demo.AuditExternal(request.RequestedBy, "referral.create", $"referral:{referral.Id}", $"{referral.OriginUnit}->{referral.DestinationService}");
            evidence.Append(new CreateEvidenceEventRequest(request.RequestedBy, "referral.create", $"referral:{referral.Id}", "POC-B03", $"priority={referral.Priority};destination={referral.DestinationService}", "referral"));
            return Results.Created($"/api/referrals/{referral.Id}", referral);
        });

        endpoints.MapPost("/api/referrals/{id:guid}/accept", (Guid id, ReferralAcceptRequest request, ReferralNetworkStore store, DemoStore demo) =>
        {
            var referral = store.Accept(id, request);
            demo.AuditExternal(request.Actor, "referral.accept", $"referral:{id}", request.Note);
            return Results.Ok(referral);
        });

        endpoints.MapPost("/api/referrals/{id:guid}/counter-referral", (
            Guid id,
            CounterReferralRequest request,
            ReferralNetworkStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var referral = store.CounterRefer(id, request);
            demo.AuditExternal(request.Professional, "referral.counter", $"referral:{id}", request.Plan);
            evidence.Append(new CreateEvidenceEventRequest(request.Professional, "referral.counter", $"referral:{id}", "POC-B03", $"status={referral.Status};return={referral.ReturnToUnit}", "counter-referral"));
            return Results.Ok(referral);
        });

        endpoints.MapGet("/api/referrals/readiness", (ReferralNetworkStore store) => Results.Ok(new
        {
            referrals = store.Referrals(null).Count,
            open = store.Referrals(null).Count(x => x.Status is "requested" or "accepted"),
            counterReferred = store.Referrals(null).Count(x => x.Status == "counter_referred"),
            capabilities = new[] { "reference", "priority", "origin/destination", "clinical question", "acceptance", "counter-reference", "return plan", "follow-up date", "audit/evidence hooks" }
        }));
        return endpoints;
    }
}

public sealed class ReferralNetworkStore
{
    private readonly ConcurrentDictionary<Guid, CareReferral> _referrals = new();

    public ReferralNetworkStore()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new CareReferral(
            Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"), "Maria da Silva",
            "UBS Vila Hortolândia", "Cardiologia", "high", "Avaliação de condição crônica e estratificação de risco.",
            "Dr. Eduardo Martins", "CRM 000001", "requested", null, null, null, null, null, null, now.AddDays(-2), now.AddDays(-2));
        _referrals[item.Id] = item;
    }

    public IReadOnlyList<CareReferral> Referrals(Guid? citizenId) => _referrals.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public CareReferral Create(CreateReferralRequest request, DemoStore demo)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.OriginUnit) || string.IsNullOrWhiteSpace(request.DestinationService) || string.IsNullOrWhiteSpace(request.ClinicalQuestion))
            throw new ArgumentException("Origem, destino e pergunta clínica são obrigatórios.");
        var priority = NormalizePriority(request.Priority);
        var now = DateTimeOffset.UtcNow;
        var item = new CareReferral(Guid.NewGuid(), citizen.Id, citizen.Name, request.OriginUnit.Trim(), request.DestinationService.Trim(), priority,
            request.ClinicalQuestion.Trim(), request.RequestedBy.Trim(), request.ProfessionalCouncil.Trim(), "requested", null, null, null, null, null, null, now, now);
        _referrals[item.Id] = item;
        return item;
    }

    public CareReferral Accept(Guid id, ReferralAcceptRequest request)
    {
        if (!_referrals.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (current.Status != "requested") throw new InvalidOperationException("Somente referência solicitada pode ser aceita.");
        var updated = current with { Status = "accepted", AcceptedBy = request.Actor.Trim(), AcceptanceNote = request.Note?.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        _referrals[id] = updated;
        return updated;
    }

    public CareReferral CounterRefer(Guid id, CounterReferralRequest request)
    {
        if (!_referrals.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (current.Status is not ("accepted" or "requested")) throw new InvalidOperationException("Referência não está aberta para contrarreferência.");
        if (string.IsNullOrWhiteSpace(request.Assessment) || string.IsNullOrWhiteSpace(request.Plan) || string.IsNullOrWhiteSpace(request.ReturnToUnit))
            throw new ArgumentException("Avaliação, plano e unidade de retorno são obrigatórios.");
        var updated = current with
        {
            Status = "counter_referred",
            CounterAssessment = request.Assessment.Trim(),
            CounterPlan = request.Plan.Trim(),
            ReturnToUnit = request.ReturnToUnit.Trim(),
            FollowUpAt = request.FollowUpAt,
            CounterReferredBy = request.Professional.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _referrals[id] = updated;
        return updated;
    }

    private static string NormalizePriority(string? value) => (value ?? "routine").Trim().ToLowerInvariant() switch
    {
        "emergency" or "emergencia" or "emergência" => "emergency",
        "urgent" or "urgente" => "urgent",
        "high" or "alta" => "high",
        "moderate" or "moderada" => "moderate",
        _ => "routine"
    };
}

public sealed record CareReferral(Guid Id, Guid CitizenId, string CitizenName, string OriginUnit, string DestinationService, string Priority, string ClinicalQuestion, string RequestedBy, string ProfessionalCouncil, string Status, string? AcceptedBy, string? AcceptanceNote, string? CounterAssessment, string? CounterPlan, string? ReturnToUnit, DateTimeOffset? FollowUpAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public string? CounterReferredBy { get; init; }
}
public sealed record CreateReferralRequest(Guid CitizenId, string OriginUnit, string DestinationService, string? Priority, string ClinicalQuestion, string RequestedBy, string ProfessionalCouncil);
public sealed record ReferralAcceptRequest(string Actor, string? Note);
public sealed record CounterReferralRequest(string Assessment, string Plan, string ReturnToUnit, DateTimeOffset? FollowUpAt, string Professional);