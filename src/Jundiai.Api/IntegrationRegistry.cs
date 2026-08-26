using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class IntegrationRegistryEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationRegistryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/integrations", (IntegrationRegistryStore store) => Results.Ok(store.All()));
        endpoints.MapGet("/api/integrations/{key}", (string key, IntegrationRegistryStore store) =>
            store.Get(key) is { } item ? Results.Ok(item) : Results.NotFound());
        endpoints.MapPost("/api/integrations/{key}/assessment", (string key, IntegrationAssessmentRequest request, IntegrationRegistryStore store, DemoStore demo, EvidenceLedgerStore evidence) =>
        {
            var item = store.Assess(key, request);
            demo.AuditExternal(request.Actor, "integration.assessment", $"integration:{key}", $"status={item.Status};env={item.Environment}");
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "integration.assessment", $"integration:{key}", "POC-B14", request.Note, "integration-evidence"));
            return Results.Ok(item);
        });
        endpoints.MapGet("/api/integrations/readiness", (IntegrationRegistryStore store) => Results.Ok(store.Readiness()));
        return endpoints;
    }
}

public sealed class IntegrationRegistryStore
{
    private readonly ConcurrentDictionary<string, IntegrationCapability> _items = new(StringComparer.OrdinalIgnoreCase);

    public IntegrationRegistryStore()
    {
        Add("cadsus", "CadSUS/CNS", "citizen-master-data", "external_authorization_required", ["cadastro cidadão", "validação CNS/CPF"], "Autorização e credenciais oficiais necessárias.");
        Add("rnds", "RNDS", "national-health-network", "external_homologation_required", ["documentos clínicos", "interoperabilidade nacional"], "Implantação depende de credenciais, certificados e conformidade com versão vigente.");
        Add("si-pni", "SI-PNI/RNDS Imunização", "immunization", "external_homologation_required", ["registro vacinal"], "POC mantém domínio interno; transmissão oficial depende de integração vigente.");
        Add("esus-aps", "e-SUS APS", "primary-care", "boundary_ready", ["cadastro individual", "domicílio", "visita", "produção APS"], "Exportação atual é demonstrativa; layout oficial será versionado na implantação.");
        Add("datasus-bpa", "DATASUS BPA", "billing", "boundary_ready", ["BPA-I", "BPA-C", "crítica", "competência"], "Motor POC possui produção/crítica/versionamento; arquivo oficial deve seguir layout vigente.");
        Add("sigtap", "SIGTAP", "billing-catalog", "catalog_seeded_demo", ["procedimentos", "CBO", "regras idade/sexo"], "Catálogo da POC é reduzido; produção exige sincronização/versionamento oficial.");
        Add("bnafar", "BNAFAR/Hórus ou integração farmacêutica aplicável", "pharmacy", "external_definition_required", ["estoque", "movimentação farmacêutica"], "Definir canal exigido pelo município/Ministério e credenciais.");
        Add("pacs", "PACS/DICOM", "diagnostic-imaging", "boundary_ready", ["study UID", "metadados de imagem", "laudo"], "Fornecedor e endpoints reais ainda não informados.");
        Add("lis", "LIS laboratório", "laboratory", "boundary_ready", ["pedido", "acession", "resultado"], "Fornecedor/protocolo real ainda não informado.");
        Add("govbr", "gov.br", "citizen-identity", "external_definition_required", ["identidade cidadão"], "Opcional conforme arquitetura/contrato e autorização de integração.");
        Add("icpbrasil", "ICP-Brasil", "digital-signature", "boundary_ready", ["assinatura clínica", "integridade documental"], "POC usa envelope RSA efêmero apenas para provar arquitetura; certificado real não é versionado no repositório.");
        Add("timestamp", "ACT/Carimbo do Tempo", "digital-signature", "external_contract_required", ["evidência temporal"], "Depende de contratação/endpoint e política definida.");
        Add("video", "Provedor de vídeo", "telemedicine", "provider_agnostic_boundary", ["sala", "áudio", "vídeo"], "Nenhuma credencial real é incluída; adaptar ao provedor definido na implantação.");
    }

    public IReadOnlyList<IntegrationCapability> All() => _items.Values.OrderBy(x => x.Domain).ThenBy(x => x.Name).ToList();
    public IntegrationCapability? Get(string key) => _items.TryGetValue(key, out var item) ? item : null;

    public IntegrationCapability Assess(string key, IntegrationAssessmentRequest request)
    {
        if (!_items.TryGetValue(key, out var current)) throw new KeyNotFoundException();
        var allowed = new[] { "not_started", "boundary_ready", "credentials_pending", "sandbox_connected", "homologation_pending", "homologated", "production_enabled", "blocked_external" };
        var status = request.Status.Trim().ToLowerInvariant();
        if (!allowed.Contains(status)) throw new ArgumentException("Status de integração inválido.");
        if (status is "homologated" or "production_enabled" && string.IsNullOrWhiteSpace(request.EvidenceReference))
            throw new ArgumentException("Homologação/produção exige referência explícita de evidência; não é permitido marcar sem prova.");
        var assessment = new IntegrationAssessment(Guid.NewGuid(), status, request.Environment?.Trim() ?? "unknown", request.EndpointReference?.Trim(), request.EvidenceReference?.Trim(), request.Note?.Trim(), request.Actor.Trim(), DateTimeOffset.UtcNow);
        var updated = current with { Status = status, Environment = assessment.Environment, LastAssessment = assessment, UpdatedAt = DateTimeOffset.UtcNow };
        _items[key] = updated;
        return updated;
    }

    public object Readiness()
    {
        var all = All();
        return new
        {
            total = all.Count,
            boundaryReady = all.Count(x => x.Status.Contains("boundary", StringComparison.OrdinalIgnoreCase) || x.Status == "catalog_seeded_demo" || x.Status == "provider_agnostic_boundary"),
            externalDependency = all.Count(x => x.Status.Contains("external", StringComparison.OrdinalIgnoreCase) || x.Status.Contains("required", StringComparison.OrdinalIgnoreCase)),
            homologated = all.Count(x => x.Status == "homologated"),
            productionEnabled = all.Count(x => x.Status == "production_enabled"),
            items = all.Select(x => new { x.Key, x.Name, x.Domain, x.Status, x.Environment, x.Note }).ToArray(),
            rule = "Nenhuma integração pode ser marcada como homologated/production_enabled sem EvidenceReference explícita."
        };
    }

    private void Add(string key, string name, string domain, string status, IReadOnlyList<string> capabilities, string note) =>
        _items[key] = new IntegrationCapability(key, name, domain, status, "poc", capabilities, note, null, DateTimeOffset.UtcNow);
}

public sealed record IntegrationCapability(string Key, string Name, string Domain, string Status, string Environment, IReadOnlyList<string> Capabilities, string Note, IntegrationAssessment? LastAssessment, DateTimeOffset UpdatedAt);
public sealed record IntegrationAssessment(Guid Id, string Status, string Environment, string? EndpointReference, string? EvidenceReference, string? Note, string Actor, DateTimeOffset AssessedAt);
public sealed record IntegrationAssessmentRequest(string Status, string? Environment, string? EndpointReference, string? EvidenceReference, string? Note, string Actor);
