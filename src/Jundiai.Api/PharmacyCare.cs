using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class PharmacyCareEndpoints
{
    public static IEndpointRouteBuilder MapPharmacyCareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/pharmacy/v2/reconciliations", (Guid? citizenId, PharmacyCareStore store) => Results.Ok(store.Reconciliations(citizenId)));
        endpoints.MapPost("/api/pharmacy/v2/reconciliations", (
            CreateMedicationReconciliationRequest request,
            PharmacyCareStore store,
            DemoStore demo,
            ClinicalOrderStore orders,
            EvidenceLedgerStore evidence) =>
        {
            var item = store.Reconcile(request, demo, orders);
            demo.AuditExternal(request.Pharmacist, "pharmacy.reconciliation.create", $"reconciliation:{item.Id}", $"discrepancies={item.Discrepancies.Count}");
            evidence.Append(new CreateEvidenceEventRequest(request.Pharmacist, "pharmacy.reconciliation.create", $"reconciliation:{item.Id}", "POC-B11", $"status={item.Status};discrepancies={item.Discrepancies.Count}", "medication-reconciliation"));
            return Results.Created($"/api/pharmacy/v2/reconciliations/{item.Id}", item);
        });

        endpoints.MapGet("/api/pharmacy/v2/dispensations", (Guid? citizenId, PharmacyCareStore store) => Results.Ok(store.Dispensations(citizenId)));
        endpoints.MapPost("/api/pharmacy/v2/dispense", (
            PharmacyCareDispenseRequest request,
            PharmacyCareStore store,
            DemoStore demo,
            ClinicalOrderStore orders,
            EvidenceLedgerStore evidence) =>
        {
            var result = store.Dispense(request, demo, orders);
            evidence.Append(new CreateEvidenceEventRequest(request.Pharmacist, "pharmacy.v2.dispense", $"dispensation:{result.Id}", "POC-B11", $"order={result.ClinicalOrderId};lot={result.InventoryLotId};qty={result.Quantity}", "dispensation"));
            return Results.Created($"/api/pharmacy/v2/dispensations/{result.Id}", result);
        });

        endpoints.MapPost("/api/pharmacy/v2/dispensations/{id:guid}/counsel", (Guid id, PharmacyCounselingRequest request, PharmacyCareStore store) =>
            Results.Ok(store.RecordCounseling(id, request)));

        endpoints.MapGet("/api/pharmacy/v2/readiness", (PharmacyCareStore store, DemoStore demo, ClinicalOrderStore orders) => Results.Ok(new
        {
            reconciliations = store.Reconciliations(null).Count,
            dispensations = store.Dispensations(null).Count,
            activeMedicationOrders = orders.Orders(null).Count(x => x.Status == "active"),
            stockLots = demo.Inventory().Count,
            capabilities = new[]
            {
                "medication reconciliation", "active clinical order validation", "lot/expiry/stock validation", "controlled prescription reference",
                "dispensation linked to clinical order", "discrepancy register", "pharmacist counseling", "audit/evidence hooks"
            },
            productionDependencies = new[] { "official medication catalog", "municipal formulary", "clinical interaction database", "BNAFAR/Horus credentials when applicable" },
            disclaimer = "POC does not perform autonomous clinical interaction decisions; discrepancies require human professional review."
        }));
        return endpoints;
    }
}

public sealed class PharmacyCareStore
{
    private readonly ConcurrentDictionary<Guid, MedicationReconciliation> _reconciliations = new();
    private readonly ConcurrentDictionary<Guid, PharmacyDispensation> _dispensations = new();

    public IReadOnlyList<MedicationReconciliation> Reconciliations(Guid? citizenId) => _reconciliations.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public IReadOnlyList<PharmacyDispensation> Dispensations(Guid? citizenId) => _dispensations.Values
        .Where(x => citizenId is null || x.CitizenId == citizenId.Value)
        .OrderByDescending(x => x.DispensedAt)
        .ToList();

    public MedicationReconciliation Reconcile(CreateMedicationReconciliationRequest request, DemoStore demo, ClinicalOrderStore orders)
    {
        var citizen = demo.Citizen(request.CitizenId) ?? throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Pharmacist) || string.IsNullOrWhiteSpace(request.ProfessionalCouncil))
            throw new ArgumentException("Farmacêutico e conselho são obrigatórios.");

        var activeOrders = orders.Orders(citizen.Id).Where(x => x.Status == "active").ToArray();
        var reported = (request.ReportedMedications ?? Array.Empty<ReportedMedication>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x with { Name = x.Name.Trim(), Dose = x.Dose?.Trim(), Frequency = x.Frequency?.Trim(), Source = string.IsNullOrWhiteSpace(x.Source) ? "patient_report" : x.Source.Trim() })
            .ToArray();
        var discrepancies = new List<MedicationDiscrepancy>();

        foreach (var order in activeOrders)
        {
            var match = reported.FirstOrDefault(x => SimilarMedication(x.Name, order.Medication));
            if (match is null)
            {
                discrepancies.Add(new MedicationDiscrepancy(Guid.NewGuid(), "ordered_not_reported", order.Medication, "Ordem ativa não apareceu na lista informada.", "review", null));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(match.Dose) && !Normalize(match.Dose).Equals(Normalize(order.Dose), StringComparison.Ordinal))
                discrepancies.Add(new MedicationDiscrepancy(Guid.NewGuid(), "dose_difference", order.Medication, $"Dose informada '{match.Dose}' difere da ordem '{order.Dose}'.", "review", null));
            if (!string.IsNullOrWhiteSpace(match.Frequency) && !Normalize(match.Frequency).Equals(Normalize(order.Frequency), StringComparison.Ordinal))
                discrepancies.Add(new MedicationDiscrepancy(Guid.NewGuid(), "frequency_difference", order.Medication, $"Frequência informada '{match.Frequency}' difere da ordem '{order.Frequency}'.", "review", null));
        }

        foreach (var medication in reported)
        {
            if (!activeOrders.Any(x => SimilarMedication(medication.Name, x.Medication)))
                discrepancies.Add(new MedicationDiscrepancy(Guid.NewGuid(), "reported_without_order", medication.Name, "Medicamento informado sem ordem clínica ativa na POC.", "review", null));
        }

        var status = discrepancies.Count == 0 ? "reconciled" : "review_required";
        var item = new MedicationReconciliation(Guid.NewGuid(), citizen.Id, citizen.Name, activeOrders.Select(x => x.Id).ToArray(), reported, discrepancies, status, request.Pharmacist.Trim(), request.ProfessionalCouncil.Trim(), request.Context?.Trim() ?? "ambulatory", DateTimeOffset.UtcNow);
        _reconciliations[item.Id] = item;
        return item;
    }

    public PharmacyDispensation Dispense(PharmacyCareDispenseRequest request, DemoStore demo, ClinicalOrderStore orders)
    {
        var order = orders.Get(request.ClinicalOrderId) ?? throw new KeyNotFoundException();
        if (order.CitizenId != request.CitizenId) throw new InvalidOperationException("Ordem clínica pertence a outro cidadão.");
        if (order.Status != "active") throw new InvalidOperationException("Dispensação exige ordem clínica ativa.");
        var lot = demo.Inventory().FirstOrDefault(x => x.Id == request.InventoryLotId) ?? throw new KeyNotFoundException();
        var warnings = new List<string>();
        if (!SimilarMedication(order.Medication, lot.Name))
            warnings.Add("Nome do item de estoque não corresponde exatamente à descrição da ordem; conferência farmacêutica humana obrigatória na POC.");
        if (lot.ExpiresOn <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)))
            warnings.Add("Lote com vencimento em até 30 dias; aplicar política FEFO e orientação local.");
        if (lot.Controlled && string.IsNullOrWhiteSpace(request.PrescriptionReference))
            throw new InvalidOperationException("Item controlado exige referência de prescrição.");

        var movement = demo.Dispense(new DispenseRequest(request.CitizenId, request.InventoryLotId, request.Quantity, request.PrescriptionReference ?? string.Empty, request.Pharmacist));
        var item = new PharmacyDispensation(
            Guid.NewGuid(), request.CitizenId, order.CitizenName, order.Id, order.Medication, lot.Id, lot.Lot, request.Quantity, lot.Unit,
            request.PrescriptionReference?.Trim(), request.Pharmacist.Trim(), request.ProfessionalCouncil.Trim(), warnings,
            "dispensed", null, null, movement.Id, DateTimeOffset.UtcNow);
        _dispensations[item.Id] = item;
        return item;
    }

    public PharmacyDispensation RecordCounseling(Guid id, PharmacyCounselingRequest request)
    {
        if (!_dispensations.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (string.IsNullOrWhiteSpace(request.Pharmacist) || string.IsNullOrWhiteSpace(request.Note))
            throw new ArgumentException("Farmacêutico e orientação são obrigatórios.");
        var updated = current with { CounselingBy = request.Pharmacist.Trim(), CounselingNote = request.Note.Trim() };
        _dispensations[id] = updated;
        return updated;
    }

    private static bool SimilarMedication(string a, string b)
    {
        var na = Normalize(a); var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        return na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal) || CommonTokens(na, nb) >= 1;
    }
    private static int CommonTokens(string a, string b)
    {
        var aa = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 4).ToHashSet(StringComparer.Ordinal);
        return b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(aa.Contains);
    }
    private static string Normalize(string? value)
    {
        var input = (value ?? string.Empty).ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(input.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))).ToArray()).Trim();
    }
}

public sealed record ReportedMedication(string Name, string? Dose, string? Frequency, string Source);
public sealed record CreateMedicationReconciliationRequest(Guid CitizenId, IReadOnlyList<ReportedMedication>? ReportedMedications, string Pharmacist, string ProfessionalCouncil, string? Context);
public sealed record MedicationDiscrepancy(Guid Id, string Type, string Medication, string Description, string Status, string? Resolution);
public sealed record MedicationReconciliation(Guid Id, Guid CitizenId, string CitizenName, IReadOnlyList<Guid> ActiveOrderIds, IReadOnlyList<ReportedMedication> ReportedMedications, IReadOnlyList<MedicationDiscrepancy> Discrepancies, string Status, string Pharmacist, string ProfessionalCouncil, string Context, DateTimeOffset CreatedAt);
public sealed record PharmacyCareDispenseRequest(Guid CitizenId, Guid ClinicalOrderId, Guid InventoryLotId, int Quantity, string? PrescriptionReference, string Pharmacist, string ProfessionalCouncil);
public sealed record PharmacyCounselingRequest(string Pharmacist, string Note);
public sealed record PharmacyDispensation(Guid Id, Guid CitizenId, string CitizenName, Guid ClinicalOrderId, string Medication, Guid InventoryLotId, string Lot, int Quantity, string Unit, string? PrescriptionReference, string Pharmacist, string ProfessionalCouncil, IReadOnlyList<string> Warnings, string Status, string? CounselingBy, string? CounselingNote, Guid InventoryMovementId, DateTimeOffset DispensedAt);