using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jundiai.Api;

public static class PrivacyGovernanceEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyGovernanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit/privacy/readiness", (PrivacyGovernanceStore store) => Results.Ok(store.Readiness()));
        endpoints.MapGet("/api/audit/privacy/policies", (PrivacyGovernanceStore store) => Results.Ok(store.Policies()));
        endpoints.MapGet("/api/audit/privacy/break-glass", (PrivacyGovernanceStore store) => Results.Ok(store.BreakGlassSessions()));

        endpoints.MapPost("/api/audit/privacy/break-glass", (
            CreateBreakGlassRequest request,
            PrivacyGovernanceStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var grant = store.OpenBreakGlass(request, demo);
            demo.AuditExternal(request.Actor, "privacy.break-glass.open", $"citizen:{request.CitizenId}", request.Reason);
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "privacy.break-glass.open", $"break-glass:{grant.Id}", "LGPD-01", request.Reason, "privacy-control"));
            return Results.Created($"/api/audit/privacy/break-glass/{grant.Id}", grant);
        });

        endpoints.MapPost("/api/audit/privacy/break-glass/{id:guid}/revoke", (
            Guid id,
            RevokeBreakGlassRequest request,
            PrivacyGovernanceStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var grant = store.RevokeBreakGlass(id, request);
            demo.AuditExternal(request.Actor, "privacy.break-glass.revoke", $"break-glass:{id}", request.Reason);
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "privacy.break-glass.revoke", $"break-glass:{id}", "LGPD-01", request.Reason, "privacy-control"));
            return Results.Ok(grant);
        });

        endpoints.MapPost("/api/audit/privacy/subject-export", (
            DataSubjectExportRequest request,
            PrivacyGovernanceStore privacy,
            DemoStore demo,
            ClinicalDocumentStore documents,
            DiagnosticsAdvancedStore diagnostics,
            TelemedicineStore telemedicine,
            AiGovernanceStore ai,
            EvidenceLedgerStore evidence) =>
        {
            var artifact = privacy.BuildSubjectExport(request, demo, documents, diagnostics, telemedicine, ai);
            demo.AuditExternal(request.Actor, "privacy.subject-export.generate", $"citizen:{request.CitizenId}", request.Purpose);
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "privacy.subject-export.generate", $"privacy-export:{artifact.Id}", "LGPD-01", artifact.Sha256, "data-subject-export"));
            return Results.Ok(artifact);
        });

        return endpoints;
    }
}

public sealed class PrivacyGovernanceStore
{
    private readonly ConcurrentDictionary<Guid, BreakGlassGrant> _breakGlass = new();
    private readonly IReadOnlyList<PrivacyPolicyRule> _policies =
    [
        new("PURPOSE-01", "Finalidade explícita", "Acesso sensível deve ter finalidade assistencial, regulatória, auditoria ou obrigação legal compatível."),
        new("MIN-01", "Minimização", "Expor apenas campos necessários ao papel e ao fluxo atual; evitar dados clínicos em dashboards gerenciais sem necessidade."),
        new("BREAKGLASS-01", "Acesso emergencial", "Break-glass exige motivo, ator, prazo curto, trilha de auditoria e revogação."),
        new("EXPORT-01", "Direito do titular", "Exportações devem ser rastreadas, ter integridade verificável e usar canal seguro na produção."),
        new("RETENTION-01", "Retenção", "Prazo e descarte devem seguir obrigação legal/contratual aplicável ao tipo documental; não há exclusão automática na POC."),
        new("AI-PRIV-01", "IA e dados", "Casos de uso de IA registram hashes e política; produção deve aplicar minimização, base legal e controles do provedor contratado.")
    ];

    public IReadOnlyList<PrivacyPolicyRule> Policies() => _policies;

    public IReadOnlyList<BreakGlassGrant> BreakGlassSessions() => _breakGlass.Values
        .OrderByDescending(x => x.OpenedAt)
        .ToList();

    public BreakGlassGrant OpenBreakGlass(CreateBreakGlassRequest request, DemoStore demo)
    {
        if (demo.Citizen(request.CitizenId) is null) throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Ator e motivo são obrigatórios.");
        var minutes = Math.Clamp(request.Minutes <= 0 ? 15 : request.Minutes, 5, 60);
        var now = DateTimeOffset.UtcNow;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var grant = new BreakGlassGrant(
            Guid.NewGuid(), request.CitizenId, request.Actor.Trim(), request.Reason.Trim(),
            Sha256(token), "active", now, now.AddMinutes(minutes), null, null, null);
        _breakGlass[grant.Id] = grant;
        return grant;
    }

    public BreakGlassGrant RevokeBreakGlass(Guid id, RevokeBreakGlassRequest request)
    {
        if (!_breakGlass.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (current.Status != "active") return current;
        var updated = current with
        {
            Status = "revoked",
            RevokedAt = DateTimeOffset.UtcNow,
            RevokedBy = request.Actor.Trim(),
            RevocationReason = request.Reason?.Trim() ?? "revogação manual"
        };
        _breakGlass[id] = updated;
        return updated;
    }

    public DataSubjectExportArtifact BuildSubjectExport(
        DataSubjectExportRequest request,
        DemoStore demo,
        ClinicalDocumentStore documents,
        DiagnosticsAdvancedStore diagnostics,
        TelemedicineStore telemedicine,
        AiGovernanceStore ai)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Purpose))
            throw new ArgumentException("Ator e finalidade são obrigatórios.");

        var payload = new
        {
            generatedFor = citizen.Name,
            citizen,
            immunizations = demo.Immunizations(citizen.Id),
            regulation = demo.Regulation().Where(x => x.CitizenId == citizen.Id).ToArray(),
            documents = documents.Documents(citizen.Id),
            diagnostics = diagnostics.Orders(citizen.Id),
            telemedicine = telemedicine.Sessions().Where(x => x.CitizenId == citizen.Id).ToArray(),
            aiRecords = ai.Decisions(citizen.Id).Select(x => new
            {
                x.Id, x.UseCase, x.Model, x.ModelVersion, x.PromptVersion, x.RiskClass,
                x.InputHash, x.OutputHash, x.HumanReviewRequired, x.ReviewStatus,
                x.Reviewer, x.ReviewerCouncil, x.CreatedAt, x.ReviewedAt
            }).ToArray(),
            generatedAt = DateTimeOffset.UtcNow,
            disclaimer = "Artefato POC. Entrega real ao titular exige autenticação forte, validação de identidade e canal seguro."
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new DataSubjectExportArtifact(
            Guid.NewGuid(), citizen.Id, citizen.Name, request.Purpose.Trim(), request.Actor.Trim(),
            "application/json", Sha256(json), json, DateTimeOffset.UtcNow);
    }

    public object Readiness()
    {
        var grants = BreakGlassSessions();
        return new
        {
            policies = _policies.Count,
            breakGlass = new
            {
                total = grants.Count,
                active = grants.Count(x => x.Status == "active" && x.ExpiresAt > DateTimeOffset.UtcNow),
                expired = grants.Count(x => x.Status == "active" && x.ExpiresAt <= DateTimeOffset.UtcNow),
                revoked = grants.Count(x => x.Status == "revoked")
            },
            capabilities = new[] { "purpose rules", "data minimization policy", "time-boxed break-glass", "audited revocation", "data-subject export with SHA-256", "AI privacy policy" },
            productionGates = new[] { "DPO/legal validation", "retention matrix", "identity proofing for subject requests", "secure delivery channel", "incident response workflow", "provider DPAs/contracts" }
        };
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record PrivacyPolicyRule(string Id, string Name, string Rule);
public sealed record CreateBreakGlassRequest(Guid CitizenId, string Actor, string Reason, int Minutes);
public sealed record RevokeBreakGlassRequest(string Actor, string? Reason);
public sealed record BreakGlassGrant(
    Guid Id, Guid CitizenId, string Actor, string Reason, string AccessTokenHash, string Status,
    DateTimeOffset OpenedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, string? RevokedBy, string? RevocationReason);
public sealed record DataSubjectExportRequest(Guid CitizenId, string Actor, string Purpose);
public sealed record DataSubjectExportArtifact(
    Guid Id, Guid CitizenId, string CitizenName, string Purpose, string Actor,
    string ContentType, string Sha256, string PayloadJson, DateTimeOffset GeneratedAt);
