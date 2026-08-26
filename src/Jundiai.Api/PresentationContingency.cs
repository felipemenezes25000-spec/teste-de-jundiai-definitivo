using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jundiai.Api;

public static class PresentationContingencyEndpoints
{
    public static IEndpointRouteBuilder MapPresentationContingencyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/poc/contingency-bundle", async (
            GenerateContingencyBundleRequest request,
            HttpContext context,
            PresentationContingencyStore store,
            CancellationToken ct) =>
        {
            var artifact = await store.GenerateAsync(request, TenantContextMiddleware.GetScope(context), ct);
            return Results.Created($"/api/poc/contingency-bundle/{artifact.VerificationCode}", artifact);
        });

        endpoints.MapGet("/api/poc/contingency-bundle/latest", (PresentationContingencyStore store) =>
            store.Latest() is { } artifact ? Results.Ok(artifact) : Results.NotFound());

        endpoints.MapGet("/api/poc/contingency-bundle/{code}", (string code, PresentationContingencyStore store) =>
            store.Find(code) is { } artifact ? Results.Ok(artifact) : Results.NotFound());

        endpoints.MapGet("/api/poc/contingency-bundle/{code}/verify", (string code, PresentationContingencyStore store) =>
            store.Verify(code) is { } verification ? Results.Ok(verification) : Results.NotFound());

        endpoints.MapGet("/api/poc/contingency-bundle/{code}/download", (string code, PresentationContingencyStore store) =>
        {
            var file = store.Download(code);
            return file is null
                ? Results.NotFound()
                : Results.File(file.Value.Bytes, "application/zip", file.Value.FileName);
        });

        endpoints.MapGet("/api/poc/contingency-bundles", (PresentationContingencyStore store) => Results.Ok(store.History()));
        return endpoints;
    }
}

public sealed class PresentationContingencyStore(
    PresentationDossierStore dossierStore,
    EvidenceLedgerStore evidence,
    DemoStore demo)
{
    private readonly object _gate = new();
    private readonly List<ContingencyBundleState> _history = [];

    public ContingencyBundleArtifact? Latest()
    {
        lock (_gate) return _history.OrderByDescending(x => x.Artifact.GeneratedAt).FirstOrDefault()?.Artifact;
    }

    public ContingencyBundleArtifact? Find(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        lock (_gate)
            return _history.FirstOrDefault(x => string.Equals(x.Artifact.VerificationCode, code.Trim(), StringComparison.OrdinalIgnoreCase))?.Artifact;
    }

    public IReadOnlyList<ContingencyBundleArtifact> History()
    {
        lock (_gate) return _history.Select(x => x.Artifact).OrderByDescending(x => x.GeneratedAt).ToList();
    }

    public async Task<ContingencyBundleArtifact> GenerateAsync(GenerateContingencyBundleRequest request, TenantScope tenant, CancellationToken ct)
    {
        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "poc.contingency" : request.Actor.Trim();
        var refresh = request.RefreshDossier ?? true;
        var dossier = refresh || dossierStore.Latest() is null
            ? await dossierStore.GenerateAsync(new GeneratePresentationDossierRequest(actor, true), tenant, ct)
            : dossierStore.Latest()!;
        var dossierVerification = dossierStore.Verify(dossier);
        if (!dossierVerification.IntegrityReady)
            throw new InvalidOperationException("Dossiê da banca não está íntegro; o kit de contingência não será gerado.");

        var generatedAt = DateTimeOffset.UtcNow;
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["dossier.json"] = JsonBytes(dossier),
            ["evidence-pack.json"] = JsonBytes(dossier.Payload.EvidencePack),
            ["release-provenance.json"] = JsonBytes(dossier.Payload.Release),
            ["verification.txt"] = TextBytes(BuildVerificationText(dossier, dossierVerification)),
            ["presentation-summary.html"] = TextBytes(BuildStandaloneHtml(dossier, dossierVerification))
        };

        var fileDigests = entries.Select(x => DigestEntry(x.Key, x.Value)).ToList();
        var manifestPayload = new ContingencyManifestPayload(
            "JUNDIAI-RCE-008-2026-CONTINGENCY",
            "2026.08.26-contingency-v1",
            Guid.NewGuid(),
            generatedAt,
            actor,
            tenant.InstitutionId,
            tenant.HealthUnitId,
            dossier.VerificationCode,
            dossier.DossierSha256,
            dossier.Payload.EvidencePack.PackageSha256,
            dossier.Payload.Release.ManifestSha256,
            dossier.Payload.Build.SourceRevision,
            dossier.Payload.Build.ValidationRunId,
            fileDigests,
            new[]
            {
                "Kit para contingência de apresentação; os arquivos JSON e HTML funcionam como evidência estática mesmo sem a aplicação online.",
                "manifest.json permite conferir cada arquivo do kit por SHA-256.",
                "presentation-summary.html é autocontido e não faz chamadas de rede.",
                "O ZIP não substitui backup produtivo, release assinada, SBOM formal, homologação ou documentação de habilitação."
            });
        var manifestJson = JsonSerializer.Serialize(manifestPayload, PocEvidencePackStore.JsonOptions);
        var manifestSha = DurableJson.Sha256Canonical(manifestJson);
        var manifestArtifact = new ContingencyManifestArtifact(manifestPayload, manifestSha, "SHA-256", "durable-json-canonical-v1");
        entries["manifest.json"] = JsonBytes(manifestArtifact);

        var zipBytes = BuildDeterministicZip(entries);
        var zipSha = Sha256(zipBytes);
        var code = BundleCode(manifestSha);
        var artifact = new ContingencyBundleArtifact(
            manifestPayload.BundleId,
            code,
            generatedAt,
            dossier.VerificationCode,
            manifestSha,
            zipSha,
            zipBytes.LongLength,
            entries.Count,
            dossier.Payload.Build.SourceRevision,
            dossier.Payload.Build.ValidationRunId,
            $"/api/poc/contingency-bundle/{code}/download",
            "Kit estático verificável; não é backup produtivo nem release assinada.");
        var state = new ContingencyBundleState(artifact, manifestArtifact, dossier, entries, zipBytes);

        lock (_gate)
        {
            _history.Add(state);
            if (_history.Count > 10) _history.RemoveRange(0, _history.Count - 10);
        }

        evidence.Append(new CreateEvidenceEventRequest(
            actor,
            "poc.contingency.generate",
            $"contingency:{artifact.BundleId}",
            "POC-ALL",
            $"code={code};manifest={manifestSha};zip={zipSha};files={entries.Count};dossier={dossier.VerificationCode}",
            "presentation-contingency"));
        demo.AuditExternal(actor, "poc.contingency.generate", $"contingency:{artifact.BundleId}", $"{code};{zipSha}");
        return artifact;
    }

    public ContingencyBundleVerification? Verify(string code)
    {
        ContingencyBundleState? state;
        lock (_gate)
            state = _history.FirstOrDefault(x => string.Equals(x.Artifact.VerificationCode, code.Trim(), StringComparison.OrdinalIgnoreCase));
        if (state is null) return null;

        var manifestPayloadJson = JsonSerializer.Serialize(state.Manifest.Payload, PocEvidencePackStore.JsonOptions);
        var calculatedManifest = DurableJson.Sha256Canonical(manifestPayloadJson);
        var manifestValid = string.Equals(calculatedManifest, state.Manifest.ManifestSha256, StringComparison.OrdinalIgnoreCase);
        var codeValid = string.Equals(BundleCode(calculatedManifest), state.Artifact.VerificationCode, StringComparison.OrdinalIgnoreCase);
        var entryChecks = state.Manifest.Payload.Files.Select(expected =>
        {
            var exists = state.Entries.TryGetValue(expected.Name, out var bytes);
            var actualHash = exists ? Sha256(bytes!) : null;
            var actualBytes = exists ? bytes!.LongLength : 0;
            return new ContingencyFileVerification(
                expected.Name,
                expected.Bytes,
                actualBytes,
                expected.Sha256,
                actualHash,
                exists && expected.Bytes == actualBytes && string.Equals(expected.Sha256, actualHash, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        var entriesValid = entryChecks.All(x => x.Valid);
        var zipHash = Sha256(state.ZipBytes);
        var zipValid = string.Equals(zipHash, state.Artifact.ZipSha256, StringComparison.OrdinalIgnoreCase);
        var dossierVerification = dossierStore.Verify(state.Dossier);
        var ready = manifestValid && codeValid && entriesValid && zipValid && dossierVerification.IntegrityReady;

        return new ContingencyBundleVerification(
            state.Artifact.BundleId,
            state.Artifact.VerificationCode,
            ready,
            manifestValid,
            codeValid,
            entriesValid,
            zipValid,
            dossierVerification.IntegrityReady,
            calculatedManifest,
            state.Manifest.ManifestSha256,
            zipHash,
            state.Artifact.ZipSha256,
            entryChecks,
            DateTimeOffset.UtcNow,
            ready
                ? "Kit, manifesto, arquivos, ZIP e Dossiê conferem. Use o HTML/JSON como contingência estática da apresentação."
                : "O kit possui divergência de integridade; gere uma nova cópia antes da apresentação.");
    }

    public (byte[] Bytes, string FileName)? Download(string code)
    {
        lock (_gate)
        {
            var state = _history.FirstOrDefault(x => string.Equals(x.Artifact.VerificationCode, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (state is null) return null;
            var safe = state.Artifact.VerificationCode.Replace("-", string.Empty, StringComparison.Ordinal);
            return (state.ZipBytes.ToArray(), $"jundiai-rce-008-2026-contingencia-{safe}.zip");
        }
    }

    private static byte[] BuildDeterministicZip(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in entries.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open();
                stream.Write(pair.Value, 0, pair.Value.Length);
            }
        }
        return memory.ToArray();
    }

    private static ContingencyFileDigest DigestEntry(string name, byte[] bytes) =>
        new(name, bytes.LongLength, Sha256(bytes));

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] JsonBytes(object value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, PocEvidencePackStore.JsonOptions));

    private static byte[] TextBytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string BundleCode(string sha256)
    {
        var prefix = sha256[..12].ToUpperInvariant();
        return $"KIT-{prefix[..4]}-{prefix[4..8]}-{prefix[8..12]}";
    }

    private static string BuildVerificationText(PresentationDossierArtifact dossier, PresentationDossierVerification verification) =>
        $"""
        JUNDIAI HEALTHOS — RCE 008/2026 — KIT DE CONTINGENCIA
        Gerado a partir do Dossie: {dossier.VerificationCode}
        Dossie SHA-256: {dossier.DossierSha256}
        Evidence Pack SHA-256: {dossier.Payload.EvidencePack.PackageSha256}
        Release Manifest SHA-256: {dossier.Payload.Release.ManifestSha256}
        Source Revision: {dossier.Payload.Build.SourceRevision ?? "nao-injetada"}
        Validation Run: {dossier.Payload.Build.ValidationRunId ?? "nao-informado"}
        Blocos: {verification.PassedBlocks}/{verification.TotalBlocks}
        Integridade do Dossie: {(verification.IntegrityReady ? "APROVADA" : "ATENCAO")}

        Este arquivo e demonstrativo. Nao equivale a homologacao, assinatura ICP-Brasil,
        carimbo do tempo oficial, SBOM formal, release assinada ou autorizacao de producao.
        """;

    private static string BuildStandaloneHtml(PresentationDossierArtifact dossier, PresentationDossierVerification verification)
    {
        var p = dossier.Payload;
        var blockers = string.Join("", p.Preflight.NonCodeBlockers.Select(x => $"<li><strong>{H(x.Id)}</strong> — {H(x.Description)} ({H(x.Severity)})</li>"));
        var checks = string.Join("", p.Preflight.Checks.Select(x => $"<li>{(x.Passed ? "✓" : "!")} <strong>{H(x.Name)}</strong> — {H(x.Detail)}</li>"));
        var blocks = string.Join("", p.EvidencePack.Payload.Blocks.Select(x => $"<tr><td>{x.Block:00}</td><td>{H(x.Name)}</td><td>{(x.Passed ? "PASSOU" : "ATENÇÃO")}</td><td>{x.Score}%</td></tr>"));
        return $$"""
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Jundiai HealthOS - Contingencia</title>
        <style>body{font-family:Arial,sans-serif;margin:32px;color:#172333}h1{margin-bottom:4px}.code{font:700 24px monospace;color:#17466f}.ok{color:#147245}.hash{font:12px monospace;word-break:break-all;background:#f4f6f8;padding:9px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccd4dd;padding:7px;text-align:left}section{margin:28px 0}small{color:#657485}@media print{body{margin:10mm}}</style></head><body>
        <p>RCE 008/2026 · snapshot estatico de contingencia</p><h1>Jundiai HealthOS</h1><div class="code">{{H(dossier.VerificationCode)}}</div>
        <p class="ok"><strong>Integridade do dossie: {{(verification.IntegrityReady ? "APROVADA" : "ATENCAO")}}</strong> · 14 blocos: {{verification.PassedBlocks}}/{{verification.TotalBlocks}}</p>
        <section><h2>Hashes</h2><p>Dossie</p><div class="hash">{{H(dossier.DossierSha256)}}</div><p>Evidence Pack</p><div class="hash">{{H(p.EvidencePack.PackageSha256)}}</div><p>Release manifest</p><div class="hash">{{H(p.Release.ManifestSha256)}}</div></section>
        <section><h2>Build</h2><p>Revision: <strong>{{H(p.Build.SourceRevision ?? "nao-injetada")}}</strong><br>Validation run: <strong>{{H(p.Build.ValidationRunId ?? "nao-informado")}}</strong><br>Repository: {{H(p.Build.Repository)}}</p></section>
        <section><h2>14 blocos</h2><table><thead><tr><th>#</th><th>Bloco</th><th>Estado</th><th>Score</th></tr></thead><tbody>{{blocks}}</tbody></table></section>
        <section><h2>Preflight</h2><ul>{{checks}}</ul></section><section><h2>Bloqueadores nao-codigo</h2><ul>{{blockers}}</ul></section>
        <section><small>Arquivo autocontido para contingencia de apresentacao. Nao substitui homologacao, habilitacao, release assinada, SBOM, ICP-Brasil, carimbo oficial ou autorizacao de producao.</small></section>
        </body></html>
        """;
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}

public sealed record GenerateContingencyBundleRequest(string? Actor, bool? RefreshDossier);
public sealed record ContingencyManifestPayload(
    string BundleType,
    string Version,
    Guid BundleId,
    DateTimeOffset GeneratedAt,
    string Actor,
    string InstitutionId,
    string? HealthUnitId,
    string DossierVerificationCode,
    string DossierSha256,
    string EvidencePackSha256,
    string ReleaseManifestSha256,
    string? SourceRevision,
    string? ValidationRunId,
    IReadOnlyList<ContingencyFileDigest> Files,
    IReadOnlyList<string> Notes);
public sealed record ContingencyManifestArtifact(
    ContingencyManifestPayload Payload,
    string ManifestSha256,
    string HashAlgorithm,
    string Canonicalization);
public sealed record ContingencyFileDigest(string Name, long Bytes, string Sha256);
public sealed record ContingencyBundleArtifact(
    Guid BundleId,
    string VerificationCode,
    DateTimeOffset GeneratedAt,
    string DossierVerificationCode,
    string ManifestSha256,
    string ZipSha256,
    long ZipBytes,
    int FileCount,
    string? SourceRevision,
    string? ValidationRunId,
    string DownloadUrl,
    string Note);
public sealed record ContingencyFileVerification(
    string Name,
    long ExpectedBytes,
    long CurrentBytes,
    string ExpectedSha256,
    string? CurrentSha256,
    bool Valid);
public sealed record ContingencyBundleVerification(
    Guid BundleId,
    string VerificationCode,
    bool IntegrityReady,
    bool ManifestHashValid,
    bool VerificationCodeValid,
    bool EntriesValid,
    bool ZipHashValid,
    bool DossierIntegrityReady,
    string CalculatedManifestSha256,
    string ExpectedManifestSha256,
    string CalculatedZipSha256,
    string ExpectedZipSha256,
    IReadOnlyList<ContingencyFileVerification> Files,
    DateTimeOffset VerifiedAt,
    string Note);
internal sealed record ContingencyBundleState(
    ContingencyBundleArtifact Artifact,
    ContingencyManifestArtifact Manifest,
    PresentationDossierArtifact Dossier,
    IReadOnlyDictionary<string, byte[]> Entries,
    byte[] ZipBytes);
