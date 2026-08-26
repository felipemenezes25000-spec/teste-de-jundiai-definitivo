using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class PocScenarioEndpoints
{
    public static IEndpointRouteBuilder MapPocScenarioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/poc/scenarios", (PocScenarioStore store) => Results.Ok(store.Runs()));
        endpoints.MapPost("/api/poc/scenarios/golden-path", (
            PocScenarioStore store,
            DemoStore demo,
            SchedulingStore scheduling,
            TelemedicineStore telemedicine,
            DiagnosticsAdvancedStore diagnostics,
            ClinicalDocumentStore documents,
            AiGovernanceStore ai,
            EvidenceLedgerStore evidence) =>
            Results.Ok(store.RunGoldenPath(demo, scheduling, telemedicine, diagnostics, documents, ai, evidence)));
        endpoints.MapGet("/api/poc/readiness", (
            ContractPackJundiaiStore contract,
            DemoStore demo,
            MunicipalOperationsStore operations,
            DemoIdentityStore identities,
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            SusBillingEngineStore billing,
            InventoryAdvancedStore inventory,
            TelemedicineStore telemedicine,
            EvidenceLedgerStore evidence,
            IntegrationRegistryStore integrations,
            LegacyMigrationStore migration,
            OperationalReadinessStore ops,
            AiGovernanceStore ai) => Results.Ok(new
        {
            contract = contract.Readiness(demo, operations, identities, scheduling, diagnostics, billing, inventory, telemedicine, evidence),
            evidence = evidence.Verify(),
            integrations = integrations.Readiness(),
            migration = new { batches = migration.Batches().Count, accepted = migration.Batches().Count(x => x.Status == "accepted") },
            operations = ops.SlaDashboard(),
            ai = new { decisions = ai.Decisions(null).Count, policies = ai.Policies().Count },
            generatedAt = DateTimeOffset.UtcNow
        }));
        return endpoints;
    }
}

public sealed class PocScenarioStore
{
    private readonly ConcurrentDictionary<string, PocScenarioRun> _runs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PocScenarioRun> Runs() => _runs.Values.OrderByDescending(x => x.ExecutedAt).ToList();

    public PocScenarioRun RunGoldenPath(
        DemoStore demo,
        SchedulingStore scheduling,
        TelemedicineStore telemedicine,
        DiagnosticsAdvancedStore diagnostics,
        ClinicalDocumentStore documents,
        AiGovernanceStore ai,
        EvidenceLedgerStore evidence)
    {
        if (_runs.TryGetValue("golden-path", out var existing)) return existing;
        lock (_runs)
        {
            if (_runs.TryGetValue("golden-path", out existing)) return existing;

            var citizen = demo.Citizens().FirstOrDefault() ?? throw new InvalidOperationException("POC sem cidadão seed.");
            var artifacts = new List<PocScenarioArtifact>();

            var regulation = demo.Regulation().FirstOrDefault(x => x.CitizenId == citizen.Id);
            if (regulation is null)
                regulation = demo.CreateRegulation(new CreateRegulationRequest(citizen.Id, citizen.HealthUnit, "Cardiologia", "high", "Ambulatório Central", "Cenário ouro da POC"));
            artifacts.Add(Artifact("regulation", regulation.Id, regulation.Status, regulation.Specialty));

            var slot = scheduling.Slots(null, null, null).FirstOrDefault(x => !x.Blocked && x.Booked < x.Capacity)
                ?? throw new InvalidOperationException("POC sem slot disponível para cenário ouro.");
            var booking = scheduling.Book(new BookSlotRequest(slot.Id, citizen.Id, citizen.Name, regulation.Priority, "poc-golden-path"));
            artifacts.Add(Artifact("scheduling", booking.Id, booking.Status, $"{booking.Specialty} · {booking.StartsAt:O}"));

            var tele = telemedicine.Sessions().FirstOrDefault(x => x.CitizenId == citizen.Id);
            if (tele is null)
            {
                tele = telemedicine.Create(new CreateTelemedicineSessionRequest(citizen.Id, citizen.Name, booking.Id, "Telemedicina Municipal", "Clínica Geral"));
                tele = telemedicine.Preflight(tele.Id, new TelemedicinePreflightRequest(true, true, true, "good", "POC browser"));
                tele = telemedicine.Consent(tele.Id, new TelemedicineConsentRequest(true, "telemed-poc-v1", citizen.Name));
                tele = telemedicine.Transition(tele.Id, new TelemedicineTransitionRequest("ready", null));
                tele = telemedicine.AddParticipant(tele.Id, new TelemedicineParticipantRequest("Dr. Eduardo Martins", "physician", "CRM 000001"));
                tele = telemedicine.Transition(tele.Id, new TelemedicineTransitionRequest("in_progress", null));
                tele = telemedicine.AttachClinicalSummary(tele.Id, new TelemedicineClinicalSummaryRequest("Dr. Eduardo Martins", "Paciente estável, sem sinal de alarme na demonstração.", "Solicitar exame e manter seguimento regulado.", "I10", ["exam-order"]));
                tele = telemedicine.Transition(tele.Id, new TelemedicineTransitionRequest("completed", null));
            }
            artifacts.Add(Artifact("telemedicine", tele.Id, tele.Status, tele.RoomKey));

            var order = diagnostics.Orders(citizen.Id).FirstOrDefault(x => x.ExamCode == "0205020046");
            if (order is null)
            {
                order = diagnostics.CreateOrder(new CreateDiagnosticOrderRequest(citizen.Id, "0205020046", "Ultrassonografia de abdome total", "imaging", "routine", citizen.HealthUnit, "Centro de Imagem", "Dr. Eduardo Martins", "CRM 000001", "I10", "Cenário ouro integrado"), demo);
                order = diagnostics.Perform(order.Id, new PerformDiagnosticOrderRequest("Dra. Paula Imagem", "CRM 000002", "Ultrassom US-POC-01", "ultrassonografia", "ACC-POC-008"));
                order = diagnostics.RegisterResult(order.Id, new RegisterDiagnosticResultRequest(null, null, null, "Exame demonstrativo sem achado agudo relevante.", false, "Dra. Paula Imagem", "CRM 000002"));
            }
            artifacts.Add(Artifact("diagnostics", order.Id, order.Status, order.ExamName));

            var document = documents.Documents(citizen.Id).FirstOrDefault(x => x.Type == "exam-order");
            if (document is null)
            {
                document = documents.Create(new CreateClinicalDocumentRequest(citizen.Id, "exam-order", "Pedido de ultrassonografia", "Solicito ultrassonografia de abdome total para continuidade do cuidado demonstrativo.", "I10", "Dr. Eduardo Martins", "CRM 000001"), demo);
                document = documents.SignDemo(document.Id, new SignClinicalDocumentRequest("Dr. Eduardo Martins", "CRM 000001"));
            }
            artifacts.Add(Artifact("document", document.Id, document.Status, document.Sha256));

            var decision = ai.Decisions(citizen.Id).FirstOrDefault(x => x.UseCase == "clinical-note-draft");
            if (decision is null)
            {
                decision = ai.Record(new CreateAiDecisionRequest(citizen.Id, "clinical-note-draft", "poc-model", "1.0", "clinical-note-v1", "Dados clínicos resumidos e anonimizados na POC.", "Rascunho: manter seguimento e revisar resultado do exame.", 0.91m, "Dr. Eduardo Martins"));
                decision = ai.Review(decision.Id, new ReviewAiDecisionRequest("approved", "Dr. Eduardo Martins", "CRM 000001", "Conteúdo revisado para o cenário demonstrativo."));
            }
            artifacts.Add(Artifact("ai-flight-recorder", decision.Id, decision.ReviewStatus, decision.OutputHash));

            foreach (var artifact in artifacts)
                evidence.Append(new CreateEvidenceEventRequest("poc.orchestrator", "golden-path.artifact", $"{artifact.Domain}:{artifact.Id}", "POC-GOLDEN", artifact.Detail, "scenario-artifact"));

            var run = new PocScenarioRun(Guid.NewGuid(), "golden-path", citizen.Id, citizen.Name, "completed", artifacts, evidence.Verify().Valid, DateTimeOffset.UtcNow);
            _runs["golden-path"] = run;
            return run;
        }
    }

    private static PocScenarioArtifact Artifact(string domain, Guid id, string status, string? detail) => new(domain, id, status, detail);
}

public sealed record PocScenarioRun(Guid Id, string Name, Guid CitizenId, string CitizenName, string Status, IReadOnlyList<PocScenarioArtifact> Artifacts, bool EvidenceChainValid, DateTimeOffset ExecutedAt);
public sealed record PocScenarioArtifact(string Domain, Guid Id, string Status, string? Detail);
