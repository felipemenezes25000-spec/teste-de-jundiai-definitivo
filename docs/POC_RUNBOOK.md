# Roteiro de demonstração — Jundiaí RCE 008/2026

Objetivo: demonstrar um ecossistema coerente e integrado, sem navegação improvisada. Toda integração externa não homologada deve ser apresentada como **fronteira preparada**, nunca como produção simulada.

## 0. Preparação antes da banca

1. Abrir `/login.html`.
2. Entrar com `admin.jundiai` / `Jundiai#008` e validar MFA `008026` no ambiente padrão da POC.
3. Abrir `/poc.html`.
4. Clicar **Preparar jornada completa**.
5. Confirmar que o cartão `Cenário ouro preparado` mostra os artefatos e cadeia de evidência íntegra.
6. Manter abertas as abas:
   - `/poc.html` — cockpit dos 14 blocos;
   - `/caretrace.html` — visão longitudinal;
   - `/` — centro municipal / Patient 360;
   - `/operations.html` — recepção, vacinação, estoque, digitalização;
   - `/citizen.html` — Porta Digital;
   - `/esus.html` — APS/território;
   - `/acs.html` — ACS offline.

As credenciais são públicas e exclusivamente demonstrativas. Não usar nenhum segredo real na POC.

---

## 1. Abertura — mostrar que não são 14 telas isoladas

Começar por `/poc.html`.

Explicar:

- há um **Contract Pack Jundiaí** orientando o software pelos 14 blocos;
- cada bloco tem estado, capacidade e evidência técnica;
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

No cockpit ou via API demonstrar:

- grades por especialidade/unidade;
- slots;
- duração;
- capacidade e encaixe configurado;
- cotas regulação/unidade/reserva;
- bloqueio de slot;
- fila de espera;
- promoção por prioridade.

Mensagem-chave: agenda não é só calendário; ela é uma política operacional governada.

---

## 5. Recepção → Patient 360

1. `/operations.html` → Recepção UBS.
2. Registrar check-in.
3. Chamar cidadão para sala.
4. `/` → Prontuário 360.
5. Mostrar condições, alergias, medicamentos, vitais e timeline.
6. Mostrar workspaces multiprofissionais.
7. Explicar separação entre leitura gerencial e escrita clínica.

---

## 6. Telemedicina integrada

O cenário ouro já prepara uma sessão completa. Demonstrar:

1. sala de espera;
2. preflight câmera/microfone;
3. consentimento;
4. participante/profissional;
5. `in_progress`;
6. resumo clínico;
7. conclusão.

Deixar claro: o transporte de vídeo está desacoplado; credenciais/provedor produtivo são etapa de implantação.

---

## 7. Laboratório e imagem

Demonstrar o motor `/api/diagnostics/v2` ou os dados do cockpit:

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

## 8. Odontologia

Demonstrar:

- 32 dentes permanentes no padrão FDI;
- superfícies O/M/D/V/L;
- histórico por superfície;
- avaliação periodontal por 6 sextantes;
- procedimento por elemento ou sextante;
- ligação direta `procedimento odontológico → SIGTAP → produção SUS`.

Isso é mais forte do que mostrar apenas um desenho de odontograma.

---

## 9. Produção e faturamento SUS

Demonstrar motor v2:

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

## 10. Vacinação

Em `/operations.html`:

- lote;
- fabricante;
- validade;
- estoque;
- dose/via/local;
- profissional/conselho;
- aplicação;
- baixa automática;
- campanha.

RNDS/SI-PNI: mostrar como integração governada, não como homologada.

---

## 11. Farmácia e cadeia logística

Demonstrar:

- lote/validade/mínimo;
- fornecedor e referência de NF;
- entrada e transferência;
- dispensação por cidadão/prescrição;
- regra de controlados;
- inventário físico versus saldo e divergência;
- alertas de mínimo/vencimento;
- recall por item+lote;
- ciência por unidade atingida;
- livro demonstrativo de controlados.

---

## 12. PSF, território e ACS offline

1. `/esus.html` → cadastro individual.
2. Cadastro domiciliar.
3. Área/microárea/unidade de referência.
4. Visita com PA, glicemia, antropometria, motivos e desfecho.
5. Exportação APS/e-SUS **demonstrativa**.
6. `/acs.html`.
7. Desligar rede do navegador/dispositivo.
8. Registrar visita.
9. Mostrar fila local.
10. Reconectar e sincronizar.

---

## 13. Documentos clínicos e integridade

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

## 14. AI Flight Recorder

Mostrar políticas e uma decisão criada pelo cenário ouro:

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

## 15. Migração de legado

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

## 16. Service desk, SLA e treinamento

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

## 17. Fechamento com analytics e evidência

Voltar a `/poc.html`.

Mostrar:

- regulação aberta/alta prioridade;
- slots e fila de espera;
- resultados críticos pendentes;
- risco de abastecimento/recall;
- telemedicina;
- produção SUS;
- SLA;
- integridade do Evidence Ledger;
- bloqueadores externos explicitamente listados.

Encerrar no CareTrace para reforçar continuidade do cuidado.

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
- `plataforma pronta para produção`;
- `segurança certificada`.

Usar:

- `fluxo POC implementado`;
- `fronteira de integração preparada`;
- `condicionado a credencial/homologação`;
- `exportação demonstrativa versionável`;
- `método de migração demonstrável`;
- `gate de produção explicitamente pendente`.
