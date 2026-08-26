# Baseline de Validação — Jundiaí HealthOS POC

Esta página registra a última baseline técnica consolidada efetivamente executada no GitHub Actions. Ela evita confundir código recém-adicionado com capacidade comprovada por build, smoke, navegador e PostgreSQL.

## Baseline validada

- **Data:** 26/08/2026
- **Workflow:** `ci`
- **Run:** `33`
- **Run ID:** `32975517312`
- **Commit validado:** `6800cf18e1a76a4b145efbf3a5c563662fa14003`
- **Conclusão:** `success`
- **Runner:** Ubuntu 24.04
- **Runtime:** .NET 8
- **Banco do teste durável:** PostgreSQL 16
- **Navegador E2E:** Chromium via Playwright

> Esta baseline comprova a revisão acima. Commits posteriores só passam a integrar uma nova baseline depois de nova validação consolidada.

## O que o run 33 comprovou

### Compilação e higiene

- validação estática dos JavaScript do frontend e do Playwright;
- validação sintática dos scripts shell;
- verificação de material sensível óbvio no repositório público;
- `dotnet restore`;
- build Release completo;
- composição do DI/startup com Release Provenance, Dossiê v2 e Kit de Contingência.

### Fluxo funcional integrado

- smoke integrado da POC;
- cenário assistencial wave 4;
- runner funcional dos **14/14 blocos**;
- Evidence Ledger íntegro;
- Contract Pack e blockers não-código preservados;
- blocker crítico `HAB-AT-29` explicitamente presente.

### Preflight da apresentação

`POST /api/poc/presentation/prepare` foi exigido como `ready=true` e validou:

- cenário ouro concluído;
- runner **14/14**;
- Evidence Pack íntegro;
- **24 páginas críticas** existentes;
- **12 assets críticos** existentes;
- **8/8 checks** aprovados;
- Evidence Ledger íntegro;
- governança de integrações sem promoção indevida;
- exposição explícita dos blockers que software não resolve.

### Evidence Pack

Foram verificados:

- geração de snapshot consolidado;
- índice dos 14 blocos;
- SHA-256 canônico de 64 caracteres;
- recomputação do hash;
- vínculo com Evidence Ledger;
- `demonstrationIntegrityReady=true`;
- exportação JSON preservando a identidade do pacote.

### Runtime artifact provenance

O run 33 validou o manifesto de proveniência dos bytes carregados pela instância:

- SHA-256 do manifesto canônico;
- presença e hash dos três artefatos runtime esperados: `Jundiai.Api.dll`, `.deps.json` e `.runtimeconfig.json`;
- releitura e recomputação dos hashes dos arquivos;
- extração do conjunto de libraries do `.deps.json`;
- hash do conjunto de libraries;
- vínculo da identidade do build ao `GITHUB_SHA` no CI.

Esse mecanismo é chamado deliberadamente de **runtime artifact provenance**. Ele **não é** SBOM formal, attestation SLSA, assinatura de release ou assinatura criptográfica do fornecedor.

### Dossiê da Banca v2

O Dossiê final foi validado contendo:

- preflight READY;
- Evidence Pack integral;
- identidade do build;
- runtime artifact provenance;
- código `JUN-XXXX-XXXX-XXXX`;
- SHA-256 canônico próprio;
- verificação do código derivado do hash;
- recomputação do hash do Dossiê;
- verificação do Evidence Pack e Evidence Ledger;
- verificação do manifesto e dos bytes runtime;
- `14/14` blocos e `8/8` checks;
- `HAB-AT-29` preservado;
- exportação JSON.

### Kit de Contingência da banca

O run 33 também gerou e verificou um ZIP de contingência contendo exatamente:

1. `dossier.json`;
2. `evidence-pack.json`;
3. `release-provenance.json`;
4. `verification.txt`;
5. `presentation-summary.html`;
6. `manifest.json`.

Foram exigidos:

- código `KIT-XXXX-XXXX-XXXX`;
- SHA-256 canônico do manifesto;
- SHA-256 do ZIP;
- cinco arquivos de conteúdo indexados no manifesto;
- tamanho e SHA-256 recalculados de cada arquivo;
- ZIP abrindo corretamente;
- HTML estático contendo o resumo dos 14 blocos;
- HTML autocontido sem dependência `http://` ou `https://`;
- Dossiê de origem íntegro.

O Kit é contingência da apresentação. Ele **não é backup, PITR, failover, DR ou evidência de operação produtiva**.

### Browser E2E

**Cinco testes Chromium passaram:**

1. login + MFA + **Preparar banca completa** → `READY`, `8/8`, `14/14`;
2. geração, verificação e download do Evidence Pack;
3. geração, verificação e download do Dossiê, incluindo provenance runtime e `sourceRevision == GITHUB_SHA` no CI;
4. geração, verificação e download do Kit de Contingência pela interface, incluindo confirmação de arquivo ZIP;
5. navegação pelas superfícies críticas sem erro fatal de documento/script/stylesheet.

Isso reduz o risco clássico de “API verde, tela quebrada na banca” e adiciona um plano B estático verificável.

### PostgreSQL, recovery e messaging

O mesmo run comprovou em PostgreSQL 16 real:

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

O run 33 não deve ser usado como evidência de:

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

Após o run 33, o workflow foi devolvido para **`workflow_dispatch` apenas** no commit `67614cda4279f61a3d55785e1caa3ac6afe9da53`. Uma consulta posterior confirmou que o total permaneceu em **33 runs**, portanto o retorno para manual-only não disparou run 34.

## Regra de uso

Ao apresentar a POC, é correto dizer:

> “A baseline consolidada de 26/08/2026, run 33, passou build, smokes funcionais, Chromium E2E 5/5, 14/14 blocos, READY 8/8, Evidence Pack, Dossiê com provenance runtime, Kit de Contingência verificável e PostgreSQL/recovery/messaging.”

Não é correto transformar essa frase em “plataforma homologada”, “produção aprovada” ou “todos os requisitos de contratação resolvidos”.
