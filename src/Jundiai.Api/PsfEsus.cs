using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class PsfEsusEndpoints
{
    private static readonly ConcurrentDictionary<Guid, IndividualRegistration> Individuals = new();
    private static readonly ConcurrentDictionary<Guid, HomeRegistration> Homes = new();
    private static readonly ConcurrentDictionary<Guid, EsusVisitRecord> Visits = new();

    public static IEndpointRouteBuilder MapPsfEsusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/psf/esus/individuals", () => Results.Ok(Individuals.Values.OrderBy(x => x.Name).ToList()));
        endpoints.MapPost("/api/psf/esus/individuals", (IndividualRegistrationRequest request, DemoStore store) =>
        {
            var citizen = store.Citizen(request.CitizenId);
            if (citizen is null) return Results.NotFound();
            var item = new IndividualRegistration(
                Guid.NewGuid(), citizen.Id, citizen.Name, citizen.Cns, citizen.Cpf,
                request.SocialName?.Trim(), request.RaceColor?.Trim(), request.Education?.Trim(),
                request.Occupation?.Trim(), request.HasDisability, request.IsPregnant,
                request.IsBedridden, request.ChronicConditions?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray() ?? [],
                citizen.Area, citizen.MicroArea, request.AcsName.Trim(), DateTimeOffset.UtcNow);
            Individuals[item.Id] = item;
            store.AuditExternal(request.AcsName, "psf.esus.individual.register", $"citizen:{citizen.Id}", $"area={citizen.Area};micro={citizen.MicroArea}");
            return Results.Created("/api/psf/esus/individuals", item);
        });

        endpoints.MapGet("/api/psf/esus/homes", () => Results.Ok(Homes.Values.OrderBy(x => x.Address).ToList()));
        endpoints.MapPost("/api/psf/esus/homes", (HomeRegistrationRequest request, MunicipalOperationsStore operations, DemoStore store) =>
        {
            var unit = operations.Units().FirstOrDefault(x => x.Code.Equals(request.ReferenceUnitCode, StringComparison.OrdinalIgnoreCase));
            if (unit is null) return Results.Problem(statusCode: 400, title: "Unidade de referência inválida");
            var home = new HomeRegistration(
                Guid.NewGuid(), request.Address.Trim(), request.Area.Trim(), request.MicroArea.Trim(),
                request.HousingType.Trim(), request.WaterSupply.Trim(), request.Sewage.Trim(), request.GarbageDisposal.Trim(),
                request.HasElectricity, request.FamilyCount, request.ResidentCount, request.AcsName.Trim(), unit.Code, unit.Name,
                DateTimeOffset.UtcNow);
            Homes[home.Id] = home;
            store.AuditExternal(request.AcsName, "psf.esus.home.register", $"home:{home.Id}", $"area={home.Area};micro={home.MicroArea}");
            return Results.Created("/api/psf/esus/homes", home);
        });

        endpoints.MapPost("/api/psf/esus/visits", (EsusVisitRequest request, DemoStore store) =>
        {
            var citizen = store.Citizen(request.CitizenId);
            if (citizen is null) return Results.NotFound();
            var visit = new EsusVisitRecord(
                Guid.NewGuid(), citizen.Id, citizen.Name, request.AcsName.Trim(), request.VisitDate,
                request.Reasons?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray() ?? [],
                request.Anthropometry, request.BloodPressure?.Trim(), request.GlycemiaMgDl,
                request.Outcome.Trim(), request.SharedWithTeam, request.OfflineCaptured,
                request.OfflineCaptured ? DateTimeOffset.UtcNow : null, DateTimeOffset.UtcNow);
            Visits[visit.Id] = visit;
            store.AuditExternal(request.AcsName, "psf.esus.visit.register", $"visit:{visit.Id}", $"citizen={citizen.Id};offline={request.OfflineCaptured}");
            return Results.Created("/api/psf/esus/visits", visit);
        });

        endpoints.MapGet("/api/psf/esus/visits", () => Results.Ok(Visits.Values.OrderByDescending(x => x.VisitDate).ToList()));

        endpoints.MapGet("/api/psf/esus/export-demo", () =>
        {
            var lines = new List<string>
            {
                "# EXPORTACAO DEMONSTRATIVA APS/e-SUS - NAO E ARQUIVO OFICIAL",
                $"GENERATED_AT|{DateTimeOffset.UtcNow:O}",
                "TYPE|REFERENCE|AREA|MICROAREA|DETAIL"
            };
            lines.AddRange(Individuals.Values.Select(x => $"INDIVIDUAL|{x.CitizenId}|{x.Area}|{x.MicroArea}|{Sanitize(x.Name)}"));
            lines.AddRange(Homes.Values.Select(x => $"HOME|{x.Id}|{x.Area}|{x.MicroArea}|{Sanitize(x.Address)}"));
            lines.AddRange(Visits.Values.Select(x => $"VISIT|{x.Id}|||{x.VisitDate:yyyy-MM-dd};{Sanitize(x.Outcome)}"));
            return Results.Text(string.Join('\n', lines), "text/plain; charset=utf-8");
        });

        return endpoints;
    }

    private static string Sanitize(string value) => value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
}

public sealed record IndividualRegistration(
    Guid Id,
    Guid CitizenId,
    string Name,
    string Cns,
    string Cpf,
    string? SocialName,
    string? RaceColor,
    string? Education,
    string? Occupation,
    bool HasDisability,
    bool IsPregnant,
    bool IsBedridden,
    IReadOnlyList<string> ChronicConditions,
    string Area,
    string MicroArea,
    string AcsName,
    DateTimeOffset RegisteredAt);

public sealed record IndividualRegistrationRequest(
    Guid CitizenId,
    string? SocialName,
    string? RaceColor,
    string? Education,
    string? Occupation,
    bool HasDisability,
    bool IsPregnant,
    bool IsBedridden,
    IReadOnlyList<string>? ChronicConditions,
    string AcsName);

public sealed record HomeRegistration(
    Guid Id,
    string Address,
    string Area,
    string MicroArea,
    string HousingType,
    string WaterSupply,
    string Sewage,
    string GarbageDisposal,
    bool HasElectricity,
    int FamilyCount,
    int ResidentCount,
    string AcsName,
    string ReferenceUnitCode,
    string ReferenceUnitName,
    DateTimeOffset RegisteredAt);

public sealed record HomeRegistrationRequest(
    string Address,
    string Area,
    string MicroArea,
    string HousingType,
    string WaterSupply,
    string Sewage,
    string GarbageDisposal,
    bool HasElectricity,
    int FamilyCount,
    int ResidentCount,
    string AcsName,
    string ReferenceUnitCode);

public sealed record Anthropometry(decimal? WeightKg, decimal? HeightCm, decimal? WaistCm);

public sealed record EsusVisitRecord(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string AcsName,
    DateOnly VisitDate,
    IReadOnlyList<string> Reasons,
    Anthropometry? Anthropometry,
    string? BloodPressure,
    int? GlycemiaMgDl,
    string Outcome,
    bool SharedWithTeam,
    bool OfflineCaptured,
    DateTimeOffset? SyncedAt,
    DateTimeOffset CreatedAt);

public sealed record EsusVisitRequest(
    Guid CitizenId,
    string AcsName,
    DateOnly VisitDate,
    IReadOnlyList<string>? Reasons,
    Anthropometry? Anthropometry,
    string? BloodPressure,
    int? GlycemiaMgDl,
    string Outcome,
    bool SharedWithTeam,
    bool OfflineCaptured);
