# Jundiaí HealthOS — RCE 008/2026

Repositório público dedicado à implementação e demonstração da solução para o RCE 008/2026 da CIJUN/Jundiaí.

A base reaproveita **conceitos e componentes técnicos seguros** do RenoveJá Public Service, mas mantém uma linha independente orientada à POC. Segredos, certificados, credenciais, dados pessoais reais e runbooks internos não devem ser copiados para este repositório.

## Entradas principais

Após iniciar a API:

- `/login.html` — autenticação + MFA demonstrativos;
- `/poc.html` — **Modo Banca**, Contract Pack, preflight e os 14 blocos;
- `/verification.html` — runner verificável dos 14 blocos;
- `/evidence-pack.html` — **Evidence Pack da banca**, com SHA-256 canônico e exportação JSON;
- `/dossier.html` — **Dossiê da Banca**, artefato verificável/imprimível com preflight, Evidence Pack e identidade do build;
- `/command-center.html` — visão executiva e alertas transversais;
- `/caretrace.html` — jornada longitudinal CareTrace;
- `/governance.html` — integrações, IA, migração, LGPD, persistência/recovery e Production Gates;
- `/registration.html` — MPI/cadastro mestre do cidadão;
- `/workforce.html` — profissionais, CBO, conselho, equipe e lotação;
- `/referrals.html` — referência e contrarreferência;
- `/clinical-ops.html` — ordens, MAR e plano de cuidado;
- `/agenda.html` — agenda/lifecycle avançado;
- `/telemedicine.html` — jornada de telemedicina;
- `/immunization-v2.html` — imunização avançada;
- `/pharmacy-care.html` — conciliação e dispensação vinculada;
- `/diagnostics.html` — laboratório/imagem;
- `/dental-v2.html` — odontologia avançada;
- `/billing-v2.html` — produção/faturamento SUS;
- `/operations.html` — recepção, vacinação, farmácia, almoxarifado, digitalização e mutirões;
- `/citizen.html` — Porta Digital e Intelligent Access;
- `/esus.html` — APS / território / fichas demonstrativas;
- `/acs.html` — ACS offline-first.

## O que já existe na POC

- login demonstrativo, PBKDF2, MFA, lockout e RBAC default-deny;
- MPI municipal, CPF/CNS, busca normalizada, duplicidade/reconciliação e cadastro profissional;
- cidadãos, território e diretório de 58 unidades demonstrativas;
- regulação, referência/contrarreferência e agenda central com grades, cotas, bloqueio, capacidade e fila de espera;
- recepção UBS;
- Patient 360 multiprofissional;
- ordens clínicas, MAR e planos de cuidado;
- odontologia FDI por superfície + periodontal por sextante + produção SUS;
- laboratório/imagem do pedido ao resultado, inclusive resultado crítico com ciência;
- PSF/e-SUS demonstrativo e ACS offline;
- imunização com screening, cobertura e eventos adversos;
- produção/faturamento SUS com catálogo SIGTAP parametrizado/reduzido, críticas, fechamento, reabertura, versionamento e checksum;
- conciliação medicamentosa, dispensação vinculada, farmácia, almoxarifado, inventário, alertas, recall e controlados;
- telemedicina com sala de espera, preflight, consentimento, participantes e máquina de estados;
- documentos clínicos com hash e envelope de assinatura **demonstrativo**;
- Evidence Ledger SHA-256 encadeado;
- Contract Pack Jundiaí e readiness dos 14 blocos;
- runner automático dos 14 blocos com evidência requisito a requisito;
- Evidence Pack consolidado e exportável com hash canônico;
- preflight **Preparar banca completa** com 8 checks, 23 páginas e 11 assets críticos;
- Dossiê da Banca com código `JUN-XXXX-XXXX-XXXX`, SHA-256 próprio, Evidence Pack embutido e identidade do build;
- endpoint de build identity com revisão/run quando injetados pelo CI/deploy;
- CareTrace e lacunas de continuidade assistencial;
- AI Flight Recorder + revisão humana;
- registro governado de integrações externas;
- workspace de migração/reconciliação de legado;
- service desk/SLA e treinamento;
- analytics executivo e segurança populacional;
- cenário ouro idempotente para preparação da banca;
- fundação PostgreSQL EF Core com migration versionada e tenant/institution scope;
- checkpoint resumido e checkpoint completo de domínios;
- manifesto SHA-256 e canonicalização determinística compatível com PostgreSQL `jsonb`;
- recovery drill/restore preview e medição de idade de checkpoint;
- inbox/outbox persistentes, idempotência, retry, dead-letter e requeue justificado;
- health/readiness, correlation ID e telemetria operacional;
- finalidade/minimização, break-glass e exportação demonstrativa do titular;
- browser E2E em Chromium para login/MFA, preflight READY, Evidence Pack, Dossiê e superfícies críticas.

## Executar

Requer .NET 8.

```bash
dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj
```

Para validação funcional local:

```bash
dotnet build src/Jundiai.Api/Jundiai.Api.csproj -c Release
bash scripts/smoke.sh
bash scripts/smoke-platform.sh
bash scripts/smoke-wave4.sh
```

Para browser E2E:

```bash
npm install
npx playwright install chromium
npm run test:e2e
```

O smoke PostgreSQL é executado em ambiente com PostgreSQL 16 configurado. O GitHub Actions permanece configurado para **execução manual**, evitando CI a cada pequeno commit.

## Baseline técnica validada

A baseline consolidada de **26/08/2026** está registrada em `docs/VALIDATION_BASELINE.md`:

- GitHub Actions **run 30**;
- run ID `32954612211`;
- commit validado `9cc2edcb47b24e3523d817b9c4f3d18ad5409408`;
- runner 14/14;
- preflight 8/8, 23 páginas e 11 assets;
- Evidence Pack íntegro;
- Dossiê verificável com vínculo ao `GITHUB_SHA` no CI;
- 4 testes Chromium E2E;
- PostgreSQL 16 + recovery + messaging.

Commits posteriores não devem ser tratados como parte dessa baseline até uma nova validação consolidada.

## Documentação importante

- `docs/POC_MATRIX.md` — matriz de aderência e maturidade atual;
- `docs/POC_RUNBOOK.md` — roteiro determinístico para a banca;
- `docs/VALIDATION_BASELINE.md` — última versão efetivamente comprovada por CI;
- `docs/POC_DEMO_CREDENTIALS.md` — credenciais exclusivamente demonstrativas;
- `docs/PRODUCTION_GATES.md` — o que obrigatoriamente precisa acontecer antes de produção.

## Regra de honestidade técnica

**IMPLEMENTADO NA POC != HOMOLOGADO != PRODUÇÃO.**

CadSUS, RNDS/SI-PNI, e-SUS/DATASUS, BNAFAR/Hórus, PACS/LIS, gov.br, ICP-Brasil, ACT/carimbo do tempo, vídeo e demais sistemas externos só podem receber status de homologação/produção quando houver credencial, ambiente e evidência externa reais.

Evidence Pack e Dossiê comprovam integridade demonstrativa do estado capturado; não equivalem a assinatura de release, SBOM formal, attestation de supply chain, ICP-Brasil ou carimbo oficial.

Da mesma forma, uma POC forte e uma fundação técnica validada não substituem habilitação documental, atestados de capacidade, equipe operacional, migração real, domínio transacional integral em PostgreSQL, backup/PITR/failover, DR produtivo, IdP corporativo, secret manager, SOC/SIEM, testes de carga, homologações e demais Production Gates.
