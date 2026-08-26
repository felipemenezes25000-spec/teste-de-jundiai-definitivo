using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class DentalAdvancedEndpoints
{
    public static IEndpointRouteBuilder MapDentalAdvancedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dental/v2/{citizenId:guid}/chart", (Guid citizenId, DentalAdvancedStore store) => Results.Ok(store.Chart(citizenId)));
        endpoints.MapPut("/api/dental/v2/{citizenId:guid}/teeth/{tooth:int}/surfaces/{surface}", (Guid citizenId, int tooth, string surface, UpdateDentalSurfaceRequest request, DentalAdvancedStore store, DemoStore audit) =>
        {
            var chart = store.UpdateSurface(citizenId, tooth, surface, request);
            audit.AuditExternal(request.Professional ?? "dentist", "dental.surface.update", $"citizen:{citizenId};tooth:{tooth};surface:{surface}", request.Condition);
            return Results.Ok(chart);
        });
        endpoints.MapPut("/api/dental/v2/{citizenId:guid}/periodontal/{sextant:int}", (Guid citizenId, int sextant, UpdatePeriodontalSextantRequest request, DentalAdvancedStore store) =>
            Results.Ok(store.UpdateSextant(citizenId, sextant, request)));
        endpoints.MapPost("/api/dental/v2/{citizenId:guid}/procedures", (Guid citizenId, RecordDentalProcedureRequest request, DentalAdvancedStore store, SusBillingEngineStore billing, DemoStore demo) =>
        {
            var procedure = store.RecordProcedure(citizenId, request);
            var production = billing.RegisterDental(citizenId, procedure.CitizenName, procedure.HealthUnit, procedure.SigtapCode, procedure.Tooth, procedure.Sextant, procedure.Professional, procedure.ProfessionalCouncil, procedure.Cid);
            demo.AuditExternal(procedure.Professional, "dental.procedure.record", $"dental-procedure:{procedure.Id}", $"SIGTAP={procedure.SigtapCode};production={production.Id}");
            return Results.Created($"/api/dental/v2/{citizenId}/procedures/{procedure.Id}", new { procedure, production });
        });
        endpoints.MapGet("/api/dental/v2/{citizenId:guid}/history", (Guid citizenId, DentalAdvancedStore store) => Results.Ok(store.History(citizenId)));
        endpoints.MapGet("/api/dental/v2/readiness", () => Results.Ok(new
        {
            permanentTeeth = 32,
            surfaces = new[] { "O", "M", "D", "V", "L" },
            periodontalSextants = 6,
            productionLink = "procedure -> SIGTAP -> BPA production",
            supportedArtifacts = new[] { "odontogram", "surface-history", "periodontal-sextant", "procedure-history", "billing-link" }
        }));
        return endpoints;
    }
}

public sealed class DentalAdvancedStore
{
    private readonly ConcurrentDictionary<Guid, DentalChartV2> _charts = new();
    private readonly ConcurrentDictionary<Guid, List<DentalProcedureV2>> _procedures = new();

    public DentalChartV2 Chart(Guid citizenId) => _charts.GetOrAdd(citizenId, CreateChart);

    public DentalChartV2 UpdateSurface(Guid citizenId, int tooth, string surface, UpdateDentalSurfaceRequest request)
    {
        ValidateTooth(tooth);
        surface = NormalizeSurface(surface);
        var chart = Chart(citizenId);
        lock (chart)
        {
            var toothState = chart.Teeth[tooth];
            var history = toothState.History.ToList();
            history.Add(new DentalSurfaceEvent(Guid.NewGuid(), surface, request.Condition.Trim(), request.Notes?.Trim(), request.Professional?.Trim() ?? "dentist", DateTimeOffset.UtcNow));
            var surfaces = toothState.Surfaces.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            surfaces[surface] = request.Condition.Trim();
            chart.Teeth[tooth] = toothState with { Surfaces = surfaces, History = history, UpdatedAt = DateTimeOffset.UtcNow };
            chart.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return chart;
    }

    public DentalChartV2 UpdateSextant(Guid citizenId, int sextant, UpdatePeriodontalSextantRequest request)
    {
        if (sextant is < 1 or > 6) throw new ArgumentException("Sextante deve estar entre 1 e 6.");
        var chart = Chart(citizenId);
        lock (chart)
        {
            chart.Periodontal[sextant] = new PeriodontalSextant(sextant, request.Bleeding, request.Calculus, request.PocketDepthMm, request.Mobility, request.Notes?.Trim(), request.Professional?.Trim() ?? "dentist", DateTimeOffset.UtcNow);
            chart.UpdatedAt = DateTimeOffset.UtcNow;
        }
        return chart;
    }

    public DentalProcedureV2 RecordProcedure(Guid citizenId, RecordDentalProcedureRequest request)
    {
        if (request.Tooth is null && request.Sextant is null) throw new ArgumentException("Informe elemento dentário ou sextante conforme o procedimento.");
        if (request.Tooth is { } tooth) ValidateTooth(tooth);
        if (request.Sextant is { } sextant && sextant is < 1 or > 6) throw new ArgumentException("Sextante inválido.");
        if (string.IsNullOrWhiteSpace(request.SigtapCode)) throw new ArgumentException("Código SIGTAP obrigatório.");
        var procedure = new DentalProcedureV2(Guid.NewGuid(), citizenId, request.CitizenName.Trim(), request.HealthUnit.Trim(), request.SigtapCode.Trim(), request.Description.Trim(), request.Tooth, request.Sextant, request.Surfaces?.Select(NormalizeSurface).Distinct().ToArray() ?? [], request.Cid?.Trim(), request.Professional.Trim(), request.ProfessionalCouncil.Trim(), DateTimeOffset.UtcNow);
        _procedures.GetOrAdd(citizenId, _ => []).Add(procedure);
        return procedure;
    }

    public IReadOnlyList<DentalProcedureV2> History(Guid citizenId) => _procedures.TryGetValue(citizenId, out var list) ? list.OrderByDescending(x => x.PerformedAt).ToList() : [];

    private static DentalChartV2 CreateChart(Guid citizenId)
    {
        var teeth = PermanentFdiTeeth.ToDictionary(
            tooth => tooth,
            tooth => new DentalToothV2(tooth, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["O"] = "healthy", ["M"] = "healthy", ["D"] = "healthy", ["V"] = "healthy", ["L"] = "healthy" }, [], DateTimeOffset.UtcNow));
        var periodontal = Enumerable.Range(1, 6).ToDictionary(x => x, x => new PeriodontalSextant(x, false, false, null, false, null, null, DateTimeOffset.UtcNow));
        return new DentalChartV2(citizenId, teeth, periodontal, DateTimeOffset.UtcNow);
    }

    private static readonly int[] PermanentFdiTeeth =
    [11,12,13,14,15,16,17,18,21,22,23,24,25,26,27,28,31,32,33,34,35,36,37,38,41,42,43,44,45,46,47,48];

    private static void ValidateTooth(int tooth)
    {
        if (!PermanentFdiTeeth.Contains(tooth)) throw new ArgumentException($"Elemento FDI permanente inválido: {tooth}.");
    }

    private static string NormalizeSurface(string surface)
    {
        var value = (surface ?? string.Empty).Trim().ToUpperInvariant();
        if (value is not ("O" or "M" or "D" or "V" or "L")) throw new ArgumentException("Superfície deve ser O, M, D, V ou L.");
        return value;
    }
}

public sealed class DentalChartV2(Guid citizenId, Dictionary<int, DentalToothV2> teeth, Dictionary<int, PeriodontalSextant> periodontal, DateTimeOffset updatedAt)
{
    public Guid CitizenId { get; } = citizenId;
    public Dictionary<int, DentalToothV2> Teeth { get; } = teeth;
    public Dictionary<int, PeriodontalSextant> Periodontal { get; } = periodontal;
    public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
}
public sealed record DentalToothV2(int Tooth, IReadOnlyDictionary<string, string> Surfaces, IReadOnlyList<DentalSurfaceEvent> History, DateTimeOffset UpdatedAt);
public sealed record DentalSurfaceEvent(Guid Id, string Surface, string Condition, string? Notes, string Professional, DateTimeOffset OccurredAt);
public sealed record PeriodontalSextant(int Sextant, bool Bleeding, bool Calculus, decimal? PocketDepthMm, bool Mobility, string? Notes, string? Professional, DateTimeOffset UpdatedAt);
public sealed record DentalProcedureV2(Guid Id, Guid CitizenId, string CitizenName, string HealthUnit, string SigtapCode, string Description, int? Tooth, int? Sextant, IReadOnlyList<string> Surfaces, string? Cid, string Professional, string ProfessionalCouncil, DateTimeOffset PerformedAt);
public sealed record UpdateDentalSurfaceRequest(string Condition, string? Notes, string? Professional);
public sealed record UpdatePeriodontalSextantRequest(bool Bleeding, bool Calculus, decimal? PocketDepthMm, bool Mobility, string? Notes, string? Professional);
public sealed record RecordDentalProcedureRequest(string CitizenName, string HealthUnit, string SigtapCode, string Description, int? Tooth, int? Sextant, IReadOnlyList<string>? Surfaces, string? Cid, string Professional, string ProfessionalCouncil);
