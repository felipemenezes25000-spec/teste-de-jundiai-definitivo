using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jundiai.Api;

public static class BillingAdvancedEndpoints
{
    public static IEndpointRouteBuilder MapBillingAdvancedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sus/sigtap", (SusBillingEngineStore store) => Results.Ok(store.Catalog()));
        endpoints.MapGet("/api/sus/billing/v2/production", (SusBillingEngineStore store) => Results.Ok(store.Production()));
        endpoints.MapPost("/api/sus/billing/v2/production", (RegisterSusProductionRequest request, SusBillingEngineStore store, DemoStore audit) =>
        {
            var item = store.RegisterProduction(request);
            audit.AuditExternal(request.ProfessionalName, "sus.production.register", $"production:{item.Id}", $"procedure={item.ProcedureCode}");
            return Results.Created($"/api/sus/billing/v2/production/{item.Id}", item);
        });

        endpoints.MapGet("/api/sus/billing/v2/batches", (SusBillingEngineStore store) => Results.Ok(store.Batches()));
        endpoints.MapPost("/api/sus/billing/v2/batches", (CreateAdvancedBillingBatchRequest request, SusBillingEngineStore store) =>
            Results.Created("/api/sus/billing/v2/batches", store.CreateBatch(request.Competence)));
        endpoints.MapPost("/api/sus/billing/v2/batches/{id:guid}/validate", (Guid id, SusBillingEngineStore store) => Results.Ok(store.Validate(id)));
        endpoints.MapPost("/api/sus/billing/v2/batches/{id:guid}/close", (Guid id, SusBillingEngineStore store) => Results.Ok(store.Close(id)));
        endpoints.MapPost("/api/sus/billing/v2/batches/{id:guid}/reopen", (Guid id, ReopenBillingBatchRequest request, SusBillingEngineStore store) => Results.Ok(store.Reopen(id, request)));
        endpoints.MapGet("/api/sus/billing/v2/batches/{id:guid}/export", (Guid id, SusBillingEngineStore store) =>
        {
            var export = store.Export(id);
            return Results.Ok(export);
        });
        endpoints.MapGet("/api/sus/billing/v2/batches/{id:guid}/history", (Guid id, SusBillingEngineStore store) => Results.Ok(store.History(id)));

        return endpoints;
    }
}

public sealed class SusBillingEngineStore
{
    private readonly ConcurrentDictionary<string, SigtapProcedure> _catalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, SusProductionItem> _production = new();
    private readonly ConcurrentDictionary<Guid, AdvancedBillingBatch> _batches = new();
    private readonly ConcurrentDictionary<Guid, List<BillingBatchEvent>> _history = new();

    public SusBillingEngineStore()
    {
        SeedProcedure("0301010072", "Consulta médica em atenção especializada", "BPA-I", ["2251", "2252", "2253"], null, null, 0, 130);
        SeedProcedure("0301100039", "Aferição de pressão arterial", "BPA-C", ["3222", "2235"], null, null, 0, 130);
        SeedProcedure("0301010030", "Consulta de profissionais de nível superior na APS", "BPA-I", ["2235", "2515", "2236"], null, null, 0, 130);
        SeedProcedure("0307030024", "Restauração de dente permanente anterior", "BPA-I", ["2232"], null, null, 5, 130);
        SeedProcedure("0307030032", "Restauração de dente permanente posterior", "BPA-I", ["2232"], null, null, 5, 130);
        SeedProcedure("0307080015", "Raspagem alisamento e polimento supragengivais por sextante", "BPA-I", ["2232"], null, null, 5, 130);
        SeedProcedure("0202020380", "Hemograma completo", "BPA-I", ["2251", "2235"], null, null, 0, 130);
        SeedProcedure("0205020046", "Ultrassonografia de abdome total", "BPA-I", ["2251"], null, null, 0, 130);

        var demoCitizen = Guid.Parse("11111111-1111-1111-1111-111111111111");
        RegisterProduction(new RegisterSusProductionRequest(demoCitizen, "Maria da Silva", "UBS Vila Hortolândia", "0301010072", "225125", "I10", "F", 67, "Dr. Eduardo Martins", "CRM 000001", DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), null, null, "municipal"));
        RegisterProduction(new RegisterSusProductionRequest(demoCitizen, "Maria da Silva", "UBS Vila Hortolândia", "0301100039", "322205", "I10", "F", 67, "Enf. Juliana Ramos", "COREN 000001", DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), null, null, "municipal"));
    }

    public IReadOnlyList<SigtapProcedure> Catalog() => _catalog.Values.OrderBy(x => x.Code).ToList();
    public IReadOnlyList<SusProductionItem> Production() => _production.Values.OrderByDescending(x => x.ServiceDate).ToList();
    public IReadOnlyList<AdvancedBillingBatch> Batches() => _batches.Values.OrderByDescending(x => x.CreatedAt).ToList();

    public SusProductionItem RegisterProduction(RegisterSusProductionRequest request)
    {
        if (!_catalog.TryGetValue(request.ProcedureCode.Trim(), out var procedure)) throw new ArgumentException("Procedimento SIGTAP não parametrizado na POC.");
        var item = new SusProductionItem(Guid.NewGuid(), request.CitizenId, request.CitizenName.Trim(), request.HealthUnit.Trim(), procedure.Code, procedure.Name, procedure.BillingForm, request.Cbo.Trim(), request.Cid?.Trim(), request.Sex?.Trim().ToUpperInvariant(), request.Age, request.ProfessionalName.Trim(), request.ProfessionalCouncil.Trim(), request.ServiceDate, request.Tooth, request.Sextant, request.Source?.Trim() ?? "municipal", DateTimeOffset.UtcNow);
        _production[item.Id] = item;
        return item;
    }

    public SusProductionItem RegisterDental(Guid citizenId, string citizenName, string unit, string procedureCode, int? tooth, int? sextant, string professional, string council, string? cid = "K02.9") =>
        RegisterProduction(new RegisterSusProductionRequest(citizenId, citizenName, unit, procedureCode, "223208", cid, null, null, professional, council, DateOnly.FromDateTime(DateTime.Today), tooth, sextant, "dental"));

    public AdvancedBillingBatch CreateBatch(string competence)
    {
        competence = NormalizeCompetence(competence);
        if (_batches.Values.Any(x => x.Competence == competence && x.Status is not "superseded")) throw new InvalidOperationException("Já existe lote ativo para a competência.");
        var items = _production.Values.Where(x => x.ServiceDate.ToString("yyyyMM") == competence).Select(x => x.Id).ToList();
        var batch = new AdvancedBillingBatch(Guid.NewGuid(), competence, 1, "draft", items, [], null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _batches[batch.Id] = batch;
        Append(batch.Id, "created", $"version={batch.Version};items={items.Count}");
        return Validate(batch.Id);
    }

    public AdvancedBillingBatch Validate(Guid id)
    {
        var batch = Required(id);
        if (batch.Status == "closed") throw new InvalidOperationException("Lote fechado; reabra antes de validar novamente.");
        var issues = new List<BillingIssue>();
        foreach (var itemId in batch.ProductionIds)
        {
            if (!_production.TryGetValue(itemId, out var item)) continue;
            if (!_catalog.TryGetValue(item.ProcedureCode, out var rule))
            {
                issues.Add(new(Guid.NewGuid(), item.Id, "SIGTAP_NOT_FOUND", "Procedimento não encontrado no catálogo parametrizado.", "blocking"));
                continue;
            }
            if (item.Age is { } age && (age < rule.MinAge || age > rule.MaxAge))
                issues.Add(new(Guid.NewGuid(), item.Id, "AGE_OUT_OF_RANGE", $"Idade {age} fora da faixa {rule.MinAge}-{rule.MaxAge}.", "blocking"));
            if (rule.AllowedSex is { Length: > 0 } sex && !string.Equals(sex, item.Sex, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(Guid.NewGuid(), item.Id, "SEX_MISMATCH", $"Procedimento parametrizado para sexo {sex}.", "blocking"));
            if (!rule.AllowedCboPrefixes.Any(prefix => item.Cbo.StartsWith(prefix, StringComparison.Ordinal)))
                issues.Add(new(Guid.NewGuid(), item.Id, "CBO_MISMATCH", $"CBO {item.Cbo} incompatível com procedimento.", "blocking"));
            if (rule.RequiresTooth && item.Tooth is null)
                issues.Add(new(Guid.NewGuid(), item.Id, "TOOTH_REQUIRED", "Procedimento odontológico requer elemento dentário.", "blocking"));
            if (rule.RequiresSextant && item.Sextant is null)
                issues.Add(new(Guid.NewGuid(), item.Id, "SEXTANT_REQUIRED", "Procedimento periodontal requer sextante.", "blocking"));
        }
        var updated = batch with { Status = issues.Any(x => x.Severity == "blocking") ? "criticized" : "validated", Issues = issues, UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        Append(id, "validated", $"issues={issues.Count}");
        return updated;
    }

    public AdvancedBillingBatch Close(Guid id)
    {
        var batch = Required(id);
        if (batch.Status != "validated") throw new InvalidOperationException("Somente lote validado e sem crítica bloqueante pode ser fechado.");
        var checksum = ComputeChecksum(BuildExportLines(batch));
        var updated = batch with { Status = "closed", ExportChecksum = checksum, UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        Append(id, "closed", $"checksum={checksum}");
        return updated;
    }

    public AdvancedBillingBatch Reopen(Guid id, ReopenBillingBatchRequest request)
    {
        var batch = Required(id);
        if (batch.Status != "closed") throw new InvalidOperationException("Somente lote fechado pode ser reaberto.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Motivo da reabertura é obrigatório.");
        var updated = batch with { Version = batch.Version + 1, Status = "draft", ExportChecksum = null, UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        Append(id, "reopened", $"reason={request.Reason.Trim()};version={updated.Version};actor={request.Actor?.Trim() ?? "billing"}");
        return updated;
    }

    public BillingExportArtifact Export(Guid id)
    {
        var batch = Required(id);
        if (batch.Status != "closed") throw new InvalidOperationException("Lote deve estar fechado antes da exportação.");
        var lines = BuildExportLines(batch);
        var checksum = ComputeChecksum(lines);
        if (!string.Equals(checksum, batch.ExportChecksum, StringComparison.Ordinal)) throw new InvalidOperationException("Checksum do lote divergiu após fechamento.");
        Append(id, "exported", $"checksum={checksum};lines={lines.Count}");
        return new BillingExportArtifact(batch.Id, batch.Competence, batch.Version, "POC-BPA-STRUCTURED", lines, checksum, DateTimeOffset.UtcNow, "Estrutura demonstrativa; transmissão/layout oficial exige implantação conforme versão DATASUS vigente.");
    }

    public IReadOnlyList<BillingBatchEvent> History(Guid id) => _history.TryGetValue(id, out var events) ? events.OrderBy(x => x.OccurredAt).ToList() : [];

    private List<string> BuildExportLines(AdvancedBillingBatch batch)
    {
        var lines = new List<string> { $"HDR|JUNDIAI|{batch.Competence}|V{batch.Version}|RCE0082026" };
        foreach (var id in batch.ProductionIds)
        {
            if (!_production.TryGetValue(id, out var p)) continue;
            lines.Add($"{p.BillingForm}|{p.ServiceDate:yyyyMMdd}|{Sanitize(p.HealthUnit)}|{p.ProcedureCode}|{Sanitize(p.Cbo)}|{Sanitize(p.Cid)}|{p.CitizenId:N}|{p.Tooth?.ToString() ?? ""}|{p.Sextant?.ToString() ?? ""}");
        }
        lines.Add($"TRL|{batch.ProductionIds.Count}");
        return lines;
    }

    private static string ComputeChecksum(IReadOnlyList<string> lines) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    private static string Sanitize(string? value) => (value ?? string.Empty).Replace("|", "/").Trim();
    private AdvancedBillingBatch Required(Guid id) => _batches.TryGetValue(id, out var batch) ? batch : throw new KeyNotFoundException();
    private void Append(Guid id, string action, string detail) => _history.GetOrAdd(id, _ => []).Add(new BillingBatchEvent(Guid.NewGuid(), id, action, detail, DateTimeOffset.UtcNow));
    private static string NormalizeCompetence(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace("-", string.Empty).Replace("/", string.Empty);
        if (normalized.Length != 6 || !int.TryParse(normalized, out _)) throw new ArgumentException("Competência deve estar em yyyyMM.");
        return normalized;
    }

    private void SeedProcedure(string code, string name, string billingForm, IReadOnlyList<string> cboPrefixes, string? sex, string? cidPrefix, int minAge, int maxAge, bool tooth = false, bool sextant = false) =>
        _catalog[code] = new SigtapProcedure(code, name, billingForm, cboPrefixes, sex, cidPrefix, minAge, maxAge, tooth || code is "0307030024" or "0307030032", sextant || code == "0307080015");
}

public sealed record SigtapProcedure(string Code, string Name, string BillingForm, IReadOnlyList<string> AllowedCboPrefixes, string? AllowedSex, string? CidPrefix, int MinAge, int MaxAge, bool RequiresTooth, bool RequiresSextant);
public sealed record SusProductionItem(Guid Id, Guid CitizenId, string CitizenName, string HealthUnit, string ProcedureCode, string ProcedureName, string BillingForm, string Cbo, string? Cid, string? Sex, int? Age, string ProfessionalName, string ProfessionalCouncil, DateOnly ServiceDate, int? Tooth, int? Sextant, string Source, DateTimeOffset CreatedAt);
public sealed record RegisterSusProductionRequest(Guid CitizenId, string CitizenName, string HealthUnit, string ProcedureCode, string Cbo, string? Cid, string? Sex, int? Age, string ProfessionalName, string ProfessionalCouncil, DateOnly ServiceDate, int? Tooth, int? Sextant, string? Source);
public sealed record AdvancedBillingBatch(Guid Id, string Competence, int Version, string Status, IReadOnlyList<Guid> ProductionIds, IReadOnlyList<BillingIssue> Issues, string? ExportChecksum, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record BillingIssue(Guid Id, Guid ProductionId, string Code, string Message, string Severity);
public sealed record BillingBatchEvent(Guid Id, Guid BatchId, string Action, string Detail, DateTimeOffset OccurredAt);
public sealed record BillingExportArtifact(Guid BatchId, string Competence, int Version, string Format, IReadOnlyList<string> Lines, string Sha256, DateTimeOffset GeneratedAt, string Disclaimer);
public sealed record CreateAdvancedBillingBatchRequest(string Competence);
public sealed record ReopenBillingBatchRequest(string Reason, string? Actor);
