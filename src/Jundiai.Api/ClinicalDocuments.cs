using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jundiai.Api;

public static class ClinicalDocumentEndpoints
{
    public static IEndpointRouteBuilder MapClinicalDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/documents", (Guid? citizenId, ClinicalDocumentStore store) => Results.Ok(store.Documents(citizenId)));
        endpoints.MapGet("/api/documents/{id:guid}", (Guid id, ClinicalDocumentStore store) =>
            store.Get(id) is { } doc ? Results.Ok(doc) : Results.NotFound());
        endpoints.MapPost("/api/documents", (CreateClinicalDocumentRequest request, ClinicalDocumentStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var doc = store.Create(request, demo);
            evidence.Append(new CreateEvidenceEventRequest(request.Professional, "clinical-document.create", $"document:{doc.Id}", "POC-B6", doc.Sha256, "document-hash"));
            demo.AuditExternal(request.Professional, "clinical-document.create", $"document:{doc.Id}", $"type={doc.Type};sha256={doc.Sha256}");
            return Results.Created($"/api/documents/{doc.Id}", doc);
        });
        endpoints.MapPost("/api/documents/{id:guid}/sign-demo", (Guid id, SignClinicalDocumentRequest request, ClinicalDocumentStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var doc = store.SignDemo(id, request);
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "clinical-document.sign-demo", $"document:{doc.Id}", "POC-B6", doc.Signature?.SignatureSha256, "signature-envelope"));
            demo.AuditExternal(request.Actor, "clinical-document.sign-demo", $"document:{id}", doc.Signature?.SignatureSha256);
            return Results.Ok(doc);
        });
        endpoints.MapPost("/api/documents/{id:guid}/revoke", (Guid id, RevokeClinicalDocumentRequest request, ClinicalDocumentStore store, DemoStore demo) =>
        {
            var doc = store.Revoke(id, request);
            demo.AuditExternal(request.Actor, "clinical-document.revoke", $"document:{id}", request.Reason);
            return Results.Ok(doc);
        });
        endpoints.MapGet("/api/documents/readiness", (ClinicalDocumentStore store) => Results.Ok(new
        {
            documents = store.Documents(null).Count,
            supported = new[] { "prescription", "exam-order", "medical-certificate", "referral", "attendance-declaration", "free-clinical-document" },
            integrity = "SHA-256 over canonical POC document content",
            demoSignature = "ephemeral RSA key generated at runtime; proves envelope/signature flow only",
            productionDependencies = new[] { "ICP-Brasil A1/A3 or approved signing service", "timestamp authority when contractually required", "certificate lifecycle and revocation policy" },
            disclaimer = "A assinatura RSA da POC NÃO é assinatura ICP-Brasil e não deve ser apresentada como documento legalmente assinado em produção."
        }));
        return endpoints;
    }
}

public sealed class ClinicalDocumentStore : IDisposable
{
    private readonly ConcurrentDictionary<Guid, ClinicalDocumentV2> _documents = new();
    private readonly RSA _demoKey = RSA.Create(2048);

    public IReadOnlyList<ClinicalDocumentV2> Documents(Guid? citizenId) => _documents.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public ClinicalDocumentV2? Get(Guid id) => _documents.TryGetValue(id, out var doc) ? doc : null;

    public ClinicalDocumentV2 Create(CreateClinicalDocumentRequest request, DemoStore demo)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new InvalidOperationException("Cidadão não encontrado.");
        var type = NormalizeType(request.Type);
        if (string.IsNullOrWhiteSpace(request.Content)) throw new ArgumentException("Conteúdo clínico obrigatório.");
        if (string.IsNullOrWhiteSpace(request.Professional) || string.IsNullOrWhiteSpace(request.ProfessionalCouncil)) throw new ArgumentException("Profissional e conselho são obrigatórios.");
        var createdAt = DateTimeOffset.UtcNow;
        var canonical = Canonical(citizen.Id, citizen.Name, type, request.Content, request.Professional, request.ProfessionalCouncil, request.Cid, createdAt);
        var sha = Sha256(canonical);
        var doc = new ClinicalDocumentV2(Guid.NewGuid(), citizen.Id, citizen.Name, type, request.Title?.Trim() ?? TypeLabel(type), request.Content.Trim(), request.Cid?.Trim(), request.Professional.Trim(), request.ProfessionalCouncil.Trim(), "draft", sha, null, null, createdAt, createdAt);
        _documents[doc.Id] = doc;
        return doc;
    }

    public ClinicalDocumentV2 SignDemo(Guid id, SignClinicalDocumentRequest request)
    {
        var current = Required(id);
        if (current.Status == "revoked") throw new InvalidOperationException("Documento revogado não pode ser assinado.");
        if (current.Signature is not null) return current;
        var payload = Encoding.UTF8.GetBytes(current.Sha256);
        var signatureBytes = _demoKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var publicKey = Convert.ToBase64String(_demoKey.ExportSubjectPublicKeyInfo());
        var signature = new ClinicalSignatureEnvelope(
            "POC-RSA-SHA256",
            Convert.ToBase64String(signatureBytes),
            Sha256(Convert.ToBase64String(signatureBytes)),
            publicKey,
            request.Actor.Trim(),
            request.ActorCouncil?.Trim(),
            DateTimeOffset.UtcNow,
            false,
            "Assinatura efêmera da POC; não ICP-Brasil.");
        var updated = current with { Status = "signed_demo", Signature = signature, UpdatedAt = DateTimeOffset.UtcNow };
        _documents[id] = updated;
        return updated;
    }

    public ClinicalDocumentV2 Revoke(Guid id, RevokeClinicalDocumentRequest request)
    {
        var current = Required(id);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Motivo de revogação obrigatório.");
        if (current.Status == "revoked") return current;
        var revocation = new DocumentRevocation(request.Actor.Trim(), request.Reason.Trim(), DateTimeOffset.UtcNow);
        var updated = current with { Status = "revoked", Revocation = revocation, UpdatedAt = DateTimeOffset.UtcNow };
        _documents[id] = updated;
        return updated;
    }

    private ClinicalDocumentV2 Required(Guid id) => _documents.TryGetValue(id, out var doc) ? doc : throw new KeyNotFoundException();
    private static string NormalizeType(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "prescription" or "receita" => "prescription",
        "exam-order" or "pedido-exame" => "exam-order",
        "medical-certificate" or "atestado" => "medical-certificate",
        "referral" or "encaminhamento" => "referral",
        "attendance-declaration" or "declaracao" or "declaração" => "attendance-declaration",
        "free" or "clinical" => "free-clinical-document",
        _ => throw new ArgumentException("Tipo de documento clínico inválido.")
    };
    private static string TypeLabel(string type) => type switch
    {
        "prescription" => "Receita médica",
        "exam-order" => "Pedido de exames",
        "medical-certificate" => "Atestado médico",
        "referral" => "Encaminhamento",
        "attendance-declaration" => "Declaração de comparecimento",
        _ => "Documento clínico"
    };
    private static string Canonical(Guid citizenId, string citizenName, string type, string content, string professional, string council, string? cid, DateTimeOffset createdAt) =>
        string.Join('|', citizenId.ToString("N"), Clean(citizenName), Clean(type), Clean(content), Clean(professional), Clean(council), Clean(cid), createdAt.ToUniversalTime().ToString("O"));
    private static string Clean(string? value) => (value ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public void Dispose() => _demoKey.Dispose();
}

public sealed record ClinicalDocumentV2(Guid Id, Guid CitizenId, string CitizenName, string Type, string Title, string Content, string? Cid, string Professional, string ProfessionalCouncil, string Status, string Sha256, ClinicalSignatureEnvelope? Signature, DocumentRevocation? Revocation, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ClinicalSignatureEnvelope(string Algorithm, string SignatureBase64, string SignatureSha256, string PublicKeySpkiBase64, string SignedBy, string? SignedByCouncil, DateTimeOffset SignedAt, bool IcpBrasil, string Disclaimer);
public sealed record DocumentRevocation(string Actor, string Reason, DateTimeOffset RevokedAt);
public sealed record CreateClinicalDocumentRequest(Guid CitizenId, string Type, string? Title, string Content, string? Cid, string Professional, string ProfessionalCouncil);
public sealed record SignClinicalDocumentRequest(string Actor, string? ActorCouncil);
public sealed record RevokeClinicalDocumentRequest(string Actor, string Reason);
