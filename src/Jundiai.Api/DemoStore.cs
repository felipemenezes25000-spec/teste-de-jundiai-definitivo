using System.Globalization;

namespace Jundiai.Api;

public sealed class DemoStore
{
    private readonly object _gate = new();
    private readonly List<Citizen> _citizens = [];
    private readonly List<RegulatedAppointment> _regulation = [];
    private readonly List<ProductionItem> _production = [];
    private readonly List<BillingBatch> _billing = [];
    private readonly List<VaccineLot> _vaccineLots = [];
    private readonly List<ImmunizationEvent> _immunizations = [];
    private readonly List<InventoryLot> _inventory = [];
    private readonly List<InventoryMovement> _movements = [];
    private readonly List<Household> _households = [];
    private readonly List<AcsVisit> _acsVisits = [];
    private readonly Dictionary<Guid, DentalChart> _dentalCharts = new();
    private readonly List<DiagnosticExam> _exams = [];
    private readonly List<AuditEntry> _audit = [];

    public DemoStore() => Seed();

    public IReadOnlyList<Citizen> Citizens()
    {
        lock (_gate) return _citizens.OrderBy(x => x.Name).ToList();
    }

    public Citizen? Citizen(Guid id)
    {
        lock (_gate) return _citizens.FirstOrDefault(x => x.Id == id);
    }

    public IReadOnlyList<RegulatedAppointment> Regulation()
    {
        lock (_gate) return _regulation.OrderByDescending(x => x.RequestedAt).ToList();
    }

    public RegulatedAppointment CreateRegulation(CreateRegulationRequest request)
    {
        lock (_gate)
        {
            var citizen = RequireCitizen(request.CitizenId);
            var item = new RegulatedAppointment(
                Guid.NewGuid(), citizen.Id, citizen.Name, request.OriginUnit, request.Specialty,
                NormalizePriority(request.Priority), "waiting", DateTimeOffset.UtcNow, null,
                request.DestinationUnit, request.Notes);
            _regulation.Add(item);
            AddAudit("poc.operator", "regulation.create", $"regulation:{item.Id}", $"{citizen.Name} · {item.Specialty}");
            return item;
        }
    }

    public RegulatedAppointment? AdvanceRegulation(Guid id, string status, DateTimeOffset? scheduledAt)
    {
        lock (_gate)
        {
            var index = _regulation.FindIndex(x => x.Id == id);
            if (index < 0) return null;
            var current = _regulation[index];
            var allowed = new[] { "waiting", "authorized", "scheduled", "checked_in", "completed", "cancelled", "no_show" };
            if (!allowed.Contains(status, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Status regulatório inválido.");

            var updated = current with
            {
                Status = status.ToLowerInvariant(),
                ScheduledAt = scheduledAt ?? current.ScheduledAt
            };
            _regulation[index] = updated;
            AddAudit("poc.operator", "regulation.transition", $"regulation:{id}", $"{current.Status} -> {updated.Status}");
            return updated;
        }
    }

    public IReadOnlyList<ProductionItem> Production()
    {
        lock (_gate) return _production.OrderByDescending(x => x.ServiceDate).ToList();
    }

    public IReadOnlyList<BillingBatch> BillingBatches()
    {
        lock (_gate) return _billing.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public BillingBatch CreateBillingBatch(string competence)
    {
        lock (_gate)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(competence ?? string.Empty, "^[0-9]{6}$"))
                throw new InvalidOperationException("Competência deve estar no formato AAAAMM.");
            if (_billing.Any(x => x.Competence == competence && x.Status != "cancelled"))
                throw new InvalidOperationException("Já existe lote para esta competência.");

            var year = int.Parse(competence[..4], CultureInfo.InvariantCulture);
            var month = int.Parse(competence[4..], CultureInfo.InvariantCulture);
            if (month is < 1 or > 12) throw new InvalidOperationException("Mês da competência inválido.");

            var batch = new BillingBatch { Competence = competence };
            batch.Items.AddRange(_production.Where(x => x.ServiceDate.Year == year && x.ServiceDate.Month == month));
            ValidateBilling(batch);
            _billing.Add(batch);
            AddAudit("poc.operator", "sus.billing.batch.create", $"billing:{batch.Id}", competence);
            return batch;
        }
    }

    public BillingBatch? RevalidateBilling(Guid id)
    {
        lock (_gate)
        {
            var batch = _billing.FirstOrDefault(x => x.Id == id);
            if (batch is null) return null;
            ValidateBilling(batch);
            AddAudit("poc.operator", "sus.billing.batch.validate", $"billing:{id}", $"issues={batch.Issues.Count}");
            return batch;
        }
    }

    public BillingBatch? CloseBilling(Guid id)
    {
        lock (_gate)
        {
            var batch = _billing.FirstOrDefault(x => x.Id == id);
            if (batch is null) return null;
            ValidateBilling(batch);
            if (batch.Issues.Any(x => x.Severity == "error"))
                throw new InvalidOperationException("O lote possui críticas impeditivas.");
            batch.Status = "closed";
            batch.ClosedAt = DateTimeOffset.UtcNow;
            AddAudit("poc.operator", "sus.billing.batch.close", $"billing:{id}", batch.Competence);
            return batch;
        }
    }

    public string ExportBillingDemonstration(Guid id)
    {
        lock (_gate)
        {
            var batch = _billing.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
            if (batch.Status != "closed") throw new InvalidOperationException("Feche o lote antes de exportar.");
            var lines = new List<string>
            {
                "# DEMONSTRACAO BPA/E-SUS - NAO E ARQUIVO OFICIAL DATASUS",
                $"COMPETENCIA|{batch.Competence}",
                "PROCEDIMENTO|CNS|CBO|CID|DATA|VALOR|ORIGEM"
            };
            foreach (var item in batch.Items)
            {
                var citizen = RequireCitizen(item.CitizenId);
                lines.Add($"{item.ProcedureCode}|{citizen.Cns}|{item.Cbo}|{item.Cid}|{item.ServiceDate:yyyyMMdd}|{item.Amount:0.00}|{item.Source}");
            }
            AddAudit("poc.operator", "sus.billing.batch.export_demo", $"billing:{id}", batch.Competence);
            return string.Join('\n', lines);
        }
    }

    public IReadOnlyList<VaccineLot> VaccineLots()
    {
        lock (_gate) return _vaccineLots.OrderBy(x => x.ExpiresOn).ToList();
    }

    public IReadOnlyList<ImmunizationEvent> Immunizations(Guid? citizenId = null)
    {
        lock (_gate)
        {
            IEnumerable<ImmunizationEvent> query = _immunizations;
            if (citizenId.HasValue) query = query.Where(x => x.CitizenId == citizenId.Value);
            return query.OrderByDescending(x => x.AppliedAt).ToList();
        }
    }

    public ImmunizationEvent AdministerVaccine(AdministerVaccineRequest request)
    {
        lock (_gate)
        {
            var citizen = RequireCitizen(request.CitizenId);
            var lot = _vaccineLots.FirstOrDefault(x => x.Id == request.VaccineLotId) ?? throw new InvalidOperationException("Lote vacinal não encontrado.");
            if (lot.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow)) throw new InvalidOperationException("Lote vacinal vencido.");
            if (lot.Stock <= 0) throw new InvalidOperationException("Lote vacinal sem estoque.");
            lot.Stock--;
            var evt = new ImmunizationEvent(Guid.NewGuid(), citizen.Id, citizen.Name, lot.Vaccine, request.Dose,
                request.Route, request.Site, lot.Lot, request.Professional, request.ProfessionalCouncil, DateTimeOffset.UtcNow);
            _immunizations.Add(evt);
            AddAudit(request.Professional, "immunization.administer", $"citizen:{citizen.Id}", $"{lot.Vaccine} {request.Dose} lote {lot.Lot}");
            return evt;
        }
    }

    public IReadOnlyList<InventoryLot> Inventory()
    {
        lock (_gate) return _inventory.OrderBy(x => x.Name).ThenBy(x => x.ExpiresOn).ToList();
    }

    public IReadOnlyList<InventoryMovement> InventoryMovements()
    {
        lock (_gate) return _movements.OrderByDescending(x => x.OccurredAt).Take(100).ToList();
    }

    public InventoryMovement Dispense(DispenseRequest request)
    {
        lock (_gate)
        {
            var citizen = RequireCitizen(request.CitizenId);
            var lot = _inventory.FirstOrDefault(x => x.Id == request.InventoryLotId) ?? throw new InvalidOperationException("Lote de estoque não encontrado.");
            if (request.Quantity <= 0) throw new InvalidOperationException("Quantidade deve ser positiva.");
            if (lot.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow)) throw new InvalidOperationException("Lote vencido não pode ser dispensado.");
            if (lot.Quantity < request.Quantity) throw new InvalidOperationException("Estoque insuficiente.");
            if (lot.Controlled && string.IsNullOrWhiteSpace(request.PrescriptionReference)) throw new InvalidOperationException("Medicamento controlado exige referência da prescrição.");
            lot.Quantity -= request.Quantity;
            var movement = new InventoryMovement(Guid.NewGuid(), lot.Id, lot.Name, "dispense", request.Quantity, lot.Unit,
                citizen.Id, request.PrescriptionReference, request.Professional, DateTimeOffset.UtcNow);
            _movements.Add(movement);
            AddAudit(request.Professional, "pharmacy.dispense", $"inventory:{lot.Id}", $"{citizen.Name} · {request.Quantity} {lot.Unit}");
            return movement;
        }
    }

    public IReadOnlyList<Household> Households()
    {
        lock (_gate) return _households.OrderBy(x => x.MicroArea).ThenBy(x => x.Address).ToList();
    }

    public IReadOnlyList<AcsVisit> AcsVisits()
    {
        lock (_gate) return _acsVisits.OrderByDescending(x => x.OccurredAt).ToList();
    }

    public IReadOnlyList<AcsVisit> SyncAcs(AcsSyncRequest request)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var created = new List<AcsVisit>();
            foreach (var input in request.Visits)
            {
                if (_households.All(x => x.Id != input.HouseholdId)) continue;
                var visit = new AcsVisit(Guid.NewGuid(), input.HouseholdId, input.AcsName, input.OccurredAt,
                    input.VisitType, input.Outcome, input.OfflineCaptured, now);
                _acsVisits.Add(visit);
                created.Add(visit);
            }
            AddAudit("acs.sync", "psf.acs.sync", "acs-visits", $"count={created.Count}");
            return created;
        }
    }

    public DentalChart Dental(Guid citizenId)
    {
        lock (_gate)
        {
            RequireCitizen(citizenId);
            if (!_dentalCharts.TryGetValue(citizenId, out var chart))
            {
                chart = CreateDentalChart(citizenId);
                _dentalCharts[citizenId] = chart;
            }
            return chart;
        }
    }

    public DentalChart UpdateTooth(Guid citizenId, int tooth, UpdateToothRequest request)
    {
        lock (_gate)
        {
            if (!ValidTooth(tooth)) throw new InvalidOperationException("Elemento dentário inválido.");
            var chart = Dental(citizenId);
            chart.Teeth[tooth] = new ToothState(tooth, request.Status, request.Surfaces, request.Procedure, request.Notes);
            chart.History.Add(new DentalHistoryEntry(Guid.NewGuid(), tooth, $"{request.Status} · {request.Procedure}", request.Professional, DateTimeOffset.UtcNow));
            AddAudit(request.Professional, "dental.odontogram.update", $"citizen:{citizenId}:tooth:{tooth}", request.Status);
            return chart;
        }
    }

    public IReadOnlyList<DiagnosticExam> Exams()
    {
        lock (_gate) return _exams.OrderByDescending(x => x.RequestedAt).ToList();
    }

    public DiagnosticExam ScheduleExam(ScheduleExamRequest request)
    {
        lock (_gate)
        {
            var citizen = RequireCitizen(request.CitizenId);
            var exam = new DiagnosticExam(Guid.NewGuid(), citizen.Id, citizen.Name, request.Type, request.Exam, "scheduled",
                DateTimeOffset.UtcNow, request.ScheduledAt, request.Unit, request.Performer, null);
            _exams.Add(exam);
            AddAudit("poc.operator", "diagnostics.schedule", $"exam:{exam.Id}", $"{citizen.Name} · {request.Exam}");
            return exam;
        }
    }

    public IReadOnlyList<AuditEntry> Audit()
    {
        lock (_gate) return _audit.OrderByDescending(x => x.OccurredAt).Take(250).ToList();
    }

    public DashboardSnapshot Dashboard()
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            return new DashboardSnapshot(
                _citizens.Count,
                _regulation.Count(x => x.Status is "waiting" or "authorized"),
                _regulation.Count(x => x.ScheduledAt.HasValue && DateOnly.FromDateTime(x.ScheduledAt.Value.LocalDateTime) == today),
                _billing.Sum(x => x.Issues.Count(i => i.Severity == "error")),
                _inventory.Count(x => x.Quantity <= x.MinimumStock),
                _vaccineLots.Count,
                _acsVisits.Count(x => x.OfflineCaptured && !x.SyncedAt.HasValue),
                _exams.Count(x => x.ScheduledAt.HasValue && DateOnly.FromDateTime(x.ScheduledAt.Value.LocalDateTime) == today),
                _production.Sum(x => x.Amount),
                DateTimeOffset.UtcNow);
        }
    }

    public void AuditExternal(string actor, string action, string resource, string? detail)
    {
        lock (_gate) AddAudit(actor, action, resource, detail);
    }

    private Citizen RequireCitizen(Guid id) => _citizens.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Cidadão não encontrado.");

    private void ValidateBilling(BillingBatch batch)
    {
        batch.Issues.Clear();
        foreach (var item in batch.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ProcedureCode) || item.ProcedureCode.Length < 8)
                batch.Issues.Add(new(item.Id, "PROCEDURE_INVALID", "Procedimento SIGTAP inválido.", "error"));
            if (string.IsNullOrWhiteSpace(item.Cbo))
                batch.Issues.Add(new(item.Id, "CBO_REQUIRED", "CBO é obrigatório.", "error"));
            if (string.IsNullOrWhiteSpace(item.Cid))
                batch.Issues.Add(new(item.Id, "CID_REQUIRED", "CID é obrigatório para esta demonstração.", "warning"));
            if (item.Amount < 0)
                batch.Issues.Add(new(item.Id, "AMOUNT_INVALID", "Valor não pode ser negativo.", "error"));
        }
        batch.Status = batch.Issues.Any(x => x.Severity == "error") ? "with_issues" : "validated";
    }

    private void AddAudit(string actor, string action, string resource, string? detail) =>
        _audit.Add(new AuditEntry(Guid.NewGuid(), actor, action, resource, detail, DateTimeOffset.UtcNow));

    private static string NormalizePriority(string value) => value.Trim().ToLowerInvariant() switch
    {
        "emergency" or "emergencia" or "emergência" => "emergency",
        "urgent" or "urgente" => "urgent",
        "high" or "alta" => "high",
        _ => "routine"
    };

    private static bool ValidTooth(int tooth)
    {
        var quadrant = tooth / 10;
        var position = tooth % 10;
        return quadrant is >= 1 and <= 8 && position is >= 1 and <= 8;
    }

    private static DentalChart CreateDentalChart(Guid citizenId)
    {
        var chart = new DentalChart { CitizenId = citizenId };
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
        foreach (var position in Enumerable.Range(1, 8))
        {
            var tooth = quadrant * 10 + position;
            chart.Teeth[tooth] = new ToothState(tooth, "healthy", null, null, null);
        }
        return chart;
    }

    private void Seed()
    {
        var familyA = Guid.NewGuid();
        var familyB = Guid.NewGuid();
        var maria = new Citizen(Guid.NewGuid(), "898001234567890", "12345678901", "Maria Aparecida Souza", new DateOnly(1968, 4, 19), "UBS Vila Hortolândia", "04", "04-02", familyA);
        var joao = new Citizen(Guid.NewGuid(), "898009876543210", "98765432100", "João Carlos Lima", new DateOnly(1981, 11, 3), "UBS Novo Horizonte", "08", "08-01", familyB);
        var ana = new Citizen(Guid.NewGuid(), "898004440001112", "44400011122", "Ana Luiza Ribeiro", new DateOnly(1994, 7, 28), "UBS Vila Hortolândia", "04", "04-02", familyA);
        _citizens.AddRange([maria, joao, ana]);

        _regulation.AddRange([
            new(Guid.NewGuid(), maria.Id, maria.Name, maria.HealthUnit, "Cardiologia", "high", "waiting", DateTimeOffset.UtcNow.AddDays(-3), null, "AME Jundiaí", "Hipertensão de difícil controle"),
            new(Guid.NewGuid(), joao.Id, joao.Name, joao.HealthUnit, "Ortopedia", "routine", "scheduled", DateTimeOffset.UtcNow.AddDays(-8), DateTimeOffset.Now.Date.AddDays(1).AddHours(9), "Ambulatório Central", "Dor crônica em joelho")
        ]);

        _production.AddRange([
            new(Guid.NewGuid(), maria.Id, maria.Name, "0301010072", "225125", "I10", DateOnly.FromDateTime(DateTime.Today), 10.00m, "medical_consultation"),
            new(Guid.NewGuid(), joao.Id, joao.Name, "0301010072", "225250", "M17", DateOnly.FromDateTime(DateTime.Today), 10.00m, "medical_consultation"),
            new(Guid.NewGuid(), ana.Id, ana.Name, "0301010030", "223505", "Z00", DateOnly.FromDateTime(DateTime.Today), 8.00m, "nursing")
        ]);

        _vaccineLots.AddRange([
            new() { Vaccine = "Influenza", Manufacturer = "Instituto Butantan", Lot = "FLU26A19", ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddMonths(8)), Stock = 124 },
            new() { Vaccine = "dT", Manufacturer = "Bio-Manguinhos", Lot = "DT26031", ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddMonths(12)), Stock = 67 }
        ]);

        _inventory.AddRange([
            new() { ItemCode = "MED-001", Name = "Losartana 50 mg", Unit = "UBS Vila Hortolândia", Lot = "LST2608", ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddMonths(10)), Quantity = 320, MinimumStock = 100, Controlled = false },
            new() { ItemCode = "MED-002", Name = "Metformina 850 mg", Unit = "UBS Vila Hortolândia", Lot = "MTF2604", ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddMonths(7)), Quantity = 82, MinimumStock = 100, Controlled = false },
            new() { ItemCode = "MED-CTRL-01", Name = "Medicamento sujeito a controle especial — demonstração", Unit = "Farmácia Central", Lot = "CTRL2601", ExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddMonths(5)), Quantity = 40, MinimumStock = 15, Controlled = true }
        ]);

        _households.AddRange([
            new(Guid.NewGuid(), "Rua das Acácias, 120", "04", "04-02", "Carla Mendes", [new(maria.Id, maria.Name, "responsável", false, false), new(ana.Id, ana.Name, "filha", false, false)]),
            new(Guid.NewGuid(), "Av. do Sol, 884", "08", "08-01", "Paulo Nunes", [new(joao.Id, joao.Name, "responsável", false, false)])
        ]);

        _exams.AddRange([
            new(Guid.NewGuid(), maria.Id, maria.Name, "laboratory", "Hemograma completo", "scheduled", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.Now.Date.AddHours(14), "Laboratório Municipal", "Biomedicina 01", null),
            new(Guid.NewGuid(), joao.Id, joao.Name, "imaging", "Radiografia de joelho", "requested", DateTimeOffset.UtcNow.AddHours(-7), null, "Centro de Imagem", null, null)
        ]);

        _dentalCharts[ana.Id] = CreateDentalChart(ana.Id);
        _dentalCharts[ana.Id].Teeth[16] = new ToothState(16, "caries", "O", "restauração indicada", "Lesão oclusal");
        AddAudit("system", "demo.seed", "dataset", "Base inicial coerente para POC");
    }
}
