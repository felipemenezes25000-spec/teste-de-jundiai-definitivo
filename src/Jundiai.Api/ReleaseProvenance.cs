using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jundiai.Api;

public static class ReleaseProvenanceEndpoints
{
    public static IEndpointRouteBuilder MapReleaseProvenanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/release-provenance", (ReleaseProvenanceStore store) => Results.Ok(store.Snapshot()));
        endpoints.MapGet("/api/platform/release-provenance/verify", (ReleaseProvenanceStore store) =>
        {
            var artifact = store.Snapshot();
            return Results.Ok(store.Verify(artifact));
        });
        return endpoints;
    }
}

public sealed class ReleaseProvenanceStore(BuildIdentityStore buildIdentity)
{
    private static readonly string[] RuntimeArtifactNames =
    [
        "Jundiai.Api.dll",
        "Jundiai.Api.deps.json",
        "Jundiai.Api.runtimeconfig.json"
    ];

    public ReleaseProvenanceArtifact Snapshot()
    {
        var root = AppContext.BaseDirectory;
        var files = RuntimeArtifactNames.Select(name => Digest(Path.Combine(root, name), name)).ToList();
        var depsPath = Path.Combine(root, "Jundiai.Api.deps.json");
        var libraries = ReadLibraries(depsPath);
        var libraryListJson = JsonSerializer.Serialize(libraries, PocEvidencePackStore.JsonOptions);
        var payload = new ReleaseProvenancePayload(
            "JUNDIAI-RCE-008-2026-RUNTIME-ARTIFACT",
            "2026.08.26-release-provenance-v1",
            DateTimeOffset.UtcNow,
            buildIdentity.Snapshot(),
            files,
            libraries,
            DurableJson.Sha256Canonical(libraryListJson),
            files.All(x => x.Exists),
            new[]
            {
                "Manifesto calculado sobre os artefatos carregados pelo processo atual.",
                "Os hashes permitem identificar mudança de bytes nos artefatos listados.",
                "A lista de libraries é extraída do .deps.json do runtime e não é apresentada como SBOM formal.",
                "Não há assinatura criptográfica externa, transparency log ou attestation neste estágio."
            });
        var payloadJson = JsonSerializer.Serialize(payload, PocEvidencePackStore.JsonOptions);
        return new ReleaseProvenanceArtifact(payload, DurableJson.Sha256Canonical(payloadJson), "SHA-256", "durable-json-canonical-v1");
    }

    public ReleaseProvenanceVerification Verify(ReleaseProvenanceArtifact artifact)
    {
        var payloadJson = JsonSerializer.Serialize(artifact.Payload, PocEvidencePackStore.JsonOptions);
        var manifestHash = DurableJson.Sha256Canonical(payloadJson);
        var manifestHashValid = string.Equals(manifestHash, artifact.ManifestSha256, StringComparison.OrdinalIgnoreCase);
        var currentFiles = artifact.Payload.Files.Select(expected =>
        {
            var current = Digest(Path.Combine(AppContext.BaseDirectory, expected.Name), expected.Name);
            return new ReleaseArtifactVerification(
                expected.Name,
                expected.Exists,
                current.Exists,
                expected.Bytes,
                current.Bytes,
                expected.Sha256,
                current.Sha256,
                expected.Exists == current.Exists &&
                expected.Bytes == current.Bytes &&
                string.Equals(expected.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        var filesValid = currentFiles.All(x => x.Valid);
        var buildRevisionBound = !string.IsNullOrWhiteSpace(artifact.Payload.Build.SourceRevision);
        return new ReleaseProvenanceVerification(
            manifestHashValid && filesValid,
            manifestHashValid,
            filesValid,
            buildRevisionBound,
            artifact.Payload.Build.SourceRevision,
            artifact.Payload.Build.ValidationRunId,
            manifestHash,
            artifact.ManifestSha256,
            currentFiles,
            DateTimeOffset.UtcNow,
            manifestHashValid && filesValid
                ? "Manifesto e bytes dos artefatos runtime conferem nesta instância. Isso ainda não é uma release assinada nem SBOM formal."
                : "Há divergência entre o manifesto e os bytes/runtime atuais; não use esta instância como evidência de release sem investigar.");
    }

    private static ReleaseArtifactDigest Digest(string path, string name)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return new ReleaseArtifactDigest(name, false, 0, null);
        using var stream = file.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new ReleaseArtifactDigest(name, true, file.Length, hash);
    }

    private static IReadOnlyList<string> ReadLibraries(string depsPath)
    {
        if (!File.Exists(depsPath)) return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(depsPath, Encoding.UTF8));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
                return Array.Empty<string>();
            return libraries.EnumerateObject()
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}

public sealed record ReleaseProvenancePayload(
    string ManifestType,
    string Version,
    DateTimeOffset GeneratedAt,
    BuildIdentitySnapshot Build,
    IReadOnlyList<ReleaseArtifactDigest> Files,
    IReadOnlyList<string> RuntimeLibraries,
    string RuntimeLibrariesSha256,
    bool RuntimeArtifactsComplete,
    IReadOnlyList<string> Notes);
public sealed record ReleaseProvenanceArtifact(
    ReleaseProvenancePayload Payload,
    string ManifestSha256,
    string HashAlgorithm,
    string Canonicalization);
public sealed record ReleaseArtifactDigest(string Name, bool Exists, long Bytes, string? Sha256);
public sealed record ReleaseArtifactVerification(
    string Name,
    bool ExpectedExists,
    bool CurrentExists,
    long ExpectedBytes,
    long CurrentBytes,
    string? ExpectedSha256,
    string? CurrentSha256,
    bool Valid);
public sealed record ReleaseProvenanceVerification(
    bool IntegrityReady,
    bool ManifestHashValid,
    bool RuntimeFilesValid,
    bool BuildRevisionBound,
    string? SourceRevision,
    string? ValidationRunId,
    string CalculatedManifestSha256,
    string ExpectedManifestSha256,
    IReadOnlyList<ReleaseArtifactVerification> Files,
    DateTimeOffset VerifiedAt,
    string Note);
