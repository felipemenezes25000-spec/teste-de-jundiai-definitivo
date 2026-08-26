# Matriz POC — Jundiaí RCE 008/2026

Estado deste repositório após a primeira fundação independente.

Legenda:

- **IMPLEMENTADO**: existe fluxo navegável/API neste repositório.
- **PARCIAL**: existe fundação ou fluxo principal, mas ainda faltam subitens da POC.
- **EXTERNO**: depende de credencial, layout oficial, homologação ou operação do contratante.
- **PENDENTE**: ainda precisa ser implementado aqui.

| Bloco da POC | Estado | Evidência atual |
|---|---|---|
| Administração / segurança / auditoria | PARCIAL | trilha de eventos e headers de segurança; autenticação/RBAC completo ainda será portado |
| Gestão de cadastros | PARCIAL | cadastro de cidadão/UBS/território seedado; CadSUS real é EXTERNO |
| Regulação | IMPLEMENTADO (fundação) | fila, prioridade, destino e transição de estado |
| Agendamento consultas/exames | PARCIAL | regulação + agenda de exames; grades/cotas avançadas ainda pendentes |
| Recepção UBS / ambulatório | PENDENTE | portar experiência UBS do RenoveJá |
| PEP multiprofissional | PENDENTE | portar prontuário e workspaces seguros do RenoveJá |
| Odontologia | PARCIAL | odontograma estruturado por elemento/faces + histórico; gráfico completo ainda pendente |
| Exames laboratório/imagem | PARCIAL | solicitação/agendamento/fila; resultados e mutirão serão ampliados |
| PSF / território | IMPLEMENTADO (fundação) | área, microárea, domicílio, família e ACS responsável |
| ACS offline/online | IMPLEMENTADO (fundação) | PWA com fila local, captura offline e sincronização posterior |
| Vacinação | IMPLEMENTADO (fundação) | vacina, lote, validade, dose, via, local, profissional e baixa de estoque |
| RNDS / SI-PNI | EXTERNO | não simular homologação; implementar adapter após contrato oficial |
| Faturamento SUS | IMPLEMENTADO (fundação) | produção nominal, competência, crítica, fechamento e exportação demonstrativa |
| BPA/e-SUS oficial | EXTERNO/PARCIAL | plugar layouts oficiais aplicáveis e validações completas |
| Farmácia / estoque | IMPLEMENTADO (fundação) | lotes, validade, mínimo, dispensação, controlados e movimento |
| Almoxarifado central | PARCIAL | mesmo domínio comporta central/unidade; entradas NF/XML, transferência e inventário serão ampliados |
| Digitalização de prontuário | PENDENTE | módulo específico de custódia/barcode/microfilmagem |
| Portal/app cidadão + telemedicina | PENDENTE DE PORT | aproveitar Intelligent Access/white-label do RenoveJá de forma sanitizada |
| BI / dashboards | IMPLEMENTADO (fundação) | centro de comando com KPIs e módulos operacionais |

## Ordem obrigatória de evolução

1. Fazer o build ficar verde no CI do repositório novo.
2. Portar autenticação/RBAC, paciente, UBS, médico e prontuário do RenoveJá sem publicar material operacional sensível.
3. Completar faturamento SUS com regras e layouts oficiais da POC.
4. Completar imunização e estoque com campanhas, cadeia de lotes, transferência e inventário.
5. Evoluir PSF/ACS para fichas e-SUS e sincronização versionada.
6. Completar odontograma gráfico e produção por elemento/sextante.
7. Completar exames com mutirão, execução e resultado.
8. Criar roteiro automatizado da POC cobrindo a jornada inteira.

## Regra de honestidade

`IMPLEMENTADO != HOMOLOGADO != PRODUÇÃO`.

Qualquer integração governamental deve permanecer fail-closed até credencial, contrato computacional oficial, homologação e evidência externa.
