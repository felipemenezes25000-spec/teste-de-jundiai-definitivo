# Jundiaí HealthOS — RCE 008/2026

Repositório público dedicado à implementação e demonstração da solução para o RCE 008/2026 da CIJUN/Jundiaí.

A base reaproveita **conceitos e componentes técnicos seguros** do RenoveJá Public Service, mas mantém uma linha independente orientada à POC. Segredos, certificados, credenciais, dados pessoais reais e runbooks internos não devem ser copiados para este repositório.

## Entradas principais

Após iniciar a API:

- `/login.html` — autenticação + MFA demonstrativos;
- `/poc.html` — **Modo Banca**, Contract Pack e os 14 blocos;
- `/caretrace.html` — jornada longitudinal CareTrace;
- `/` — centro municipal / Patient 360 / regulação / faturamento;
- `/operations.html` — recepção, vacinação, farmácia, almoxarifado, digitalização e mutirões;
- `/citizen.html` — Porta Digital e Intelligent Access;
- `/esus.html` — APS / território / fichas demonstrativas;
- `/acs.html` — ACS offline-first.

## O que já existe na POC

- login demonstrativo, PBKDF2, MFA, lockout e RBAC default-deny;
- cidadãos, território e diretório de 58 unidades demonstrativas;
- regulação e agenda central com grades, cotas, bloqueio, capacidade e fila de espera;
- recepção UBS;
- Patient 360 multiprofissional;
- odontologia FDI por superfície + periodontal por sextante + produção SUS;
- laboratório/imagem do pedido ao resultado, inclusive resultado crítico com ciência;
- PSF/e-SUS demonstrativo e ACS offline;
- imunização e campanhas;
- produção/faturamento SUS com catálogo SIGTAP reduzido, críticas, fechamento, reabertura e checksum;
- farmácia, almoxarifado, inventário, alertas, recall e controlados;
- telemedicina com sala de espera, preflight, consentimento, participantes e máquina de estados;
- documentos clínicos com hash e envelope de assinatura **demonstrativo**;
- Evidence Ledger SHA-256 encadeado;
- Contract Pack Jundiaí e readiness dos 14 blocos;
- CareTrace e lacunas de continuidade assistencial;
- AI Flight Recorder + revisão humana;
- registro governado de integrações externas;
- workspace de migração/reconciliação de legado;
- service desk/SLA e treinamento;
- analytics executivo e segurança populacional;
- cenário ouro idempotente para preparação da banca.

## Executar

Requer .NET 8.

```bash
dotnet run --project src/Jundiai.Api/Jundiai.Api.csproj
```

Para validação consolidada:

```bash
dotnet build src/Jundiai.Api/Jundiai.Api.csproj -c Release
bash scripts/smoke.sh
```

O GitHub Actions permanece configurado para **execução manual**, evitando um CI a cada pequeno commit.

## Documentação importante

- `docs/POC_MATRIX.md` — matriz de aderência atual;
- `docs/POC_RUNBOOK.md` — roteiro determinístico para a banca;
- `docs/POC_DEMO_CREDENTIALS.md` — credenciais exclusivamente demonstrativas;
- `docs/PRODUCTION_GATES.md` — o que obrigatoriamente precisa acontecer antes de produção.

## Regra de honestidade técnica

**IMPLEMENTADO NA POC != HOMOLOGADO != PRODUÇÃO.**

CadSUS, RNDS/SI-PNI, e-SUS/DATASUS, BNAFAR/Hórus, PACS/LIS, gov.br, ICP-Brasil, ACT/carimbo do tempo, vídeo e demais sistemas externos só podem receber status de homologação/produção quando houver credencial, ambiente e evidência externa reais.

Da mesma forma, uma POC forte não substitui habilitação documental, atestados de capacidade, equipe operacional, implantação, migração real, persistência, backup/DR, observabilidade, testes de carga e demais Production Gates.
