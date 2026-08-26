namespace Jundiai.Api;

public sealed record Citizen(
    Guid Id,
    string Cns,
    string Cpf,
    string Name,
    DateOnly BirthDate,
    string HealthUnit,
    string Area,
    string MicroArea,
    Guid FamilyId);

public sealed record RegulatedAppointment(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string OriginUnit,
    string Specialty,
    string Priority,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ScheduledAt,
    string? DestinationUnit,
    string? Notes);

public sealed record CreateRegulationRequest(
    Guid CitizenId,
    string OriginUnit,
    string Specialty,
    string Priority,
    string? DestinationUnit,
    string? Notes);

public sealed record ProductionItem(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string ProcedureCode,
    string Cbo,
    string Cid,
    DateOnly ServiceDate,
    decimal Amount,
    string Source);

public sealed record BillingValidationIssue(Guid ProductionId, string Code, string Message, string Severity);

public sealed class BillingBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Competence { get; init; } = string.Empty;
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    public List<ProductionItem> Items { get; } = [];
    public List<BillingValidationIssue> Issues { get; } = [];
}

public sealed record CreateBillingBatchRequest(string Competence);

public sealed class VaccineLot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Vaccine { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Lot { get; init; } = string.Empty;
    public DateOnly ExpiresOn { get; init; }
    public int Stock { get; set; }
}

public sealed record ImmunizationEvent(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string Vaccine,
    string Dose,
    string Route,
    string Site,
    string Lot,
    string Professional,
    string ProfessionalCouncil,
    DateTimeOffset AppliedAt);

public sealed record AdministerVaccineRequest(
    Guid CitizenId,
    Guid VaccineLotId,
    string Dose,
    string Route,
    string Site,
    string Professional,
    string ProfessionalCouncil);

public sealed class InventoryLot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ItemCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Lot { get; init; } = string.Empty;
    public DateOnly ExpiresOn { get; init; }
    public int Quantity { get; set; }
    public int MinimumStock { get; init; }
    public bool Controlled { get; init; }
}

public sealed record DispenseRequest(Guid CitizenId, Guid InventoryLotId, int Quantity, string PrescriptionReference, string Professional);

public sealed record InventoryMovement(
    Guid Id,
    Guid InventoryLotId,
    string ItemName,
    string Type,
    int Quantity,
    string Unit,
    Guid? CitizenId,
    string? Reference,
    string Actor,
    DateTimeOffset OccurredAt);

public sealed record Household(
    Guid Id,
    string Address,
    string Area,
    string MicroArea,
    string AcsName,
    IReadOnlyList<FamilyMember> Members);

public sealed record FamilyMember(Guid CitizenId, string Name, string Relationship, bool Bedridden, bool Pregnant);

public sealed record AcsVisit(
    Guid Id,
    Guid HouseholdId,
    string AcsName,
    DateTimeOffset OccurredAt,
    string VisitType,
    string Outcome,
    bool OfflineCaptured,
    DateTimeOffset? SyncedAt);

public sealed record AcsVisitInput(Guid HouseholdId, string AcsName, string VisitType, string Outcome, DateTimeOffset OccurredAt, bool OfflineCaptured);
public sealed record AcsSyncRequest(IReadOnlyList<AcsVisitInput> Visits);

public sealed class DentalChart
{
    public Guid CitizenId { get; init; }
    public Dictionary<int, ToothState> Teeth { get; } = new();
    public List<DentalHistoryEntry> History { get; } = [];
}

public sealed record ToothState(int Tooth, string Status, string? Surfaces, string? Procedure, string? Notes);
public sealed record UpdateToothRequest(string Status, string? Surfaces, string? Procedure, string? Notes, string Professional);
public sealed record DentalHistoryEntry(Guid Id, int Tooth, string Action, string Professional, DateTimeOffset OccurredAt);

public sealed record DiagnosticExam(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string Type,
    string Exam,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ScheduledAt,
    string Unit,
    string? Performer,
    string? ResultSummary);

public sealed record ScheduleExamRequest(Guid CitizenId, string Type, string Exam, DateTimeOffset ScheduledAt, string Unit, string? Performer);

public sealed record AuditEntry(Guid Id, string Actor, string Action, string Resource, string? Detail, DateTimeOffset OccurredAt);

public sealed record DashboardSnapshot(
    int Citizens,
    int WaitingRegulation,
    int ScheduledToday,
    int OpenBillingIssues,
    int LowStockLots,
    int VaccineLots,
    int PendingAcsSync,
    int ExamsToday,
    decimal ProductionValue,
    DateTimeOffset GeneratedAt);
