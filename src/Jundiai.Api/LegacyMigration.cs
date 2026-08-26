using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jundiai.Api;

public static class LegacyMigrationEndpoints
{
    public static IEndpointRouteBuilder MapLegacyMigrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/migration/batches", (LegacyMigrationStore store) => Results.Ok(store.Batches()));
        endpoints.MapGet("/api/migration/batches/{id:guid}", (Guid id, LegacyMigrationStore store) =>
            store.Get(id) is { } batch ? Results.Ok(batch) : Results.NotFound());
        endpoints.MapPost("/api/migration/batches", (CreateMigrationBatchRequest request, LegacyMigrationStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var batch = store.Create(request);
            demo.AuditExternal(request.Actor, "migration.batch.create", $"migration:{batch.Id}", $"source={batch.SourceSystem};entity={batch.Entity}");
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "migration.batch.create", $"migration:{batch.Id}", "POC-B2", batch.SourceManifestSha256, "migration-manifest"));
            return Results.Created($"/api/migration/batches/{batch.Id}", batch);
        });
        endpoints.MapPost("/api/migration/batches/{id:guid}/map", (Guid id, SetMigrationMappingRequest request, LegacyMigrationStore store) => Results.Ok(store.SetMapping(id, request)));
        endpoints.MapPost("/api/migration/batches/{id:guid}/validate", (Guid id, ValidateMigrationBatchRequest request, LegacyMigrationStore store, DemoStore demo) =>
        {
            var batch = store.Validate(id, request);
            demo.AuditExternal(request.Actor, "migration.batch.validate", $"migration:{id}", $"valid={batch.Validation?.ValidRows};errors={batch.Validation?.ErrorRows}");
            return Results.Ok(batch);
        });
        endpoints.MapPost("/api/migration/batches/{id:guid}/reconcile", (Guid id, ReconcileMigrationBatchRequest request, LegacyMigrationStore store, DemoStore demo) =>
        {
            var batch = store.Reconcile(id, request);
            demo.AuditExternal(request.Actor, "migration.batch.reconcile", $"migration:{id}", $"target={batch.Reconciliation?.TargetRows};delta={batch.Reconciliation?.Delta}");
            return Results.Ok(batch);
        });
        endpoints.MapPost("/api/migration/batches/{id:guid}/accept", (Guid id, AcceptMigrationBatchRequest request, LegacyMigrationStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var batch = store.Accept(id, request);
            demo.AuditExternal(request.Actor, "migration.batch.accept", $"migration:{id}", request.Note);
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "migration.batch.accept", $"migration:{id}", "POC-B2", batch.AcceptanceSha256, "migration-acceptance"));
            return Results.Ok(batch);
        });
        endpoints.MapGet("/api/migration/readiness", (LegacyMigrationStore store) => Results.Ok(new
        {
            batches = store.Batches().Count,
            capabilities = new[] { "manifesto de origem com SHA-256", "mapeamento campo-a-campo", "validação e quarentena", "reconciliação origem-destino", "aceite versionado", "trilha de auditoria/evidência" },
            productionDependencies = new[] { "dump/consulta autorizada do legado CIJUN", "dicionário de dados real", "volumetria", "janela de corte", "critérios formais de aceite", "tratamento LGPD" },
            note = "O workspace demonstra método de migração; não afirma migração real de dados municipais sem acesso formal à origem."
        }));
        return endpoints;
    }
}

public sealed class LegacyMigrationStore
{
    private readonly ConcurrentDictionary<Guid, MigrationBatch> _batches = new();

    public LegacyMigrationStore()
    {
        var demo = Create(new CreateMigrationBatchRequest("LEGADO-CIJUN-DEMO", "citizens", 10000, "schema:citizen-demo-v1", "seed"));
        SetMapping(demo.Id, new SetMigrationMappingRequest([
            new("paciente_id", "external_id", "string", true, null),
            new("nome", "name", "trim-uppercase-normalization", true, null),
            new("cpf", "cpf", "digits-only", false, "validate-cpf"),
            new("cns", "cns", "digits-only", false, "validate-cns"),
            new("unidade", "health_unit", "unit-crosswalk", true, "unit-map")
        ], "seed"));
    }

    public IReadOnlyList<MigrationBatch> Batches() => _batches.Values.OrderByDescending(x => x.CreatedAt).ToList();
    public MigrationBatch? Get(Guid id) => _batches.TryGetValue(id, out var batch) ? batch : null;

    public MigrationBatch Create(CreateMigrationBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceSystem) || string.IsNullOrWhiteSpace(request.Entity)) throw new ArgumentException("Sistema origem e entidade são obrigatórios.");
        if (request.SourceRows < 0) throw new ArgumentException("Quantidade de linhas não pode ser negativa.");
        var manifest = $"{request.SourceSystem.Trim()}|{request.Entity.Trim()}|{request.SourceRows}|{request.SourceSchemaFingerprint?.Trim()}|{DateTimeOffset.UtcNow:O}";
        var batch = new MigrationBatch(Guid.NewGuid(), request.SourceSystem.Trim(), request.Entity.Trim(), request.SourceRows, request.SourceSchemaFingerprint?.Trim(), Sha256(manifest), "mapping", [], null, null, null, null, request.Actor.Trim(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _batches[batch.Id] = batch;
        return batch;
    }

    public MigrationBatch SetMapping(Guid id, SetMigrationMappingRequest request)
    {
        var current = Required(id);
        if (current.Status is "accepted" or "rejected") throw new InvalidOperationException("Lote encerrado não pode ter mapeamento alterado.");
        if (request.Fields is null || request.Fields.Count == 0) throw new ArgumentException("Informe ao menos um mapeamento.");
        var duplicateTargets = request.Fields.GroupBy(x => x.TargetField, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicateTargets.Length > 0) throw new ArgumentException($"Campos destino duplicados: {string.Join(',', duplicateTargets)}");
        var updated = current with { Status = "mapped", Mapping = request.Fields.ToArray(), UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        return updated;
    }

    public MigrationBatch Validate(Guid id, ValidateMigrationBatchRequest request)
    {
        var current = Required(id);
        if (current.Mapping.Count == 0) throw new InvalidOperationException("Defina o mapeamento antes da validação.");
        if (request.ValidRows < 0 || request.ErrorRows < 0 || request.QuarantinedRows < 0) throw new ArgumentException("Contagens não podem ser negativas.");
        if (request.ValidRows + request.ErrorRows + request.QuarantinedRows != current.SourceRows)
            throw new ArgumentException("Valid + error + quarantine deve reconciliar com SourceRows.");
        var issues = request.Issues?.Where(x => !string.IsNullOrWhiteSpace(x.Code)).ToArray() ?? [];
        var validation = new MigrationValidation(request.ValidRows, request.ErrorRows, request.QuarantinedRows, issues, request.Actor.Trim(), DateTimeOffset.UtcNow);
        var updated = current with { Status = request.ErrorRows == 0 ? "validated" : "validated_with_errors", Validation = validation, UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        return updated;
    }

    public MigrationBatch Reconcile(Guid id, ReconcileMigrationBatchRequest request)
    {
        var current = Required(id);
        if (current.Validation is null) throw new InvalidOperationException("Valide o lote antes da reconciliação.");
        if (request.TargetRows < 0) throw new ArgumentException("Quantidade destino inválida.");
        var expected = current.Validation.ValidRows;
        var delta = request.TargetRows - expected;
        var reconciliation = new MigrationReconciliation(expected, request.TargetRows, delta, request.DuplicateRows, request.OrphanRows, request.Actor.Trim(), request.Note?.Trim(), DateTimeOffset.UtcNow);
        var updated = current with { Status = delta == 0 && request.DuplicateRows == 0 && request.OrphanRows == 0 ? "reconciled" : "reconciliation_issue", Reconciliation = reconciliation, UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        return updated;
    }

    public MigrationBatch Accept(Guid id, AcceptMigrationBatchRequest request)
    {
        var current = Required(id);
        if (current.Status != "reconciled") throw new InvalidOperationException("Somente lote reconciliado sem divergência pode ser aceito.");
        var acceptance = Sha256($"{current.Id:N}|{current.SourceManifestSha256}|{current.Reconciliation?.TargetRows}|{request.Actor.Trim()}|{request.Note?.Trim()}|{DateTimeOffset.UtcNow:O}");
        var updated = current with { Status = "accepted", AcceptanceSha256 = acceptance, AcceptedBy = request.Actor.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        _batches[id] = updated;
        return updated;
    }

    private MigrationBatch Required(Guid id) => _batches.TryGetValue(id, out var batch) ? batch : throw new KeyNotFoundException();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record MigrationBatch(Guid Id, string SourceSystem, string Entity, int SourceRows, string? SourceSchemaFingerprint, string SourceManifestSha256, string Status, IReadOnlyList<MigrationFieldMapping> Mapping, MigrationValidation? Validation, MigrationReconciliation? Reconciliation, string? AcceptanceSha256, string? AcceptedBy, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record MigrationFieldMapping(string SourceField, string TargetField, string Transform, bool Required, string? Validator);
public sealed record MigrationValidation(int ValidRows, int ErrorRows, int QuarantinedRows, IReadOnlyList<MigrationIssue> Issues, string Actor, DateTimeOffset ValidatedAt);
public sealed record MigrationIssue(string Code, string Message, int Count, string Severity);
public sealed record MigrationReconciliation(int ExpectedRows, int TargetRows, int Delta, int DuplicateRows, int OrphanRows, string Actor, string? Note, DateTimeOffset ReconciledAt);
public sealed record CreateMigrationBatchRequest(string SourceSystem, string Entity, int SourceRows, string? SourceSchemaFingerprint, string Actor);
public sealed record SetMigrationMappingRequest(IReadOnlyList<MigrationFieldMapping> Fields, string Actor);
public sealed record ValidateMigrationBatchRequest(int ValidRows, int ErrorRows, int QuarantinedRows, IReadOnlyList<MigrationIssue>? Issues, string Actor);
public sealed record ReconcileMigrationBatchRequest(int TargetRows, int DuplicateRows, int OrphanRows, string Actor, string? Note);
public sealed record AcceptMigrationBatchRequest(string Actor, string? Note);
