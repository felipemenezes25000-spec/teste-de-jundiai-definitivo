using System.Collections.Concurrent;

namespace Jundiai.Api;

public sealed class MunicipalOperationsStore
{
    private readonly object _gate = new();
    private readonly List<HealthUnitDemo> _units = [];
    private readonly List<ReceptionTicket> _reception = [];
    private readonly List<DigitizedRecord> _records = [];
    private readonly List<WarehouseItem> _warehouse = [];
    private readonly List<WarehouseMovement> _warehouseMovements = [];
    private readonly List<VaccinationCampaign> _campaigns = [];
    private readonly List<DiagnosticMutirao> _mutiroes = [];

    public MunicipalOperationsStore()
    {
        Seed();
    }

    public IReadOnlyList<HealthUnitDemo> Units()
    {
        lock (_gate) return _units.OrderBy(x => x.Name).ToList();
    }

    public IReadOnlyList<ReceptionTicket> ReceptionQueue()
    {
        lock (_gate) return _reception.OrderBy(x => x.Status == "waiting" ? 0 : 1).ThenBy(x => x.ArrivedAt).ToList();
    }

    public ReceptionTicket CheckIn(CheckInRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            var citizen = clinical.Citizen(request.CitizenId) ?? throw new InvalidOperationException("Cidadão não encontrado.");
            var unit = _units.FirstOrDefault(x => x.Code.Equals(request.UnitCode, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("Unidade não encontrada.");
            var ticket = new ReceptionTicket(
                Guid.NewGuid(), citizen.Id, citizen.Name, unit.Code, unit.Name,
                request.Service.Trim(), NormalizePriority(request.Priority), "waiting",
                DateTimeOffset.UtcNow, null, null, request.Notes?.Trim());
            _reception.Add(ticket);
            clinical.AuditExternal("ubs.reception", "ubs.checkin", $"ticket:{ticket.Id}", $"{citizen.Name} · {unit.Name}");
            return ticket;
        }
    }

    public ReceptionTicket? Call(Guid ticketId, string room, string professional, DemoStore clinical)
    {
        lock (_gate)
        {
            var index = _reception.FindIndex(x => x.Id == ticketId);
            if (index < 0) return null;
            var current = _reception[index];
            var updated = current with
            {
                Status = "called",
                CalledAt = DateTimeOffset.UtcNow,
                Room = room.Trim()
            };
            _reception[index] = updated;
            clinical.AuditExternal(professional, "ubs.reception.call", $"ticket:{ticketId}", room);
            return updated;
        }
    }

    public IReadOnlyList<DigitizedRecord> Records(Guid? citizenId = null)
    {
        lock (_gate)
        {
            IEnumerable<DigitizedRecord> query = _records;
            if (citizenId.HasValue) query = query.Where(x => x.CitizenId == citizenId.Value);
            return query.OrderByDescending(x => x.ScannedAt).ToList();
        }
    }

    public DigitizedRecord RegisterRecord(RegisterDigitizedRecordRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            var citizen = clinical.Citizen(request.CitizenId) ?? throw new InvalidOperationException("Cidadão não encontrado.");
            if (request.Pages <= 0) throw new InvalidOperationException("Quantidade de páginas deve ser positiva.");
            var barcode = string.IsNullOrWhiteSpace(request.Barcode)
                ? $"JDI-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..28].ToUpperInvariant()
                : request.Barcode.Trim().ToUpperInvariant();
            if (_records.Any(x => x.Barcode.Equals(barcode, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Código de barras já utilizado.");

            var record = new DigitizedRecord(
                Guid.NewGuid(), citizen.Id, citizen.Name, barcode, request.DocumentType.Trim(), request.Pages,
                request.SourceUnit.Trim(), request.StorageReference?.Trim(), "available",
                DateTimeOffset.UtcNow, null, null, null);
            _records.Add(record);
            clinical.AuditExternal(request.Actor, "records.scan.register", $"record:{record.Id}", $"barcode={record.Barcode};pages={record.Pages}");
            return record;
        }
    }

    public DigitizedRecord? CheckoutRecord(Guid id, RecordCustodyRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            var index = _records.FindIndex(x => x.Id == id);
            if (index < 0) return null;
            var current = _records[index];
            if (current.Status == "checked_out") throw new InvalidOperationException("Prontuário já está em custódia externa.");
            var updated = current with
            {
                Status = "checked_out",
                CheckedOutAt = DateTimeOffset.UtcNow,
                Custodian = request.Actor.Trim(),
                CustodyReason = request.Reason.Trim()
            };
            _records[index] = updated;
            clinical.AuditExternal(request.Actor, "records.custody.checkout", $"record:{id}", request.Reason);
            return updated;
        }
    }

    public DigitizedRecord? ReturnRecord(Guid id, string actor, DemoStore clinical)
    {
        lock (_gate)
        {
            var index = _records.FindIndex(x => x.Id == id);
            if (index < 0) return null;
            var current = _records[index];
            if (current.Status != "checked_out") throw new InvalidOperationException("Prontuário não está em custódia externa.");
            var updated = current with
            {
                Status = "available",
                ReturnedAt = DateTimeOffset.UtcNow,
                Custodian = null,
                CustodyReason = null
            };
            _records[index] = updated;
            clinical.AuditExternal(actor, "records.custody.return", $"record:{id}", current.Barcode);
            return updated;
        }
    }

    public IReadOnlyList<WarehouseItem> Warehouse()
    {
        lock (_gate) return _warehouse.OrderBy(x => x.Name).ThenBy(x => x.ExpiresOn).ToList();
    }

    public IReadOnlyList<WarehouseMovement> WarehouseMovements()
    {
        lock (_gate) return _warehouseMovements.OrderByDescending(x => x.OccurredAt).Take(200).ToList();
    }

    public WarehouseMovement ReceiveWarehouse(ReceiveWarehouseRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            if (request.Quantity <= 0) throw new InvalidOperationException("Quantidade deve ser positiva.");
            if (request.ExpiresOn <= DateOnly.FromDateTime(DateTime.UtcNow)) throw new InvalidOperationException("Validade precisa ser futura.");
            var existing = _warehouse.FirstOrDefault(x =>
                x.ItemCode.Equals(request.ItemCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                x.Lot.Equals(request.Lot.Trim(), StringComparison.OrdinalIgnoreCase) &&
                x.Location.Equals(request.Location.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new WarehouseItem(
                    Guid.NewGuid(), request.ItemCode.Trim(), request.Name.Trim(), request.Lot.Trim(),
                    request.ExpiresOn, request.Location.Trim(), request.Quantity, request.MinimumStock,
                    request.Supplier?.Trim(), request.InvoiceReference?.Trim());
                _warehouse.Add(existing);
            }
            else
            {
                var index = _warehouse.FindIndex(x => x.Id == existing.Id);
                existing = existing with { Quantity = existing.Quantity + request.Quantity };
                _warehouse[index] = existing;
            }

            var movement = new WarehouseMovement(
                Guid.NewGuid(), existing.Id, existing.Name, "receipt", request.Quantity,
                null, existing.Location, request.InvoiceReference, request.Actor, DateTimeOffset.UtcNow);
            _warehouseMovements.Add(movement);
            clinical.AuditExternal(request.Actor, "warehouse.receive", $"warehouse:{existing.Id}", $"qty={request.Quantity};invoice={request.InvoiceReference}");
            return movement;
        }
    }

    public WarehouseMovement TransferWarehouse(TransferWarehouseRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            var item = _warehouse.FirstOrDefault(x => x.Id == request.WarehouseItemId)
                       ?? throw new InvalidOperationException("Lote não encontrado no almoxarifado.");
            if (request.Quantity <= 0) throw new InvalidOperationException("Quantidade deve ser positiva.");
            if (item.Quantity < request.Quantity) throw new InvalidOperationException("Quantidade maior que o saldo disponível.");
            var unit = _units.FirstOrDefault(x => x.Code.Equals(request.DestinationUnitCode, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("Unidade de destino não encontrada.");

            var index = _warehouse.FindIndex(x => x.Id == item.Id);
            _warehouse[index] = item with { Quantity = item.Quantity - request.Quantity };
            var movement = new WarehouseMovement(
                Guid.NewGuid(), item.Id, item.Name, "transfer", request.Quantity,
                item.Location, unit.Name, request.Reference?.Trim(), request.Actor, DateTimeOffset.UtcNow);
            _warehouseMovements.Add(movement);
            clinical.AuditExternal(request.Actor, "warehouse.transfer", $"warehouse:{item.Id}", $"{item.Location}->{unit.Name};qty={request.Quantity}");
            return movement;
        }
    }

    public IReadOnlyList<VaccinationCampaign> Campaigns()
    {
        lock (_gate) return _campaigns.OrderByDescending(x => x.StartsOn).ToList();
    }

    public VaccinationCampaign CreateCampaign(CreateVaccinationCampaignRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            if (request.EndsOn < request.StartsOn) throw new InvalidOperationException("Fim da campanha não pode anteceder o início.");
            var campaign = new VaccinationCampaign(
                Guid.NewGuid(), request.Name.Trim(), request.Vaccine.Trim(), request.StartsOn,
                request.EndsOn, request.TargetPopulation.Trim(), request.UnitCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "planned", DateTimeOffset.UtcNow);
            _campaigns.Add(campaign);
            clinical.AuditExternal(request.Actor, "immunization.campaign.create", $"campaign:{campaign.Id}", campaign.Name);
            return campaign;
        }
    }

    public IReadOnlyList<DiagnosticMutirao> Mutiroes()
    {
        lock (_gate) return _mutiroes.OrderByDescending(x => x.StartsAt).ToList();
    }

    public DiagnosticMutirao CreateMutirao(CreateDiagnosticMutiraoRequest request, DemoStore clinical)
    {
        lock (_gate)
        {
            if (request.Capacity <= 0) throw new InvalidOperationException("Capacidade precisa ser positiva.");
            var mutirao = new DiagnosticMutirao(
                Guid.NewGuid(), request.Name.Trim(), request.Exam.Trim(), request.Unit.Trim(), request.StartsAt,
                request.EndsAt, request.Capacity, 0, request.Performer?.Trim(), "planned");
            _mutiroes.Add(mutirao);
            clinical.AuditExternal(request.Actor, "diagnostics.mutirao.create", $"mutirao:{mutirao.Id}", $"capacity={request.Capacity}");
            return mutirao;
        }
    }

    private static string NormalizePriority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "emergency" or "emergencia" or "emergência" => "emergency",
        "urgent" or "urgente" => "urgent",
        "high" or "alta" => "high",
        _ => "routine"
    };

    private void Seed()
    {
        var known = new[]
        {
            ("UBS-HORT", "UBS Vila Hortolândia", "UBS"),
            ("UBS-NH", "UBS Novo Horizonte", "UBS"),
            ("AMB-CENTRAL", "Ambulatório Central", "Ambulatório"),
            ("FARM-CENTRAL", "Farmácia Central", "Farmácia"),
            ("LAB-MUN", "Laboratório Municipal", "Diagnóstico"),
            ("IMG-CENTRAL", "Centro de Imagem", "Diagnóstico")
        };
        foreach (var item in known)
            _units.Add(new HealthUnitDemo(item.Item1, item.Item2, item.Item3, true));

        for (var i = known.Length + 1; i <= 58; i++)
            _units.Add(new HealthUnitDemo($"UNIT-{i:000}", $"Unidade Municipal demonstrativa {i:00}", "Rede", true));

        _warehouse.AddRange([
            new(Guid.NewGuid(), "MED-001", "Losartana 50 mg", "LST-CENT-2608", DateOnly.FromDateTime(DateTime.Today.AddMonths(11)), "Almoxarifado Central", 2400, 500, "Fornecedor homologado A", "NF-DEMO-2026-0081"),
            new(Guid.NewGuid(), "INS-010", "Seringa 3 mL", "SRG2607", DateOnly.FromDateTime(DateTime.Today.AddMonths(20)), "Almoxarifado Central", 5200, 1200, "Fornecedor homologado B", "NF-DEMO-2026-0082"),
            new(Guid.NewGuid(), "EPI-004", "Luva de procedimento M", "LUV2603", DateOnly.FromDateTime(DateTime.Today.AddMonths(18)), "Almoxarifado Central", 1800, 2000, "Fornecedor homologado C", "NF-DEMO-2026-0083")
        ]);

        _campaigns.Add(new VaccinationCampaign(Guid.NewGuid(), "Campanha Influenza 2026", "Influenza", DateOnly.FromDateTime(DateTime.Today.AddDays(-20)), DateOnly.FromDateTime(DateTime.Today.AddDays(40)), "Pessoas idosas, grupos prioritários e público definido pela campanha", ["UBS-HORT", "UBS-NH"], "active", DateTimeOffset.UtcNow.AddDays(-25)));
        _mutiroes.Add(new DiagnosticMutirao(Guid.NewGuid(), "Mutirão de ultrassonografia", "Ultrassonografia", "Centro de Imagem", DateTimeOffset.Now.Date.AddDays(5).AddHours(7), DateTimeOffset.Now.Date.AddDays(5).AddHours(17), 80, 37, "Equipe Diagnóstico 01", "planned"));
    }
}

public sealed record HealthUnitDemo(string Code, string Name, string Type, bool Active);
public sealed record ReceptionTicket(Guid Id, Guid CitizenId, string CitizenName, string UnitCode, string UnitName, string Service, string Priority, string Status, DateTimeOffset ArrivedAt, DateTimeOffset? CalledAt, string? Room, string? Notes);
public sealed record CheckInRequest(Guid CitizenId, string UnitCode, string Service, string? Priority, string? Notes);
public sealed record CallReceptionRequest(string Room, string Professional);

public sealed record DigitizedRecord(Guid Id, Guid CitizenId, string CitizenName, string Barcode, string DocumentType, int Pages, string SourceUnit, string? StorageReference, string Status, DateTimeOffset ScannedAt, DateTimeOffset? CheckedOutAt, DateTimeOffset? ReturnedAt, string? Custodian, string? CustodyReason = null);
public sealed record RegisterDigitizedRecordRequest(Guid CitizenId, string? Barcode, string DocumentType, int Pages, string SourceUnit, string? StorageReference, string Actor);
public sealed record RecordCustodyRequest(string Actor, string Reason);

public sealed record WarehouseItem(Guid Id, string ItemCode, string Name, string Lot, DateOnly ExpiresOn, string Location, int Quantity, int MinimumStock, string? Supplier, string? InvoiceReference);
public sealed record WarehouseMovement(Guid Id, Guid WarehouseItemId, string ItemName, string Type, int Quantity, string? From, string To, string? Reference, string Actor, DateTimeOffset OccurredAt);
public sealed record ReceiveWarehouseRequest(string ItemCode, string Name, string Lot, DateOnly ExpiresOn, string Location, int Quantity, int MinimumStock, string? Supplier, string? InvoiceReference, string Actor);
public sealed record TransferWarehouseRequest(Guid WarehouseItemId, int Quantity, string DestinationUnitCode, string? Reference, string Actor);

public sealed record VaccinationCampaign(Guid Id, string Name, string Vaccine, DateOnly StartsOn, DateOnly EndsOn, string TargetPopulation, IReadOnlyList<string> UnitCodes, string Status, DateTimeOffset CreatedAt);
public sealed record CreateVaccinationCampaignRequest(string Name, string Vaccine, DateOnly StartsOn, DateOnly EndsOn, string TargetPopulation, IReadOnlyList<string> UnitCodes, string Actor);

public sealed record DiagnosticMutirao(Guid Id, string Name, string Exam, string Unit, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, int Booked, string? Performer, string Status);
public sealed record CreateDiagnosticMutiraoRequest(string Name, string Exam, string Unit, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, string? Performer, string Actor);

public static class MunicipalOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMunicipalOperations(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/units", (MunicipalOperationsStore store) => Results.Ok(store.Units()));

        endpoints.MapGet("/api/ubs/reception", (MunicipalOperationsStore store) => Results.Ok(store.ReceptionQueue()));
        endpoints.MapPost("/api/ubs/reception/checkin", (CheckInRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => Results.Created("/api/ubs/reception", store.CheckIn(request, clinical))));
        endpoints.MapPost("/api/ubs/reception/{id:guid}/call", (Guid id, CallReceptionRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => store.Call(id, request.Room, request.Professional, clinical) is { } ticket ? Results.Ok(ticket) : Results.NotFound()));

        endpoints.MapGet("/api/records/digitized", (Guid? citizenId, MunicipalOperationsStore store) => Results.Ok(store.Records(citizenId)));
        endpoints.MapPost("/api/records/digitized", (RegisterDigitizedRecordRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => Results.Created("/api/records/digitized", store.RegisterRecord(request, clinical))));
        endpoints.MapPost("/api/records/digitized/{id:guid}/checkout", (Guid id, RecordCustodyRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => store.CheckoutRecord(id, request, clinical) is { } record ? Results.Ok(record) : Results.NotFound()));
        endpoints.MapPost("/api/records/digitized/{id:guid}/return", (Guid id, RecordReturnRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => store.ReturnRecord(id, request.Actor, clinical) is { } record ? Results.Ok(record) : Results.NotFound()));

        endpoints.MapGet("/api/warehouse", (MunicipalOperationsStore store) => Results.Ok(store.Warehouse()));
        endpoints.MapGet("/api/warehouse/movements", (MunicipalOperationsStore store) => Results.Ok(store.WarehouseMovements()));
        endpoints.MapPost("/api/warehouse/receive", (ReceiveWarehouseRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => Results.Created("/api/warehouse/movements", store.ReceiveWarehouse(request, clinical))));
        endpoints.MapPost("/api/warehouse/transfer", (TransferWarehouseRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => Results.Created("/api/warehouse/movements", store.TransferWarehouse(request, clinical))));

        endpoints.MapGet("/api/immunization/campaigns", (MunicipalOperationsStore store) => Results.Ok(store.Campaigns()));
        endpoints.MapPost("/api/immunization/campaigns", (CreateVaccinationCampaignRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => Results.Created("/api/immunization/campaigns", store.CreateCampaign(request, clinical))));

        endpoints.MapGet("/api/diagnostics/mutiroes", (MunicipalOperationsStore store) => Results.Ok(store.Mutiroes()));
        endpoints.MapPost("/api/diagnostics/mutiroes", (CreateDiagnosticMutiraoRequest request, MunicipalOperationsStore store, DemoStore clinical) =>
            Execute(() => Results.Created("/api/diagnostics/mutiroes", store.CreateMutirao(request, clinical))));

        return endpoints;
    }

    private static IResult Execute(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Regra de negócio", detail: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Entrada inválida", detail: ex.Message);
        }
    }
}

public sealed record RecordReturnRequest(string Actor);
