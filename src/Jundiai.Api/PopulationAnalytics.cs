namespace Jundiai.Api;

public static class PopulationAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapPopulationAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/analytics/executive", (
            DemoStore demo,
            MunicipalOperationsStore operations,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            InventoryAdvancedStore inventory,
            TelemedicineStore telemedicine,
            SusBillingEngineStore billing,
            OperationalReadinessStore ops) => Results.Ok(PopulationAnalytics.BuildExecutive(demo, operations, scheduling, diagnostics, inventory, telemedicine, billing, ops)));
        endpoints.MapGet("/api/analytics/regulation", (DemoStore demo, SchedulingStore scheduling) => Results.Ok(PopulationAnalytics.BuildRegulation(demo, scheduling)));
        endpoints.MapGet("/api/analytics/supply", (DemoStore demo, InventoryAdvancedStore inventory) => Results.Ok(PopulationAnalytics.BuildSupply(demo, inventory)));
        endpoints.MapGet("/api/analytics/clinical-safety", (
            DemoStore demo,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            TelemedicineStore telemedicine,
            ClinicalOrderStore clinicalOrders) => Results.Ok(PopulationAnalytics.BuildClinicalSafety(demo, scheduling, diagnostics, telemedicine, clinicalOrders)));
        endpoints.MapGet("/api/analytics/readiness", () => Results.Ok(new
        {
            implemented = new[] { "executive-snapshot", "regulation-aging", "waitlist", "supply-risk", "critical-results", "care-continuity-gaps", "telemedicine-state", "clinical-orders", "service-desk-SLA" },
            note = "Indicadores são calculados sobre os dados desta instância POC; séries históricas reais exigem persistência e carga municipal."
        }));
        return endpoints;
    }
}

public static class PopulationAnalytics
{
    public static object BuildExecutive(
        DemoStore demo,
        MunicipalOperationsStore operations,
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        InventoryAdvancedStore inventory,
        TelemedicineStore telemedicine,
        SusBillingEngineStore billing,
        OperationalReadinessStore ops)
    {
        var regulation = demo.Regulation();
        var diag = diagnostics.Orders(null);
        var alerts = inventory.Alerts();
        var sla = ops.SlaDashboard();
        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            network = new { healthUnits = operations.Units().Count, citizensInPoc = demo.Citizens().Count, households = demo.Households().Count },
            access = new
            {
                regulationOpen = regulation.Count(x => x.Status is "waiting" or "authorized"),
                regulationHighPriority = regulation.Count(x => x.Status is "waiting" or "authorized" && x.Priority is "emergency" or "urgent" or "high"),
                scheduleSlots = scheduling.Slots(null, null, null).Count,
                waitlist = scheduling.Waitlist().Count
            },
            diagnostics = new
            {
                orders = diag.Count,
                open = diag.Count(x => x.Status is not ("completed" or "cancelled")),
                criticalPendingAcknowledgement = diag.Count(x => x.Result?.Critical == true && x.Result.CriticalAcknowledgementStatus != "acknowledged")
            },
            supply = new
            {
                lots = inventory.Lots().Count,
                alerts = alerts.Count,
                criticalAlerts = alerts.Count(x => x.Severity == "critical"),
                activeRecalls = inventory.Recalls().Count(x => x.Status != "acknowledged")
            },
            telemedicine = new
            {
                sessions = telemedicine.Sessions().Count,
                active = telemedicine.Sessions().Count(x => x.Status is "waiting_room" or "ready" or "in_progress" or "interrupted")
            },
            billing = new
            {
                production = billing.Production().Count,
                batches = billing.Batches().Count,
                criticized = billing.Batches().Count(x => x.Status == "criticized")
            },
            support = sla,
            source = "current-state POC snapshot"
        };
    }

    public static object BuildRegulation(DemoStore demo, SchedulingStore scheduling)
    {
        var now = DateTimeOffset.UtcNow;
        var open = demo.Regulation().Where(x => x.Status is "waiting" or "authorized").ToList();
        return new
        {
            generatedAt = now,
            open = open.Count,
            byPriority = open.GroupBy(x => x.Priority).OrderByDescending(g => PriorityRank(g.Key)).Select(g => new { priority = g.Key, count = g.Count(), oldestDays = Math.Round(g.Max(x => (now - x.RequestedAt).TotalDays), 1) }).ToArray(),
            bySpecialty = open.GroupBy(x => x.Specialty).OrderByDescending(g => g.Count()).Select(g => new { specialty = g.Key, count = g.Count(), averageWaitingDays = Math.Round(g.Average(x => (now - x.RequestedAt).TotalDays), 1), oldestDays = Math.Round(g.Max(x => (now - x.RequestedAt).TotalDays), 1) }).ToArray(),
            waitlist = scheduling.Waitlist().GroupBy(x => x.Specialty).Select(g => new { specialty = g.Key, count = g.Count(), highPriority = g.Count(x => x.Priority is "emergency" or "very_high" or "high") }).ToArray(),
            availableSlots = scheduling.Slots(null, null, null).Count(x => !x.Blocked && x.Booked < x.Capacity)
        };
    }

    public static object BuildSupply(DemoStore demo, InventoryAdvancedStore inventory)
    {
        var alerts = inventory.Alerts();
        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            pharmacy = new { lots = demo.Inventory().Count, lowStock = demo.Inventory().Count(x => x.Quantity <= x.MinimumStock) },
            advanced = new
            {
                lots = inventory.Lots().Count,
                alertsBySeverity = alerts.GroupBy(x => x.Severity).ToDictionary(g => g.Key, g => g.Count()),
                alertsByType = alerts.GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.Count()),
                recalls = inventory.Recalls().Select(x => new { x.Id, x.ItemCode, x.Lot, x.Status, targetUnits = x.TargetUnits.Count, acknowledgements = x.Acknowledgements.Count }).ToArray()
            }
        };
    }

    public static object BuildClinicalSafety(
        DemoStore demo,
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        TelemedicineStore telemedicine,
        ClinicalOrderStore clinicalOrders)
    {
        var critical = diagnostics.Orders(null).Where(x => x.Result?.Critical == true).ToList();
        var continuity = demo.Citizens().Select(c => CareTraceBuilder.Continuity(c, demo, scheduling, diagnostics, telemedicine, clinicalOrders)).ToList();
        var gaps = continuity.SelectMany(x => x.Gaps.Select(g => new { citizenId = x.CitizenId, g.Domain, g.SourceId, g.Severity, g.Description, g.SuggestedOperationalAction })).ToList();
        return new
        {
            generatedAt = DateTimeOffset.UtcNow,
            criticalResults = critical.Select(x => new { x.Id, x.CitizenId, x.CitizenName, x.ExamName, x.Status, acknowledgement = x.Result?.CriticalAcknowledgementStatus, releasedAt = x.Result?.ReleasedAt }).ToArray(),
            continuity = new
            {
                citizensReviewed = continuity.Count,
                withGaps = continuity.Count(x => x.Gaps.Count > 0),
                criticalGaps = gaps.Count(x => x.Severity == "critical"),
                highGaps = gaps.Count(x => x.Severity == "high"),
                gaps
            }
        };
    }

    private static int PriorityRank(string priority) => priority switch { "emergency" => 5, "urgent" => 4, "high" => 3, "moderate" => 2, _ => 1 };
}