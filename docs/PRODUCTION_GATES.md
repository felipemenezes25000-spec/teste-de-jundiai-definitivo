# Production Gates — Jundiaí HealthOS

Este arquivo evita que evolução rápida de POC seja confundida com autorização para go-live.

## Gate 1 — Persistência transacional

**Status atual:** PENDENTE PARA PRODUÇÃO.

Critérios de saída:

- PostgreSQL provisionado por ambiente;
- schema versionado/migrations;
- transações por caso de uso;
- isolamento por instituição/unidade quando aplicável;
- índices e constraints de integridade;
- retenção/arquivamento documentados;
- nenhum dado clínico relevante dependendo apenas de memória de processo.

## Gate 2 — Identidade e segredo

**Status atual:** POC.

Critérios de saída:

- IdP produtivo definido;
- MFA produtivo;
- lifecycle de sessão/token;
- secret manager;
- certificados fora do source control;
- RBAC/escopo por unidade testado;
- revisão de acesso privilegiado.

## Gate 3 — Integrações externas

**Status atual:** FRONTEIRAS/ADAPTERS POC.

Cada integração precisa de:

1. owner externo;
2. ambiente;
3. endpoint/protocolo/versionamento;
4. credencial/certificado;
5. tratamento de erro/retry/idempotência;
6. evidência de sandbox;
7. evidência de homologação, quando aplicável;
8. evidência de produção antes de marcar `production_enabled`.

O `IntegrationRegistryStore` impede marcar homologação/produção sem `EvidenceReference`.

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
- armazenamento/retensão da evidência.

## Gate 6 — Observabilidade e operação

**Status atual:** domínio de service desk/SLA demonstrável.

Critérios de saída:

- logs estruturados centralizados;
- tracing e correlation ID;
- métricas técnicas e assistenciais;
- alertas;
- SLO/SLA operacional;
- escala de suporte;
- runbooks;
- gestão de incidentes/problemas/mudanças;
- evidência de equipe e cobertura contratual.

## Gate 7 — Continuidade / DR

**Status atual:** arquitetura descrita, sem infraestrutura produtiva nesta POC.

Critérios de saída:

- backups automáticos;
- PITR;
- restore testado;
- RPO/RTO aprovados;
- DR runbook;
- exercício de recuperação;
- dependências externas inventariadas.

## Gate 8 — Performance e segurança

Critérios de saída:

- volumetria municipal validada;
- teste de carga e endurance;
- limites/rate limiting;
- SAST/dependency scan;
- DAST/pentest conforme exigência;
- hardening;
- TLS/ciphers/certificados;
- revisão LGPD;
- testes de autorização negativa.

## Gate 9 — E2E dos 14 blocos

Critérios de saída:

- jornada automatizada por bloco;
- cenário ouro automatizado;
- testes de browser para fluxos críticos;
- evidência de build imutável;
- regressão após mudança relevante;
- roteiro de banca reproduzível.

## Regra de promoção

Nenhuma pontuação alta em `/api/contract/jundiai/readiness` promove automaticamente um Production Gate. A POC mede **demonstrabilidade**; os gates medem **aptidão para operação real**.
