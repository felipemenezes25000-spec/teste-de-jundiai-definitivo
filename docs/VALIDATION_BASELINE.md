# Baseline de Validação — Jundiaí HealthOS POC

Esta página registra a última baseline técnica consolidada que foi efetivamente executada no GitHub Actions. Ela evita confundir código recém-adicionado com capacidade já comprovada por build, smoke e navegador.

## Baseline validada

- **Data:** 26/08/2026
- **Workflow:** `ci`
- **Run:** `30`
- **Run ID:** `32954612211`
- **Commit validado:** `9cc2edcb47b24e3523d817b9c4f3d18ad5409408`
- **Conclusão:** `success`
- **Runner:** Ubuntu 24.04
- **Runtime:** .NET 8
- **Banco do teste durável:** PostgreSQL 16
- **Navegador E2E:** Chromium via Playwright

> Esta baseline comprova a versão acima. Commits posteriores só passam a integrar uma nova baseline depois de nova validação consolidada.

## O que o run 30 comprovou

### Compilação e higiene

- validação estática dos JavaScript do frontend e testes E2E;
- validação sintática dos scripts shell;
- verificação de material sensível óbvio no repositório público;
- `dotnet restore`;
- build Release da API;
- nenhum módulo recém-adicionado impediu a composição do container de DI ou o startup da aplicação.

### Fluxo funcional integrado

- smoke integrado da POC;
- cenário assistencial wave 4;
- runner funcional dos **14/14 blocos**;
- Evidence Ledger íntegro;
- Contract Pack e blockers não-código preservados;
- blocker crítico `HAB-AT-29` explicitamente presente.

### Preflight da apresentação

O endpoint `POST /api/poc/presentation/prepare` foi exigido como `ready=true` e validou:

- cenário ouro concluído;
- runner **14/14**;
- Evidence Pack íntegro;
- **23 páginas críticas** existentes;
- **11 assets críticos** existentes;
- **8/8 checks** aprovados;
- Evidence Ledger íntegro;
- governança de integrações sem promoção indevida;
- exposição explícita dos blockers que software não resolve.

### Evidence Pack

Foram verificados:

- geração de snapshot consolidado;
- índice dos 14 blocos;
- SHA-256 canônico de 64 caracteres;
- recomputação de hash;
- vínculo com Evidence Ledger;
- `demonstrationIntegrityReady=true`;
- exportação JSON preservando a identidade do pacote.

### Dossiê da Banca

O run 30 também validou o artefato final da apresentação:

- geração de dossiê contendo preflight, Evidence Pack, identidade de build e disclaimers;
- código curto no formato `JUN-XXXX-XXXX-XXXX`;
- SHA-256 canônico próprio do dossiê;
- verificação do código derivado do hash;
- verificação do Evidence Pack embutido;
- verificação do Evidence Ledger;
- preflight `READY`, `14/14` blocos e `8/8` checks;
- blocker `HAB-AT-29` preservado dentro do artefato;
- exportação JSON;
- identidade de build vinculada ao `GITHUB_SHA` durante a execução no GitHub Actions.

O Dossiê é uma prova de integridade demonstrativa. Ele **não é** assinatura de release, SBOM, attestation SLSA, assinatura ICP-Brasil ou carimbo oficial do tempo.

### Browser E2E

Quatro testes Chromium passaram:

1. login + MFA + botão **Preparar banca completa** → `READY`, `8/8`, `14/14`;
2. geração, verificação e download do Evidence Pack;
3. geração, verificação e download do Dossiê da Banca, inclusive conferência de `sourceRevision == GITHUB_SHA` no CI;
4. navegação pelas superfícies críticas da apresentação sem erro fatal de documento/script/stylesheet.

Isso reduz o risco clássico de “API verde, tela quebrada na banca”.

### PostgreSQL, recovery e messaging

O mesmo run comprovou, em PostgreSQL 16 real:

- migrations e readiness;
- checkpoint resumido;
- checkpoint completo de bounded contexts;
- manifesto SHA-256;
- canonicalização compatível com `jsonb`;
- recovery drill e restore preview;
- inbox idempotente;
- outbox idempotente;
- retry;
- dead-letter;
- requeue manual justificado;
- trilha no Evidence Ledger.

## O que esta baseline NÃO comprova

O run 30 não deve ser usado como evidência de:

- homologação CadSUS, RNDS, SI-PNI, e-SUS/DATASUS, BNAFAR/Hórus, PACS/LIS, gov.br ou outros terceiros;
- arquivo BPA oficialmente homologado;
- assinatura ICP-Brasil ou ACT/carimbo oficial;
- migração real do legado CIJUN;
- cobertura operacional 24x7 ou disponibilidade da equipe exigida;
- resolução do `HAB-AT-29` ou de qualquer habilitação documental;
- PostgreSQL como fonte transacional de verdade de todos os bounded contexts;
- backup gerenciado, PITR, failover ou DR produtivo;
- pentest, hardening completo, SAST/DAST ou certificação de segurança;
- teste de carga com volumetria real do município;
- release assinada, SBOM formal ou attestation de supply chain;
- autorização para go-live.

## Estado do CI após a baseline

Após o run 30, o workflow foi devolvido para **`workflow_dispatch` apenas**. Uma consulta posterior confirmou que o total permaneceu em 30 runs, portanto o commit de retorno para manual-only não disparou um run 31.

## Regra de uso

Ao apresentar a POC, é correto dizer:

> “A baseline consolidada de 26/08/2026, run 30, passou build, smokes funcionais, Chromium E2E, Evidence Pack, Dossiê verificável e PostgreSQL/recovery/messaging.”

Não é correto transformar essa frase em “plataforma homologada”, “produção aprovada” ou “todos os requisitos de contratação resolvidos”.
