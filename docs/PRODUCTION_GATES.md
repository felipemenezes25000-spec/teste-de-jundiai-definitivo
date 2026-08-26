# Production Gates — Jundiaí HealthOS

Este arquivo evita que evolução rápida de POC seja confundida com autorização para go-live.

## Convenção de status

- **POC** — fluxo demonstrável, sem pretensão de produção.
- **FUNDAÇÃO IMPLEMENTADA** — mecanismo técnico relevante existe e foi testado, mas ainda falta industrialização/implantação.
- **PENDENTE PARA PRODUÇÃO** — critério obrigatório ainda não foi atendido no contexto produtivo final.
- **EXTERNO** — depende de credencial, contrato, autorização, fornecedor ou homologação de terceiro.

## Gate 1 — Persistência transacional

**Status atual:** FUNDAÇÃO IMPLEMENTADA / PENDENTE PARA PRODUÇÃO.

Já existe:

- PostgreSQL 16 testado em CI;
- EF Core + migration versionada;
- escopo por instituição/unidade;
- transação no checkpoint durável;
- esquema para envelopes, outbox e idempotência;
- checkpoint resumido e checkpoint completo de bounded contexts;
- SHA-256 canônico compatível com `jsonb`.

Critérios de saída para produção:

- PostgreSQL provisionado e gerenciado por ambiente;
- migrar todos os stores de domínio para fonte transacional durável;
- schema completo com índices/constraints por domínio;
- transações por caso de uso;
- isolamento institucional/unidade aplicado de ponta a ponta;
- retenção/arquivamento documentados;
- nenhum dado clínico relevante dependendo apenas de memória de processo.

## Gate 2 — Identidade e segredo

**Status atual:** POC.

Já existe login/MFA/lockout/sessão e RBAC default-deny para demonstrabilidade.

Critérios de saída:

- IdP produtivo definido;
- MFA produtivo;
- lifecycle de sessão/token;
- secret manager;
- certificados fora do source control;
- RBAC/escopo por unidade testado contra identidades reais;
- revisão de acesso privilegiado;
- rotação/revogação e trilha de credenciais.

## Gate 3 — Integrações externas

**Status atual:** FRONTEIRAS/ADAPTERS POC + FUNDAÇÃO DE MESSAGING IMPLEMENTADA.

Já existe:

- registro governado de integrações;
- bloqueio de `homologated`/`production_enabled` sem `EvidenceReference`;
- outbox persistente;
- replay idempotente;
- inbox com receipt persistido e deduplicação;
- retry/dead-letter;
- requeue manual justificado;
- trilha de evidência e isolamento por instituição.

Cada integração produtiva ainda precisa de:

1. owner externo;
2. ambiente;
3. endpoint/protocolo/versionamento;
4. credencial/certificado;
5. worker/consumer real;
6. política de backoff/jitter/circuit breaker;
7. broker/queue quando aplicável;
8. tratamento de erro/retry/idempotência por adapter;
9. evidência de sandbox;
10. evidência de homologação, quando aplicável;
11. evidência de produção antes de marcar `production_enabled`.

## Gate 4 — Migração

**Status atual:** MÉTODO POC.

Critérios de saída:

- acesso formal ao legado;
- dicionário e volumetria reais;
- mapeamento aprovado;
- regras de qualidade/quarentena;
- ensaios de carga;
- reconciliação quantitativa e amostral;
- plano de cutover/rollback;
- aceite formal.

## Gate 5 — Documentos e assinatura

**Status atual:** hash + envelope RSA demonstrativo.

Critérios de saída:

- provedor/certificado ICP-Brasil definido quando exigido;
- política de assinatura e revogação;
- carimbo do tempo quando aplicável;
- validação independente do documento;
- armazenamento/retensão da evidência;
- ciclo de certificado e custódia de chave definidos.

## Gate 6 — Observabilidade, LGPD e operação

**Status atual:** FUNDAÇÃO POC IMPLEMENTADA / OPERAÇÃO PRODUTIVA PENDENTE.

Já existe:

- correlation ID;
- health/live/readiness;
- métricas agregadas de volume, erro e latência;
- service desk/SLA demonstrativo;
- finalidade/minimização demonstrativas;
- break-glass temporário com trilha e revogação;
- exportação demonstrativa do titular com hash.

Critérios de saída:

- logs estruturados centralizados;
- tracing distribuído;
- métricas técnicas e assistenciais gerenciadas;
- alertas e on-call;
- SLO/SLA operacional;
- SIEM/SOC quando exigido;
- retenção e política de acesso a logs;
- escala de suporte;
- runbooks;
- gestão de incidentes/problemas/mudanças;
- plano de resposta a incidente de segurança/LGPD;
- evidência de equipe e cobertura contratual.

## Gate 7 — Continuidade / DR

**Status atual:** FUNDAÇÃO DE RECOVERY IMPLEMENTADA / DR PRODUTIVO PENDENTE.

Já existe na POC com PostgreSQL:

- checkpoint completo de domínios;
- manifesto SHA-256;
- verificação de integridade após round-trip `jsonb`;
- preview de desserialização/restauração sem mutar a POC;
- inventário de domínios críticos;
- recovery drill automatizado;
- medição da idade do checkpoint/RPO observado do exercício.

Critérios de saída:

- backups automáticos gerenciados;
- PITR;
- cópia offsite/isolada;
- restore em ambiente isolado testado;
- benchmark de RTO;
- RPO/RTO aprovados contratualmente;
- estratégia de failover;
- DR runbook;
- exercício periódico de recuperação;
- dependências externas inventariadas no plano de continuidade.

> O recovery drill da POC é evidência de engenharia; não deve ser apresentado como DR produtivo certificado. O Kit de Contingência da banca também não é backup ou DR.

## Gate 8 — Performance e segurança

**Status atual:** PENDENTE PARA PRODUÇÃO.

Critérios de saída:

- volumetria municipal validada;
- teste de carga e endurance;
- limites/rate limiting;
- SAST/dependency scan;
- DAST/pentest conforme exigência;
- hardening;
- TLS/ciphers/certificados;
- revisão LGPD;
- testes de autorização negativa;
- modelagem de ameaça e correção de achados críticos.

## Gate 9 — E2E e evidência dos 14 blocos

**Status atual:** FUNDAÇÃO IMPLEMENTADA / BROWSER E2E IMPLEMENTADO PARA A APRESENTAÇÃO.

Já existe e foi validado no run 33:

- smoke integrado;
- smoke de governança;
- smoke assistencial wave 4;
- runner dos 14 blocos com **14/14**;
- cenário ouro;
- Evidence Ledger;
- Evidence Pack com índice dos 14 blocos, integrações, gates, evidências e SHA-256 canônico;
- preflight **8/8**, **24 páginas** e **12 assets** críticos;
- Dossiê da Banca v2 com hash próprio, código de verificação, identidade de build e provenance runtime;
- Kit de Contingência ZIP com manifesto/hash e HTML autocontido;
- exportações JSON verificáveis;
- Chromium/Playwright com **5 testes E2E** cobrindo login/MFA, `Preparar banca completa`, Evidence Pack, Dossiê, Kit de Contingência e superfícies críticas;
- conferência no CI de `sourceRevision == GITHUB_SHA`.

Ainda falta para maturidade produtiva ampla:

- jornadas de browser por cada fluxo de negócio dos 14 blocos, inclusive caminhos negativos;
- testes de acessibilidade;
- matriz de responsividade/dispositivos;
- cobertura multibrowser quando necessária;
- execução de regressão contra infraestrutura final de apresentação/implantação;
- critérios formais de aceite automatizado por release candidate.

## Gate 10 — Proveniência de release e supply chain

**Status atual:** FUNDAÇÃO DE RUNTIME PROVENANCE IMPLEMENTADA / PENDENTE PARA PRODUÇÃO.

Já existe e foi validado no run 33:

- endpoint de identidade do build;
- captura de repository/revision/run quando injetados pelo CI/deploy;
- runtime artifact manifest canônico;
- SHA-256 de `Jundiai.Api.dll`, `.deps.json` e `.runtimeconfig.json`;
- releitura dos bytes e recomputação de hash;
- inventário das libraries do `.deps.json` e hash do conjunto;
- Dossiê incorporando identidade + runtime provenance;
- Kit de Contingência incorporando `release-provenance.json`;
- validação de que `sourceRevision` no CI correspondia ao `GITHUB_SHA`;
- hashes canônicos do Evidence Pack, Dossiê, manifesto de contingência e ZIP.

Isso melhora a relação `commit → processo → bytes demonstrados`, mas ainda **não equivale a provenance de release assinada**.

Critérios de saída:

- SBOM formal CycloneDX/SPDX ou equivalente aprovado;
- lock/registro de dependências e política de vulnerabilidades;
- digest imutável da imagem/artefato efetivamente implantado;
- artifact attestation/proveniência de build quando adotada;
- assinatura/verificação do release candidate quando aplicável;
- relação rastreável `commit → build → artefato → deploy` na infraestrutura final;
- política de retenção de evidência de release;
- processo de promoção/rollback de versão.

## Gate 11 — Habilitação e operação contratual

**Status atual:** FORA DO CÓDIGO / CRÍTICO.

Inclui, entre outros:

- atestado(s) de capacidade técnica compatíveis com a exigência do certame, inclusive o requisito relacionado ao quantitativo de unidades de saúde identificado na análise;
- profissionais/equipe e cobertura exigidos;
- documentos jurídicos, fiscais e econômico-financeiros;
- responsabilidades de implantação e suporte;
- qualquer comprovação formal exigida no edital/anexos.

A POC não substitui esse gate.

## Baseline de referência

A última validação consolidada está congelada em `docs/VALIDATION_BASELINE.md`:

- run 33;
- ID `32975517312`;
- commit `6800cf18e1a76a4b145efbf3a5c563662fa14003`;
- conclusão `success`.

O workflow foi devolvido para `workflow_dispatch` apenas após essa validação e a contagem permaneceu em 33 runs.

## Regra de promoção

Nenhuma pontuação alta em `/api/contract/jundiai/readiness`, nenhum `14/14`, nenhum Evidence Pack, nenhum Dossiê íntegro, nenhum Kit de Contingência e nenhum browser E2E promovem automaticamente um Production Gate.

A POC mede **demonstrabilidade, coerência técnica e integridade do estado apresentado**; os Production Gates medem **aptidão para operação real, supply chain, implantação e cumprimento de contrato**.
