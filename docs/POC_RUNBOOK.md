# Roteiro de demonstração — Jundiaí RCE 008/2026

Objetivo: demonstrar um ecossistema coerente e integrado, sem navegação improvisada. Toda integração externa não homologada deve ser apresentada como **fronteira preparada**, nunca como produção simulada.

## 0. Preparação antes da banca

1. Iniciar a aplicação e confirmar `/api/health/ready`.
2. Abrir `/login.html`.
3. Entrar com `admin.jundiai` / `Jundiai#008` e validar MFA `008026` no ambiente padrão da POC.
4. Abrir `/poc.html`.
5. Clicar **Preparar jornada completa**.
6. Confirmar que o cartão `Cenário ouro preparado` mostra artefatos e cadeia de evidência íntegra.
7. Abrir `/verification.html` e executar/verificar os **14 blocos**.
8. Abrir `/evidence-pack.html` e clicar **Gerar Evidence Pack**.
9. Clicar **Verificar integridade** e confirmar:
   - package hash `OK`;
   - Evidence Ledger `OK`;
   - blocos `14/14`.
10. Se a instância estiver com PostgreSQL configurado, abrir `/governance.html` → Persistência e confirmar readiness/recovery; não criar afirmações de DR produtivo.
11. Manter abertas as abas:
   - `/poc.html` — cockpit dos 14 blocos;
   - `/evidence-pack.html` — pacote verificável;
   - `/verification.html` — runner 14/14;
   - `/command-center.html` — visão executiva;
   - `/caretrace.html` — visão longitudinal;
   - `/` — centro municipal / Patient 360;
   - `/operations.html` — recepção, vacinação, estoque e digitalização;
   - `/citizen.html` — Porta Digital;
   - `/governance.html` — integrações, IA, LGPD, persistência e Production Gates;
   - `/esus.html` — APS/território;
   - `/acs.html` — ACS offline.

As credenciais são públicas e exclusivamente demonstrativas. Não usar nenhum segredo real na POC.

### Checklist de contingência da apresentação

Antes de compartilhar a tela:

- confirmar que as páginas principais carregam;
- evitar atualização do navegador no meio de um fluxo que dependa de estado em memória;
- manter o Evidence Pack já gerado em uma aba;
- ter o JSON exportado do Evidence Pack como evidência complementar, se permitido;
- manter explícito que o pacote registra o **estado daquela instância/execução**;
- não depender de qualquer integração externa real para completar o roteiro.

---

## 1. Abertura — mostrar que não são 14 telas isoladas

Começar por `/poc.html`.

Explicar:

- há um **Contract Pack Jundiaí** orientando o software pelos 14 blocos;
- cada bloco tem estado, capacidade e evidência técnica;
- o runner verifica os 14 blocos contra o estado funcional;
- o Evidence Pack consolida a prova técnica numa estrutura verificável;
- a nota de readiness é da **POC**, não uma alegação de homologação/produção;
- bloqueadores externos e de habilitação aparecem explicitamente em vez de serem escondidos.

Em seguida abrir `/caretrace.html` e mostrar uma única linha do tempo do cidadão atravessando vários módulos.

Mensagem-chave:

> “A plataforma não trata cada módulo como silo. O evento continua pertencendo ao domínio de origem, mas pode ser acompanhado longitudinalmente para continuidade assistencial, gestão e auditoria.”

---

## 2. Segurança, autenticação e auditoria

Em `/login.html` demonstrar:

- usuário/papel;
- senha derivada PBKDF2-SHA256;
- MFA em perfil sensível;
- lockout;
- sessão;
- RBAC default-deny.

Casos de RBAC esperados:

- ACS acessa PSF/agenda de leitura, mas não faturamento;
- farmacêutico acessa estoque e documentos necessários, mas não escreve prontuário;
- gestor enxerga operação/indicadores e administração, mas **não recebe clinical.write**;
- clínico escreve prontuário/documentos e revisa IA clínica, mas não administra faturamento;
- auditor lê evidências sem receber escrita clínica;
- rota interna sem política é negada por padrão.

Mostrar `/api/evidence/verify` se houver ferramenta de API disponível: a cadeia deve retornar `valid=true`.

---

## 3. Porta Digital → regulação

1. Abrir `/citizen.html`.
2. Testar queixa não emergencial, por exemplo `dor forte no joelho há três dias e dificuldade para andar`.
3. Mostrar prioridade, rota, especialidade e revisão humana obrigatória.
4. Aceitar consentimento e fazer handoff.
5. Repetir o handoff para explicar idempotência.
6. Abrir Regulação no centro municipal.

### Prova de segurança

Testar `dor no peito e falta de ar intensa`.

Esperado:

- `riskLevel=emergency`;
- red flags explícitas;
- orientação de urgência presencial/SAMU conforme gravidade;
- bloqueio do handoff eletivo.

Não atribuir essa decisão a IA generativa: o kernel é determinístico.

---

## 4. Agenda centralizada

Em `/agenda.html` demonstrar:

- grades por especialidade/unidade;
- slots;
- duração;
- capacidade e encaixe configurado;
- cotas regulação/unidade/reserva;
- bloqueio de slot;
- fila de espera;
- promoção por prioridade;
- cancelamento/remarcação/no-show;
- relatório de perda/ocupação.

Mensagem-chave: agenda não é só calendário; ela é uma política operacional governada.

---

## 5. Recepção → Patient 360 → plano de cuidado

1. `/operations.html` → Recepção UBS.
2. Registrar check-in.
3. Chamar cidadão para sala.
4. `/` → Prontuário 360.
5. Mostrar condições, alergias, medicamentos, vitais e timeline.
6. Abrir `/clinical-ops.html`.
7. Mostrar ordens clínicas, administração/MAR e plano de cuidado.
8. Explicar separação entre leitura gerencial e escrita clínica.

---

## 6. Referência e contrarreferência

Em `/referrals.html` demonstrar:

- origem APS;
- destino/especialidade;
- prioridade e hipótese/razão clínica;
- status da referência;
- retorno/contrarreferência;
- continuidade da informação para o cuidado longitudinal.

Relacionar com a fila regulatória sem dizer que integração externa de regulação está homologada.

---

## 7. Telemedicina integrada

O cenário ouro já prepara uma sessão completa. Demonstrar em `/telemedicine.html`:

1. sala de espera;
2. preflight câmera/microfone;
3. consentimento;
4. participante/profissional;
5. `in_progress`;
6. resumo clínico;
7. conclusão.

Deixar claro: o transporte de vídeo está desacoplado; credenciais/provedor produtivo são etapa de implantação.

---

## 8. Laboratório e imagem

Em `/diagnostics.html` demonstrar:

1. pedido com indicação clínica;
2. agendamento;
3. coleta/barcode quando laboratorial;
4. execução/equipamento/modalidade;
5. resultado/laudo;
6. anexo/metadado DICOM quando aplicável;
7. resultado crítico;
8. ciência explícita do resultado crítico.

PACS e LIS reais devem ser apresentados no registro de integrações como dependências externas.

---

## 9. Odontologia

Em `/dental-v2.html` demonstrar:

- 32 dentes permanentes no padrão FDI;
- superfícies O/M/D/V/L;
- histórico por superfície;
- avaliação periodontal por 6 sextantes;
- procedimento por elemento ou sextante;
- ligação direta `procedimento odontológico → SIGTAP → produção SUS`.

Isso é mais forte do que mostrar apenas um desenho de odontograma.

---

## 10. Produção e faturamento SUS

Em `/billing-v2.html` demonstrar:

1. produção nominal;
2. procedimento SIGTAP parametrizado;
3. CBO/CID e regras;
4. crítica de idade/sexo/CBO/dente/sextante;
5. competência;
6. validação;
7. fechamento;
8. SHA-256 do artefato;
9. reabertura com nova versão e motivo;
10. histórico.

Frase correta:

> “O motor de produção, crítica e versionamento é demonstrável. O serializer/transmissor oficial será aderente ao layout DATASUS vigente na implantação.”

Nunca dizer que a exportação POC já é arquivo DATASUS homologado.

---

## 11. Imunização

Em `/immunization-v2.html` e, quando útil, `/operations.html`:

- calendário demonstrativo;
- screening;
- contraindicação/adiamento;
- lote/fabricante/validade;
- dose/via/local;
- profissional/conselho;
- aplicação;
- baixa automática de estoque;
- cobertura;
- evento adverso;
- campanhas.

RNDS/SI-PNI: mostrar como integração governada, não como homologada.

---

## 12. Farmácia e cadeia logística

Em `/pharmacy-care.html` e `/operations.html` demonstrar:

- conciliação medicamentosa;
- divergências que exigem revisão humana;
- ordem clínica ativa;
- dispensação vinculada à ordem;
- lote/validade/mínimo;
- fornecedor e referência de NF;
- entrada e transferência;
- regra de controlados;
- inventário físico versus saldo e divergência;
- alertas de mínimo/vencimento;
- recall por item+lote;
- ciência por unidade atingida;
- orientação farmacêutica;
- livro demonstrativo de controlados.

---

## 13. PSF, território e ACS offline

1. `/esus.html` → cadastro individual.
2. Cadastro domiciliar.
3. Área/microárea/unidade de referência.
4. Visita com PA, glicemia, antropometria, motivos e desfecho.
5. Exportação APS/e-SUS **demonstrativa**.
6. `/acs.html`.
7. Desligar rede do navegador/dispositivo, se a situação da banca permitir.
8. Registrar visita.
9. Mostrar fila local.
10. Reconectar e sincronizar.

---

## 14. Documentos clínicos e integridade

Demonstrar:

- receita;
- pedido de exame;
- atestado;
- encaminhamento;
- declaração de comparecimento;
- hash SHA-256;
- revogação;
- envelope de assinatura demonstrativa.

A assinatura RSA da POC é apenas prova arquitetural. **Não chamar de ICP-Brasil.**

---

## 15. AI Flight Recorder

Em `/governance.html` mostrar políticas e uma decisão criada pelo cenário ouro:

- use case;
- modelo e versão;
- prompt version;
- hash de entrada/saída;
- classe de risco;
- confiança;
- `humanReviewRequired`;
- revisão humana;
- override/rejeição com motivo.

Destacar que `autonomous-prescription` é proibido e que emergência não é delegada a modelo generativo.

---

## 16. Integrações governadas

Ainda em `/governance.html`, mostrar CadSUS, RNDS, SI-PNI, e-SUS, DATASUS BPA, SIGTAP, BNAFAR/Hórus, PACS, LIS, gov.br, ICP-Brasil, ACT e vídeo.

Explicar a regra:

> “O sistema não aceita marcar uma integração como homologada ou produção sem uma referência explícita de evidência.”

Mostrar status como `boundary_ready`, `external_homologation_required`, `external_definition_required` ou equivalente conforme o item.

---

## 17. Persistência, recovery e messaging

Se a demonstração estiver executando com PostgreSQL configurado, em `/governance.html` → Persistência mostrar:

- PostgreSQL conectado;
- migrations sem pendência;
- checkpoint completo;
- manifesto SHA-256;
- recovery drill;
- integridade dos envelopes após round-trip `jsonb`;
- preview de restauração;
- RPO observado do exercício;
- inbox idempotente;
- outbox;
- retry/dead-letter/requeue.

Frase correta:

> “Nós implementamos e testamos a fundação de durabilidade, recovery e mensageria. Backup gerenciado, PITR, failover, DR, broker e workers produtivos continuam Production Gates.”

Se PostgreSQL não estiver configurado na máquina da apresentação, mostrar o readiness em fallback e explicar que o smoke CI com PostgreSQL é a evidência automatizada da fundação; não improvisar conexão produtiva.

---

## 18. LGPD e acesso emergencial

Em `/governance.html` → LGPD demonstrar:

- finalidade/minimização;
- break-glass com motivo;
- janela temporal;
- trilha/auditoria;
- revogação;
- exportação demonstrativa do titular com hash.

Não afirmar conformidade jurídica total só porque o fluxo existe; políticas, DPO/encarregado, retenção, contratos e processo de incidente continuam implantação/governança organizacional.

---

## 19. Migração de legado

Mostrar o workspace de migração:

- manifesto de origem com SHA-256;
- mapeamento campo-a-campo;
- transforms/validators;
- linhas válidas/erro/quarentena;
- reconciliação origem-destino;
- duplicados/órfãos;
- aceite somente sem divergência.

Frase correta: método pronto para receber o legado real; nenhuma migração municipal é alegada sem acesso formal aos dados.

---

## 20. Service desk, SLA e treinamento

Mostrar:

- tickets P1/P2/P3/P4 equivalentes;
- alvo de resposta/atendimento da POC;
- breach;
- histórico de status;
- turmas por perfil;
- capacidade;
- presença/avaliação.

Isso demonstra ferramenta/processo, mas a disponibilidade de equipe 24x7 precisa existir operacionalmente.

---

## 21. Command Center

Em `/command-center.html` mostrar:

- regulação aberta/alta prioridade;
- slots, ocupação, faltas e fila de espera;
- resultados críticos pendentes;
- risco de abastecimento/recall;
- telemedicina;
- prevenção/imunização;
- produção SUS;
- alertas operacionais.

Mensagem-chave: a camada gerencial observa o ecossistema sem substituir a fonte de verdade de cada domínio.

---

## 22. Evidence Pack — fechamento probatório

Voltar a `/evidence-pack.html`.

Se o pacote ainda não foi gerado após a demonstração, clicar **Gerar Evidence Pack** para capturar o estado final da sessão.

Mostrar:

1. `14/14` blocos aprovados pelo runner;
2. score da POC;
3. índice dos 14 blocos;
4. tela e endpoints de evidência por bloco;
5. referências recentes do Evidence Ledger;
6. registro das integrações e seus status reais;
7. bloqueadores não resolvidos por código;
8. estado de persistência/recovery/messaging;
9. SHA-256 do pacote;
10. canonicalização `durable-json-canonical-v1`.

Clicar **Verificar integridade**.

Esperado:

- `packageHashValid=true`;
- `ledgerChainValid=true`;
- `passedBlocks=14`;
- `totalBlocks=14`;
- `demonstrationIntegrityReady=true`.

Se for útil/permitido, clicar **Exportar JSON**. Explicar:

> “Esse arquivo é um snapshot do que esta instância demonstrou. O SHA-256 é recalculável sobre JSON canônico, e a cadeia local de evidências também é verificada. Isso não substitui assinatura ICP-Brasil, homologação externa ou aceite contratual.”

Esse fechamento é preferível a terminar apenas numa tela bonita: deixa uma prova técnica estruturada da sessão.

---

## 23. Fechamento executivo

Voltar a `/poc.html` e encerrar no `/caretrace.html`.

Resumo sugerido da narrativa:

- os 14 blocos não são projetos separados;
- há uma jornada assistencial transversal;
- cada bloco possui regra, backend, tela e evidência;
- existe governança explícita de IA, integrações, LGPD, migração e industrialização;
- a fundação PostgreSQL/recovery/messaging foi testada separadamente;
- o Evidence Pack prova o estado da demonstração;
- o produto não mascara dependências externas ou habilitação documental.

---

## Frases proibidas sem evidência externa

Não afirmar:

- `RNDS homologada`;
- `CadSUS em produção`;
- `SI-PNI homologado`;
- `BNAFAR operacional`;
- `PACS/LIS integrados em produção`;
- `arquivo BPA homologado pelo DATASUS`;
- `assinatura ICP-Brasil` para a RSA da POC;
- `migração CIJUN concluída`;
- `backup/PITR/DR produtivo` por causa do recovery drill da POC;
- `plataforma pronta para produção`;
- `segurança certificada`;
- `habilitação atendida` apenas porque o software cobre os blocos.

Usar:

- `fluxo POC implementado`;
- `fundação técnica implementada e testada`;
- `fronteira de integração preparada`;
- `condicionado a credencial/homologação`;
- `exportação demonstrativa versionável`;
- `método de migração demonstrável`;
- `recovery drill da POC`;
- `gate de produção explicitamente pendente`;
- `Evidence Pack verificável da demonstração`.
