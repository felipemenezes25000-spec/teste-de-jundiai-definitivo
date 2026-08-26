namespace Jundiai.Api;

public static class PresentationPreparationEndpoints
{
    public static IEndpointRouteBuilder MapPresentationPreparationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/poc/presentation/prepare", async (
            PresentationPrepareRequest request,
            HttpContext context,
            PresentationPreparationStore store,
            CancellationToken ct) =>
        {
            var result = await store.PrepareAsync(request, TenantContextMiddleware.GetScope(context), ct);
            return Results.Ok(result);
        });

        endpoints.MapGet("/api/poc/presentation/latest", (PresentationPreparationStore store) =>
            store.Latest() is { } latest ? Results.Ok(latest) : Results.NotFound());

        endpoints.MapGet("/api/poc/presentation/checklist", (PresentationPreparationStore store) => Results.Ok(store.StaticChecklist()));
        return endpoints;
    }
}

public sealed class PresentationPreparationStore(
    IWebHostEnvironment environment,
    PocScenarioStore scenarios,
    DemoStore demo,
    SchedulingStore scheduling,
    TelemedicineStore telemedicine,
    DiagnosticsAdvancedStore diagnostics,
    ClinicalDocumentStore documents,
    AiGovernanceStore ai,
    EvidenceLedgerStore evidence,
    PocVerificationRunnerStore verification,
    MunicipalOperationsStore operations,
    DemoIdentityStore identities,
    CitizenMasterDataStore masterData,
    ProfessionalRegistryStore professionals,
    ReferralNetworkStore referrals,
    ClinicalOrderStore clinical,
    ImmunizationAdvancedStore immunization,
    SusBillingEngineStore billing,
    InventoryAdvancedStore inventory,
    PharmacyCareStore pharmacy,
    PocEvidencePackStore evidencePack,
    ContractPackJundiaiStore contract,
    IntegrationRegistryStore integrations,
    PersistenceRuntimeState persistenceRuntime)
{
    private readonly object _gate = new();
    private PresentationPreparationResult? _latest;

    private static readonly string[] CriticalPages =
    [
        "login.html", "poc.html", "verification.html", "evidence-pack.html", "dossier.html", "contingency.html", "command-center.html", "caretrace.html", "governance.html",
        "registration.html", "workforce.html", "referrals.html", "clinical-ops.html", "agenda.html", "telemedicine.html", "immunization-v2.html",
        "pharmacy-care.html", "diagnostics.html", "dental-v2.html", "billing-v2.html", "operations.html", "citizen.html", "esus.html", "acs.html"
    ];

    private static readonly string[] CriticalAssets =
    [
        "poc.css", "poc.js", "verification.js", "evidence-pack.js", "dossier.js", "contingency.js", "command-center.js", "caretrace.js", "governance.js", "governance-persistence.js",
        "governance-privacy.js", "sw.js"
    ];

    public PresentationPreparationResult? Latest()
    {
        lock (_gate) return _latest;
    }

    public object StaticChecklist() => new
    {
        pages = CriticalPages.Select(CheckWebAsset).ToArray(),
        assets = CriticalAssets.Select(CheckWebAsset).ToArray(),
        presentationOrder = new[]
        {
            "login + MFA", "Modo POC", "cenário ouro", "runner 14/14", "jornada assistencial", "governança", "Evidence Pack", "integrity verify", "Dossiê da Banca", "provenance runtime", "Kit de Contingência", "download ZIP / impressão-PDF"
        },
        rule = "Preflight de apresentação verifica demonstrabilidade; não promove Production Gates."
    };

    public async Task<PresentationPreparationResult> PrepareAsync(PresentationPrepareRequest request, TenantScope tenant, CancellationToken ct)
    {
        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "poc.presentation" : request.Actor.Trim();
        var scenario = scenarios.RunGoldenPath(demo, scheduling, telemedicine, diagnostics, documents, ai, evidence);
        var run = verification.Run(demo, operations, identities, masterData, professionals, referrals, scheduling, clinical, diagnostics, immunization, billing, inventory, pharmacy, telemedicine, evidence);
        var pack = await evidencePack.GenerateAsync(new GenerateEvidencePackRequest(actor, false), tenant, ct);
        var packVerification = evidencePack.Verify(pack);
        var ledger = evidence.Verify();
        var pages = CriticalPages.Select(CheckWebAsset).ToList();
        var assets = CriticalAssets.Select(CheckWebAsset).ToList();
        var blockers = contract.NonCodeBlockers();
        var integrationItems = integrations.All();
        var integrationGovernanceValid = integrationItems.All(x =>
        {
            var elevatedStatus =
                string.Equals(x.Status, "homologated", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Status, "production_enabled", StringComparison.OrdinalIgnoreCase);
            return !elevatedStatus || !string.IsNullOrWhiteSpace(x.LastAssessment?.EvidenceReference);
        });

        var checks = new List<PresentationCheck>
        {
            Check("scenario-golden", "Cenário ouro", scenario.Status == "completed" && scenario.EvidenceChainValid, $"artifacts={scenario.Artifacts.Count};citizen={scenario.CitizenName}"),
            Check("runner-14", "Runner 14 blocos", run.PassedBlocks == 14 && run.TotalBlocks == 14, $"passed={run.PassedBlocks}/{run.TotalBlocks};score={run.OverallScore}"),
            Check("evidence-pack", "Evidence Pack", packVerification.PackageHashValid && packVerification.LedgerChainValid && packVerification.PassedBlocks == 14, $"sha256={pack.PackageSha256}"),
            Check("web-pages", "Telas críticas", pages.All(x => x.Exists && x.Bytes > 0), $"ready={pages.Count(x => x.Exists)}/{pages.Count}"),
            Check("web-assets", "Assets críticos", assets.All(x => x.Exists && x.Bytes > 0), $"ready={assets.Count(x => x.Exists)}/{assets.Count}"),
            Check("ledger", "Evidence Ledger", ledger.Valid, $"checked={ledger.CheckedEvents}"),
            Check("integration-governance", "Governança de integrações", integrationGovernanceValid, $"integrations={integrationItems.Count};homologated={integrationItems.Count(x => x.Status == "homologated")};production={integrationItems.Count(x => x.Status == "production_enabled")}"),
            Check("blocker-disclosure", "Bloqueadores não-código explícitos", blockers.Any(x => x.Id == "HAB-AT-29"), $"blockers={blockers.Count};critical={blockers.Count(x => x.Severity == "critical")}")
        };

        var ready = checks.All(x => x.Passed);
        var result = new PresentationPreparationResult(
            Guid.NewGuid(),
            ready ? "ready" : "attention",
            ready,
            tenant.InstitutionId,
            tenant.HealthUnitId,
            actor,
            scenario.Id,
            run.Id,
            pack.Payload.PackId,
            pack.PackageSha256,
            run.PassedBlocks,
            run.TotalBlocks,
            run.OverallScore,
            pages,
            assets,
            checks,
            blockers,
            persistenceRuntime.Configured ? "durable-postgresql-configured" : "poc-memory-fallback",
            new[]
            {
                "Apresentação pronta não significa produção pronta.",
                "HAB-AT-29 continua dependência documental crítica.",
                "Integrações externas continuam condicionadas a credencial/homologação oficial.",
                "O Dossiê da Banca inclui Evidence Pack, identidade de build e manifesto dos artefatos runtime; isso ainda não equivale a SBOM/attestation/release assinada.",
                "O Kit de Contingência é uma cópia estática verificável para a banca; não é backup ou DR produtivo.",
                persistenceRuntime.Configured
                    ? "PostgreSQL está configurado nesta instância; Production Gates de backup/PITR/DR continuam separados."
                    : "Esta instância usa fallback em memória; a fundação PostgreSQL/recovery/messaging é validada separadamente no CI."
            },
            DateTimeOffset.UtcNow);

        lock (_gate) _latest = result;
        evidence.Append(new CreateEvidenceEventRequest(
            actor,
            "poc.presentation.prepare",
            $"presentation:{result.Id}",
            "POC-ALL",
            $"ready={ready};blocks={run.PassedBlocks}/{run.TotalBlocks};pack={pack.PackageSha256};pages={pages.Count(x => x.Exists)}/{pages.Count}",
            "presentation-preflight"));
        demo.AuditExternal(actor, "poc.presentation.prepare", $"presentation:{result.Id}", result.Status);
        return result;
    }

    private PresentationWebAsset CheckWebAsset(string relativePath)
    {
        var root = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(root)) return new PresentationWebAsset(relativePath, false, 0);
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var file = new FileInfo(fullPath);
        return new PresentationWebAsset(relativePath, file.Exists, file.Exists ? file.Length : 0);
    }

    private static PresentationCheck Check(string id, string name, bool passed, string detail) => new(id, name, passed, passed ? "ok" : "attention", detail);
}

public sealed record PresentationPrepareRequest(string? Actor);
public sealed record PresentationWebAsset(string Path, bool Exists, long Bytes);
public sealed record PresentationCheck(string Id, string Name, bool Passed, string Status, string Detail);
public sealed record PresentationPreparationResult(
    Guid Id,
    string Status,
    bool Ready,
    string InstitutionId,
    string? HealthUnitId,
    string Actor,
    Guid ScenarioRunId,
    Guid VerificationRunId,
    Guid EvidencePackId,
    string EvidencePackSha256,
    int PassedBlocks,
    int TotalBlocks,
    int OverallScore,
    IReadOnlyList<PresentationWebAsset> Pages,
    IReadOnlyList<PresentationWebAsset> Assets,
    IReadOnlyList<PresentationCheck> Checks,
    IReadOnlyList<JundiaiNonCodeBlocker> NonCodeBlockers,
    string PersistenceMode,
    IReadOnlyList<string> Notes,
    DateTimeOffset PreparedAt);
