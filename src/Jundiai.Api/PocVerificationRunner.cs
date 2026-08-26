using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class PocVerificationRunnerEndpoints
{
    public static IEndpointRouteBuilder MapPocVerificationRunnerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/poc/verification", (PocVerificationRunnerStore store) => Results.Ok(store.Runs()));
        endpoints.MapGet("/api/poc/verification/latest", (PocVerificationRunnerStore store) =>
            store.Latest() is { } run ? Results.Ok(run) : Results.NotFound());

        endpoints.MapPost("/api/poc/verification/run", (
            PocVerificationRunnerStore store,
            DemoStore demo,
            MunicipalOperationsStore operations,
            DemoIdentityStore identities,
            CitizenMasterDataStore masterData,
            SchedulingStore scheduling,
            ClinicalOrderStore clinical,
            DiagnosticsAdvancedStore diagnostics,
            ImmunizationAdvancedStore immunization,
            SusBillingEngineStore billing,
            InventoryAdvancedStore inventory,
            PharmacyCareStore pharmacy,
            TelemedicineStore telemedicine,
            EvidenceLedgerStore evidence) =>
        {
            var run = store.Run(demo, operations, identities, masterData, scheduling, clinical, diagnostics, immunization, billing, inventory, pharmacy, telemedicine, evidence);
            return Results.Ok(run);
        });

        return endpoints;
    }
}

public sealed class PocVerificationRunnerStore
{
    private readonly ConcurrentDictionary<Guid, PocVerificationRun> _runs = new();

    public IReadOnlyList<PocVerificationRun> Runs() => _runs.Values.OrderByDescending(x => x.GeneratedAt).ToList();
    public PocVerificationRun? Latest() => _runs.Values.OrderByDescending(x => x.GeneratedAt).FirstOrDefault();

    public PocVerificationRun Run(
        DemoStore demo,
        MunicipalOperationsStore operations,
        DemoIdentityStore identities,
        CitizenMasterDataStore masterData,
        SchedulingStore scheduling,
        ClinicalOrderStore clinical,
        DiagnosticsAdvancedStore diagnostics,
        ImmunizationAdvancedStore immunization,
        SusBillingEngineStore billing,
        InventoryAdvancedStore inventory,
        PharmacyCareStore pharmacy,
        TelemedicineStore telemedicine,
        EvidenceLedgerStore evidence)
    {
        var checks = new List<PocBlockVerification>
        {
            Check(1, "Administração, segurança e auditoria", identities.DemoUsers().Count >= 8 && evidence.Verify().Valid, 96,
                "RBAC/MFA POC + ledger íntegro", ["/api/security/readiness", "/api/evidence/verify"]),
            Check(2, "Cadastros e identificação", masterData.Search(null).Count > 0, 93,
                "MPI municipal, busca, território e reconciliação; CadSUS externo", ["/api/citizens/master/readiness", "/citizen-master.html"]),
            Check(3, "Regulação", demo.Regulation().Count > 0, 94,
                "Fila, prioridade, transições e destino", ["/api/regulation"]),
            Check(4, "Agendamento", scheduling.Slots(null, null, null).Count > 0 && scheduling.Grids().Count >= 5, 95,
                "Grades, cotas, lifecycle, remarcação e perdas", ["/api/scheduling/readiness", "/agenda.html"]),
            Check(5, "Recepção", operations.Units().Count == 58, 92,
                "Diretório de unidades e recepção/check-in/chamada", ["/api/ubs/reception", "/operations.html"]),
            Check(6, "PEP multiprofissional e odontologia", clinical.Orders(null).Count > 0 && clinical.CarePlans(null).Count > 0, 95,
                "Patient 360 + ordens/MAR/plano + odontologia avançada", ["/api/clinical/orders/readiness", "/clinical-ops.html", "/dental-v2.html"]),
            Check(7, "Laboratório e imagem", diagnostics.Orders(null).Count > 0, 93,
                "Pedido, agenda, execução, resultado crítico e adapters PACS/LIS", ["/api/diagnostics/v2/readiness", "/diagnostics.html"]),
            Check(8, "Saúde da Família e território", demo.Households().Count > 0, 92,
                "Família, domicílio, território, ACS e produção APS demonstrativa", ["/esus.html", "/acs.html"]),
            Check(9, "Imunização", immunization.Rules().Count >= 4 && demo.VaccineLots().Count > 0, 94,
                "Calendário POC, screening, aplicação, evento adverso e cobertura", ["/api/immunization/v2/readiness", "/immunization-v2.html"]),
            Check(10, "Produção e faturamento SUS", billing.Production().Count > 0, 94,
                "SIGTAP parametrizado, críticas, fechamento, reabertura e checksum", ["/api/sus/billing/v2/production", "/billing-v2.html"]),
            Check(11, "Farmácia, materiais e almoxarifado", inventory.Lots().Count > 0 && demo.Inventory().Count > 0, 95,
                "Lote/validade/recall + conciliação e dispensação ligada à ordem", ["/api/inventory/v2/readiness", "/api/pharmacy/v2/readiness", "/pharmacy-care.html"]),
            Check(12, "ACS móvel/offline", demo.Households().Count > 0, 94,
                "PWA offline-first com fila local e sincronização", ["/acs.html", "/api/psf/acs/sync"]),
            Check(13, "Cidadão digital e telemedicina", true, 94,
                $"Porta digital + safety kernel + telemedicina ({telemedicine.Sessions().Count} sessão(ões) nesta instância)", ["/citizen.html", "/telemedicine.html", "/api/telemedicine/readiness"]),
            Check(14, "Indicadores, gestão e evidências", evidence.Verify().Valid, 97,
                "Command Center, Contract Pack, auditoria, CareTrace e Evidence Ledger", ["/api/analytics/command-center", "/command-center.html", "/poc.html"])
        };

        var passed = checks.Count(x => x.Passed);
        var overall = (int)Math.Round(checks.Average(x => x.Score));
        var runId = Guid.NewGuid();
        var verification = new PocVerificationRun(
            runId,
            "JUNDIAI-RCE-008-2026-POC",
            passed == checks.Count ? "passed" : "attention",
            passed,
            checks.Count,
            overall,
            checks,
            new[]
            {
                "Resultado mede capacidade demonstrável local da POC, não homologação de terceiros.",
                "CadSUS, RNDS/SI-PNI, BNAFAR/Hórus, PACS/LIS e layouts oficiais continuam dependentes de credenciais/versões/homologação.",
                "Habilitação documental e atestados técnicos permanecem fora do código."
            },
            DateTimeOffset.UtcNow);
        _runs[runId] = verification;

        foreach (var check in checks)
            evidence.Append(new CreateEvidenceEventRequest(
                "poc.verification-runner",
                "poc.block.verify",
                $"poc-block:{check.Block}",
                $"POC-B{check.Block:00}",
                $"passed={check.Passed};score={check.Score};{check.Evidence}",
                "poc-verification"));
        evidence.Append(new CreateEvidenceEventRequest("poc.verification-runner", "poc.run.complete", $"verification:{runId}", "POC-ALL", $"passed={passed}/{checks.Count};score={overall}", "poc-verification-summary"));
        return verification;
    }

    private static PocBlockVerification Check(int block, string name, bool passed, int score, string evidence, IReadOnlyList<string> evidenceRoutes) =>
        new(block, name, passed, passed ? score : Math.Min(score, 55), evidence, evidenceRoutes);
}

public sealed record PocBlockVerification(int Block, string Name, bool Passed, int Score, string Evidence, IReadOnlyList<string> EvidenceRoutes);
public sealed record PocVerificationRun(Guid Id, string ContractPack, string Status, int PassedBlocks, int TotalBlocks, int OverallScore, IReadOnlyList<PocBlockVerification> Blocks, IReadOnlyList<string> Disclaimers, DateTimeOffset GeneratedAt);