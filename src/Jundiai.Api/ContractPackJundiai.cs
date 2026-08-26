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
            SchedulingStore scheduling,
            DiagnosticsAdvancedStore diagnostics,
            SusBillingEngineStore billing,
            InventoryAdvancedStore inventory,
            TelemedicineStore telemedicine,
            EvidenceLedgerStore evidence) =>
            Results.Ok(store.Readiness(demo, operations, identities, scheduling, diagnostics, billing, inventory, telemedicine, evidence)));
        endpoints.MapGet("/api/contract/jundiai/evidence-map", (ContractPackJundiaiStore store) => Results.Ok(store.EvidenceMap()));
        endpoints.MapGet("/api/contract/jundiai/non-code-blockers", () => Results.Ok(new[]
        {
            new { id = "HAB-AT-29", severity = "critical", owner = "habilitação", description = "Comprovar atestado(s) de capacidade técnica compatíveis com a exigência editalícia referente ao quantitativo de unidades de saúde. Não é resolvido por software." },
            new { id = "EXT-CADSUS", severity = "external", owner = "integração", description = "Acesso/homologação CadSUS depende de autorização e credenciais oficiais." },
            new { id = "EXT-RNDS", severity = "external", owner = "integração", description = "RNDS/SI-PNI e demais serviços nacionais dependem de credenciais, versões e homologações vigentes." },
            new { id = "MIG-LEGACY", severity = "external", owner = "implantação", description = "Migração definitiva depende de acesso ao legado, dicionário de dados, volumes e critérios de aceite da CIJUN/município." },
            new { id = "OPS-24X7", severity = "operational", owner = "operação", description = "SLA, suporte presencial/remoto e equipe dedicada exigem comprovação operacional além do código." }
        }));
        return endpoints;
    }
}

public sealed class ContractPackJundiaiStore
{
    private readonly IReadOnlyList<JundiaiPocBlock> _blocks;

    public ContractPackJundiaiStore()
    {
        _blocks =
        [
            Block(1, "Administração, segurança e auditoria", "security", "/login.html", ["RBAC default-deny", "sessão", "MFA POC", "lockout", "auditoria", "ledger de evidências", "LGPD/break-glass"], ["/api/security/readiness", "/api/access/context", "/api/evidence/verify", "/api/audit/privacy/readiness"]),
            Block(2, "Cadastros e identificação do cidadão", "registration", "/registration.html", ["MPI municipal", "CPF/CNS", "busca normalizada", "demografia", "endereço/contato", "território", "duplicidade", "merge auditado"], ["/api/citizens/master/readiness", "/api/citizens/master", "/api/citizens/master/duplicates/candidates"]),
            Block(3, "Regulação", "regulation", "/", ["fila", "prioridade", "transição", "destino", "trilha"], ["/api/regulation"]),
            Block(4, "Agendamento", "scheduling", "/agenda.html", ["grades", "cotas", "bloqueios", "encaixe controlado", "fila de espera", "check-in", "falta", "cancelamento", "remarcação", "perda/ocupação"], ["/api/scheduling/readiness", "/api/scheduling/bookings", "/api/scheduling/loss-report"]),
            Block(5, "Recepção e atendimento na unidade", "reception", "/operations.html", ["check-in", "prioridade", "chamada", "sala", "profissional"], ["/api/ubs/reception", "/api/units"]),
            Block(6, "Prontuário multiprofissional e odontologia", "ehr", "/", ["Patient 360", "médico", "enfermagem", "multi", "odontograma por superfície", "periodontal", "produção odontológica"], ["/api/clinical/workspaces", "/api/dental/v2/readiness"]),
            Block(7, "Laboratório e imagem", "diagnostics", "/diagnostics.html", ["pedido", "agenda", "coleta", "execução", "resultado", "crítico", "PACS/LIS boundary"], ["/api/diagnostics/v2/readiness", "/api/diagnostics/v2/orders"]),
            Block(8, "Saúde da Família e território", "psf", "/esus.html", ["família", "domicílio", "área", "microárea", "visita", "produção APS"], ["/api/psf/households", "/api/psf/esus/export-demo"]),
            Block(9, "Imunização", "immunization", "/operations.html", ["lote", "validade", "dose", "aplicação", "estoque", "campanha"], ["/api/immunization/lots", "/api/immunization/campaigns"]),
            Block(10, "Produção e faturamento SUS", "billing", "/billing-v2.html", ["produção nominal", "SIGTAP parametrizado", "críticas", "fechamento", "reabertura", "checksum", "exportação POC"], ["/api/sus/sigtap", "/api/sus/billing/v2/batches"]),
            Block(11, "Farmácia, materiais e almoxarifado", "inventory", "/operations.html", ["estoque", "lote", "validade", "dispensação", "inventário", "recall", "controlados"], ["/api/inventory/v2/readiness", "/api/warehouse"]),
            Block(12, "ACS móvel/offline", "acs", "/acs.html", ["captura offline", "fila local", "sincronização", "visita territorial"], ["/api/psf/acs/visits", "/api/psf/acs/sync"]),
            Block(13, "Cidadão digital e telemedicina", "citizen-telemedicine", "/telemedicine.html", ["porta digital", "red flags", "consentimento", "handoff", "sala de espera", "preflight", "participantes", "teleconsulta", "resumo clínico"], ["/api/citizen/intelligent-access/evaluate", "/api/telemedicine/readiness", "/api/telemedicine/sessions"]),
            Block(14, "Indicadores, gestão e evidências", "analytics", "/poc.html", ["dashboard", "readiness", "auditoria", "evidência", "mapa de aderência", "telemetria", "Production Gates"], ["/api/dashboard", "/api/contract/jundiai/readiness", "/api/evidence/verify", "/api/operations/telemetry"])
        ];
    }

    public object Definition() => new
    {
        contractPack = "JUNDIAI-RCE-008-2026-POC",
        version = "2026.08.26",
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
        SchedulingStore scheduling,
        DiagnosticsAdvancedStore diagnostics,
        SusBillingEngineStore billing,
        InventoryAdvancedStore inventory,
        TelemedicineStore telemedicine,
        EvidenceLedgerStore evidence)
    {
        var states = new List<JundiaiBlockReadiness>
        {
            State(1, identities.DemoUsers().Count >= 8 && evidence.Verify().Valid ? "implemented_poc" : "partial", 94, "Autenticação POC, MFA em perfis sensíveis, RBAC, ledger encadeado, break-glass e governança de privacidade."),
            State(2, demo.Citizens().Count > 0 ? "implemented_poc" : "pending", 89, "MPI municipal com CPF/CNS, busca normalizada, demografia, território, correção justificada e reconciliação; CadSUS permanece fronteira externa."),
            State(3, demo.Regulation().Count > 0 ? "implemented_poc" : "partial", 92, "Fila regulatória, prioridades e máquina de estados disponíveis."),
            State(4, scheduling.Slots(null, null, null).Count > 0 ? "implemented_poc" : "partial", 94, "Grades, cotas, bloqueios, overbooking, fila, lifecycle, remarcação, no-show e relatório de perda/ocupação."),
            State(5, operations.Units().Count == 58 ? "implemented_poc" : "partial", 88, "Recepção/check-in/chamada integrada ao diretório demonstrativo de 58 unidades."),
            State(6, demo.Citizens().Count > 0 ? "implemented_poc" : "partial", 90, "Patient 360 multiprofissional mais odontologia avançada e ligação com faturamento."),
            State(7, diagnostics.Orders(null).Count > 0 ? "implemented_poc" : "partial", 90, "Pedido até laudo/resultado crítico; PACS/LIS reais dependem de integração."),
            State(8, demo.Households().Count > 0 ? "implemented_poc" : "partial", 87, "Território, domicílio, família, ACS e formulários APS demonstrativos."),
            State(9, demo.VaccineLots().Count > 0 ? "implemented_poc" : "partial", 87, "Lote, aplicação, campanha e baixa de estoque; RNDS/SI-PNI externos."),
            State(10, billing.Production().Count > 0 ? "implemented_poc" : "partial", 91, "Produção, catálogo parametrizado, crítica, versionamento, checksum e cockpit próprio; layout oficial exige implantação vigente."),
            State(11, inventory.Lots().Count > 0 ? "implemented_poc" : "partial", 91, "Lote/validade, inventário, recall, alertas e livro demonstrativo de controlados."),
            State(12, demo.Households().Count > 0 ? "implemented_poc" : "partial", 90, "PWA ACS com persistência local no navegador e sincronização posterior."),
            State(13, "implemented_poc", 93, $"Porta Digital, kernel determinístico e sala de telemedicina operável; {telemedicine.Sessions().Count} sessão(ões) na instância."),
            State(14, evidence.Verify().Valid ? "implemented_poc" : "partial", 95, "Dashboard, auditoria, Contract Pack, Evidence Ledger, telemetria, governança e Production Gates explícitos.")
        };
        var weighted = (int)Math.Round(states.Average(x => x.Score));
        return new JundiaiContractReadiness("JUNDIAI-RCE-008-2026-POC", DateTimeOffset.UtcNow, weighted, states,
        [
            "Atestado(s) de capacidade técnica / quantitativo de unidades: validar habilitação documental em paralelo.",
            "CadSUS, RNDS/SI-PNI, BNAFAR/Hórus, PACS/LIS e outros terceiros: integração/homologação externa não deve ser falsamente declarada como concluída.",
            "PostgreSQL, migration, checkpoint/outbox e tenant scope possuem fundação implementada; ainda falta migrar todos os stores de domínio e executar hardening/DR/segurança de produção.",
            "IdP corporativo, observabilidade centralizada, backup/PITR, DR e migração real do legado continuam gates de industrialização/implantação."
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
public sealed record JundiaiBlockReadiness(int Block, string Status, int Score, string Evidence);
public sealed record JundiaiContractReadiness(string ContractPack, DateTimeOffset GeneratedAt, int OverallScore, IReadOnlyList<JundiaiBlockReadiness> Blocks, IReadOnlyList<string> CriticalNotes);
