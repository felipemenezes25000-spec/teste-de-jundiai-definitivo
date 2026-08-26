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
- 5 testes Chromium naquela baseline;
- PostgreSQL/recovery/messaging;
- Dossiê v2 + Kit de Contingência + provenance runtime.

## Alterações implementadas após a baseline

### Segurança fail-closed e compatibilidade autenticada

- APIs protegidas sem sessão válida retornam `401`;
- `X-Demo-Role` fica desabilitado por padrão;
- header de papel enviado quando desabilitado retorna `403` e não concede privilégio;
- opt-in só por `JUNDIAI_ALLOW_DEMO_ROLE_HEADER=true` em ambiente demonstrativo controlado;
- `/api/security/readiness` expõe essa política;
- MFA default `008026` só é permitido quando `Jundiai:Security:PocMode=true` e `Jundiai:Security:Mfa:AllowDefaultCode=true`; desligado esse fallback e sem variável de ambiente, o MFA falha fechado;
- `scripts/smoke-security.sh` prova identidade ACS/médico/admin, autorização negativa, MFA, revogação de sessão, headers defensivos e uma segunda instância sem fallback MFA;
- `auth-client.js` injeta Bearer em `/api/*`, neutraliza headers demonstrativos legados, limpa sessão em `401` e preserva a rota de retorno para o login;
- Centro Municipal, Modo POC, CareTrace, Governança, Faturamento v2, e-SUS e Backoffice foram conectados ao guard compartilhado;
- ACS Campo usa sessão Bearer real;
- exportação demonstrativa e-SUS deixou de ser link direto para API protegida e passou a download autenticado por `fetch` + Blob;
- login aceita somente `next` local seguro e volta à tela anterior após autenticação;
- `scripts/verify-frontend-auth.py` audita páginas + scripts, bloqueia link/form direto para API protegida e exige guard ou Bearer explícito.

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

### Preflight / PWA

- `index.html` passou a fazer parte das páginas críticas;
- `auth-client.js` passou a ser asset crítico;
- preflight implementado nesta wave passa a esperar **25 páginas / 13 assets**, mantendo os mesmos 8 checks conceituais;
- service worker v15 inclui `auth-client.js` no cache da shell protegida;
- a baseline 33 continua 24/12 até a nova validação ficar verde.

### Browser E2E / acessibilidade / responsividade

O próximo run está preparado para executar:

- 7 testes no fluxo principal de apresentação;
- 2 testes de Governança/Supply Chain e 401/403;
- 3 testes adicionais de hardening de frontend:
  - navegação autenticada das superfícies críticas monitorando `document/script/stylesheet/xhr/fetch` para 401/403/5xx;
  - prova de que o guard remove `X-Demo-Role` forjado e mantém o papel autenticado;
  - sessão expirada → limpeza de storage → login com `?next=` preservado;
- semântica mínima e overflow horizontal em 390×844, 768×1024 e 1366×768;
- labels/autocomplete/navegação por teclado no login.

Total preparado: **12 testes Chromium**.

Esses testes ainda não devem ser descritos como “passaram” até a próxima execução manual consolidada.

## Workflow preparado

O workflow continua exclusivamente:

```yaml
on:
  workflow_dispatch:
```

A próxima execução manual inclui, além das barreiras anteriores:

1. `node --check`, incluindo `auth-client.js` e as três suítes E2E;
2. `bash -n` dos smokes;
3. `py_compile` dos verificadores de inventário e cobertura de autenticação;
4. verificação de drift de dependências;
5. auditoria estática de autenticação das superfícies internas;
6. build Release;
7. smoke de autorização negativa;
8. smokes funcional/plataforma/wave 4;
9. 12 testes Chromium E2E;
10. PostgreSQL durability/recovery/messaging.

## Regra de promoção

Somente após um novo run totalmente verde:

- atualizar `docs/VALIDATION_BASELINE.md`;
- atualizar README/POC_MATRIX/PRODUCTION_GATES com o novo número do run;
- promover 25 páginas / 13 assets, os testes Chromium, o hardening de autorização e o inventário de dependências para baseline comprovada.

Até lá, a forma correta de falar é:

> “A wave de segurança, autenticação de frontend, responsividade e dependency inventory está implementada e preparada para validação consolidada; a baseline comprovada continua sendo o run 33.”
