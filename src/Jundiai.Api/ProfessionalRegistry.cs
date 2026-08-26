using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class ProfessionalRegistryEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalRegistryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/professionals", (string? unit, string? occupation, ProfessionalRegistryStore store) => Results.Ok(store.Search(unit, occupation)));
        endpoints.MapGet("/api/professionals/{id:guid}", (Guid id, ProfessionalRegistryStore store) =>
            store.Get(id) is { } professional ? Results.Ok(professional) : Results.NotFound());
        endpoints.MapGet("/api/professionals/credential-alerts", (ProfessionalRegistryStore store) => Results.Ok(store.CredentialAlerts()));
        endpoints.MapPost("/api/professionals/{id:guid}/assignments", (Guid id, CreateProfessionalAssignmentRequest request, ProfessionalRegistryStore store, DemoStore demo) =>
        {
            var professional = store.AddAssignment(id, request);
            demo.AuditExternal(request.Actor, "professional.assignment.create", $"professional:{id}", $"unit={request.Unit};team={request.Team}");
            return Results.Ok(professional);
        });
        endpoints.MapPost("/api/professionals/{id:guid}/status", (Guid id, ProfessionalStatusRequest request, ProfessionalRegistryStore store, DemoStore demo) =>
        {
            var professional = store.SetStatus(id, request);
            demo.AuditExternal(request.Actor, "professional.status", $"professional:{id}", $"status={professional.Status};reason={request.Reason}");
            return Results.Ok(professional);
        });
        endpoints.MapGet("/api/professionals/readiness", (ProfessionalRegistryStore store) => Results.Ok(new
        {
            professionals = store.Search(null, null).Count,
            active = store.Search(null, null).Count(x => x.Status == "active"),
            assignments = store.Search(null, null).Sum(x => x.Assignments.Count),
            credentialAlerts = store.CredentialAlerts().Count,
            capabilities = new[] { "professional registry", "CBO", "council", "credential validity", "unit/team assignment", "workload", "active/inactive status", "credential alerts", "audit hooks" }
        }));
        return endpoints;
    }
}

public sealed class ProfessionalRegistryStore
{
    private readonly ConcurrentDictionary<Guid, HealthProfessional> _professionals = new();

    public ProfessionalRegistryStore()
    {
        Seed("Dr. Eduardo Martins", "physician", "225125", "CRM", "000001", DateOnly.FromDateTime(DateTime.Today.AddYears(2)), "UBS Vila Hortolândia", "Equipe APS 01", 20);
        Seed("Enf. Juliana Ramos", "nurse", "223505", "COREN", "000001", DateOnly.FromDateTime(DateTime.Today.AddYears(1)), "UBS Vila Hortolândia", "Equipe APS 01", 30);
        Seed("Dra. Ana Ribeiro", "dentist", "223208", "CRO", "000001", DateOnly.FromDateTime(DateTime.Today.AddMonths(8)), "CEO Jundiaí", "Odonto 01", 20);
        Seed("Farm. Paula Santos", "pharmacist", "223405", "CRF", "000001", DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), "Farmácia Central", "Assistência Farmacêutica", 30);
        Seed("ACS Marcos Lima", "acs", "515105", "MUNICIPAL", "ACS-001", null, "UBS Vila Hortolândia", "Equipe APS 01", 40);
        Seed("Dra. Paula Imagem", "physician", "225320", "CRM", "000002", DateOnly.FromDateTime(DateTime.Today.AddYears(1)), "Centro de Imagem", "Diagnóstico", 20);
        Seed("Bio. Carla Lab", "biomedical", "221205", "CRBM", "000001", DateOnly.FromDateTime(DateTime.Today.AddDays(45)), "Laboratório Municipal", "Laboratório", 30);
        Seed("Psic. Lucas Souza", "psychologist", "251510", "CRP", "000001", DateOnly.FromDateTime(DateTime.Today.AddYears(1)), "CAPS Demonstrativo", "Saúde Mental", 20);
    }

    public IReadOnlyList<HealthProfessional> Search(string? unit, string? occupation) => _professionals.Values
        .Where(x => string.IsNullOrWhiteSpace(occupation) || x.Occupation.Contains(occupation, StringComparison.OrdinalIgnoreCase))
        .Where(x => string.IsNullOrWhiteSpace(unit) || x.Assignments.Any(a => a.Unit.Contains(unit, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(x => x.Name)
        .ToList();

    public HealthProfessional? Get(Guid id) => _professionals.TryGetValue(id, out var item) ? item : null;

    public HealthProfessional AddAssignment(Guid id, CreateProfessionalAssignmentRequest request)
    {
        if (!_professionals.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Unit) || string.IsNullOrWhiteSpace(request.Team)) throw new ArgumentException("Unidade e equipe são obrigatórias.");
        if (request.WeeklyHours is <= 0 or > 60) throw new ArgumentException("Carga horária semanal inválida.");
        var assignments = current.Assignments.ToList();
        assignments.Add(new ProfessionalAssignment(Guid.NewGuid(), request.Unit.Trim(), request.Team.Trim(), request.WeeklyHours, request.StartsOn, request.EndsOn, request.Role?.Trim(), DateTimeOffset.UtcNow));
        var updated = current with { Assignments = assignments, UpdatedAt = DateTimeOffset.UtcNow };
        _professionals[id] = updated;
        return updated;
    }

    public HealthProfessional SetStatus(Guid id, ProfessionalStatusRequest request)
    {
        if (!_professionals.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (status is not ("active" or "inactive" or "suspended" or "leave")) throw new ArgumentException("Status profissional inválido.");
        if (status != "active" && string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Mudança para status não ativo exige motivo.");
        var updated = current with { Status = status, StatusReason = request.Reason?.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        _professionals[id] = updated;
        return updated;
    }

    public IReadOnlyList<ProfessionalCredentialAlert> CredentialAlerts()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return _professionals.Values
            .Where(x => x.CredentialValidUntil is not null)
            .Select(x =>
            {
                var days = x.CredentialValidUntil!.Value.DayNumber - today.DayNumber;
                return new ProfessionalCredentialAlert(x.Id, x.Name, x.Council, x.CouncilNumber, x.CredentialValidUntil.Value, days, days < 0 ? "expired" : days <= 60 ? "warning" : "ok");
            })
            .Where(x => x.Status != "ok")
            .OrderBy(x => x.DaysRemaining)
            .ToList();
    }

    private void Seed(string name, string occupation, string cbo, string council, string number, DateOnly? validUntil, string unit, string team, int hours)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _professionals[id] = new HealthProfessional(id, name, occupation, cbo, council, number, validUntil, "active", null,
            [new ProfessionalAssignment(Guid.NewGuid(), unit, team, hours, DateOnly.FromDateTime(DateTime.Today.AddMonths(-6)), null, occupation, now)], now, now);
    }
}

public sealed record HealthProfessional(Guid Id, string Name, string Occupation, string Cbo, string Council, string CouncilNumber, DateOnly? CredentialValidUntil, string Status, string? StatusReason, IReadOnlyList<ProfessionalAssignment> Assignments, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ProfessionalAssignment(Guid Id, string Unit, string Team, int WeeklyHours, DateOnly StartsOn, DateOnly? EndsOn, string? Role, DateTimeOffset CreatedAt);
public sealed record ProfessionalCredentialAlert(Guid ProfessionalId, string Name, string Council, string CouncilNumber, DateOnly ValidUntil, int DaysRemaining, string Status);
public sealed record CreateProfessionalAssignmentRequest(string Unit, string Team, int WeeklyHours, DateOnly StartsOn, DateOnly? EndsOn, string? Role, string Actor);
public sealed record ProfessionalStatusRequest(string Status, string? Reason, string Actor);