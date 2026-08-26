using System.Reflection;
using System.Runtime.InteropServices;

namespace Jundiai.Api;

public static class BuildIdentityEndpoints
{
    public static IEndpointRouteBuilder MapBuildIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/build-identity", (BuildIdentityStore store) => Results.Ok(store.Snapshot()));
        return endpoints;
    }
}

public sealed class BuildIdentityStore
{
    private readonly DateTimeOffset _processStartedAt = DateTimeOffset.UtcNow;

    public BuildIdentitySnapshot Snapshot()
    {
        var assembly = typeof(Program).Assembly;
        var assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assemblyVersion;

        var revisionFromEnvironment = FirstNonEmpty(
            Environment.GetEnvironmentVariable("JUNDIAI_BUILD_SHA"),
            Environment.GetEnvironmentVariable("GITHUB_SHA"),
            Environment.GetEnvironmentVariable("SOURCE_REVISION_ID"));
        var revision = revisionFromEnvironment ?? ExtractRevision(informationalVersion);
        var workflowRunId = FirstNonEmpty(
            Environment.GetEnvironmentVariable("JUNDIAI_VALIDATION_RUN"),
            Environment.GetEnvironmentVariable("GITHUB_RUN_ID"));
        var workflowRunNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
        var repository = FirstNonEmpty(
            Environment.GetEnvironmentVariable("JUNDIAI_REPOSITORY"),
            Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"),
            "felipemenezes25000-spec/teste-de-jundiai-definitivo");

        return new BuildIdentitySnapshot(
            "Jundiai HealthOS",
            "RCE 008/2026",
            assemblyVersion,
            informationalVersion,
            revision,
            !string.IsNullOrWhiteSpace(revisionFromEnvironment),
            repository,
            workflowRunId,
            workflowRunNumber,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            _processStartedAt,
            DateTimeOffset.UtcNow,
            revision is null
                ? "A revisão de código não foi injetada neste processo. Defina JUNDIAI_BUILD_SHA no build/deploy para vincular a instância a um commit específico."
                : "A revisão informada identifica o build desta instância; assinatura de release, SBOM e artefato imutável continuam Production Gates separados.");
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string? ExtractRevision(string informationalVersion)
    {
        var plus = informationalVersion.LastIndexOf('+');
        if (plus < 0 || plus == informationalVersion.Length - 1) return null;
        var candidate = informationalVersion[(plus + 1)..].Trim();
        return candidate.Length >= 7 && candidate.All(Uri.IsHexDigit) ? candidate : null;
    }
}

public sealed record BuildIdentitySnapshot(
    string Service,
    string Contract,
    string AssemblyVersion,
    string InformationalVersion,
    string? SourceRevision,
    bool SourceRevisionInjected,
    string Repository,
    string? ValidationRunId,
    string? ValidationRunNumber,
    string Runtime,
    string RuntimeIdentifier,
    string OperatingSystem,
    string Architecture,
    string Environment,
    DateTimeOffset ProcessStartedAt,
    DateTimeOffset GeneratedAt,
    string Note);
