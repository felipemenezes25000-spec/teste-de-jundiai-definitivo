namespace Jundiai.Api;

public static class CareTraceEndpoints
{
    public static IEndpointRouteBuilder MapCareTraceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/care-trace/{citizenId:guid}", (
            Guid citizenId,
            DemoStore demo,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            TelemedicineStore telemedicine,
            ClinicalDocumentStore documents,
            ClinicalOrderStore clinicalOrders,
            DentalAdvancedStore dental,
            SusBillingEngineStore billing) =>
        {
            var citizen = demo.Citizen(citizenId);
            if (citizen is null) return Results.NotFound();
            return Results.Ok(CareTraceBuilder.Build(citizen, demo, scheduling, diagnostics, telemedicine, documents, clinicalOrders, dental, billing));
        });
        endpoints.MapGet("/api/care-trace/{citizenId:guid}/continuity", (
            Guid citizenId,
            DemoStore demo,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            TelemedicineStore telemedicine,
            ClinicalOrderStore clinicalOrders) =>
        {
            var citizen = demo.Citizen(citizenId);
            if (citizen is null) return Results.NotFound();
            return Results.Ok(CareTraceBuilder.Continuity(citizen, demo, scheduling, diagnostics, telemedicine, clinicalOrders));
        });
        endpoints.MapGet("/api/care-trace/readiness", () => Results.Ok(new
        {
            implemented = new[]
            {
                "citizen-root", "regulation", "scheduling", "territory", "ACS", "immunization", "diagnostics",
                "telemedicine", "documents", "clinical-orders", "MAR", "care-plan", "dental", "SUS-production", "pharmacy-dispense", "continuity-gaps"
            },
            purpose = "Unificar evidência longitudinal de cuidado entre módulos sem criar um prontuário paralelo.",
            productionNote = "Na produção, IDs e eventos devem vir de persistência transacional e integrações oficiais, preservando origem/proveniência."
        }));
        return endpoints;
    }
}

public static class CareTraceBuilder
{
    public static CareTraceGraph Build(
        Citizen citizen,
        DemoStore demo,
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        TelemedicineStore telemedicine,
        ClinicalDocumentStore documents,
        ClinicalOrderStore clinicalOrders,
        DentalAdvancedStore dental,
        SusBillingEngineStore billing)
    {
        var nodes = new List<CareTraceNode>();
        var edges = new List<CareTraceEdge>();
        var root = Add(nodes, "citizen", citizen.Id.ToString(), citizen.Name, DateTimeOffset.UtcNow, new Dictionary<string, string>
        {
            ["cns"] = citizen.Cns,
            ["healthUnit"] = citizen.HealthUnit,
            ["area"] = citizen.Area,
            ["microArea"] = citizen.MicroArea
        });

        foreach (var household in demo.Households().Where(h => h.Members.Any(m => m.CitizenId == citizen.Id)))
        {
            var node = Add(nodes, "territory", household.Id.ToString(), household.Address, DateTimeOffset.UtcNow.AddYears(-1), new Dictionary<string,string>{{"area",household.Area},{"microArea",household.MicroArea},{"acs",household.AcsName}});
            Link(edges, root, node, "belongs_to_household");
            foreach (var visit in demo.AcsVisits().Where(v => v.HouseholdId == household.Id))
            {
                var visitNode = Add(nodes, "acs_visit", visit.Id.ToString(), visit.VisitType, visit.OccurredAt, new Dictionary<string,string>{{"outcome",visit.Outcome},{"acs",visit.AcsName},{"offline",visit.OfflineCaptured.ToString()}});
                Link(edges, node, visitNode, "visited");
            }
        }

        foreach (var regulation in demo.Regulation().Where(x => x.CitizenId == citizen.Id))
        {
            var node = Add(nodes, "regulation", regulation.Id.ToString(), regulation.Specialty, regulation.RequestedAt, new Dictionary<string,string>{{"status",regulation.Status},{"priority",regulation.Priority},{"origin",regulation.OriginUnit},{"destination",regulation.DestinationUnit ?? ""}});
            Link(edges, root, node, "requested_care");
        }

        foreach (var booking in scheduling.Bookings().Where(x => x.CitizenId == citizen.Id))
        {
            var node = Add(nodes, "scheduling", booking.Id.ToString(), $"{booking.Specialty} · {booking.Unit}", booking.UpdatedAt, new Dictionary<string,string>
            {
                ["status"] = booking.Status,
                ["startsAt"] = booking.StartsAt.ToString("O"),
                ["source"] = booking.Source,
                ["reason"] = booking.ClosureReason ?? ""
            });
            Link(edges, root, node, "scheduled_care");
        }

        foreach (var immunization in demo.Immunizations(citizen.Id))
        {
            var node = Add(nodes, "immunization", immunization.Id.ToString(), $"{immunization.Vaccine} · {immunization.Dose}", immunization.AppliedAt, new Dictionary<string,string>{{"lot",immunization.Lot},{"professional",immunization.Professional}});
            Link(edges, root, node, "received_vaccine");
        }

        foreach (var movement in demo.InventoryMovements().Where(x => x.CitizenId == citizen.Id))
        {
            var node = Add(nodes, "pharmacy", movement.Id.ToString(), movement.ItemName, movement.OccurredAt, new Dictionary<string,string>{{"type",movement.Type},{"quantity",movement.Quantity.ToString()},{"reference",movement.Reference ?? ""}});
            Link(edges, root, node, "received_medication");
        }

        foreach (var order in clinicalOrders.Orders(citizen.Id))
        {
            var node = Add(nodes, "clinical_order", order.Id.ToString(), $"{order.Medication} · {order.Dose}", order.UpdatedAt, new Dictionary<string,string>
            {
                ["status"] = order.Status,
                ["route"] = order.Route,
                ["frequency"] = order.Frequency,
                ["orderedBy"] = order.OrderedBy
            });
            Link(edges, root, node, "has_clinical_order");
            foreach (var administration in clinicalOrders.Administrations(order.Id))
            {
                var administrationNode = Add(nodes, "medication_administration", administration.Id.ToString(), $"{administration.Medication} · {administration.Outcome}", administration.AdministeredAt, new Dictionary<string,string>
                {
                    ["dose"] = administration.Dose,
                    ["route"] = administration.Route,
                    ["professional"] = administration.Professional,
                    ["reason"] = administration.Reason ?? ""
                });
                Link(edges, node, administrationNode, "administered_as");
            }
        }

        foreach (var plan in clinicalOrders.CarePlans(citizen.Id))
        {
            var node = Add(nodes, "care_plan", plan.Id.ToString(), plan.Goal, plan.UpdatedAt, new Dictionary<string,string>
            {
                ["status"] = plan.Status,
                ["createdBy"] = plan.CreatedBy,
                ["tasks"] = plan.Tasks.Count.ToString(),
                ["completedTasks"] = plan.Tasks.Count(x => x.Status == "completed").ToString()
            });
            Link(edges, root, node, "care_plan");
        }

        foreach (var order in diagnostics.Orders(citizen.Id))
        {
            var date = order.Result?.ReleasedAt ?? order.Execution?.PerformedAt ?? order.ScheduledAt ?? order.RequestedAt;
            var node = Add(nodes, "diagnostic", order.Id.ToString(), order.ExamName, date, new Dictionary<string,string>{{"status",order.Status},{"kind",order.Kind},{"priority",order.Priority},{"critical",(order.Result?.Critical ?? false).ToString()}});
            Link(edges, root, node, "diagnostic_journey");
        }

        foreach (var session in telemedicine.Sessions().Where(x => x.CitizenId == citizen.Id))
        {
            var node = Add(nodes, "telemedicine", session.Id.ToString(), session.Specialty, session.UpdatedAt, new Dictionary<string,string>{{"status",session.Status},{"unit",session.HealthUnit},{"consent",(session.Consent?.Accepted ?? false).ToString()}});
            Link(edges, root, node, "virtual_care");
        }

        foreach (var document in documents.Documents(citizen.Id))
        {
            var node = Add(nodes, "clinical_document", document.Id.ToString(), document.Title, document.UpdatedAt, new Dictionary<string,string>{{"type",document.Type},{"status",document.Status},{"sha256",document.Sha256}});
            Link(edges, root, node, "documented");
        }

        foreach (var procedure in dental.History(citizen.Id))
        {
            var node = Add(nodes, "dental", procedure.Id.ToString(), procedure.Description, procedure.PerformedAt, new Dictionary<string,string>{{"sigtap",procedure.SigtapCode},{"tooth",procedure.Tooth?.ToString() ?? ""},{"sextant",procedure.Sextant?.ToString() ?? ""}});
            Link(edges, root, node, "dental_care");
        }

        foreach (var item in billing.Production().Where(x => x.CitizenId == citizen.Id))
        {
            var node = Add(nodes, "sus_production", item.Id.ToString(), item.ProcedureName, new DateTimeOffset(item.ServiceDate.ToDateTime(new TimeOnly(12,0)), TimeSpan.FromHours(-3)), new Dictionary<string,string>{{"procedure",item.ProcedureCode},{"form",item.BillingForm},{"source",item.Source}});
            Link(edges, root, node, "generated_production");
        }

        var ordered = nodes.OrderByDescending(x => x.OccurredAt).ToList();
        var continuity = Continuity(citizen, demo, scheduling, diagnostics, telemedicine, clinicalOrders);
        return new CareTraceGraph(citizen.Id, citizen.Name, ordered, edges, continuity, DateTimeOffset.UtcNow);
    }

    public static CareContinuitySummary Continuity(
        Citizen citizen,
        DemoStore demo,
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        TelemedicineStore telemedicine,
        ClinicalOrderStore clinicalOrders)
    {
        var gaps = new List<CareGap>();
        foreach (var item in demo.Regulation().Where(x => x.CitizenId == citizen.Id && x.Status is "waiting" or "authorized"))
        {
            var days = (DateTimeOffset.UtcNow - item.RequestedAt).TotalDays;
            if (days >= 7 || item.Priority is "emergency" or "urgent" or "high")
                gaps.Add(new("regulation", item.Id.ToString(), item.Priority is "emergency" or "urgent" ? "critical" : "high", $"Regulação {item.Specialty} em {item.Status} há {Math.Floor(days)} dia(s).", "Revisar prioridade/destino e registrar decisão regulatória."));
        }
        foreach (var booking in scheduling.Bookings().Where(x => x.CitizenId == citizen.Id && x.Status == "no_show"))
            gaps.Add(new("scheduling", booking.Id.ToString(), "high", $"Falta registrada em {booking.Specialty} ({booking.Unit}).", "Reavaliar necessidade, contatar cidadão e considerar fila/remarcação conforme protocolo municipal."));
        foreach (var order in diagnostics.Orders(citizen.Id).Where(x => x.Result?.Critical == true && x.Result.CriticalAcknowledgementStatus != "acknowledged"))
            gaps.Add(new("diagnostic", order.Id.ToString(), "critical", $"Resultado crítico de {order.ExamName} ainda sem ciência registrada.", "Registrar ciência do profissional e conduta."));
        foreach (var session in telemedicine.Sessions().Where(x => x.CitizenId == citizen.Id && x.Status == "interrupted"))
            gaps.Add(new("telemedicine", session.Id.ToString(), "high", "Teleconsulta interrompida sem conclusão.", "Retomar sala ou converter para atendimento presencial/regulado."));
        foreach (var order in clinicalOrders.Orders(citizen.Id).Where(x => x.Status == "held"))
            gaps.Add(new("clinical_order", order.Id.ToString(), "high", $"Ordem de {order.Medication} está suspensa.", "Revisar motivo da suspensão e definir continuidade pela equipe responsável."));
        foreach (var plan in clinicalOrders.CarePlans(citizen.Id).Where(x => x.Status == "active" && x.Tasks.Any(t => t.Status == "open" && t.DueAt < DateTimeOffset.UtcNow)))
            gaps.Add(new("care_plan", plan.Id.ToString(), "high", $"Plano de cuidado possui tarefa vencida: {plan.Goal}.", "Reatribuir ou concluir a tarefa e registrar desfecho."));
        return new CareContinuitySummary(citizen.Id, gaps.OrderByDescending(x => RiskRank(x.Severity)).ToList(), gaps.Count == 0 ? "stable" : gaps.Any(x => x.Severity == "critical") ? "critical_attention" : "attention", DateTimeOffset.UtcNow);
    }

    private static CareTraceNode Add(List<CareTraceNode> nodes, string type, string sourceId, string label, DateTimeOffset occurredAt, IReadOnlyDictionary<string,string> metadata)
    {
        var node = new CareTraceNode(Guid.NewGuid(), type, sourceId, label, occurredAt, metadata);
        nodes.Add(node);
        return node;
    }
    private static void Link(List<CareTraceEdge> edges, CareTraceNode from, CareTraceNode to, string relation) => edges.Add(new(Guid.NewGuid(), from.Id, to.Id, relation));
    private static int RiskRank(string severity) => severity switch { "critical" => 3, "high" => 2, _ => 1 };
}

public sealed record CareTraceGraph(Guid CitizenId, string CitizenName, IReadOnlyList<CareTraceNode> Nodes, IReadOnlyList<CareTraceEdge> Edges, CareContinuitySummary Continuity, DateTimeOffset GeneratedAt);
public sealed record CareTraceNode(Guid Id, string Type, string SourceId, string Label, DateTimeOffset OccurredAt, IReadOnlyDictionary<string,string> Metadata);
public sealed record CareTraceEdge(Guid Id, Guid From, Guid To, string Relation);
public sealed record CareContinuitySummary(Guid CitizenId, IReadOnlyList<CareGap> Gaps, string Status, DateTimeOffset GeneratedAt);
public sealed record CareGap(string Domain, string SourceId, string Severity, string Description, string SuggestedOperationalAction);
