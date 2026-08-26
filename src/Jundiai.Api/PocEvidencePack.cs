using System.Text;
using System.Text.Json;

namespace Jundiai.Api;

public static class PocEvidencePackEndpoints
{
    public static IEndpointRouteBuilder MapPocEvidencePackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/poc/evidence-pack", async (
            GenerateEvidencePackRequest request,
            HttpContext context,
            PocEvidencePackStore store,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var pack = await store.GenerateAsync(request, scope, ct);
            return Results.Created($"/api/poc/evidence-pack/{pack.Payload.PackId}", pack);
        });

        endpoints.MapGet("/api/poc/evidence-pack/latest", (PocEvidencePackStore store) =>
            store.Latest() is { } pack ? Results.Ok(pack) : Results.NotFound());

        endpoints.MapGet("/api/poc/evidence-pack/latest/manifest", (PocEvidencePackStore store) =>
            store.Latest() is { } pack ? Results.Ok(store.Manifest(pack)) : Results.NotFound());

        endpoints.MapGet("/api/poc/evidence-pack/latest/verify", (PocEvidencePackStore store) =>
            store.Latest() is { } pack ? Results.Ok(store.Verify(pack)) : Results.NotFound());

        endpoints.MapGet("/api/poc/evidence-pack/latest/export", (PocEvidencePackStore store) =>
        {
            var pack = store.Latest();
            if (pack is null) return Results.NotFound();
            var json = JsonSerializer.Serialize(pack, PocEvidencePackStore.JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var fileName = $"jundiai-rce-008-2026-evidence-pack-{pack.Payload.GeneratedAt:yyyyMMdd-HHmmss}.json";
            return Results.File(bytes, "application/json; charset=utf-8", fileName);
        });

        return endpoints;
    }
}

public sealed class PocEvidencePackStore(
    ContractPackJundiaiStore contract,
    PocVerificationRunnerStore verification,
    DemoStore demo,
    MunicipalOperationsStore operations,
    DemoIdentityStore identities,
    CitizenMasterDataStore masterData,
    ProfessionalRegistryStore professionals,
    ReferralNetworkStore referrals,
    SchedulingStore scheduling,
    ClinicalOrderStore clinical,
    DiagnosticsAdvancedStore diagnostics,
    ImmunizationAdvancedStore immunization,
    SusBillingEngineStore billing,
    InventoryAdvancedStore inventory,
    PharmacyCareStore pharmacy,
    TelemedicineStore telemedicine,
    IntegrationRegistryStore integrations,
    EvidenceLedgerStore evidence,
    PersistenceRuntimeState persistenceRuntime,
    PersistenceService persistence,
    PersistenceRecoveryService recovery,
    IntegrationMessagingPersistenceService messaging)
{
    private readonly object _gate = new();
    private PocEvidencePackArtifact? _latest;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public PocEvidencePackArtifact? Latest()
    {
        lock (_gate) return _latest;
    }

    public async Task<PocEvidencePackArtifact> GenerateAsync(GenerateEvidencePackRequest request, TenantScope tenant, CancellationToken ct)
    {
        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "poc.evidence-pack" : request.Actor.Trim();
        var shouldRun = request.ReRunVerification ?? true;
        var verificationRun = shouldRun || verification.Latest() is null
            ? verification.Run(demo, operations, identities, masterData, professionals, referrals, scheduling, clinical, diagnostics, immunization, billing, inventory, pharmacy, telemedicine, evidence)
            : verification.Latest()!;

        var contractReadiness = contract.Readiness(demo, operations, identities, masterData, scheduling, clinical, diagnostics, immunization, billing, inventory, pharmacy, telemedicine, evidence);
        var ledgerVerification = evidence.Verify();
        var ledgerEvents = evidence.Events().TakeLast(250).ToList();
        var verificationByBlock = verificationRun.Blocks.ToDictionary(x => x.Block);
        var readinessByBlock = contractReadiness.Blocks.ToDictionary(x => x.Block);

        var blocks = contract.Blocks().OrderBy(x => x.Number).Select(block =>
        {
            verificationByBlock.TryGetValue(block.Number, out var check);
            readinessByBlock.TryGetValue(block.Number, out var state);
            var requirementIds = new[] { $"POC-B{block.Number:00}", $"POC-B{block.Number}" };
            var blockEvidence = ledgerEvents
                .Where(x => x.RequirementId is not null && requirementIds.Contains(x.RequirementId, StringComparer.OrdinalIgnoreCase))
                .TakeLast(25)
                .Select(x => new PocEvidenceReference(x.Sequence, x.Action, x.Resource, x.Hash, x.OccurredAt))
                .ToList();
            return new PocEvidencePackBlock(
                block.Number,
                block.Name,
                block.Slug,
                check?.Passed ?? false,
                check?.Score ?? state?.Score ?? 0,
                state?.Status ?? (check?.Passed == true ? "implemented_poc" : "attention"),
                check?.Evidence ?? state?.Evidence ?? "Sem evidência calculada.",
                block.UiRoute,
                block.Capabilities,
                block.EvidenceEndpoints,
                blockEvidence);
        }).ToList();

        var persistenceState = new PocEvidencePersistenceState(
            persistenceRuntime.Provider,
            persistenceRuntime.Configured,
            persistenceRuntime.Mode,
            await persistence.ReadinessAsync(ct),
            await recovery.ReadinessAsync(tenant, ct),
            await messaging.ReadinessAsync(tenant, ct));

        var payload = new PocEvidencePackPayload(
            "JUNDIAI-RCE-008-2026-POC",
            "2026.08.26-evidence-pack-v1",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            tenant.InstitutionId,
            tenant.HealthUnitId,
            verificationRun,
            contractReadiness,
            blocks,
            integrations.All(),
            contract.NonCodeBlockers(),
            PlatformReadinessModel.Build(),
            persistenceState,
            ledgerVerification,
            ledgerEvents,
            new[]
            {
                "Evidence Pack registra o estado demonstrável desta instância da POC; não declara homologação de terceiros nem produção municipal.",
                "Integrações externas permanecem condicionadas a credenciais, versões, contratos e homologações oficiais vigentes.",
                "Persistência PostgreSQL/recovery/messaging representam fundação técnica; backup gerenciado, PITR, failover, DR, workers e broker produtivos continuam gates de implantação.",
                "Habilitação documental, atestados de capacidade técnica e obrigações operacionais não são resolvidos pelo código."
            });

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var packageSha = DurableJson.Sha256Canonical(payloadJson);
        var artifact = new PocEvidencePackArtifact(payload, packageSha, "SHA-256", "durable-json-canonical-v1");

        lock (_gate) _latest = artifact;
        evidence.Append(new CreateEvidenceEventRequest(
            actor,
            "poc.evidence-pack.generate",
            $"evidence-pack:{payload.PackId}",
            "POC-ALL",
            $"blocks={blocks.Count};passed={verificationRun.PassedBlocks}/{verificationRun.TotalBlocks};sha256={packageSha};institution={tenant.InstitutionId}",
            "poc-evidence-pack"));
        demo.AuditExternal(actor, "poc.evidence-pack.generate", $"evidence-pack:{payload.PackId}", packageSha);
        return artifact;
    }

    public PocEvidencePackVerification Verify(PocEvidencePackArtifact pack)
    {
        var payloadJson = JsonSerializer.Serialize(pack.Payload, JsonOptions);
        var calculated = DurableJson.Sha256Canonical(payloadJson);
        var packageValid = string.Equals(calculated, pack.PackageSha256, StringComparison.OrdinalIgnoreCase);
        var ledger = evidence.Verify();
        var blockCount = pack.Payload.Blocks.Count;
        var passed = pack.Payload.Blocks.Count(x => x.Passed);
        return new PocEvidencePackVerification(
            pack.Payload.PackId,
            packageValid,
            calculated,
            pack.PackageSha256,
            ledger.Valid,
            passed,
            blockCount,
            packageValid && ledger.Valid && passed == blockCount,
            DateTimeOffset.UtcNow,
            "Verificação comprova integridade do payload exportado e da cadeia local de evidências; não substitui assinatura digital ICP-Brasil ou carimbo do tempo oficial.");
    }

    public PocEvidencePackManifest Manifest(PocEvidencePackArtifact pack)
    {
        var verificationReport = Verify(pack);
        return new PocEvidencePackManifest(
            pack.Payload.PackId,
            pack.Payload.ContractPack,
            pack.Payload.Version,
            pack.Payload.GeneratedAt,
            pack.Payload.InstitutionId,
            pack.Payload.HealthUnitId,
            pack.Payload.Verification.PassedBlocks,
            pack.Payload.Verification.TotalBlocks,
            pack.Payload.Verification.OverallScore,
            pack.Payload.Blocks.Count,
            pack.Payload.Integrations.Count,
            pack.Payload.NonCodeBlockers.Count,
            pack.Payload.EvidenceEvents.Count,
            pack.PackageSha256,
            pack.Canonicalization,
            verificationReport.PackageHashValid,
            verificationReport.LedgerChainValid);
    }
}

public sealed record GenerateEvidencePackRequest(string? Actor, bool? ReRunVerification);
public sealed record PocEvidenceReference(long Sequence, string Action, string Resource, string Hash, DateTimeOffset OccurredAt);
public sealed record PocEvidencePackBlock(
    int Block,
    string Name,
    string Slug,
    bool Passed,
    int Score,
    string Status,
    string Evidence,
    string UiRoute,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> EvidenceEndpoints,
    IReadOnlyList<PocEvidenceReference> LedgerEvidence);
public sealed record PocEvidencePersistenceState(string Provider, bool Configured, string Mode, object Database, object Recovery, object Messaging);
public sealed record PocEvidencePackPayload(
    string ContractPack,
    string Version,
    Guid PackId,
    DateTimeOffset GeneratedAt,
    string InstitutionId,
    string? HealthUnitId,
    PocVerificationRun Verification,
    JundiaiContractReadiness ContractReadiness,
    IReadOnlyList<PocEvidencePackBlock> Blocks,
    IReadOnlyList<IntegrationCapability> Integrations,
    IReadOnlyList<JundiaiNonCodeBlocker> NonCodeBlockers,
    object ProductionReadiness,
    PocEvidencePersistenceState Persistence,
    EvidenceVerification LedgerVerification,
    IReadOnlyList<EvidenceLedgerEvent> EvidenceEvents,
    IReadOnlyList<string> Disclaimers);
public sealed record PocEvidencePackArtifact(PocEvidencePackPayload Payload, string PackageSha256, string HashAlgorithm, string Canonicalization);
public sealed record PocEvidencePackVerification(
    Guid PackId,
    bool PackageHashValid,
    string CalculatedSha256,
    string ExpectedSha256,
    bool LedgerChainValid,
    int PassedBlocks,
    int TotalBlocks,
    bool DemonstrationIntegrityReady,
    DateTimeOffset VerifiedAt,
    string Disclaimer);
public sealed record PocEvidencePackManifest(
    Guid PackId,
    string ContractPack,
    string Version,
    DateTimeOffset GeneratedAt,
    string InstitutionId,
    string? HealthUnitId,
    int PassedBlocks,
    int TotalBlocks,
    int OverallScore,
    int IndexedBlocks,
    int IntegrationCount,
    int NonCodeBlockerCount,
    int EvidenceEventCount,
    string PackageSha256,
    string Canonicalization,
    bool PackageHashValid,
    bool LedgerChainValid);