using System.Security.Cryptography;
using System.Text;

namespace Jundiai.Api;

public static class EvidenceLedgerEndpoints
{
    public static IEndpointRouteBuilder MapEvidenceLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/evidence/ledger", (EvidenceLedgerStore store) => Results.Ok(store.Events()));
        endpoints.MapGet("/api/evidence/verify", (EvidenceLedgerStore store) => Results.Ok(store.Verify()));
        endpoints.MapPost("/api/evidence/events", (CreateEvidenceEventRequest request, EvidenceLedgerStore store, DemoStore demo) =>
        {
            var evt = store.Append(request);
            demo.AuditExternal(request.Actor, "evidence.append", $"evidence:{evt.Id}", evt.Hash);
            return Results.Created($"/api/evidence/events/{evt.Id}", evt);
        });
        endpoints.MapPost("/api/evidence/document-hash", (DocumentHashRequest request, EvidenceLedgerStore store) => Results.Ok(store.HashDocument(request)));
        endpoints.MapGet("/api/evidence/readiness", (EvidenceLedgerStore store) => Results.Ok(new
        {
            events = store.Events().Count,
            verification = store.Verify(),
            capabilities = new[] { "hash SHA-256", "cadeia append-only demonstrativa", "verificação de integridade", "hash de documento", "referência de requisito da POC" },
            disclaimer = "Mecanismo POC de integridade. Não substitui assinatura ICP-Brasil, carimbo do tempo ou armazenamento WORM homologado."
        }));
        return endpoints;
    }
}

public sealed class EvidenceLedgerStore
{
    private readonly object _gate = new();
    private readonly List<EvidenceLedgerEvent> _events = [];

    public EvidenceLedgerStore()
    {
        Append(new CreateEvidenceEventRequest("system", "poc.bootstrap", "RCE-008-2026", "POC", "Inicialização do ledger de evidências", "bootstrap"));
    }

    public IReadOnlyList<EvidenceLedgerEvent> Events()
    {
        lock (_gate) return _events.OrderBy(x => x.Sequence).ToList();
    }

    public EvidenceLedgerEvent Append(CreateEvidenceEventRequest request)
    {
        lock (_gate)
        {
            var previous = _events.LastOrDefault();
            var sequence = previous?.Sequence + 1 ?? 1;
            var previousHash = previous?.Hash ?? new string('0', 64);
            var occurredAt = DateTimeOffset.UtcNow;
            var evidenceType = request.EvidenceType?.Trim() ?? "event";
            var payload = Canonical(sequence, request.Actor, request.Action, request.Resource, request.RequirementId, request.Detail, evidenceType, occurredAt, previousHash);
            var hash = Sha256(payload);
            var evt = new EvidenceLedgerEvent(Guid.NewGuid(), sequence, request.Actor.Trim(), request.Action.Trim(), request.Resource.Trim(), request.RequirementId?.Trim(), request.Detail?.Trim(), evidenceType, occurredAt, previousHash, hash);
            _events.Add(evt);
            return evt;
        }
    }

    public EvidenceVerification Verify()
    {
        lock (_gate)
        {
            var previous = new string('0', 64);
            for (var i = 0; i < _events.Count; i++)
            {
                var evt = _events[i];
                var expected = Sha256(Canonical(evt.Sequence, evt.Actor, evt.Action, evt.Resource, evt.RequirementId, evt.Detail, evt.EvidenceType, evt.OccurredAt, previous));
                if (!string.Equals(evt.PreviousHash, previous, StringComparison.Ordinal) || !string.Equals(evt.Hash, expected, StringComparison.Ordinal))
                    return new(false, i + 1, evt.Sequence, evt.Id, "Divergência de hash ou encadeamento detectada.", DateTimeOffset.UtcNow);
                previous = evt.Hash;
            }
            return new(true, _events.Count, null, null, "Cadeia íntegra na memória desta instância.", DateTimeOffset.UtcNow);
        }
    }

    public DocumentIntegrityArtifact HashDocument(DocumentHashRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content)) throw new ArgumentException("Conteúdo obrigatório para geração do hash.");
        var hash = Sha256(request.Content);
        return new DocumentIntegrityArtifact(Guid.NewGuid(), request.Name?.Trim() ?? "documento", hash, "SHA-256", request.Reference?.Trim(), DateTimeOffset.UtcNow,
            "Hash demonstrativo de integridade; assinatura digital ICP-Brasil e carimbo do tempo são integrações separadas.");
    }

    private static string Canonical(long sequence, string actor, string action, string resource, string? requirementId, string? detail, string evidenceType, DateTimeOffset occurredAt, string previousHash) =>
        string.Join('|', new[]
        {
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Clean(actor),
            Clean(action),
            Clean(resource),
            Clean(requirementId),
            Clean(detail),
            Clean(evidenceType),
            occurredAt.ToUniversalTime().ToString("O"),
            previousHash
        });

    private static string Clean(string? value) => (value ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record EvidenceLedgerEvent(Guid Id, long Sequence, string Actor, string Action, string Resource, string? RequirementId, string? Detail, string EvidenceType, DateTimeOffset OccurredAt, string PreviousHash, string Hash);
public sealed record EvidenceVerification(bool Valid, int CheckedEvents, long? FailedSequence, Guid? FailedEventId, string Message, DateTimeOffset VerifiedAt);
public sealed record CreateEvidenceEventRequest(string Actor, string Action, string Resource, string? RequirementId, string? Detail, string? EvidenceType);
public sealed record DocumentHashRequest(string? Name, string Content, string? Reference);
public sealed record DocumentIntegrityArtifact(Guid Id, string Name, string Sha256, string Algorithm, string? Reference, DateTimeOffset GeneratedAt, string Disclaimer);
