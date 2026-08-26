namespace Jundiai.Api;

public static class MunicipalCommandCenterEndpoints
{
    public static IEndpointRouteBuilder MapMunicipalCommandCenterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/analytics/command-center", (
            DemoStore demo,
            MunicipalOperationsStore operations,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            InventoryAdvancedStore inventory,
            ImmunizationAdvancedStore immunization,
            PharmacyCareStore pharmacy,
            ClinicalOrderStore clinicalOrders,
            SusBillingEngineStore billing,
            TelemedicineStore telemedicine) =>
            Results.Ok(MunicipalCommandCenter.Build(demo, operations, scheduling, diagnostics, inventory, immunization, pharmacy, clinicalOrders, billing, telemedicine)));

        endpoints.MapGet("/api/analytics/alerts", (
            DemoStore demo,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            InventoryAdvancedStore inventory,
            ClinicalOrderStore clinicalOrders,
            TelemedicineStore telemedicine) =>
            Results.Ok(MunicipalCommandCenter.Alerts(demo, scheduling, diagnostics, inventory, clinicalOrders, telemedicine)));

        return endpoints;
    }
}

public static class MunicipalCommandCenter
{
    public static object Build(
        DemoStore demo,
        MunicipalOperationsStore operations,
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        InventoryAdvancedStore inventory,
        ImmunizationAdvancedStore immunization,
        PharmacyCareStore pharmacy,
        ClinicalOrderStore clinicalOrders,
        SusBillingEngineStore billing,
        TelemedicineStore telemedicine)
    {
        var regulation = demo.Regulation();
        var bookings = scheduling.Bookings();
        var scheduleLoss = scheduling.LossReport();
        var diagnosticOrders = diagnostics.Orders(null);
        var inventoryAlerts = inventory.Alerts();
        var vaccineCoverage = immunization.Coverage(demo);
        var alerts = Alerts(demo, scheduling, diagnostics, inventory, clinicalOrders, telemedicine);

        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            network = new
            {
                healthUnits = operations.Units().Count,
                citizens = demo.Citizens().Count,
                households = demo.Households().Count,
                activeMedicationOrders = clinicalOrders.Orders(null).Count(x => x.Status == "active")
            },
            access = new
            {
                regulationOpen = regulation.Count(x => x.Status is "waiting" or "authorized"),
                highPriority = regulation.Count(x => x.Status is "waiting" or "authorized" && x.Priority is "emergency" or "urgent" or "high"),
                bookings = bookings.Count,
                noShow = bookings.Count(x => x.Status == "no_show"),
                cancelled = bookings.Count(x => x.Status == "cancelled"),
                waitlist = scheduling.Waitlist().Count,
                lossReport = scheduleLoss
            },
            clinical = new
            {
                diagnosticOrders = diagnosticOrders.Count,
                criticalPending = diagnosticOrders.Count(x => x.Result?.Critical == true && x.Result.CriticalAcknowledgementStatus != "acknowledged"),
                carePlans = clinicalOrders.CarePlans(null).Count,
                medicationAdministrations = clinicalOrders.AllAdministrations().Count,
                telemedicineSessions = telemedicine.Sessions().Count,
                telemedicineActive = telemedicine.Sessions().Count(x => x.Status is "waiting_room" or "ready" or "in_progress" or "interrupted")
            },
            prevention = new
            {
                vaccineEvents = demo.Immunizations().Count,
                scheduleRules = immunization.Rules().Count,
                adverseEvents = immunization.AdverseEvents(null).Count,
                coverage = vaccineCoverage
            },
            supply = new
            {
                pharmacyLots = demo.Inventory().Count,
                lowStock = demo.Inventory().Count(x => x.Quantity <= x.MinimumStock),
                advancedAlerts = inventoryAlerts.Count,
                criticalAlerts = inventoryAlerts.Count(x => x.Severity == "critical"),
                reconciliations = pharmacy.Reconciliations(null).Count,
                linkedDispensations = pharmacy.Dispensations(null).Count
            },
            billing = new
            {
                production = billing.Production().Count,
                batches = billing.Batches().Count,
                criticized = billing.Batches().Count(x => x.Status == "criticized"),
                closed = billing.Batches().Count(x => x.Status == "closed")
            },
            alertSummary = new
            {
                total = alerts.Count,
                critical = alerts.Count(x => x.Severity == "critical"),
                high = alerts.Count(x => x.Severity == "high")
            },
            alerts = alerts.Take(25).ToArray(),
            source = "live POC state across integrated bounded contexts"
        };
    }

    public static IReadOnlyList<MunicipalOperationalAlert> Alerts(
        DemoStore demo,
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        InventoryAdvancedStore inventory,
        ClinicalOrderStore clinicalOrders,
        TelemedicineStore telemedicine)
    {
        var now = DateTimeOffset.UtcNow;
        var alerts = new List<MunicipalOperationalAlert>();

        foreach (var item in demo.Regulation().Where(x => x.Status is "waiting" or "authorized"))
        {
            var days = (now - item.RequestedAt).TotalDays;
            if (item.Priority is "emergency" or "urgent" || days >= 7)
                alerts.Add(new("regulation", item.Id.ToString(), item.Priority is "emergency" or "urgent" ? "critical" : "high", $"{item.CitizenName}: {item.Specialty} em {item.Status} há {Math.Floor(days)} dia(s).", "Revisar prioridade e destino regulatório."));
        }

        foreach (var order in diagnostics.Orders(null).Where(x => x.Result?.Critical == true && x.Result.CriticalAcknowledgementStatus != "acknowledged"))
            alerts.Add(new("diagnostics", order.Id.ToString(), "critical", $"Resultado crítico de {order.ExamName} sem ciência registrada para {order.CitizenName}.", "Registrar ciência e conduta imediatamente."));

        foreach (var item in inventory.Alerts())
            alerts.Add(new("inventory", item.Id.ToString(), item.Severity == "critical" ? "critical" : "high", item.Message, "Executar ação de estoque/recall definida pela unidade."));

        foreach (var booking in scheduling.Bookings().Where(x => x.Status == "no_show"))
            alerts.Add(new("scheduling", booking.Id.ToString(), "medium", $"Falta registrada: {booking.CitizenName} · {booking.Specialty}.", "Avaliar remarcação e estratégia de redução de absenteísmo."));

        foreach (var plan in clinicalOrders.CarePlans(null))
        foreach (var task in plan.Tasks.Where(x => x.Status == "open" && x.DueAt is { } due && due < now))
            alerts.Add(new("care-plan", task.Id.ToString(), "high", $"Tarefa de cuidado vencida para {plan.CitizenName}: {task.Description}.", "Reatribuir ou concluir tarefa com justificativa."));

        foreach (var session in telemedicine.Sessions().Where(x => x.Status == "interrupted"))
            alerts.Add(new("telemedicine", session.Id.ToString(), "high", $"Teleconsulta interrompida de {session.CitizenName}.", "Retomar sessão ou converter para atendimento presencial/regulado."));

        return alerts.OrderByDescending(x => SeverityRank(x.Severity)).ThenBy(x => x.Domain).ToList();
    }

    private static int SeverityRank(string value) => value switch { "critical" => 4, "high" => 3, "medium" => 2, _ => 1 };
}

public sealed record MunicipalOperationalAlert(string Domain, string SourceId, string Severity, string Description, string SuggestedAction);