using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jundiai.Api;

public static class AiGovernanceEndpoints
{
    public static IEndpointRouteBuilder MapAiGovernanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ai/decisions", (Guid? citizenId, AiGovernanceStore store) => Results.Ok(store.Decisions(citizenId)));
        endpoints.MapGet("/api/ai/decisions/{id:guid}", (Guid id, AiGovernanceStore store) =>
            store.Get(id) is { } decision ? Results.Ok(decision) : Results.NotFound());
        endpoints.MapPost("/api/ai/decisions", (CreateAiDecisionRequest request, AiGovernanceStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var decision = store.Record(request);
            demo.AuditExternal(request.Actor, "ai.decision.record", $"ai-decision:{decision.Id}", $"useCase={decision.UseCase};risk={decision.RiskClass}");
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "ai.decision.record", $"ai-decision:{decision.Id}", "POC-B14", decision.OutputHash, "ai-flight-recorder"));
            return Results.Created($"/api/ai/decisions/{decision.Id}", decision);
        });
        endpoints.MapPost("/api/ai/decisions/{id:guid}/review", (Guid id, ReviewAiDecisionRequest request, AiGovernanceStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var decision = store.Review(id, request);
            demo.AuditExternal(request.Reviewer, "ai.decision.review", $"ai-decision:{id}", $"outcome={request.Outcome}");
            evidence.Append(new CreateEvidenceEventRequest(request.Reviewer, "ai.decision.review", $"ai-decision:{id}", "POC-B14", request.Outcome, "human-review"));
            return Results.Ok(decision);
        });
        endpoints.MapGet("/api/ai/policies", (AiGovernanceStore store) => Results.Ok(store.Policies()));
        endpoints.MapGet("/api/ai/readiness", (AiGovernanceStore store) => Results.Ok(new
        {
            decisions = store.Decisions(null).Count,
            policies = store.Policies().Count,
            guardrails = new[]
            {
                "input/output hashing", "model and prompt version", "risk class", "mandatory human review by policy",
                "override reason", "no autonomous prescription", "no emergency routing delegated to generative AI", "audit/evidence hooks"
            },
            principle = "AI can assist; deterministic safety rules and licensed human review remain authoritative for clinical actions in this POC."
        }));
        return endpoints;
    }
}

public sealed class AiGovernanceStore
{
    private readonly ConcurrentDictionary<Guid, AiDecisionRecord> _decisions = new();
    private readonly IReadOnlyList<AiUseCasePolicy> _policies =
    [
        new("intelligent-access-summary", "clinical-support", "high", true, false, "IA pode resumir informações; red flags e encaminhamento de emergência ficam fora do modelo generativo."),
        new("clinical-note-draft", "clinical-support", "high", true, false, "Rascunho não entra no prontuário sem aceite do profissional."),
        new("coding-suggestion", "administrative-clinical", "medium", true, false, "Sugestão de CID/SIGTAP/CBO requer conferência humana e validação determinística."),
        new("population-risk-prioritization", "management", "medium", true, false, "Pode apoiar priorização; não nega atendimento nem altera fila crítica automaticamente."),
        new("document-summarization", "administrative", "low", false, false, "Sem decisão clínica autônoma."),
        new("autonomous-prescription", "clinical-action", "prohibited", true, true, "Uso proibido como decisão autônoma na plataforma.")
    ];

    public IReadOnlyList<AiDecisionRecord> Decisions(Guid? citizenId) => _decisions.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();
    public AiDecisionRecord? Get(Guid id) => _decisions.TryGetValue(id, out var item) ? item : null;
    public IReadOnlyList<AiUseCasePolicy> Policies() => _policies;

    public AiDecisionRecord Record(CreateAiDecisionRequest request)
    {
        var policy = _policies.FirstOrDefault(x => x.UseCase.Equals(request.UseCase.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Caso de uso de IA não possui política cadastrada.");
        if (policy.Prohibited) throw new InvalidOperationException("Caso de uso proibido pela política de governança da POC.");
        if (string.IsNullOrWhiteSpace(request.Model) || string.IsNullOrWhiteSpace(request.ModelVersion)) throw new ArgumentException("Modelo e versão são obrigatórios no Flight Recorder.");
        var inputHash = Sha256(request.Input ?? string.Empty);
        var outputHash = Sha256(request.Output ?? string.Empty);
        var record = new AiDecisionRecord(
            Guid.NewGuid(), request.CitizenId, request.UseCase.Trim(), request.Model.Trim(), request.ModelVersion.Trim(), request.PromptVersion?.Trim() ?? "unversioned",
            policy.RiskClass, inputHash, outputHash, request.Confidence, request.Actor.Trim(), policy.RequiresHumanReview,
            policy.RequiresHumanReview ? "pending" : "not_required", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _decisions[record.Id] = record;
        return record;
    }

    public AiDecisionRecord Review(Guid id, ReviewAiDecisionRequest request)
    {
        if (!_decisions.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (!current.HumanReviewRequired) throw new InvalidOperationException("Decisão não exige revisão humana pela política vigente.");
        var outcome = (request.Outcome ?? string.Empty).Trim().ToLowerInvariant();
        if (outcome is not ("approved" or "overridden" or "rejected")) throw new ArgumentException("Outcome deve ser approved, overridden ou rejected.");
        if (outcome != "approved" && string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Override/rejeição exige justificativa.");
        var updated = current with
        {
            ReviewStatus = outcome,
            Reviewer = request.Reviewer.Trim(),
            ReviewerCouncil = request.ReviewerCouncil?.Trim(),
            ReviewReason = request.Reason?.Trim(),
            ReviewedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _decisions[id] = updated;
        return updated;
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record AiUseCasePolicy(string UseCase, string Category, string RiskClass, bool RequiresHumanReview, bool Prohibited, string Rule);
public sealed record AiDecisionRecord(Guid Id, Guid? CitizenId, string UseCase, string Model, string ModelVersion, string PromptVersion, string RiskClass, string InputHash, string OutputHash, decimal? Confidence, string Actor, bool HumanReviewRequired, string ReviewStatus, string? Reviewer, string? ReviewerCouncil, string? ReviewReason, DateTimeOffset? ReviewedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreateAiDecisionRequest(Guid? CitizenId, string UseCase, string Model, string ModelVersion, string? PromptVersion, string? Input, string? Output, decimal? Confidence, string Actor);
public sealed record ReviewAiDecisionRequest(string Outcome, string Reviewer, string? ReviewerCouncil, string? Reason);
