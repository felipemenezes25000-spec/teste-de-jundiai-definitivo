# Matriz POC — Jundiaí RCE 008/2026

Estado funcional do repositório. Esta matriz separa deliberadamente **fluxo demonstrável**, **industrialização** e **integração/homologação externa**.

## Legenda

- **IMPLEMENTADO POC**: API + regra + fluxo demonstrável existem neste repositório.
- **PARCIAL**: núcleo existe, mas depende de maior profundidade, persistência, integração ou dado real.
- **EXTERNO**: depende de credencial, fornecedor, layout, homologação ou autorização oficial.
- **OPERACIONAL**: depende de equipe/processo/SLA além do software.

> Regra central: **IMPLEMENTADO POC != HOMOLOGADO != PRODUÇÃO**.

## 14 blocos

| # | Bloco | Estado | Evidência atual |
|---:|---|---|---|
| 1 | Administração, segurança e auditoria | IMPLEMENTADO POC | login demonstrativo, PBKDF2-SHA256, lockout, MFA em perfis sensíveis, sessão randômica, RBAC default-deny, auditoria e ledger SHA-256 encadeado |
| 2 | Cadastros | PARCIAL alto | cidadão, CPF/CNS, unidade, território, área/microárea, 58 unidades demo, workspace de migração; CadSUS é EXTERNO |
| 3 | Regulação | IMPLEMENTADO POC | fila, prioridade, origem/destino, transições, handoff da Porta Digital e rastreabilidade |
| 4 | Agendamento | IMPLEMENTADO POC | grades, slots, cotas, bloqueio, capacidade/encaixe, fila de espera e promoção por prioridade |
| 5 | Recepção | IMPLEMENTADO POC | check-in, prioridade, fila, chamada, sala e profissional |
| 6 | PEP multiprofissional / odontologia | IMPLEMENTADO POC | Patient 360, workspaces profissionais, encontros, documentos clínicos, odontograma FDI por superfície, periodontal por sextante e produção odontológica |
| 7 | Laboratório e imagem | IMPLEMENTADO POC / EXTERNO nas integrações | pedido, agenda, coleta, execução, laudo, resultado crítico/ciência, anexos e metadados; PACS/LIS reais são EXTERNOS |
| 8 | Saúde da Família / território | IMPLEMENTADO POC | família, domicílio, área, microárea, ACS, cadastro individual/domiciliar e visita APS |
| 9 | Imunização | IMPLEMENTADO POC / EXTERNO na transmissão | lote, validade, dose, via, local, profissional, baixa de estoque e campanhas; RNDS/SI-PNI real é EXTERNO |
| 10 | Produção e faturamento SUS | IMPLEMENTADO POC / PARCIAL oficial | produção nominal, catálogo SIGTAP reduzido, críticas CBO/idade/sexo/dente/sextante, competência, fechamento, reabertura, histórico e checksum; arquivo oficial vigente é etapa de implantação |
| 11 | Farmácia / materiais / almoxarifado | IMPLEMENTADO POC | dispensação, lote, validade, mínimos, fornecedor/NF, inventário e divergência, recall por lote, ciência por unidade, alertas e livro demonstrativo de controlados |
| 12 | ACS móvel/offline | IMPLEMENTADO POC | PWA, persistência local, captura sem rede e sincronização posterior |
| 13 | Cidadão + telemedicina | IMPLEMENTADO POC / EXTERNO no vídeo produtivo | Porta Digital, red flags determinísticas, consentimento/handoff idempotente, sala de espera, preflight, participantes, máquina de estados e resumo clínico |
| 14 | Analytics / gestão / evidência | IMPLEMENTADO POC | dashboard executivo, regulação aging, risco de abastecimento, segurança clínica, SLA, Contract Pack, Evidence Ledger, CareTrace e AI Flight Recorder |

## Diferenciais HealthOS já demonstráveis

### Contract Pack Jundiaí

`/api/contract/jundiai/readiness` calcula aderência dos 14 blocos e mantém uma lista explícita de bloqueadores não resolvidos por código. `/poc.html` transforma isso em cockpit para a banca.

### Cenário Ouro

`POST /api/poc/scenarios/golden-path` prepara, de forma idempotente, uma jornada integrada contendo:

1. regulação;
2. agendamento;
3. teleconsulta completa;
4. diagnóstico e resultado;
5. documento clínico com hash e assinatura demonstrativa;
6. decisão de IA registrada e revisada por humano;
7. evidências encadeadas.

### CareTrace

`/caretrace.html` reúne eventos de território, ACS, regulação, imunização, farmácia, diagnóstico, telemedicina, documentos, odontologia e produção SUS numa visão longitudinal, preservando a origem de cada evento e sinalizando lacunas de continuidade.

### Governança de IA

O AI Flight Recorder registra modelo, versão, prompt, hash de entrada/saída, confiança, risco, necessidade de revisão humana, resultado da revisão e eventual override. Prescrição autônoma é explicitamente proibida na política da POC e red flags de emergência não são delegadas a IA generativa.

### Registro de integrações

CadSUS, RNDS, SI-PNI, e-SUS APS, DATASUS BPA, SIGTAP, BNAFAR/Hórus, PACS, LIS, gov.br, ICP-Brasil, carimbo do tempo e vídeo são tratados como integrações governadas. O sistema **não permite marcar homologado/produção sem referência de evidência**.

### Migração de legado

Há workspace com manifesto SHA-256 de origem, mapping campo-a-campo, validação, quarentena, reconciliação e aceite. Isso demonstra método; não afirma migração real sem acesso formal ao legado CIJUN.

### Operação e treinamento

Há service desk demonstrativo com severidade/SLA, transições e breach, mais plano de treinamento, turmas, capacidade e lista de presença. Cobertura contratual 24x7 e equipe dedicada continuam OPERACIONAIS.

## O que ainda é industrialização de produção

- PostgreSQL como fonte transacional de verdade em substituição aos stores em memória;
- migrations, retenção, arquivo e isolamento real por instituição/unidade;
- IdP/MFA produtivos, secret manager e ciclo de certificados;
- observabilidade central, SLO/SLA, correlação, alertas e SOC/processo de incidente;
- backup, PITR, restauração testada, DR e RTO/RPO contratual;
- outbox/inbox, retries e idempotência persistente para integrações;
- testes de carga/capacidade com volumetria real municipal;
- E2E em navegador cobrindo os 14 blocos;
- implantação e migração reais do legado CIJUN;
- assinatura ICP-Brasil/carimbo de tempo reais;
- provedor de vídeo e rede TURN conforme arquitetura final;
- layouts/protocolos oficiais vigentes de DATASUS/e-SUS e demais sistemas;
- credenciais/homologações CadSUS, RNDS, SI-PNI, BNAFAR/Hórus, PACS/LIS etc.

## Bloqueador comercial/documental crítico

A qualidade da POC **não substitui a habilitação**. A comprovação de capacidade técnica exigida pelo certame — incluindo o requisito relacionado ao quantitativo de unidades de saúde identificado na análise do edital — deve ser resolvida documentalmente em paralelo.
