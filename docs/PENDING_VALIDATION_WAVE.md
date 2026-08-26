# Wave pendente de validação consolidada

Este documento registra alterações implementadas **depois** da baseline validada do run 33. Ele existe para impedir que código novo seja apresentado como capacidade já comprovada por CI.

## Baseline ainda válida

A última baseline comprovada permanece:

- run `33`;
- run ID `32975517312`;
- commit `6800cf18e1a76a4b145efbf3a5c563662fa14003`;
- conclusão `success`;
- 14/14 blocos;
- preflight 8/8;
- 24 páginas / 12 assets;
- 5 testes Chromium;
- PostgreSQL/recovery/messaging;
- Dossiê v2 + Kit de Contingência + provenance runtime.

## Alterações implementadas após a baseline

### Segurança fail-closed

- APIs protegidas sem sessão válida retornam `401`;
- `X-Demo-Role` fica desabilitado por padrão;
- header de papel enviado quando desabilitado retorna `403` e não concede privilégio;
- opt-in só por `JUNDIAI_ALLOW_DEMO_ROLE_HEADER=true` em ambiente demonstrativo controlado;
- `/api/security/readiness` expõe essa política;
- Evidence Pack, Dossiê, Contingência e Governança deixaram de depender de fallback de papel nas chamadas novas/ajustadas;
- `scripts/smoke-security.sh` prova identidade ACS/médico/admin, autorização negativa, MFA e revogação de sessão.

### Supply chain / dependency inventory

- `src/Jundiai.Api/supply-chain.inventory.json` registra dependências diretas .NET/npm e imagens de container;
- o arquivo declara `formalSbom=false` de forma explícita;
- ausência atual de `package-lock.json` é registrada como gap, não ocultada;
- `scripts/verify-dependency-inventory.py` falha em caso de drift entre inventário e `.csproj`, `package.json`, `Dockerfile` ou `compose.yaml`;
- o inventário é copiado para output/publish;
- `ReleaseProvenanceStore` passou de 3 para 4 artefatos runtime hasheados, incluindo `supply-chain.inventory.json`;
- `/api/platform/dependency-inventory` expõe inventário, SHA-256 e limitações;
- Dossiê incorpora e apresenta o hash do inventário;
- Governança ganhou aba **Supply chain** com bytes runtime, bibliotecas, dependências diretas, hash e gaps.

> O inventário POC **não é SBOM formal**. CycloneDX/SPDX, lockfiles completos, análise de vulnerabilidade, digests imutáveis de imagem, artifact attestation e assinatura continuam Production Gates.

### Browser E2E / acessibilidade / responsividade

O próximo run está preparado para executar:

- os 5 testes Chromium da baseline 33;
- teste de semântica mínima e overflow horizontal em 390×844, 768×1024 e 1366×768;
- teste de labels/autocomplete/navegação por teclado no login;
- teste da aba Supply chain na Governança;
- teste de `401` anônimo e `403` para header forjado via `APIRequestContext` do Playwright.

Total preparado: **9 testes Chromium**.

Esses testes ainda não devem ser descritos como “passaram” até a próxima execução manual consolidada.

## Workflow preparado

O workflow continua exclusivamente:

```yaml
on:
  workflow_dispatch:
```

A próxima execução manual inclui, além das barreiras anteriores:

1. `node --check` dos novos testes;
2. `bash -n` do smoke de segurança;
3. `py_compile` do verificador de inventário;
4. verificação de drift de dependências;
5. build Release;
6. smoke de autorização negativa;
7. smokes funcional/plataforma/wave 4;
8. Chromium E2E;
9. PostgreSQL durability/recovery/messaging.

## Regra de promoção

Somente após um novo run totalmente verde:

- atualizar `docs/VALIDATION_BASELINE.md`;
- atualizar README/POC_MATRIX/PRODUCTION_GATES com o novo número do run;
- tratar os 9 testes, o hardening de autorização e o inventário de dependências como baseline comprovada.

Até lá, a forma correta de falar é:

> “A wave de segurança, responsividade e dependency inventory está implementada e preparada para validação consolidada; a baseline comprovada continua sendo o run 33.”
