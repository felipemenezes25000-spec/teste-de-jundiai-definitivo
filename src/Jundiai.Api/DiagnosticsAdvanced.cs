using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class DiagnosticsAdvancedEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsAdvancedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/diagnostics/v2/orders", (Guid? citizenId, DiagnosticsAdvancedStore store) =>
            Results.Ok(store.Orders(citizenId)));
        endpoints.MapGet("/api/diagnostics/v2/orders/{id:guid}", (Guid id, DiagnosticsAdvancedStore store) =>
            store.Get(id) is { } order ? Results.Ok(order) : Results.NotFound());
        endpoints.MapPost("/api/diagnostics/v2/orders", (CreateDiagnosticOrderRequest request, DiagnosticsAdvancedStore store, DemoStore demo) =>
        {
            var order = store.CreateOrder(request, demo);
            demo.AuditExternal(request.RequestedBy, "diagnostics.order.create", $"diagnostic-order:{order.Id}", $"{order.ExamCode} · {order.ExamName}");
            return Results.Created($"/api/diagnostics/v2/orders/{order.Id}", order);
        });
        endpoints.MapPost("/api/diagnostics/v2/orders/{id:guid}/schedule", (Guid id, ScheduleDiagnosticOrderRequest request, DiagnosticsAdvancedStore store, DemoStore demo) =>
        {
            var order = store.Schedule(id, request);
            demo.AuditExternal(request.Actor, "diagnostics.order.schedule", $"diagnostic-order:{id}", order.ScheduledAt?.ToString("O"));
            return Results.Ok(order);
        });
        endpoints.MapPost("/api/diagnostics/v2/orders/{id:guid}/collect", (Guid id, DiagnosticCollectionRequest request, DiagnosticsAdvancedStore store, DemoStore demo) =>
        {
            var order = store.Collect(id, request);
            demo.AuditExternal(request.Professional, "diagnostics.collection", $"diagnostic-order:{id}", request.Specimen);
            return Results.Ok(order);
        });
        endpoints.MapPost("/api/diagnostics/v2/orders/{id:guid}/perform", (Guid id, PerformDiagnosticOrderRequest request, DiagnosticsAdvancedStore store, DemoStore demo) =>
        {
            var order = store.Perform(id, request);
            demo.AuditExternal(request.Professional, "diagnostics.perform", $"diagnostic-order:{id}", request.DeviceOrModality);
            return Results.Ok(order);
        });
        endpoints.MapPost("/api/diagnostics/v2/orders/{id:guid}/result", (Guid id, RegisterDiagnosticResultRequest request, DiagnosticsAdvancedStore store, DemoStore demo) =>
        {
            var order = store.RegisterResult(id, request);
            demo.AuditExternal(request.ReleasedBy, "diagnostics.result.release", $"diagnostic-order:{id}", $"critical={request.Critical}");
            return Results.Ok(order);
        });
        endpoints.MapPost("/api/diagnostics/v2/orders/{id:guid}/attachments", (Guid id, DiagnosticAttachmentRequest request, DiagnosticsAdvancedStore store) =>
            Results.Ok(store.Attach(id, request)));
        endpoints.MapPost("/api/diagnostics/v2/orders/{id:guid}/acknowledge-critical", (Guid id, CriticalResultAcknowledgementRequest request, DiagnosticsAdvancedStore store, DemoStore demo) =>
        {
            var order = store.AcknowledgeCritical(id, request);
            demo.AuditExternal(request.Actor, "diagnostics.critical.ack", $"diagnostic-order:{id}", request.Note);
            return Results.Ok(order);
        });
        endpoints.MapGet("/api/diagnostics/v2/integrations", () => Results.Ok(new[]
        {
            new { adapter = "PACS/DICOM", status = "boundary_ready", production = false, note = "Metadados e identificadores preparados; conexão depende do PACS homologado e credenciais do município." },
            new { adapter = "LIS/HL7-FHIR", status = "boundary_ready", production = false, note = "Contrato de integração preparado para resultado laboratorial; sem alegação de homologação externa." }
        }));
        endpoints.MapGet("/api/diagnostics/v2/readiness", (DiagnosticsAdvancedStore store) => Results.Ok(new
        {
            orderCount = store.Orders(null).Count,
            capabilities = new[]
            {
                "solicitação clínica estruturada", "agendamento", "coleta", "execução", "laudo/resultado",
                "resultado crítico com ciência", "anexos por metadados", "rastreabilidade de equipamento/modalidade", "fronteira PACS/LIS"
            },
            externalDependencies = new[] { "PACS/DICOM real", "LIS real", "credenciais e homologação dos fornecedores municipais" }
        }));
        return endpoints;
    }
}

public sealed class DiagnosticsAdvancedStore
{
    private readonly ConcurrentDictionary<Guid, DiagnosticOrderV2> _orders = new();

    public DiagnosticsAdvancedStore(DemoStore demo)
    {
        var citizen = demo.Citizens().FirstOrDefault() ?? throw new InvalidOperationException("POC sem cidadão seed para diagnóstico.");
        var id = Guid.NewGuid();
        _orders[id] = new DiagnosticOrderV2(
            id,
            citizen.Id,
            citizen.Name,
            "0202020380",
            "Hemograma completo",
            "laboratory",
            "routine",
            citizen.HealthUnit,
            "Laboratório Municipal",
            "Dr. Eduardo Martins",
            "CRM 000001",
            "I10",
            "Acompanhamento de condição crônica",
            "scheduled",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddHours(4),
            null,
            null,
            null,
            null,
            [],
            null,
            DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<DiagnosticOrderV2> Orders(Guid? citizenId) => _orders.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.RequestedAt)
        .ToList();

    public DiagnosticOrderV2? Get(Guid id) => _orders.TryGetValue(id, out var order) ? order : null;

    public DiagnosticOrderV2 CreateOrder(CreateDiagnosticOrderRequest request, DemoStore demo)
    {
        if (string.IsNullOrWhiteSpace(request.ExamCode) || string.IsNullOrWhiteSpace(request.ExamName))
            throw new ArgumentException("Código e nome do exame são obrigatórios.");
        var citizen = demo.Citizen(request.CitizenId) ?? throw new InvalidOperationException("Cidadão não encontrado.");
        var kind = NormalizeKind(request.Kind);
        var priority = NormalizePriority(request.Priority);
        var order = new DiagnosticOrderV2(
            Guid.NewGuid(), citizen.Id, citizen.Name, request.ExamCode.Trim(), request.ExamName.Trim(), kind, priority,
            request.OriginUnit.Trim(), request.DestinationUnit?.Trim(), request.RequestedBy.Trim(), request.RequestedByCouncil.Trim(),
            request.Cid?.Trim(), request.ClinicalIndication?.Trim(), "requested", DateTimeOffset.UtcNow, null, null, null, null, null, [], null, DateTimeOffset.UtcNow);
        _orders[order.Id] = order;
        return order;
    }

    public DiagnosticOrderV2 Schedule(Guid id, ScheduleDiagnosticOrderRequest request)
    {
        var current = Required(id);
        if (current.Status is "completed" or "cancelled") throw new InvalidOperationException("Pedido encerrado não pode ser reagendado.");
        if (request.ScheduledAt <= DateTimeOffset.UtcNow.AddMinutes(-5)) throw new ArgumentException("Agendamento deve ser futuro.");
        var updated = current with
        {
            Status = "scheduled",
            ScheduledAt = request.ScheduledAt,
            DestinationUnit = string.IsNullOrWhiteSpace(request.Unit) ? current.DestinationUnit : request.Unit.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _orders[id] = updated;
        return updated;
    }

    public DiagnosticOrderV2 Collect(Guid id, DiagnosticCollectionRequest request)
    {
        var current = Required(id);
        if (current.Kind != "laboratory") throw new InvalidOperationException("Coleta de espécime é aplicável a exame laboratorial.");
        if (current.Status is not ("requested" or "scheduled")) throw new InvalidOperationException("Pedido não está apto para coleta.");
        if (string.IsNullOrWhiteSpace(request.Specimen)) throw new ArgumentException("Espécime obrigatório.");
        var collection = new DiagnosticCollection(Guid.NewGuid(), request.Specimen.Trim(), request.Container?.Trim(), request.Barcode?.Trim() ?? $"LAB-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), request.Professional.Trim(), DateTimeOffset.UtcNow);
        var updated = current with { Status = "collected", Collection = collection, UpdatedAt = DateTimeOffset.UtcNow };
        _orders[id] = updated;
        return updated;
    }

    public DiagnosticOrderV2 Perform(Guid id, PerformDiagnosticOrderRequest request)
    {
        var current = Required(id);
        var allowed = current.Kind == "laboratory" ? new[] { "collected", "scheduled", "requested" } : new[] { "scheduled", "requested" };
        if (!allowed.Contains(current.Status)) throw new InvalidOperationException("Pedido não está apto para execução.");
        var execution = new DiagnosticExecution(Guid.NewGuid(), request.Professional.Trim(), request.ProfessionalCouncil?.Trim(), request.DeviceOrModality?.Trim(), request.Method?.Trim(), request.ExternalAccessionId?.Trim(), DateTimeOffset.UtcNow);
        var updated = current with { Status = "performed", Execution = execution, UpdatedAt = DateTimeOffset.UtcNow };
        _orders[id] = updated;
        return updated;
    }

    public DiagnosticOrderV2 RegisterResult(Guid id, RegisterDiagnosticResultRequest request)
    {
        var current = Required(id);
        if (current.Status is not ("performed" or "collected")) throw new InvalidOperationException("Resultado exige exame coletado/executado.");
        if (string.IsNullOrWhiteSpace(request.Conclusion)) throw new ArgumentException("Conclusão/laudo obrigatório.");
        var result = new DiagnosticResultV2(
            Guid.NewGuid(), request.StructuredValue?.Trim(), request.Unit?.Trim(), request.ReferenceRange?.Trim(), request.Conclusion.Trim(),
            request.Critical, request.ReleasedBy.Trim(), request.ReleasedByCouncil?.Trim(), DateTimeOffset.UtcNow,
            request.Critical ? "pending_acknowledgement" : "not_required", null, null, null);
        var updated = current with { Status = request.Critical ? "critical_result" : "completed", Result = result, UpdatedAt = DateTimeOffset.UtcNow };
        _orders[id] = updated;
        return updated;
    }

    public DiagnosticOrderV2 Attach(Guid id, DiagnosticAttachmentRequest request)
    {
        var current = Required(id);
        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentType)) throw new ArgumentException("Nome e content-type são obrigatórios.");
        var attachment = new DiagnosticAttachment(Guid.NewGuid(), request.FileName.Trim(), request.ContentType.Trim(), request.ExternalUrl?.Trim(), request.DicomStudyUid?.Trim(), request.Sha256?.Trim(), request.Actor?.Trim() ?? "diagnostics", DateTimeOffset.UtcNow);
        var updated = current with { Attachments = current.Attachments.Concat([attachment]).ToList(), UpdatedAt = DateTimeOffset.UtcNow };
        _orders[id] = updated;
        return updated;
    }

    public DiagnosticOrderV2 AcknowledgeCritical(Guid id, CriticalResultAcknowledgementRequest request)
    {
        var current = Required(id);
        if (current.Result?.Critical != true) throw new InvalidOperationException("O pedido não possui resultado crítico.");
        if (current.Result.CriticalAcknowledgementStatus == "acknowledged") return current;
        var result = current.Result with
        {
            CriticalAcknowledgementStatus = "acknowledged",
            CriticalAcknowledgedBy = request.Actor.Trim(),
            CriticalAcknowledgedAt = DateTimeOffset.UtcNow,
            CriticalAcknowledgementNote = request.Note?.Trim()
        };
        var updated = current with { Status = "completed", Result = result, UpdatedAt = DateTimeOffset.UtcNow };
        _orders[id] = updated;
        return updated;
    }

    private DiagnosticOrderV2 Required(Guid id) => _orders.TryGetValue(id, out var order) ? order : throw new KeyNotFoundException();
    private static string NormalizeKind(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "lab" or "laboratory" or "laboratorio" or "laboratório" => "laboratory",
        "image" or "imaging" or "imagem" => "imaging",
        _ => throw new ArgumentException("Tipo deve ser laboratory ou imaging.")
    };
    private static string NormalizePriority(string? value) => (value ?? "routine").Trim().ToLowerInvariant() switch
    {
        "emergency" or "emergência" or "emergencia" => "emergency",
        "urgent" or "urgente" => "urgent",
        "high" or "alta" => "high",
        _ => "routine"
    };
}

public sealed record DiagnosticOrderV2(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string ExamCode,
    string ExamName,
    string Kind,
    string Priority,
    string OriginUnit,
    string? DestinationUnit,
    string RequestedBy,
    string RequestedByCouncil,
    string? Cid,
    string? ClinicalIndication,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ScheduledAt,
    DiagnosticCollection? Collection,
    DiagnosticExecution? Execution,
    DiagnosticResultV2? Result,
    string? CancellationReason,
    IReadOnlyList<DiagnosticAttachment> Attachments,
    string? AttendanceDeclarationReference,
    DateTimeOffset UpdatedAt);

public sealed record DiagnosticCollection(Guid Id, string Specimen, string? Container, string Barcode, string Professional, DateTimeOffset CollectedAt);
public sealed record DiagnosticExecution(Guid Id, string Professional, string? ProfessionalCouncil, string? DeviceOrModality, string? Method, string? ExternalAccessionId, DateTimeOffset PerformedAt);
public sealed record DiagnosticResultV2(Guid Id, string? StructuredValue, string? Unit, string? ReferenceRange, string Conclusion, bool Critical, string ReleasedBy, string? ReleasedByCouncil, DateTimeOffset ReleasedAt, string CriticalAcknowledgementStatus, string? CriticalAcknowledgedBy, DateTimeOffset? CriticalAcknowledgedAt, string? CriticalAcknowledgementNote);
public sealed record DiagnosticAttachment(Guid Id, string FileName, string ContentType, string? ExternalUrl, string? DicomStudyUid, string? Sha256, string Actor, DateTimeOffset AttachedAt);
public sealed record CreateDiagnosticOrderRequest(Guid CitizenId, string ExamCode, string ExamName, string Kind, string? Priority, string OriginUnit, string? DestinationUnit, string RequestedBy, string RequestedByCouncil, string? Cid, string? ClinicalIndication);
public sealed record ScheduleDiagnosticOrderRequest(DateTimeOffset ScheduledAt, string? Unit, string Actor);
public sealed record DiagnosticCollectionRequest(string Specimen, string? Container, string? Barcode, string Professional);
public sealed record PerformDiagnosticOrderRequest(string Professional, string? ProfessionalCouncil, string? DeviceOrModality, string? Method, string? ExternalAccessionId);
public sealed record RegisterDiagnosticResultRequest(string? StructuredValue, string? Unit, string? ReferenceRange, string Conclusion, bool Critical, string ReleasedBy, string? ReleasedByCouncil);
public sealed record DiagnosticAttachmentRequest(string FileName, string ContentType, string? ExternalUrl, string? DicomStudyUid, string? Sha256, string? Actor);
public sealed record CriticalResultAcknowledgementRequest(string Actor, string? Note);