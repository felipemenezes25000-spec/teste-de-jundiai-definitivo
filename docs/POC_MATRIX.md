# Matriz POC — Jundiaí RCE 008/2026

Estado funcional do repositório. Esta matriz separa deliberadamente **fluxo demonstrável**, **fundação de industrialização**, **integração/homologação externa** e **obrigação operacional/documental**.

## Legenda

- **IMPLEMENTADO POC**: API + regra + fluxo demonstrável existem neste repositório.
- **FUNDAÇÃO IMPLEMENTADA**: mecanismo técnico foi implementado e testado, mas ainda não representa operação produtiva completa.
- **PARCIAL**: núcleo existe, mas depende de maior profundidade, dado real, integração ou implantação.
- **EXTERNO**: depende de credencial, fornecedor, layout, homologação ou autorização oficial.
- **OPERACIONAL**: depende de equipe/processo/SLA além do software.

> Regra central: **IMPLEMENTADO POC != HOMOLOGADO != PRODUÇÃO**.

## 14 blocos

| # | Bloco | Estado | Evidência atual |
|---:|---|---|---|
| 1 | Administração, segurança e auditoria | IMPLEMENTADO POC | login demonstrativo, PBKDF2-SHA256, lockout, MFA em perfis sensíveis, sessão randômica, RBAC default-deny, auditoria, LGPD/break-glass e ledger SHA-256 encadeado |
| 2 | Cadastros | IMPLEMENTADO POC / EXTERNO no CadSUS | MPI municipal, CPF/CNS, busca normalizada, demografia, endereço/contato, território, duplicidade/reconciliação, cadastro profissional e 58 unidades demo; CadSUS real é EXTERNO |
| 3 | Regulação | IMPLEMENTADO POC | fila, prioridade, origem/destino, transições, referência/contrarreferência, handoff da Porta Digital e rastreabilidade |
| 4 | Agendamento | IMPLEMENTADO POC | grades, slots, cotas, bloqueio, capacidade/encaixe, fila de espera, lifecycle, remarcação, no-show e relatório de perda/ocupação |
| 5 | Recepção | IMPLEMENTADO POC | check-in, prioridade, fila, chamada, sala, profissional e diretório demonstrativo de unidades |
| 6 | PEP multiprofissional / odontologia | IMPLEMENTADO POC | Patient 360, workspaces profissionais, encontros, ordens clínicas, MAR, planos de cuidado, documentos, odontograma FDI por superfície, periodontal por sextante e produção odontológica |
| 7 | Laboratório e imagem | IMPLEMENTADO POC / EXTERNO nas integrações | pedido, agenda, coleta, execução, laudo, resultado crítico/ciência, anexos e metadados; PACS/LIS reais são EXTERNOS |
| 8 | Saúde da Família / território | IMPLEMENTADO POC | família, domicílio, área, microárea, ACS, cadastro individual/domiciliar, visita APS e exportação demonstrativa |
| 9 | Imunização | IMPLEMENTADO POC / EXTERNO na transmissão | calendário POC, screening, lote, validade, dose, via/local, profissional, baixa de estoque, cobertura, evento adverso e campanhas; RNDS/SI-PNI real é EXTERNO |
| 10 | Produção e faturamento SUS | IMPLEMENTADO POC / PARCIAL oficial | produção nominal, catálogo SIGTAP parametrizado/reduzido, críticas CBO/idade/sexo/dente/sextante, competência, fechamento, reabertura, versionamento e checksum; layout oficial vigente é etapa de implantação |
| 11 | Farmácia / materiais / almoxarifado | IMPLEMENTADO POC | conciliação medicamentosa, ordem clínica ativa, dispensação vinculada, lote, validade, mínimos, fornecedor/NF, inventário/divergência, recall, ciência por unidade, alertas e livro demonstrativo de controlados |
| 12 | ACS móvel/offline | IMPLEMENTADO POC | PWA offline-first, persistência local, fila de captura sem rede e sincronização posterior |
| 13 | Cidadão + telemedicina | IMPLEMENTADO POC / EXTERNO no vídeo produtivo | Porta Digital, red flags determinísticas, consentimento/handoff idempotente, sala de espera, preflight, participantes, máquina de estados, teleconsulta e resumo clínico |
| 14 | Analytics / gestão / evidência | IMPLEMENTADO POC | Command Center, dashboards, regulação aging, risco de abastecimento, segurança clínica, SLA, Contract Pack, runner 14/14, Evidence Ledger, CareTrace, AI Flight Recorder, Production Gates, Evidence Pack, preflight browser e Dossiê da Banca com identidade do build |

## Diferenciais HealthOS demonstráveis

### Contract Pack Jundiaí

`/api/contract/jundiai/readiness` calcula aderência dos 14 blocos e mantém bloqueadores não resolvidos por software. `/poc.html` transforma isso em cockpit determinístico para a banca.

### Runner dos 14 blocos

`POST /api/poc/verification/run` verifica os 14 blocos contra o estado funcional da instância e grava evidências requisito a requisito no Evidence Ledger. A baseline validada executa **14/14 blocos**.

### Evidence Pack da banca

`POST /api/poc/evidence-pack` gera um snapshot consolidado contendo:

- resultado fresco/reutilizado do runner 14/14;
- readiness do Contract Pack;
- índice de cada bloco com capacidades, tela, endpoints e referências do Evidence Ledger;
- registro das integrações e seu estado real;
- Production Gates;
- estado de persistência/recovery/messaging;
- bloqueadores não resolvidos por código;
- janela de eventos do Evidence Ledger;
- SHA-256 calculado sobre JSON canônico determinístico.

`/evidence-pack.html` apresenta o pacote e permite verificar/exportar o JSON. `/api/poc/evidence-pack/latest/verify` recalcula o hash e verifica também a cadeia do Evidence Ledger.

### Preparar Banca + Browser E2E

`POST /api/poc/presentation/prepare` orquestra a preparação e só devolve `ready=true` quando passam os **8 checks** do preflight:

1. cenário ouro;
2. runner 14/14;
3. Evidence Pack;
4. páginas críticas;
5. assets críticos;
6. Evidence Ledger;
7. governança de integrações;
8. exposição dos blockers não-código.

A baseline do run 30 validou **23 páginas**, **11 assets** e **8/8 checks**. Playwright/Chromium testa login + MFA, botão `Preparar banca completa`, Evidence Pack, Dossiê e superfícies críticas de apresentação.

### Dossiê da Banca e identidade do build

`POST /api/poc/dossier` congela:

- o preflight da apresentação;
- o Evidence Pack integral;
- a identidade do build/processo;
- os blockers explícitos;
- disclaimers de não-produção.

O artefato recebe:

- SHA-256 canônico próprio;
- código curto `JUN-XXXX-XXXX-XXXX` derivado do hash;
- endpoint independente de verificação;
- exportação JSON;
- tela `/dossier.html` pronta para impressão/PDF.

`GET /api/platform/build-identity` expõe revisão e run quando `JUNDIAI_BUILD_SHA`, `GITHUB_SHA` ou equivalentes são injetados. No GitHub Actions do run 30, o browser E2E comprovou que a revisão exportada pelo Dossiê era exatamente o `GITHUB_SHA` da execução.

Isso é uma **prova de integridade e provenance demonstrativa**, não assinatura de release, SBOM formal ou attestation produtiva.

### Cenário Ouro

`POST /api/poc/scenarios/golden-path` prepara, de forma idempotente, uma jornada integrada contendo:

1. regulação;
2. agendamento;
3. teleconsulta completa;
4. diagnóstico e resultado;
5. documento clínico com hash e assinatura demonstrativa;
6. decisão de IA registrada e revisada por humano;
7. evidências encadeadas.

### CareTrace

`/caretrace.html` reúne eventos de território, ACS, regulação, imunização, farmácia, diagnóstico, telemedicina, documentos, odontologia e produção SUS numa visão longitudinal, preservando a origem de cada evento e sinalizando lacunas de continuidade.

### Governança de IA

O AI Flight Recorder registra modelo, versão, prompt, hash de entrada/saída, confiança, risco, necessidade de revisão humana, resultado da revisão e eventual override. Prescrição autônoma é explicitamente proibida na política da POC e red flags de emergência não são delegadas a IA generativa.

### Registro de integrações

CadSUS, RNDS, SI-PNI, e-SUS APS, DATASUS BPA, SIGTAP, BNAFAR/Hórus, PACS, LIS, gov.br, ICP-Brasil, carimbo do tempo e vídeo são tratados como integrações governadas. O sistema **não permite marcar `homologated`/`production_enabled` sem EvidenceReference explícita**.

### Persistência e recovery

A fundação PostgreSQL possui EF Core, migration versionada, escopo instituição/unidade, checkpoint resumido, checkpoint completo de bounded contexts, manifesto SHA-256, canonicalização JSON compatível com `jsonb`, verificação de integridade, preview seguro de restauração e recovery drill com medição de idade/RPO observado.

Isso é **FUNDAÇÃO IMPLEMENTADA**, não promessa de backup/PITR/failover produtivos.

### Inbox / outbox / idempotência

Existe fundação persistente para:

- outbox transacional;
- replay idempotente por chave;
- inbox com receipt persistido e deduplicação;
- SHA-256 canônico de payload;
- estado de retry;
- dead-letter;
- requeue manual justificado;
- isolamento por instituição.

Workers reais, backoff/jitter, broker/queue gerenciada e adapters externos produtivos ainda são implantação.

### LGPD e observabilidade

Há finalidade/minimização demonstrativas, break-glass temporário com trilha, revogação, exportação do titular com hash, correlation ID, health/readiness, métricas agregadas de requisição/erro/latência e telemetria operacional. SOC, SIEM, retenção formal, SLO/SLA produtivos e resposta a incidentes continuam industrialização.

### Migração de legado

Há workspace com manifesto SHA-256 de origem, mapping campo-a-campo, validação, quarentena, reconciliação e aceite. Isso demonstra método; não afirma migração real sem acesso formal ao legado CIJUN.

### Operação e treinamento

Há service desk demonstrativo com severidade/SLA, transições e breach, mais plano de treinamento, turmas, capacidade e lista de presença. Cobertura contratual 24x7, equipe dedicada e comprovação operacional continuam OPERACIONAIS.

## O que ainda separa a POC de produção

- migrar os stores de domínio em memória para PostgreSQL como fonte transacional de verdade;
- definir retenção, arquivamento legal e isolamento produtivo completo;
- IdP/MFA corporativos, secret manager e ciclo de certificados;
- observabilidade central, logs/traces/metrics gerenciados, alertas e SOC/processo de incidente;
- backup gerenciado, PITR, cópia offsite, restauração em ambiente isolado, RTO/RPO contratual e failover/DR;
- workers/broker de integração, backoff/jitter e inbox/outbox por adapter produtivo;
- testes de carga/capacidade com volumetria real municipal;
- ampliar E2E de navegador para jornadas negativas, acessibilidade, responsividade, múltiplos browsers/dispositivos e todos os fluxos de negócio por bloco;
- implantação e migração reais do legado CIJUN;
- assinatura ICP-Brasil/carimbo de tempo reais;
- provedor de vídeo e rede TURN conforme arquitetura final;
- layouts/protocolos oficiais vigentes de DATASUS/e-SUS e demais sistemas;
- credenciais/homologações CadSUS, RNDS, SI-PNI, BNAFAR/Hórus, PACS/LIS etc.;
- SBOM formal, release assinada, artifact attestation e supply-chain provenance produtiva.

## Baseline técnica validada — run 30

A baseline consolidada atual está detalhada em `docs/VALIDATION_BASELINE.md`.

- **Data:** 26/08/2026
- **Run:** 30
- **Run ID:** `32954612211`
- **Commit validado:** `9cc2edcb47b24e3523d817b9c4f3d18ad5409408`
- **Conclusão:** success

O run comprovou em uma mesma execução:

- validação de JavaScript e shell;
- higiene básica de segredo no repositório público;
- restore/build Release .NET 8;
- smokes funcional, plataforma e wave 4;
- runner 14/14;
- preflight 8/8, 23 páginas e 11 assets;
- Evidence Pack íntegro;
- Dossiê da Banca com hash/código/export e `sourceRevision == GITHUB_SHA` no CI;
- **4 testes Chromium E2E**;
- PostgreSQL 16, migrations, checkpoints, recovery drill, inbox/outbox, idempotência, retry, dead-letter e requeue.

Após a validação, o GitHub Actions foi retornado para **manual-only** e não houve run 31 automático.

## Bloqueador comercial/documental crítico

A qualidade da POC **não substitui a habilitação**. A comprovação de capacidade técnica exigida pelo certame — incluindo o requisito relacionado ao quantitativo de unidades de saúde identificado na análise do edital — deve ser resolvida documentalmente em paralelo. O software preserva esse fato explicitamente como `HAB-AT-29`.
