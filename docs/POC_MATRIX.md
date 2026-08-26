# Matriz POC — Jundiaí RCE 008/2026

Estado funcional deste repositório. Esta matriz diferencia implementação demonstrável de integração/homologação externa.

Legenda:

- **IMPLEMENTADO**: fluxo navegável e API existem neste repositório.
- **PARCIAL**: fluxo principal existe, mas faltam subitens avançados do edital.
- **EXTERNO**: depende de credencial, layout oficial, homologação ou ambiente do contratante/órgão público.
- **PENDENTE**: ainda não existe implementação suficiente aqui.

| Bloco da POC | Estado | Evidência atual |
|---|---|---|
| Administração / segurança / auditoria | IMPLEMENTADO para POC | RBAC por papel/permissão, default-deny em APIs internas, trilha de eventos e headers básicos |
| Gestão de cadastros | PARCIAL | cidadão, CNS/CPF, unidade, território, área/microárea e diretório demonstrativo de 58 unidades; CadSUS real é EXTERNO |
| Regulação | IMPLEMENTADO | fila, prioridade, origem/destino, transição e handoff da Porta Digital |
| Agendamento consultas/exames | PARCIAL | regulação, agenda diagnóstica e mutirões; grades/cotas/encaixes completos ainda precisam expansão |
| Recepção UBS / ambulatório | IMPLEMENTADO | check-in, fila, prioridade e chamada para sala |
| PEP multiprofissional | IMPLEMENTADO para POC | Patient 360, timeline, condições, alergias, medicamentos, vitais e workspaces profissionais |
| Odontologia | PARCIAL | odontograma estruturado por elemento/faces + histórico; gráfico anatômico coroa/raiz e produção por sextante ainda faltam |
| Exames laboratório/imagem | PARCIAL alto | pedido, agenda, unidade, executor e mutirão; anexos/resultados estruturados e integração PACS/LIS ainda serão ampliados |
| PSF / território | IMPLEMENTADO para POC | área, microárea, domicílio, família, ACS, cadastro individual/domiciliar e visita |
| ACS offline/online | IMPLEMENTADO | PWA com fila local, captura offline e sincronização posterior |
| Fichas APS/e-SUS | IMPLEMENTADO como demonstração | cadastro individual, domiciliar e visita + exportação explicitamente demonstrativa |
| e-SUS oficial | EXTERNO/PARCIAL | layout/transmissão oficial dependem de contrato computacional e homologação aplicáveis |
| Vacinação | IMPLEMENTADO para POC | vacina, lote, validade, dose, via, local, profissional/COREN, baixa de estoque e campanhas |
| RNDS / SI-PNI | EXTERNO | não simular homologação; adapter deve operar apenas após credenciais e homologação oficiais |
| Faturamento SUS | IMPLEMENTADO como fundação POC | produção nominal, competência, críticas, fechamento e exportação demonstrativa |
| BPA/e-SUS oficial | PARCIAL/EXTERNO | faltam todos os layouts oficiais aplicáveis, retificações e validações normativas completas |
| Farmácia / dispensação | IMPLEMENTADO para POC | lote, validade, mínimo, controlado, prescrição, dispensação e rastreabilidade |
| Almoxarifado central | IMPLEMENTADO como fundação | entrada, fornecedor/NF de referência, lotes, mínimos, transferências e movimentos; XML fiscal/inventário cego completo ainda faltam |
| Digitalização de prontuário | IMPLEMENTADO como fundação | barcode, páginas, origem, referência física, retirada, devolução e custódia |
| Portal/app cidadão + telemedicina | PARCIAL alto | Porta Digital navegável, red flags, roteamento, consentimento e handoff idempotente; videoteleconsulta completa será portada depois |
| BI / dashboards | IMPLEMENTADO para POC | centro de comando com KPIs e módulos operacionais |
| Suporte / operação 24x7 | OPERACIONAL, não software | exige equipe e processo contratual fora deste repositório |

## O que este repositório já permite demonstrar de ponta a ponta

1. cidadão entra na Porta Digital;
2. red flags determinísticas bloqueiam jornada eletiva quando há sinal de emergência;
3. caso não emergencial gera rota sugerida e revisão humana obrigatória;
4. consentimento cria handoff idempotente para a regulação;
5. recepção da UBS registra chegada e chama o cidadão;
6. Patient 360 apresenta contexto longitudinal e workspaces multiprofissionais;
7. exames podem ser agendados e agrupados em mutirão;
8. produção nominal alimenta lote de faturamento SUS demonstrativo;
9. lote recebe críticas e pode ser fechado quando válido;
10. vacinação controla lote/validade/dose/via/local/profissional e reduz estoque;
11. farmácia dispensa por cidadão e prescrição, com regra específica para controlados;
12. almoxarifado controla entrada e transferência para unidade;
13. PSF organiza território, família, ficha individual, domicílio e visitas;
14. ACS captura visita offline e sincroniza depois;
15. acervo físico/digitalizado recebe barcode e trilha de custódia;
16. todas as mutações relevantes geram eventos de auditoria.

## Gaps que continuam importantes

- persistência PostgreSQL e isolamento real por instituição/unidade;
- autenticação real/MFA em vez de papel demonstrativo por header;
- grades complexas, cotas, encaixes, bloqueios e perda de agenda;
- odontograma anatômico completo e faturamento por elemento/sextante;
- resultados/anexos diagnósticos, PACS/DICOM e LIS quando aplicável;
- layouts BPA/e-SUS oficiais, retificação, reabertura e críticas completas;
- XML de nota fiscal, inventário completo, recall e BNAFAR;
- integração CadSUS, RNDS, SI-PNI, BNAFAR e demais serviços governamentais mediante credenciais/homologação;
- videoteleconsulta e documentos assinados portados da base RenoveJá;
- migração de legado real da CIJUN;
- evidência de produção, disponibilidade, backup/DR e segurança operacional.

## Regra de honestidade

`IMPLEMENTADO != HOMOLOGADO != PRODUÇÃO`.

Nenhuma integração governamental deve ser apresentada como operacional sem credencial, contrato computacional oficial, homologação e evidência externa.
