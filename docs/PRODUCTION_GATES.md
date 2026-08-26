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

> O recovery drill da POC é evidência de engenharia; não deve ser apresentado como DR produtivo certificado.

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

**Status atual:** FUNDAÇÃO IMPLEMENTADA / BROWSER E2E PENDENTE.

Já existe:

- smoke integrado;
- smoke de governança;
- smoke assistencial wave 4;
- runner dos 14 blocos;
- cenário ouro;
- Evidence Ledger;
- Evidence Pack com índice dos 14 blocos, integrações, gates, evidências e SHA-256 canônico;
- exportação JSON verificável;
- roteiro de banca reproduzível.

Critérios de saída:

- E2E real em navegador para fluxos críticos;
- jornada automatizada de UI por bloco;
- testes de acessibilidade/responsividade definidos;
- evidência de build imutável/release candidate;
- regressão após mudança relevante;
- execução final do roteiro na infraestrutura de apresentação.

## Gate 10 — Habilitação e operação contratual

**Status atual:** FORA DO CÓDIGO / CRÍTICO.

Inclui, entre outros:

- atestado(s) de capacidade técnica compatíveis com a exigência do certame, inclusive o requisito relacionado ao quantitativo de unidades de saúde identificado na análise;
- profissionais/equipe e cobertura exigidos;
- documentos jurídicos, fiscais e econômico-financeiros;
- responsabilidades de implantação e suporte;
- qualquer comprovação formal exigida no edital/anexos.

A POC não substitui esse gate.

## Regra de promoção

Nenhuma pontuação alta em `/api/contract/jundiai/readiness`, nenhum `14/14` no runner e nenhum Evidence Pack íntegro promovem automaticamente um Production Gate.

A POC mede **demonstrabilidade e coerência técnica**; os Production Gates medem **aptidão para operação real e cumprimento de implantação/contrato**.
