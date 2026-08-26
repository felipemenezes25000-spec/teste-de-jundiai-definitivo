using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class InventoryAdvancedEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAdvancedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/inventory/v2/lots", (InventoryAdvancedStore store) => Results.Ok(store.Lots()));
        endpoints.MapGet("/api/inventory/v2/alerts", (InventoryAdvancedStore store) => Results.Ok(store.Alerts()));
        endpoints.MapGet("/api/inventory/v2/controlled-ledger", (InventoryAdvancedStore store) => Results.Ok(store.ControlledLedger()));
        endpoints.MapGet("/api/inventory/v2/recalls", (InventoryAdvancedStore store) => Results.Ok(store.Recalls()));
        endpoints.MapGet("/api/inventory/v2/stocktakes", (InventoryAdvancedStore store) => Results.Ok(store.Stocktakes()));
        endpoints.MapPost("/api/inventory/v2/receive", (ReceiveInventoryV2Request request, InventoryAdvancedStore store, DemoStore demo) =>
        {
            var lot = store.Receive(request);
            demo.AuditExternal(request.Actor, "inventory.v2.receive", $"inventory-lot:{lot.Id}", $"qty={request.Quantity};invoice={request.InvoiceNumber}");
            return Results.Created($"/api/inventory/v2/lots/{lot.Id}", lot);
        });
        endpoints.MapPost("/api/inventory/v2/stocktakes", (CreateStocktakeRequest request, InventoryAdvancedStore store, DemoStore demo) =>
        {
            var result = store.Stocktake(request);
            demo.AuditExternal(request.Actor, "inventory.stocktake", $"inventory-lot:{request.LotId}", $"system={result.SystemQuantity};counted={result.CountedQuantity};delta={result.Delta}");
            return Results.Created("/api/inventory/v2/stocktakes", result);
        });
        endpoints.MapPost("/api/inventory/v2/recalls", (CreateRecallRequest request, InventoryAdvancedStore store, DemoStore demo) =>
        {
            var recall = store.CreateRecall(request);
            demo.AuditExternal(request.Actor, "inventory.recall.create", $"recall:{recall.Id}", $"lot={recall.Lot};reason={recall.Reason}");
            return Results.Created($"/api/inventory/v2/recalls/{recall.Id}", recall);
        });
        endpoints.MapPost("/api/inventory/v2/recalls/{id:guid}/acknowledge", (Guid id, RecallAcknowledgementRequest request, InventoryAdvancedStore store, DemoStore demo) =>
        {
            var recall = store.AcknowledgeRecall(id, request);
            demo.AuditExternal(request.Actor, "inventory.recall.ack", $"recall:{id}", request.Unit);
            return Results.Ok(recall);
        });
        endpoints.MapPost("/api/inventory/v2/controlled-ledger", (ControlledLedgerRequest request, InventoryAdvancedStore store, DemoStore demo) =>
        {
            var entry = store.RegisterControlled(request);
            demo.AuditExternal(request.Actor, "inventory.controlled.register", $"inventory-lot:{request.LotId}", $"type={request.Type};qty={request.Quantity}");
            return Results.Created("/api/inventory/v2/controlled-ledger", entry);
        });
        endpoints.MapGet("/api/inventory/v2/readiness", (InventoryAdvancedStore store) => Results.Ok(new
        {
            lots = store.Lots().Count,
            alerts = store.Alerts().Count,
            capabilities = new[]
            {
                "lote e validade", "fornecedor e nota fiscal", "rastreabilidade de recebimento", "inventário com divergência",
                "recall por lote", "ciência por unidade", "alerta de vencimento e estoque mínimo", "livro demonstrativo de controlados"
            },
            externalDependencies = new[] { "XML fiscal real conforme fornecedor", "BNAFAR/Hórus ou integração oficial aplicável", "políticas municipais de controlados" }
        }));
        return endpoints;
    }
}

public sealed class InventoryAdvancedStore
{
    private readonly ConcurrentDictionary<Guid, InventoryLotV2> _lots = new();
    private readonly ConcurrentDictionary<Guid, InventoryStocktake> _stocktakes = new();
    private readonly ConcurrentDictionary<Guid, InventoryRecall> _recalls = new();
    private readonly ConcurrentDictionary<Guid, ControlledLedgerEntry> _controlledLedger = new();

    public InventoryAdvancedStore()
    {
        Receive(new ReceiveInventoryV2Request("MED-001", "Losartana 50 mg", "comprimido", "LJ2608A", DateOnly.FromDateTime(DateTime.Today.AddMonths(10)), 5000, 1000, "Almoxarifado Central", "Fornecedor Demonstrativo A", "NF-2026-00801", null, false, "seed"));
        Receive(new ReceiveInventoryV2Request("MED-CTR-001", "Medicamento sujeito a controle especial - demonstração", "comprimido", "CTR2607", DateOnly.FromDateTime(DateTime.Today.AddMonths(5)), 300, 80, "Farmácia Central", "Fornecedor Demonstrativo B", "NF-2026-00802", null, true, "seed"));
        Receive(new ReceiveInventoryV2Request("MAT-010", "Seringa descartável 3 ml", "unidade", "SER2601", DateOnly.FromDateTime(DateTime.Today.AddDays(45)), 500, 600, "Almoxarifado Central", "Fornecedor Demonstrativo C", "NF-2026-00803", null, false, "seed"));
    }

    public IReadOnlyList<InventoryLotV2> Lots() => _lots.Values.OrderBy(x => x.Name).ThenBy(x => x.ExpiresOn).ToList();
    public IReadOnlyList<InventoryStocktake> Stocktakes() => _stocktakes.Values.OrderByDescending(x => x.CountedAt).ToList();
    public IReadOnlyList<InventoryRecall> Recalls() => _recalls.Values.OrderByDescending(x => x.CreatedAt).ToList();
    public IReadOnlyList<ControlledLedgerEntry> ControlledLedger() => _controlledLedger.Values.OrderByDescending(x => x.OccurredAt).ToList();

    public IReadOnlyList<InventoryAlert> Alerts()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var alerts = new List<InventoryAlert>();
        foreach (var lot in _lots.Values)
        {
            var days = lot.ExpiresOn.DayNumber - today.DayNumber;
            if (days < 0)
                alerts.Add(new(Guid.NewGuid(), lot.Id, "expired", "critical", $"Lote {lot.Lot} vencido há {Math.Abs(days)} dia(s).", DateTimeOffset.UtcNow));
            else if (days <= 60)
                alerts.Add(new(Guid.NewGuid(), lot.Id, "expiring", days <= 30 ? "high" : "medium", $"Lote {lot.Lot} vence em {days} dia(s).", DateTimeOffset.UtcNow));
            if (lot.Quantity <= lot.MinimumStock)
                alerts.Add(new(Guid.NewGuid(), lot.Id, "low_stock", lot.Quantity == 0 ? "critical" : "high", $"Saldo {lot.Quantity} abaixo/igual ao mínimo {lot.MinimumStock}.", DateTimeOffset.UtcNow));
            if (lot.BlockedByRecall)
                alerts.Add(new(Guid.NewGuid(), lot.Id, "recall", "critical", $"Lote {lot.Lot} bloqueado por recall.", DateTimeOffset.UtcNow));
        }
        return alerts.OrderByDescending(x => SeverityRank(x.Severity)).ThenBy(x => x.Message).ToList();
    }

    public InventoryLotV2 Receive(ReceiveInventoryV2Request request)
    {
        if (request.Quantity <= 0) throw new ArgumentException("Quantidade recebida deve ser positiva.");
        if (request.MinimumStock < 0) throw new ArgumentException("Estoque mínimo não pode ser negativo.");
        if (string.IsNullOrWhiteSpace(request.ItemCode) || string.IsNullOrWhiteSpace(request.Lot)) throw new ArgumentException("Código do item e lote são obrigatórios.");
        var existing = _lots.Values.FirstOrDefault(x =>
            x.ItemCode.Equals(request.ItemCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.Lot.Equals(request.Lot.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.Location.Equals(request.Location.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var updated = existing with { Quantity = existing.Quantity + request.Quantity, UpdatedAt = DateTimeOffset.UtcNow };
            _lots[existing.Id] = updated;
            return updated;
        }

        var lot = new InventoryLotV2(
            Guid.NewGuid(), request.ItemCode.Trim(), request.Name.Trim(), request.Unit.Trim(), request.Lot.Trim(), request.ExpiresOn,
            request.Quantity, request.MinimumStock, request.Location.Trim(), request.Supplier?.Trim(), request.InvoiceNumber?.Trim(),
            request.InvoiceXmlSha256?.Trim(), request.Controlled, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _lots[lot.Id] = lot;
        return lot;
    }

    public InventoryStocktake Stocktake(CreateStocktakeRequest request)
    {
        if (!_lots.TryGetValue(request.LotId, out var lot)) throw new KeyNotFoundException();
        if (request.CountedQuantity < 0) throw new ArgumentException("Quantidade contada não pode ser negativa.");
        var result = new InventoryStocktake(Guid.NewGuid(), lot.Id, lot.ItemCode, lot.Lot, lot.Location, lot.Quantity, request.CountedQuantity, request.CountedQuantity - lot.Quantity, request.Reason?.Trim(), request.Actor.Trim(), DateTimeOffset.UtcNow);
        _stocktakes[result.Id] = result;
        _lots[lot.Id] = lot with { Quantity = request.CountedQuantity, UpdatedAt = DateTimeOffset.UtcNow };
        return result;
    }

    public InventoryRecall CreateRecall(CreateRecallRequest request)
    {
        var affected = _lots.Values.Where(x =>
            x.ItemCode.Equals(request.ItemCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.Lot.Equals(request.Lot.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (affected.Count == 0) throw new InvalidOperationException("Nenhum lote correspondente encontrado.");
        foreach (var lot in affected) _lots[lot.Id] = lot with { BlockedByRecall = true, UpdatedAt = DateTimeOffset.UtcNow };
        var targetUnits = affected.Select(x => x.Location).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var recall = new InventoryRecall(Guid.NewGuid(), request.ItemCode.Trim(), request.Lot.Trim(), request.Reason.Trim(), request.AuthorityReference?.Trim(), request.Actor.Trim(), targetUnits, [], "open", DateTimeOffset.UtcNow, null);
        _recalls[recall.Id] = recall;
        return recall;
    }

    public InventoryRecall AcknowledgeRecall(Guid id, RecallAcknowledgementRequest request)
    {
        if (!_recalls.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        var acks = current.Acknowledgements.ToList();
        if (!acks.Any(x => x.Unit.Equals(request.Unit.Trim(), StringComparison.OrdinalIgnoreCase)))
            acks.Add(new RecallAcknowledgement(Guid.NewGuid(), request.Unit.Trim(), request.Actor.Trim(), request.Note?.Trim(), DateTimeOffset.UtcNow));
        var allAcknowledged = current.TargetUnits.All(unit => acks.Any(x => x.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase)));
        var updated = current with { Acknowledgements = acks, Status = allAcknowledged ? "acknowledged" : "open", ClosedAt = allAcknowledged ? DateTimeOffset.UtcNow : null };
        _recalls[id] = updated;
        return updated;
    }

    public ControlledLedgerEntry RegisterControlled(ControlledLedgerRequest request)
    {
        if (!_lots.TryGetValue(request.LotId, out var lot)) throw new KeyNotFoundException();
        if (!lot.Controlled) throw new InvalidOperationException("Lote não está marcado como sujeito a controle especial.");
        if (request.Quantity <= 0) throw new ArgumentException("Quantidade deve ser positiva.");
        var type = (request.Type ?? string.Empty).Trim().ToLowerInvariant();
        if (type is not ("entry" or "dispense" or "loss" or "adjustment")) throw new ArgumentException("Tipo inválido para livro controlado.");
        if (type != "entry" && string.IsNullOrWhiteSpace(request.Reference)) throw new ArgumentException("Movimento de saída/ajuste exige referência.");
        var entry = new ControlledLedgerEntry(Guid.NewGuid(), lot.Id, lot.ItemCode, lot.Lot, type, request.Quantity, request.Reference?.Trim(), request.CitizenReference?.Trim(), request.Professional?.Trim(), request.Actor.Trim(), DateTimeOffset.UtcNow);
        _controlledLedger[entry.Id] = entry;
        return entry;
    }

    private static int SeverityRank(string value) => value switch { "critical" => 4, "high" => 3, "medium" => 2, _ => 1 };
}

public sealed record InventoryLotV2(Guid Id, string ItemCode, string Name, string Unit, string Lot, DateOnly ExpiresOn, int Quantity, int MinimumStock, string Location, string? Supplier, string? InvoiceNumber, string? InvoiceXmlSha256, bool Controlled, bool BlockedByRecall, DateTimeOffset ReceivedAt, DateTimeOffset UpdatedAt);
public sealed record InventoryAlert(Guid Id, Guid LotId, string Type, string Severity, string Message, DateTimeOffset GeneratedAt);
public sealed record InventoryStocktake(Guid Id, Guid LotId, string ItemCode, string Lot, string Location, int SystemQuantity, int CountedQuantity, int Delta, string? Reason, string Actor, DateTimeOffset CountedAt);
public sealed record InventoryRecall(Guid Id, string ItemCode, string Lot, string Reason, string? AuthorityReference, string CreatedBy, IReadOnlyList<string> TargetUnits, IReadOnlyList<RecallAcknowledgement> Acknowledgements, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ClosedAt);
public sealed record RecallAcknowledgement(Guid Id, string Unit, string Actor, string? Note, DateTimeOffset AcknowledgedAt);
public sealed record ControlledLedgerEntry(Guid Id, Guid LotId, string ItemCode, string Lot, string Type, int Quantity, string? Reference, string? CitizenReference, string? Professional, string Actor, DateTimeOffset OccurredAt);
public sealed record ReceiveInventoryV2Request(string ItemCode, string Name, string Unit, string Lot, DateOnly ExpiresOn, int Quantity, int MinimumStock, string Location, string? Supplier, string? InvoiceNumber, string? InvoiceXmlSha256, bool Controlled, string Actor);
public sealed record CreateStocktakeRequest(Guid LotId, int CountedQuantity, string? Reason, string Actor);
public sealed record CreateRecallRequest(string ItemCode, string Lot, string Reason, string? AuthorityReference, string Actor);
public sealed record RecallAcknowledgementRequest(string Unit, string Actor, string? Note);
public sealed record ControlledLedgerRequest(Guid LotId, string Type, int Quantity, string? Reference, string? CitizenReference, string? Professional, string Actor);
