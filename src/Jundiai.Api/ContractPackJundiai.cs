namespace Jundiai.Api;

public static class ContractPackJundiaiEndpoints
{
    public static IEndpointRouteBuilder MapContractPackJundiaiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/contract/jundiai", (ContractPackJundiaiStore store) => Results.Ok(store.Definition()));
        endpoints.MapGet("/api/contract/jundiai/readiness", (
            ContractPackJundiaiStore store,
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
            Results.Ok(store.Readiness(demo, operations, identities, masterData, scheduling, clinical, diagnostics, immunization, billing, inventory, pharmacy, telemedicine, evidence)));
        endpoints.MapGet("/api/contract/jundiai/evidence-map", (ContractPackJundiaiStore store) => Results.Ok(store.EvidenceMap()));
        endpoints.MapGet("/api/contract/jundiai/non-code-blockers", (ContractPackJundiaiStore store) => Results.Ok(store.NonCodeBlockers()));
        return endpoints;
    }
}

public sealed class ContractPackJundiaiStore
{
    private readonly IReadOnlyList<JundiaiPocBlock> _blocks;
    private readonly IReadOnlyList<JundiaiNonCodeBlocker> _nonCodeBlockers;

    public ContractPackJundiaiStore()
    {
        _blocks =
        [
            Block(1, "Administração, segurança e auditoria", "security", "/login.html", ["RBAC default-deny", "sessão", "MFA POC", "lockout", "auditoria", "ledger de evidências", "LGPD/break-glass"], ["/api/security/readiness", "/api/access/context", "/api/evidence/verify", "/api/audit/privacy/readiness"]),
            Block(2, "Cadastros e identificação do cidadão", "registration", "/registration.html", ["MPI municipal", "CPF/CNS", "busca normalizada", "demografia", "endereço/contato", "território", "duplicidade", "merge auditado"], ["/api/citizens/master/readiness", "/api/citizens/master", "/api/citizens/master/duplicates/candidates"]),
            Block(3, "Regulação", "regulation", "/", ["fila", "prioridade", "transição", "destino", "trilha"], ["/api/regulation"]),
            Block(4, "Agendamento", "scheduling", "/agenda.html", ["grades", "cotas", "bloqueios", "encaixe controlado", "fila de espera", "check-in", "falta", "cancelamento", "remarcação", "perda/ocupação"], ["/api/scheduling/readiness", "/api/scheduling/bookings", "/api/scheduling/loss-report"]),
            Block(5, "Recepção e atendimento na unidade", "reception", "/operations.html", ["check-in", "prioridade", "chamada", "sala", "profissional"], ["/api/ubs/reception", "/api/units"]),
            Block(6, "Prontuário multiprofissional e odontologia", "ehr", "/clinical-ops.html", ["Patient 360", "médico", "enfermagem", "ordem clínica", "MAR", "plano de cuidado", "odontograma por superfície", "periodontal", "produção odontológica"], ["/api/clinical/workspaces", "/api/clinical/orders/readiness", "/api/dental/v2/readiness"]),
            Block(7, "Laboratório e imagem", "diagnostics", "/diagnostics.html", ["pedido", "agenda", "coleta", "execução", "resultado", "crítico", "PACS/LIS boundary"], ["/api/diagnostics/v2/readiness", "/api/diagnostics/v2/orders"]),
            Block(8, "Saúde da Família e território", "psf", "/esus.html", ["família", "domicílio", "área", "microárea", "visita", "produção APS"], ["/api/psf/households", "/api/psf/esus/export-demo"]),
            Block(9, "Imunização", "immunization", "/immunization-v2.html", ["lote", "validade", "calendário POC", "screening", "contraindicação/adiamento", "aplicação", "evento adverso", "cobertura", "estoque"], ["/api/immunization/v2/readiness", "/api/immunization/v2/schedule", "/api/immunization/v2/coverage"]),
            Block(10, "Produção e faturamento SUS", "billing", "/billing-v2.html", ["produção nominal", "SIGTAP parametrizado", "críticas", "fechamento", "reabertura", "checksum", "exportação POC"], ["/api/sus/sigtap", "/api/sus/billing/v2/batches"]),
            Block(11, "Farmácia, materiais e almoxarifado", "inventory", "/pharmacy-care.html", ["estoque", "lote", "validade", "inventário", "recall", "controlados", "conciliação medicamentosa", "ordem ativa", "dispensação vinculada", "orientação farmacêutica"], ["/api/inventory/v2/readiness", "/api/pharmacy/v2/readiness", "/api/warehouse"]),
            Block(12, "ACS móvel/offline", "acs", "/acs.html", ["captura offline", "fila local", "sincronização", "visita territorial"], ["/api/psf/acs/visits", "/api/psf/acs/sync"]),
            Block(13, "Cidadão digital e telemedicina", "citizen-telemedicine", "/telemedicine.html", ["porta digital", "red flags", "consentimento", "handoff", "sala de espera", "preflight", "participantes", "teleconsulta", "resumo clínico"], ["/api/citizen/intelligent-access/evaluate", "/api/telemedicine/readiness", "/api/telemedicine/sessions"]),
            Block(14, "Indicadores, gestão e evidências", "analytics", "/command-center.html", ["Command Center", "alertas operacionais", "dashboard", "readiness", "auditoria", "evidência", "mapa de aderência", "telemetria", "runner dos 14 blocos", "Production Gates", "Evidence Pack verificável"], ["/api/analytics/command-center", "/api/poc/verification/latest", "/api/contract/jundiai/readiness", "/api/evidence/verify", "/api/poc/evidence-pack/latest/verify"])
        ];

        _nonCodeBlockers =
        [
            new("HAB-AT-29", "critical", "habilitação", "Comprovar atestado(s) de capacidade técnica compatíveis com a exigência editalícia referente ao quantitativo de unidades de saúde. Não é resolvido por software."),
            new("EXT-CADSUS", "external", "integração", "Acesso/homologação CadSUS depende de autorização e credenciais oficiais."),
            new("EXT-RNDS", "external", "integração", "RNDS/SI-PNI e demais serviços nacionais dependem de credenciais, versões e homologações vigentes."),
            new("MIG-LEGACY", "external", "implantação", "Migração definitiva depende de acesso ao legado, dicionário de dados, volumes e critérios de aceite da CIJUN/município."),
            new("OPS-24X7", "operational", "operação", "SLA, suporte presencial/remoto e equipe dedicada exigem comprovação operacional além do código.")
        ];
    }

    public IReadOnlyList<JundiaiPocBlock> Blocks() => _blocks;
    public IReadOnlyList<JundiaiNonCodeBlocker> NonCodeBlockers() => _nonCodeBlockers;

    public object Definition() => new
    {
        contractPack = "JUNDIAI-RCE-008-2026-POC",
        version = "2026.08.26-evidence-pack",
        purpose = "Implementação demonstrativa orientada aos blocos de POC. Não substitui a leitura jurídica/técnica do edital e anexos vigentes.",
        blocks = _blocks,
        semantics = new
        {
            implementedPoc = "fluxo executável na POC",
            partial = "há fluxo implementado, mas resta profundidade, integração ou homologação",
            external = "depende de sistema/credencial/homologação externa",
            production = "não inferir produção/homologação a partir de implemented_poc"
        }
    };

    public JundiaiContractReadiness Readiness(
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
        var states = new List<JundiaiBlockReadiness>
        {
            State(1, identities.DemoUsers().Count >= 8 && evidence.Verify().Valid ? "implemented_poc" : "partial", 94, "Autenticação POC, MFA em perfis sensíveis, RBAC, ledger encadeado, break-glass e governança de privacidade."),
            State(2, masterData.Search(null).Count > 0 ? "implemented_poc" : "pending", 93, "MPI municipal com CPF/CNS, busca normalizada, demografia, território, correção justificada e reconciliação; CadSUS permanece fronteira externa."),
            State(3, demo.Regulation().Count > 0 ? "implemented_poc" : "partial", 92, "Fila regulatória, prioridades e máquina de estados disponíveis."),
            State(4, scheduling.Slots(null, null, null).Count > 0 ? "implemented_poc" : "partial", 95, "Grades, cotas, bloqueios, overbooking, fila, lifecycle, remarcação, no-show e relatório de perda/ocupação."),
            State(5, operations.Units().Count == 58 ? "implemented_poc" : "partial", 90, "Recepção/check-in/chamada integrada ao diretório demonstrativo de 58 unidades."),
            State(6, clinical.Orders(null).Count > 0 && clinical.CarePlans(null).Count > 0 ? "implemented_poc" : "partial", 95, "Patient 360, ordens clínicas, MAR, plano multiprofissional, odontologia avançada e ligação com faturamento."),
            State(7, diagnostics.Orders(null).Count > 0 ? "implemented_poc" : "partial", 93, "Pedido até laudo/resultado crítico; PACS/LIS reais dependem de integração."),
            State(8, demo.Households().Count > 0 ? "implemented_poc" : "partial", 90, "Território, domicílio, família, ACS e formulários APS demonstrativos."),
            State(9, immunization.Rules().Count >= 4 && demo.VaccineLots().Count > 0 ? "implemented_poc" : "partial", 94, "Calendário demonstrativo, screening, lote/aplicação, evento adverso e cobertura; RNDS/SI-PNI externos."),
            State(10, billing.Production().Count > 0 ? "implemented_poc" : "partial", 93, "Produção, catálogo parametrizado, crítica, versionamento, checksum e cockpit próprio; layout oficial exige implantação vigente."),
            State(11, inventory.Lots().Count > 0 && demo.Inventory().Count > 0 ? "implemented_poc" : "partial", 95, $"Lote/validade, inventário, recall/controlados, conciliação farmacêutica e {pharmacy.Dispensations(null).Count} dispensação(ões) vinculada(s) nesta instância."),
            State(12, demo.Households().Count > 0 ? "implemented_poc" : "partial", 92, "PWA ACS com persistência local no navegador e sincronização posterior."),
            State(13, "implemented_poc", 94, $"Porta Digital, kernel determinístico e sala de telemedicina operável; {telemedicine.Sessions().Count} sessão(ões) na instância."),
            State(14, evidence.Verify().Valid ? "implemented_poc" : "partial", 97, "Command Center, alertas, Contract Pack, runner dos 14 blocos, Evidence Ledger, telemetria, governança, Production Gates e Evidence Pack verificável.")
        };
        var weighted = (int)Math.Round(states.Average(x => x.Score));
        return new JundiaiContractReadiness("JUNDIAI-RCE-008-2026-POC", DateTimeOffset.UtcNow, weighted, states,
        [
            "Atestado(s) de capacidade técnica / quantitativo de unidades: validar habilitação documental em paralelo.",
            "CadSUS, RNDS/SI-PNI, BNAFAR/Hórus, PACS/LIS e outros terceiros: integração/homologação externa não deve ser falsamente declarada como concluída.",
            "PostgreSQL, migrations, checkpoint completo, recovery drill, inbox/outbox, idempotência, retry/dead-letter e tenant scope possuem fundação implementada; ainda falta migrar todos os stores de domínio e executar hardening/DR/segurança de produção.",
            "IdP corporativo, backup/PITR gerenciado, failover/DR, workers/broker de integração e migração real do legado continuam gates de industrialização/implantação."
        ]);
    }

    public IReadOnlyList<object> EvidenceMap() => _blocks.Select(block => (object)new
    {
        block.Number,
        block.Name,
        block.Slug,
        route = block.UiRoute,
        endpoints = block.EvidenceEndpoints,
        demonstration = block.Capabilities.Select((capability, index) => new
        {
            order = index + 1,
            capability,
            evidenceType = index % 2 == 0 ? "api-state" : "ui-flow"
        }).ToArray()
    }).ToList();

    private static JundiaiPocBlock Block(int number, string name, string slug, string uiRoute, IReadOnlyList<string> capabilities, IReadOnlyList<string> evidenceEndpoints) =>
        new(number, name, slug, uiRoute, capabilities, evidenceEndpoints);
    private static JundiaiBlockReadiness State(int block, string status, int score, string evidence) => new(block, status, score, evidence);
}

public sealed record JundiaiPocBlock(int Number, string Name, string Slug, string UiRoute, IReadOnlyList<string> Capabilities, IReadOnlyList<string> EvidenceEndpoints);
public sealed record JundiaiNonCodeBlocker(string Id, string Severity, string Owner, string Description);
public sealed record JundiaiBlockReadiness(int Block, string Status, int Score, string Evidence);
public sealed record JundiaiContractReadiness(string ContractPack, DateTimeOffset GeneratedAt, int OverallScore, IReadOnlyList<JundiaiBlockReadiness> Blocks, IReadOnlyList<string> CriticalNotes);