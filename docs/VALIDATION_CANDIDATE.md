# Candidato de validação consolidada

Este arquivo marca o lote acumulado após a baseline 33 que deve ser submetido a uma única validação consolidada.

Escopo do candidato:

- segurança fail-closed e RBAC negativo;
- MFA com fallback POC explicitamente configurável e fail-closed quando desabilitado;
- auth guard Bearer para superfícies legadas;
- auditoria estática de autenticação de frontend;
- inventário de dependências com detecção de drift, explicitamente não-SBOM;
- release provenance com quatro artefatos runtime;
- Governança/Supply Chain;
- responsividade e semântica de telas-chave;
- preflight 8/8 com 25 páginas / 13 assets;
- 12 testes Chromium preparados;
- Evidence Pack, Dossiê e Kit de Contingência;
- PostgreSQL/recovery/inbox/outbox/dead-letter.

Este documento **não afirma aprovação**. O resultado só deve ser promovido para `VALIDATION_BASELINE.md` após o workflow completo terminar verde.
