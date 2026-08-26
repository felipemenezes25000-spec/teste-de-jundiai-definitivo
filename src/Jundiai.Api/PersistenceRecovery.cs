using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Jundiai.Api;

public static class PersistenceRecoveryEndpoints
{
    public static IEndpointRouteBuilder MapPersistenceRecoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit/persistence/checkpoints", async (
            HttpContext context,
            PersistenceRecoveryService service,
            CancellationToken ct) =>
            Results.Ok(await service.CheckpointsAsync(TenantContextMiddleware.GetScope(context), ct)));

        endpoints.MapGet("/api/audit/persistence/checkpoints/{checkpointId:guid}/manifest", async (
            Guid checkpointId,
            HttpContext context,
            PersistenceRecoveryService service,
            CancellationToken ct) =>
            Results.Ok(await service.ManifestAsync(checkpointId, TenantContextMiddleware.GetScope(context), ct)));

        endpoints.MapPost("/api/audit/persistence/checkpoints/full", async (
            FullCheckpointRequest request,
            HttpContext context,
            PersistenceRecoveryService service,
            DemoStore demo,
            CitizenMasterDataStore masterData,
            SchedulingStore scheduling,
            ClinicalOrderStore clinical,
            DiagnosticsAdvancedStore diagnostics,
            ImmunizationAdvancedStore immunization,
            PharmacyCareStore pharmacy,
            ReferralNetworkStore referrals,
            EvidenceLedgerStore evidence,
            IntegrationRegistryStore integrations,
            SusBillingEngineStore billing,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var result = await service.CreateFullCheckpointAsync(
                request, scope, demo, masterData, scheduling, clinical, diagnostics, immunization,
                pharmacy, referrals, evidence, integrations, billing, ct);
            evidence.Append(new CreateEvidenceEventRequest(
                "persistence.recovery",
                "persistence.full-checkpoint",
                $"checkpoint:{result.CheckpointId}",
                "POC-B01",
                $"institution={scope.InstitutionId};envelopes={result.EnvelopeCount};manifest={result.ManifestSha256}",
                "durability-checkpoint"));
            return Results.Created($"/api/audit/persistence/checkpoints/{result.CheckpointId}/manifest", result);
        });

        endpoints.MapPost("/api/audit/persistence/recovery-drill", async (
            RecoveryDrillRequest request,
            HttpContext context,
            PersistenceRecoveryService service,
            EvidenceLedgerStore evidence,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var result = await service.RunRecoveryDrillAsync(request, scope, ct);
            evidence.Append(new CreateEvidenceEventRequest(
                request.Actor ?? "persistence.recovery",
                "persistence.recovery-drill",
                $"checkpoint:{result.CheckpointId}",
                "POC-B01",
                $"valid={result.IntegrityValid};critical={result.CriticalKindsPresent}/{result.CriticalKindsExpected};restorePreview={result.RestorePreviewValid}",
                "recovery-drill"));
            return Results.Ok(result);
        });

        endpoints.MapGet("/api/audit/persistence/recovery/readiness", async (
            HttpContext context,
            PersistenceRecoveryService service,
            CancellationToken ct) =>
            Results.Ok(await service.ReadinessAsync(TenantContextMiddleware.GetScope(context), ct)));

        return endpoints;
    }
}

public sealed class PersistenceRecoveryService(IServiceProvider services, PersistenceRuntimeState runtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CriticalKinds =
    [
        "citizens-master",
        "scheduling-bookings",
        "clinical-orders",
        "diagnostics-orders",
        "immunizations-history",
        "pharmacy-dispensations",
        "referrals",
        "sus-production-v2",
        "evidence-ledger"
    ];

    public async Task<FullCheckpointResult> CreateFullCheckpointAsync(
        FullCheckpointRequest request,
        TenantScope scope,
        DemoStore demo,
        CitizenMasterDataStore masterData,
        SchedulingStore scheduling,
        ClinicalOrderStore clinical,
        DiagnosticsAdvancedStore diagnostics,
        ImmunizationAdvancedStore immunization,
        PharmacyCareStore pharmacy,
        ReferralNetworkStore referrals,
        EvidenceLedgerStore evidence,
        IntegrationRegistryStore integrations,
        SusBillingEngineStore billing,
        CancellationToken ct)
    {
        var factory = RequiredFactory();
        var checkpointId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var label = string.IsNullOrWhiteSpace(request.Label) ? "full-domain-checkpoint" : request.Label.Trim();

        var snapshots = new (string Kind, string ResourceId, object Payload)[]
        {
            ("citizens-core", "all", demo.Citizens()),
            ("citizens-master", "all", masterData.Search(null)),
            ("regulation", "all", demo.Regulation()),
            ("scheduling-bookings", "all", scheduling.Bookings()),
            ("scheduling-waitlist", "all", scheduling.Waitlist()),
            ("clinical-orders", "all", clinical.Orders(null)),
            ("clinical-administrations", "all", clinical.AllAdministrations()),
            ("clinical-care-plans", "all", clinical.CarePlans(null)),
            ("diagnostics-orders", "all", diagnostics.Orders(null)),
            ("immunizations-history", "all", demo.Immunizations()),
            ("immunization-adverse-events", "all", immunization.AdverseEvents(null)),
            ("pharmacy-inventory", "all", demo.Inventory()),
            ("pharmacy-reconciliations", "all", pharmacy.Reconciliations(null)),
            ("pharmacy-dispensations", "all", pharmacy.Dispensations(null)),
            ("referrals", "all", referrals.Referrals(null)),
            ("sus-production-v2", "all", billing.Production()),
            ("sus-batches-v2", "all", billing.Batches()),
            ("integrations", "registry", integrations.All()),
            ("evidence-ledger", "all", evidence.Events())
        };

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hashes = new List<string>(snapshots.Length);
        foreach (var snapshot in snapshots)
        {
            var json = JsonSerializer.Serialize(snapshot.Payload, JsonOptions);
            var hash = Sha256(json);
            hashes.Add($"{snapshot.Kind}|{snapshot.ResourceId}|{hash}");
            db.DurableEnvelopes.Add(new DurableEnvelope
            {
                Id = Guid.NewGuid(),
                CheckpointId = checkpointId,
                InstitutionId = scope.InstitutionId,
                HealthUnitId = scope.HealthUnitId,
                Kind = snapshot.Kind,
                ResourceId = snapshot.ResourceId,
                PayloadJson = json,
                ContentHash = hash,
                OccurredAt = now,
                Label = label
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var manifestHash = Sha256(string.Join('\n', hashes.OrderBy(x => x, StringComparer.Ordinal)));
        return new FullCheckpointResult(checkpointId, scope.InstitutionId, scope.HealthUnitId, snapshots.Length, label, manifestHash, now);
    }

    public async Task<IReadOnlyList<CheckpointSummary>> CheckpointsAsync(TenantScope scope, CancellationToken ct)
    {
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.DurableEnvelopes.AsNoTracking()
            .Where(x => x.InstitutionId == scope.InstitutionId)
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync(ct);
        return rows
            .GroupBy(x => x.CheckpointId)
            .Select(g => new CheckpointSummary(
                g.Key,
                g.First().InstitutionId,
                g.First().HealthUnitId,
                g.First().Label ?? "checkpoint",
                g.Count(),
                g.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count(),
                g.Max(x => x.OccurredAt)))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public async Task<CheckpointManifest> ManifestAsync(Guid checkpointId, TenantScope scope, CancellationToken ct)
    {
        var rows = await RowsAsync(checkpointId, scope, ct);
        var entries = rows.OrderBy(x => x.Kind).ThenBy(x => x.ResourceId)
            .Select(x => new CheckpointManifestEntry(x.Kind, x.ResourceId, x.ContentHash, Encoding.UTF8.GetByteCount(x.PayloadJson), x.OccurredAt))
            .ToList();
        var manifestHash = Sha256(string.Join('\n', entries.Select(x => $"{x.Kind}|{x.ResourceId}|{x.ContentHash}")));
        return new CheckpointManifest(checkpointId, scope.InstitutionId, rows.First().HealthUnitId, rows.First().Label ?? "checkpoint", entries, manifestHash, rows.Max(x => x.OccurredAt));
    }

    public async Task<RecoveryDrillResult> RunRecoveryDrillAsync(RecoveryDrillRequest request, TenantScope scope, CancellationToken ct)
    {
        var summaries = await CheckpointsAsync(scope, ct);
        var checkpointId = request.CheckpointId ?? summaries.FirstOrDefault()?.CheckpointId
            ?? throw new InvalidOperationException("Nenhum checkpoint disponível para recovery drill.");
        var rows = await RowsAsync(checkpointId, scope, ct);

        var failures = new List<string>();
        foreach (var row in rows)
        {
            var computed = Sha256(row.PayloadJson);
            if (!string.Equals(computed, row.ContentHash, StringComparison.OrdinalIgnoreCase))
                failures.Add($"hash:{row.Kind}:{row.ResourceId}");
        }

        var kinds = rows.Select(x => x.Kind).ToHashSet(StringComparer.Ordinal);
        var missing = CriticalKinds.Where(x => !kinds.Contains(x)).ToArray();
        var restorePreviewFailures = new List<string>();
        foreach (var row in rows)
        {
            try { using var _ = JsonDocument.Parse(row.PayloadJson); }
            catch (JsonException) { restorePreviewFailures.Add($"json:{row.Kind}:{row.ResourceId}"); }
        }

        var createdAt = rows.Max(x => x.OccurredAt);
        var age = Math.Max(0, (DateTimeOffset.UtcNow - createdAt).TotalSeconds);
        var integrityValid = failures.Count == 0;
        var restorePreviewValid = restorePreviewFailures.Count == 0;
        return new RecoveryDrillResult(
            checkpointId,
            scope.InstitutionId,
            integrityValid,
            restorePreviewValid,
            CriticalKinds.Length - missing.Length,
            CriticalKinds.Length,
            missing,
            failures.Concat(restorePreviewFailures).ToArray(),
            Math.Round(age, 1),
            rows.Count,
            request.Actor?.Trim() ?? "persistence.recovery",
            DateTimeOffset.UtcNow,
            "Recovery drill da POC valida integridade e desserialização dos snapshots; não equivale a restauração produtiva/PITR/DR testado em infraestrutura final.");
    }

    public async Task<object> ReadinessAsync(TenantScope scope, CancellationToken ct)
    {
        if (!runtime.Configured)
            return new
            {
                configured = false,
                mode = runtime.Mode,
                checkpoints = 0,
                recoveryDrillAvailable = false,
                productionGap = "Configurar PostgreSQL, política de backup/PITR, retenção, cópia externa e exercício de DR."
            };

        var checkpoints = await CheckpointsAsync(scope, ct);
        return new
        {
            configured = true,
            mode = "durable-postgresql",
            checkpoints = checkpoints.Count,
            latestCheckpointAt = checkpoints.FirstOrDefault()?.CreatedAt,
            recoveryDrillAvailable = checkpoints.Count > 0,
            criticalKinds = CriticalKinds,
            capabilities = new[] { "full-domain checkpoint", "per-institution isolation", "SHA-256 envelope verification", "manifest hash", "restore JSON preview", "RPO age measurement" },
            productionGaps = new[] { "managed backup/PITR", "offsite copy", "restore into isolated environment", "RTO benchmark", "scheduled DR exercise", "retention/legal archive policy" }
        };
    }

    private async Task<List<DurableEnvelope>> RowsAsync(Guid checkpointId, TenantScope scope, CancellationToken ct)
    {
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.DurableEnvelopes.AsNoTracking()
            .Where(x => x.CheckpointId == checkpointId && x.InstitutionId == scope.InstitutionId)
            .ToListAsync(ct);
        if (rows.Count == 0) throw new KeyNotFoundException("Checkpoint não encontrado para a instituição corrente.");
        return rows;
    }

    private IDbContextFactory<JundiaiDbContext> RequiredFactory()
    {
        if (!runtime.Configured)
            throw new InvalidOperationException("Persistência PostgreSQL não está configurada nesta instância da POC.");
        return services.GetRequiredService<IDbContextFactory<JundiaiDbContext>>();
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record FullCheckpointRequest(string? Label);
public sealed record RecoveryDrillRequest(Guid? CheckpointId, string? Actor);
public sealed record FullCheckpointResult(Guid CheckpointId, string InstitutionId, string? HealthUnitId, int EnvelopeCount, string Label, string ManifestSha256, DateTimeOffset CreatedAt);
public sealed record CheckpointSummary(Guid CheckpointId, string InstitutionId, string? HealthUnitId, string Label, int EnvelopeCount, int KindCount, DateTimeOffset CreatedAt);
public sealed record CheckpointManifest(Guid CheckpointId, string InstitutionId, string? HealthUnitId, string Label, IReadOnlyList<CheckpointManifestEntry> Entries, string ManifestSha256, DateTimeOffset CreatedAt);
public sealed record CheckpointManifestEntry(string Kind, string ResourceId, string ContentHash, int PayloadBytes, DateTimeOffset OccurredAt);
public sealed record RecoveryDrillResult(Guid CheckpointId, string InstitutionId, bool IntegrityValid, bool RestorePreviewValid, int CriticalKindsPresent, int CriticalKindsExpected, IReadOnlyList<string> MissingCriticalKinds, IReadOnlyList<string> Failures, double RpoAgeSeconds, int EnvelopeCount, string Actor, DateTimeOffset ExecutedAt, string Disclaimer);
