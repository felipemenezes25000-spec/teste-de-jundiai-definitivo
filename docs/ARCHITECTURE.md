# Arquitetura — Jundiaí HealthOS

## Princípio

Este repositório é uma linha independente para o RCE 008/2026. Ele reutiliza padrões e componentes de produto do RenoveJá Public Service, mas não depende do histórico Git privado para executar.

A primeira fundação usa ASP.NET Core 8 + frontend estático/PWA para reduzir risco de demonstração e manter a POC acessível integralmente por navegador.

```text
Browser / PWA ACS
       ↓
ASP.NET Core 8
       ↓
Jornadas integradas de saúde pública
       ↓
Regulação · Produção · Faturamento · Imunização · Estoque · PSF · Dental · Exames
       ↓
Audit trail
```

## Bounded contexts iniciais

### Citizen & Territory

Cidadão, CNS/CPF, UBS, área, microárea, família e domicílio formam a identidade operacional local. CadSUS permanece uma fronteira externa, não uma verdade simulada.

### Regulation

Fila regulada com prioridade, origem, destino, agendamento e transições explícitas. A evolução deve incorporar a trilha append-only e SLAs do RenoveJá.

### SUS Billing

Produção nominal é consolidada por competência, criticada e fechada. A exportação atual é deliberadamente marcada como demonstração. Layout BPA/e-SUS oficial será adapter/versionado.

### Immunization

Aplicação é transacional sobre lote válido e com estoque. O evento guarda vacina, dose, via, local, lote, profissional/conselho e instante.

### Pharmacy & Inventory

Lote, validade, estoque mínimo, controlados e dispensação por cidadão. Próxima fase: entradas, transferências, inventário, XML fiscal, rastreabilidade/recall e BNAFAR.

### Family Health / ACS

Território é Área → Microárea → Domicílio → Família → Cidadão. O ACS possui PWA com fila local e sincronização posterior; PHI deve ser minimizada no armazenamento local.

### Dental

Odontograma por elemento com faces, status, procedimento e histórico. Próxima fase: representação anatômica visual, coroa/raiz e produção SIGTAP por elemento/sextante.

### Diagnostics

Fila e agenda para laboratório/imagem, preparada para mutirões, execução, anexos e resultados.

## O que será portado do RenoveJá

Prioridade de portabilidade:

1. autenticação, RBAC, escopo institucional/unidade e auditoria;
2. citizen/patient 360 e recepção UBS;
3. prontuário, resumo pré-consulta e workspaces profissionais;
4. teleconsulta e consentimentos;
5. Intelligent Access / Porta Digital;
6. white-label institucional;
7. contrato/evidência/HealthOS read models;
8. adapters SUS governados.

Documentação operacional, credenciais, detalhes de conta AWS, runbooks internos e outros artefatos do repositório privado não devem ser publicados automaticamente neste repositório público.

## Persistência

A primeira entrega usa store em memória para permitir validar contrato de API e UX da POC sem carregar dependências externas. A próxima wave deve trocar a implementação por PostgreSQL mantendo os contratos HTTP e os invariantes dos domínios.

## Segurança

Esta fundação ainda não deve ser chamada de produção. Antes disso são obrigatórios:

- autenticação forte;
- autorização por instituição/unidade;
- persistência isolada;
- RLS/tenant isolation;
- criptografia e segredo por provedor;
- logging sem PHI desnecessária;
- backup/DR;
- rate limiting;
- testes de integração e segurança;
- evidência de runtime.
