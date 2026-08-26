using System.Text;
using System.Text.Json;

namespace Jundiai.Api;

public static class PresentationDossierEndpoints
{
    public static IEndpointRouteBuilder MapPresentationDossierEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/poc/dossier", async (
            GeneratePresentationDossierRequest request,
            HttpContext context,
            PresentationDossierStore store,
            CancellationToken ct) =>
        {
            var artifact = await store.GenerateAsync(request, TenantContextMiddleware.GetScope(context), ct);
            return Results.Created($"/api/poc/dossier/{artifact.VerificationCode}", artifact);
        });

        endpoints.MapGet("/api/poc/dossier/latest", (PresentationDossierStore store) =>
            store.Latest() is { } artifact ? Results.Ok(artifact) : Results.NotFound());

        endpoints.MapGet("/api/poc/dossier/{code}", (string code, PresentationDossierStore store) =>
            store.Find(code) is { } artifact ? Results.Ok(artifact) : Results.NotFound());

        endpoints.MapGet("/api/poc/dossier/{code}/verify", (string code, PresentationDossierStore store) =>
            store.Find(code) is { } artifact ? Results.Ok(store.Verify(artifact)) : Results.NotFound());

        endpoints.MapGet("/api/poc/dossier/{code}/export", (string code, PresentationDossierStore store) =>
        {
            var artifact = store.Find(code);
            if (artifact is null) return Results.NotFound();
            var json = JsonSerializer.Serialize(artifact, PocEvidencePackStore.JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var safeCode = artifact.VerificationCode.Replace("-", string.Empty, StringComparison.Ordinal);
            return Results.File(bytes, "application/json; charset=utf-8", $"jundiai-rce-008-2026-dossie-{safeCode}.json");
        });

        endpoints.MapGet("/api/poc/dossiers", (PresentationDossierStore store) => Results.Ok(store.History()));
        return endpoints;
    }
}

public sealed class PresentationDossierStore(
    PresentationPreparationStore presentation,
    PocEvidencePackStore evidencePack,
    BuildIdentityStore buildIdentity,
    ReleaseProvenanceStore releaseProvenance,
    EvidenceLedgerStore evidence,
    DemoStore demo)
{
    private readonly object _gate = new();
    private readonly List<PresentationDossierArtifact> _history = [];
    private PresentationDossierArtifact? _latest;

    public PresentationDossierArtifact? Latest()
    {
        lock (_gate) return _latest;
    }

    public PresentationDossierArtifact? Find(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        lock (_gate)
            return _history.FirstOrDefault(x => string.Equals(x.VerificationCode, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<PresentationDossierSummary> History()
    {
        lock (_gate)
            return _history
                .OrderByDescending(x => x.Payload.GeneratedAt)
                .Select(x => new PresentationDossierSummary(
                    x.Payload.DossierId,
                    x.VerificationCode,
                    x.Payload.GeneratedAt,
                    x.Payload.Preflight.Ready,
                    x.Payload.Preflight.PassedBlocks,
                    x.Payload.Preflight.TotalBlocks,
                    x.Payload.EvidencePack.PackageSha256,
                    x.Payload.Release.ManifestSha256,
                    x.DossierSha256,
                    x.Payload.Build.SourceRevision,
                    x.Payload.Build.ValidationRunId))
                .ToList();
    }

    public async Task<PresentationDossierArtifact> GenerateAsync(GeneratePresentationDossierRequest request, TenantScope tenant, CancellationToken ct)
    {
        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "poc.dossier" : request.Actor.Trim();
        var refresh = request.RefreshPreflight ?? true;
        var preflight = refresh || presentation.Latest() is null
            ? await presentation.PrepareAsync(new PresentationPrepareRequest(actor), tenant, ct)
            : presentation.Latest()!;
        var pack = evidencePack.Latest() ?? throw new InvalidOperationException("Evidence Pack não foi gerado pelo preflight.");
        var build = buildIdentity.Snapshot();
        var release = releaseProvenance.Snapshot();

        var payload = new PresentationDossierPayload(
            "JUNDIAI-RCE-008-2026-POC",
            "2026.08.26-dossier-v2",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            actor,
            tenant.InstitutionId,
            tenant.HealthUnitId,
            preflight,
            pack,
            build,
            release,
            new[]
            {
                "Este dossiê registra o estado demonstrável da POC no instante de geração.",
                "O SHA-256 do dossiê cobre preflight, Evidence Pack, identidade de build e manifesto dos artefatos runtime.",
                "Código de verificação é derivado do hash do dossiê e serve para conferência rápida dentro desta instância.",
                "O manifesto runtime hasheia DLL/deps/runtimeconfig, mas não é apresentado como SBOM formal nem attestation assinada.",
                "HAB-AT-29, homologações externas, operação 24x7 e demais obrigações não-código permanecem fora da capacidade de resolução do software.",
                "Integridade de POC não equivale a assinatura ICP-Brasil, carimbo do tempo oficial, homologação ou autorização de produção."
            });

        var payloadJson = JsonSerializer.Serialize(payload, PocEvidencePackStore.JsonOptions);
        var dossierSha = DurableJson.Sha256Canonical(payloadJson);
        var code = VerificationCode(dossierSha);
        var artifact = new PresentationDossierArtifact(payload, code, dossierSha, "SHA-256", "durable-json-canonical-v1");

        lock (_gate)
        {
            _latest = artifact;
            _history.Add(artifact);
            if (_history.Count > 20) _history.RemoveRange(0, _history.Count - 20);
        }

        evidence.Append(new CreateEvidenceEventRequest(
            actor,
            "poc.dossier.generate",
            $"dossier:{payload.DossierId}",
            "POC-ALL",
            $"code={code};sha256={dossierSha};pack={pack.PackageSha256};release={release.ManifestSha256};build={build.SourceRevision ?? "not-injected"}",
            "presentation-dossier"));
        demo.AuditExternal(actor, "poc.dossier.generate", $"dossier:{payload.DossierId}", $"{code};{dossierSha}");
        return artifact;
    }

    public PresentationDossierVerification Verify(PresentationDossierArtifact artifact)
    {
        var payloadJson = JsonSerializer.Serialize(artifact.Payload, PocEvidencePackStore.JsonOptions);
        var calculated = DurableJson.Sha256Canonical(payloadJson);
        var dossierHashValid = string.Equals(calculated, artifact.DossierSha256, StringComparison.OrdinalIgnoreCase);
        var codeValid = string.Equals(VerificationCode(calculated), artifact.VerificationCode, StringComparison.OrdinalIgnoreCase);
        var packVerification = evidencePack.Verify(artifact.Payload.EvidencePack);
        var releaseVerification = releaseProvenance.Verify(artifact.Payload.Release);
        var preflightReady = artifact.Payload.Preflight.Ready &&
                             artifact.Payload.Preflight.PassedBlocks == 14 &&
                             artifact.Payload.Preflight.TotalBlocks == 14;
        var ledger = evidence.Verify();
        var buildRevisionBound = !string.IsNullOrWhiteSpace(artifact.Payload.Build.SourceRevision);
        var integrityReady = dossierHashValid && codeValid && packVerification.DemonstrationIntegrityReady &&
                             releaseVerification.IntegrityReady && preflightReady && ledger.Valid;

        return new PresentationDossierVerification(
            artifact.Payload.DossierId,
            artifact.VerificationCode,
            integrityReady,
            dossierHashValid,
            codeValid,
            packVerification.PackageHashValid,
            packVerification.LedgerChainValid && ledger.Valid,
            releaseVerification.ManifestHashValid,
            releaseVerification.RuntimeFilesValid,
            preflightReady,
            buildRevisionBound,
            artifact.Payload.Build.SourceRevision,
            artifact.Payload.Build.ValidationRunId,
            calculated,
            artifact.DossierSha256,
            artifact.Payload.Preflight.PassedBlocks,
            artifact.Payload.Preflight.TotalBlocks,
            DateTimeOffset.UtcNow,
            integrityReady
                ? "Dossiê, Evidence Pack, ledger e artefatos runtime conferem nesta instância. Isso não substitui release assinada/SBOM/attestation produtivos."
                : "Uma ou mais provas do dossiê divergem; investigue antes de usar este artefato como evidência da apresentação.");
    }

    private static string VerificationCode(string sha256)
    {
        var prefix = sha256[..12].ToUpperInvariant();
        return $"JUN-{prefix[..4]}-{prefix[4..8]}-{prefix[8..12]}";
    }
}

public sealed record GeneratePresentationDossierRequest(string? Actor, bool? RefreshPreflight);
public sealed record PresentationDossierPayload(
    string ContractPack,
    string Version,
    Guid DossierId,
    DateTimeOffset GeneratedAt,
    string Actor,
    string InstitutionId,
    string? HealthUnitId,
    PresentationPreparationResult Preflight,
    PocEvidencePackArtifact EvidencePack,
    BuildIdentitySnapshot Build,
    ReleaseProvenanceArtifact Release,
    IReadOnlyList<string> Disclaimers);
public sealed record PresentationDossierArtifact(
    PresentationDossierPayload Payload,
    string VerificationCode,
    string DossierSha256,
    string HashAlgorithm,
    string Canonicalization);
public sealed record PresentationDossierSummary(
    Guid DossierId,
    string VerificationCode,
    DateTimeOffset GeneratedAt,
    bool Ready,
    int PassedBlocks,
    int TotalBlocks,
    string EvidencePackSha256,
    string ReleaseManifestSha256,
    string DossierSha256,
    string? SourceRevision,
    string? ValidationRunId);
public sealed record PresentationDossierVerification(
    Guid DossierId,
    string VerificationCode,
    bool IntegrityReady,
    bool DossierHashValid,
    bool VerificationCodeValid,
    bool EvidencePackHashValid,
    bool EvidenceLedgerValid,
    bool ReleaseManifestHashValid,
    bool RuntimeFilesValid,
    bool PreflightReady,
    bool BuildRevisionBound,
    string? SourceRevision,
    string? ValidationRunId,
    string CalculatedSha256,
    string ExpectedSha256,
    int PassedBlocks,
    int TotalBlocks,
    DateTimeOffset VerifiedAt,
    string Note);
