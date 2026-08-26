using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class ClinicalOrderEndpoints
{
    public static IEndpointRouteBuilder MapClinicalOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/clinical/orders", (Guid? citizenId, ClinicalOrderStore store) => Results.Ok(store.Orders(citizenId)));
        endpoints.MapGet("/api/clinical/orders/{id:guid}", (Guid id, ClinicalOrderStore store) =>
            store.Get(id) is { } order ? Results.Ok(order) : Results.NotFound());
        endpoints.MapGet("/api/clinical/orders/{id:guid}/administrations", (Guid id, ClinicalOrderStore store) => Results.Ok(store.Administrations(id)));

        endpoints.MapPost("/api/clinical/orders", (
            CreateClinicalMedicationOrderRequest request,
            ClinicalOrderStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var order = store.Create(request, demo);
            demo.AuditExternal(request.OrderedBy, "clinical.order.create", $"clinical-order:{order.Id}", $"medication={order.Medication};route={order.Route}");
            evidence.Append(new CreateEvidenceEventRequest(request.OrderedBy, "clinical.order.create", $"clinical-order:{order.Id}", "POC-B06", $"status={order.Status};medication={order.Medication}", "clinical-order"));
            return Results.Created($"/api/clinical/orders/{order.Id}", order);
        });

        endpoints.MapPost("/api/clinical/orders/{id:guid}/transition", (
            Guid id,
            ClinicalOrderTransitionRequest request,
            ClinicalOrderStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var order = store.Transition(id, request);
            demo.AuditExternal(request.Actor, "clinical.order.transition", $"clinical-order:{id}", $"status={order.Status};reason={request.Reason}");
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "clinical.order.transition", $"clinical-order:{id}", "POC-B06", $"status={order.Status};reason={request.Reason}", "clinical-order"));
            return Results.Ok(order);
        });

        endpoints.MapPost("/api/clinical/orders/{id:guid}/administer", (
            Guid id,
            AdministerClinicalMedicationRequest request,
            ClinicalOrderStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            var administration = store.Administer(id, request);
            demo.AuditExternal(request.Professional, "clinical.medication.administer", $"clinical-order:{id}", $"outcome={administration.Outcome}");
            evidence.Append(new CreateEvidenceEventRequest(request.Professional, "clinical.medication.administer", $"administration:{administration.Id}", "POC-B06", $"order={id};outcome={administration.Outcome}", "medication-administration"));
            return Results.Created($"/api/clinical/orders/{id}/administrations", administration);
        });

        endpoints.MapGet("/api/clinical/care-plans", (Guid? citizenId, ClinicalOrderStore store) => Results.Ok(store.CarePlans(citizenId)));
        endpoints.MapPost("/api/clinical/care-plans", (CreateCarePlanRequest request, ClinicalOrderStore store, DemoStore demo) =>
        {
            var plan = store.CreateCarePlan(request, demo);
            demo.AuditExternal(request.CreatedBy, "clinical.care-plan.create", $"care-plan:{plan.Id}", plan.Goal);
            return Results.Created($"/api/clinical/care-plans/{plan.Id}", plan);
        });
        endpoints.MapPost("/api/clinical/care-plans/{planId:guid}/tasks/{taskId:guid}/complete", (Guid planId, Guid taskId, CompleteCareTaskRequest request, ClinicalOrderStore store, DemoStore demo) =>
        {
            var plan = store.CompleteTask(planId, taskId, request);
            demo.AuditExternal(request.Actor, "clinical.care-plan.task.complete", $"care-plan:{planId};task:{taskId}", request.Note);
            return Results.Ok(plan);
        });

        endpoints.MapGet("/api/clinical/orders/readiness", (ClinicalOrderStore store) => Results.Ok(new
        {
            orders = store.Orders(null).Count,
            administrations = store.AllAdministrations().Count,
            carePlans = store.CarePlans(null).Count,
            capabilities = new[]
            {
                "medication order", "dose/route/frequency", "start/end", "hold/discontinue/cancel",
                "MAR administration log", "omission/refusal/not-given outcome", "multiprofessional care plan", "task ownership/completion", "audit/evidence hooks"
            },
            note = "A POC demonstra workflow e rastreabilidade; protocolos medicamentosos reais devem ser parametrizados/validados clinicamente na implantação."
        }));
        return endpoints;
    }
}

public sealed class ClinicalOrderStore
{
    private readonly ConcurrentDictionary<Guid, ClinicalMedicationOrder> _orders = new();
    private readonly ConcurrentDictionary<Guid, List<ClinicalMedicationAdministration>> _administrations = new();
    private readonly ConcurrentDictionary<Guid, CarePlanV2> _carePlans = new();

    public ClinicalOrderStore()
    {
        var citizenId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var now = DateTimeOffset.UtcNow;
        var order = new ClinicalMedicationOrder(
            Guid.NewGuid(), citizenId, "Maria da Silva", "Medicamento demonstrativo de uso contínuo", "50 mg", "oral", "12/12h",
            now.AddDays(-7), null, "active", "Dr. Eduardo Martins", "CRM 000001", "Exemplo de ordem clínica sem finalidade prescritiva real.",
            now.AddDays(-7), now.AddDays(-7), null, null);
        _orders[order.Id] = order;

        var task = new CarePlanTask(Guid.NewGuid(), "Aferir pressão arterial e registrar evolução", "nurse", "Enf. Juliana Ramos", now.AddDays(1), "open", null, null, null);
        var plan = new CarePlanV2(Guid.NewGuid(), citizenId, "Maria da Silva", "Acompanhamento longitudinal da condição crônica", "active", "Dr. Eduardo Martins", now.AddDays(-3), [task], now.AddDays(-3));
        _carePlans[plan.Id] = plan;
    }

    public IReadOnlyList<ClinicalMedicationOrder> Orders(Guid? citizenId) => _orders.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public ClinicalMedicationOrder? Get(Guid id) => _orders.TryGetValue(id, out var order) ? order : null;

    public ClinicalMedicationOrder Create(CreateClinicalMedicationOrderRequest request, DemoStore demo)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Medication) || string.IsNullOrWhiteSpace(request.Dose) || string.IsNullOrWhiteSpace(request.Route) || string.IsNullOrWhiteSpace(request.Frequency))
            throw new ArgumentException("Medicamento, dose, via e frequência são obrigatórios.");
        if (request.EndsAt is { } end && end <= request.StartsAt) throw new ArgumentException("Fim deve ser posterior ao início.");
        if (string.IsNullOrWhiteSpace(request.OrderedBy) || string.IsNullOrWhiteSpace(request.ProfessionalCouncil)) throw new ArgumentException("Profissional e conselho são obrigatórios.");
        var now = DateTimeOffset.UtcNow;
        var order = new ClinicalMedicationOrder(
            Guid.NewGuid(), citizen.Id, citizen.Name, request.Medication.Trim(), request.Dose.Trim(), request.Route.Trim(), request.Frequency.Trim(),
            request.StartsAt, request.EndsAt, "active", request.OrderedBy.Trim(), request.ProfessionalCouncil.Trim(), request.Instructions?.Trim(),
            now, now, null, null);
        _orders[order.Id] = order;
        return order;
    }

    public ClinicalMedicationOrder Transition(Guid id, ClinicalOrderTransitionRequest request)
    {
        if (!_orders.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        var target = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        var allowed = current.Status switch
        {
            "active" => new[] { "held", "discontinued", "cancelled", "completed" },
            "held" => new[] { "active", "discontinued", "cancelled" },
            _ => Array.Empty<string>()
        };
        if (!allowed.Contains(target)) throw new InvalidOperationException($"Transição de ordem inválida: {current.Status} → {target}.");
        if (target is "held" or "discontinued" or "cancelled" && string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Suspensão/descontinuação/cancelamento exige motivo.");
        var updated = current with
        {
            Status = target,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastTransitionBy = request.Actor?.Trim(),
            LastTransitionReason = request.Reason?.Trim()
        };
        _orders[id] = updated;
        return updated;
    }

    public ClinicalMedicationAdministration Administer(Guid id, AdministerClinicalMedicationRequest request)
    {
        if (!_orders.TryGetValue(id, out var order)) throw new KeyNotFoundException();
        var outcome = NormalizeAdministrationOutcome(request.Outcome);
        if (order.Status != "active" && outcome == "given") throw new InvalidOperationException("Dose administrada exige ordem ativa.");
        if (string.IsNullOrWhiteSpace(request.Professional) || string.IsNullOrWhiteSpace(request.ProfessionalCouncil)) throw new ArgumentException("Profissional e conselho são obrigatórios.");
        if (outcome != "given" && string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Dose não administrada exige motivo.");
        var administration = new ClinicalMedicationAdministration(
            Guid.NewGuid(), order.Id, order.CitizenId, order.CitizenName, order.Medication,
            string.IsNullOrWhiteSpace(request.Dose) ? order.Dose : request.Dose.Trim(),
            string.IsNullOrWhiteSpace(request.Route) ? order.Route : request.Route.Trim(),
            outcome, request.Reason?.Trim(), request.Professional.Trim(), request.ProfessionalCouncil.Trim(),
            request.AdministeredAt ?? DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _administrations.GetOrAdd(id, _ => []).Add(administration);
        return administration;
    }

    public IReadOnlyList<ClinicalMedicationAdministration> Administrations(Guid orderId) =>
        _administrations.TryGetValue(orderId, out var list) ? list.OrderByDescending(x => x.AdministeredAt).ToList() : [];

    public IReadOnlyList<ClinicalMedicationAdministration> AllAdministrations() => _administrations.Values.SelectMany(x => x).OrderByDescending(x => x.AdministeredAt).ToList();

    public IReadOnlyList<CarePlanV2> CarePlans(Guid? citizenId) => _carePlans.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public CarePlanV2 CreateCarePlan(CreateCarePlanRequest request, DemoStore demo)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Goal) || request.Tasks is null || request.Tasks.Count == 0) throw new ArgumentException("Objetivo e ao menos uma tarefa são obrigatórios.");
        var now = DateTimeOffset.UtcNow;
        var tasks = request.Tasks.Select(x => new CarePlanTask(
            Guid.NewGuid(), x.Description.Trim(), x.Profession.Trim(), x.Owner?.Trim(), x.DueAt, "open", null, null, null)).ToArray();
        var plan = new CarePlanV2(Guid.NewGuid(), citizen.Id, citizen.Name, request.Goal.Trim(), "active", request.CreatedBy.Trim(), now, tasks, now);
        _carePlans[plan.Id] = plan;
        return plan;
    }

    public CarePlanV2 CompleteTask(Guid planId, Guid taskId, CompleteCareTaskRequest request)
    {
        if (!_carePlans.TryGetValue(planId, out var plan)) throw new KeyNotFoundException();
        var task = plan.Tasks.FirstOrDefault(x => x.Id == taskId) ?? throw new KeyNotFoundException();
        if (task.Status == "completed") return plan;
        var updatedTask = task with { Status = "completed", CompletedAt = DateTimeOffset.UtcNow, CompletedBy = request.Actor.Trim(), CompletionNote = request.Note?.Trim() };
        var tasks = plan.Tasks.Select(x => x.Id == taskId ? updatedTask : x).ToArray();
        var status = tasks.All(x => x.Status == "completed") ? "completed" : plan.Status;
        var updated = plan with { Tasks = tasks, Status = status, UpdatedAt = DateTimeOffset.UtcNow };
        _carePlans[planId] = updated;
        return updated;
    }

    private static string NormalizeAdministrationOutcome(string? value) => (value ?? "given").Trim().ToLowerInvariant() switch
    {
        "given" or "administrada" => "given",
        "refused" or "recusada" => "refused",
        "omitted" or "omitida" => "omitted",
        "not_available" or "indisponivel" or "indisponível" => "not_available",
        "held" or "suspensa" => "held",
        _ => throw new ArgumentException("Outcome de administração inválido.")
    };
}

public sealed record ClinicalMedicationOrder(
    Guid Id, Guid CitizenId, string CitizenName, string Medication, string Dose, string Route, string Frequency,
    DateTimeOffset StartsAt, DateTimeOffset? EndsAt, string Status, string OrderedBy, string ProfessionalCouncil,
    string? Instructions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? LastTransitionBy, string? LastTransitionReason);
public sealed record CreateClinicalMedicationOrderRequest(
    Guid CitizenId, string Medication, string Dose, string Route, string Frequency, DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt, string OrderedBy, string ProfessionalCouncil, string? Instructions);
public sealed record ClinicalOrderTransitionRequest(string Status, string? Reason, string? Actor);
public sealed record ClinicalMedicationAdministration(
    Guid Id, Guid OrderId, Guid CitizenId, string CitizenName, string Medication, string Dose, string Route,
    string Outcome, string? Reason, string Professional, string ProfessionalCouncil, DateTimeOffset AdministeredAt, DateTimeOffset RecordedAt);
public sealed record AdministerClinicalMedicationRequest(
    string? Dose, string? Route, string? Outcome, string? Reason, string Professional, string ProfessionalCouncil, DateTimeOffset? AdministeredAt);
public sealed record CarePlanTask(
    Guid Id, string Description, string Profession, string? Owner, DateTimeOffset? DueAt, string Status,
    DateTimeOffset? CompletedAt, string? CompletedBy, string? CompletionNote);
public sealed record CarePlanV2(
    Guid Id, Guid CitizenId, string CitizenName, string Goal, string Status, string CreatedBy,
    DateTimeOffset CreatedAt, IReadOnlyList<CarePlanTask> Tasks, DateTimeOffset UpdatedAt);
public sealed record CreateCarePlanTaskRequest(string Description, string Profession, string? Owner, DateTimeOffset? DueAt);
public sealed record CreateCarePlanRequest(Guid CitizenId, string Goal, string CreatedBy, IReadOnlyList<CreateCarePlanTaskRequest> Tasks);
public sealed record CompleteCareTaskRequest(string Actor, string? Note);
