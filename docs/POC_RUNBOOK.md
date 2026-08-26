# Roteiro de demonstração — Jundiaí RCE 008/2026

Objetivo: demonstrar uma jornada coerente e orgânica, evitando navegação solta por telas. Toda integração externa não homologada deve ser apresentada como adapter/gate, nunca como produção simulada.

## Preparação

- abrir `/` em navegador moderno;
- manter `/citizen.html`, `/operations.html`, `/esus.html` e `/acs.html` em abas separadas;
- usar apenas dados de demonstração;
- reiniciar o processo antes da sessão caso seja necessário restaurar o seed em memória.

## Jornada 1 — cidadão até regulação

1. Abrir `Porta Digital do cidadão`.
2. Mostrar identificação da UBS de referência e território.
3. Digitar uma queixa não emergencial, por exemplo: `dor forte no joelho há três dias e dificuldade para andar`.
4. Demonstrar score, rota, prioridade, especialidade sugerida e `humanReviewRequired=true`.
5. Aceitar consentimento e confirmar handoff.
6. Repetir a confirmação para explicar idempotência: a segunda chamada não deve criar outra solicitação.
7. Abrir `Regulação` no centro de comando e localizar a nova entrada.

### Segurança obrigatória

Repetir a jornada com `dor no peito e falta de ar intensa`. O resultado deve ser `emergency`, exibir red flags e impedir handoff eletivo.

## Jornada 2 — recepção UBS e prontuário

1. Abrir `/operations.html` → `Recepção UBS`.
2. Registrar check-in de um cidadão.
3. Mostrar entrada na fila e chamar para sala.
4. Voltar para `/` → `Prontuário 360`.
5. Demonstrar condições, alergias, medicamentos, vitais e linha do tempo.
6. Explicar os workspaces por profissão e a separação entre `clinical.read` e `clinical.write`.

## Jornada 3 — exames

1. Abrir `Exames` no centro de comando.
2. Mostrar fila/agendamento por tipo, unidade e executor.
3. Abrir `/operations.html` → `Mutirões`.
4. Demonstrar capacidade e ocupação de mutirão de diagnóstico.

## Jornada 4 — faturamento SUS

1. Abrir `Faturamento SUS`.
2. Mostrar produção nominal com procedimento, CBO, CID e valor.
3. Criar lote da competência corrente.
4. Explicar críticas impeditivas e alertas.
5. Fechar lote quando válido.
6. Deixar claro que a exportação atual é demonstrativa e que o layout oficial BPA/e-SUS será versionado/homologado antes de transmissão real.

## Jornada 5 — vacinação

1. Abrir `/operations.html` → `Imunização`.
2. Mostrar lotes, validade e estoque.
3. Aplicar uma vacina.
4. Mostrar histórico com dose, via, local, lote e profissional/COREN.
5. Voltar ao lote e evidenciar a baixa de estoque.
6. Mostrar campanhas vacinais.

## Jornada 6 — farmácia e almoxarifado

1. Abrir `Farmácia`.
2. Dispensar um item para cidadão com referência de prescrição.
3. Mostrar rastreabilidade da movimentação.
4. Destacar a regra de controlado: referência da prescrição é obrigatória.
5. Abrir `Almoxarifado`.
6. Mostrar lote, fornecedor, referência de NF, validade e estoque mínimo.
7. Transferir quantidade para uma unidade e mostrar redução do saldo central + movimento.

## Jornada 7 — PSF / território / ACS

1. Abrir `/esus.html`.
2. Criar cadastro individual.
3. Criar cadastro domiciliar com área, microárea e unidade de referência.
4. Registrar visita domiciliar com motivos, PA, glicemia, antropometria e desfecho.
5. Mostrar exportação demonstrativa APS/e-SUS.
6. Abrir `/acs.html`.
7. Desconectar a rede do navegador/dispositivo.
8. Registrar visita; demonstrar que permanece na fila local.
9. Reconectar e sincronizar.

## Jornada 8 — prontuário digitalizado

1. Abrir `/operations.html` → `Digitalização`.
2. Registrar documento histórico/prontuário físico.
3. Gerar barcode.
4. Demonstrar páginas, origem e referência física.
5. Retirar em custódia e depois devolver.
6. Abrir `Auditoria` no centro de comando para mostrar os eventos.

## Teste de RBAC na apresentação

Usar o endpoint de contexto ou ferramenta de API para demonstrar papéis. Casos esperados:

- `acs` pode acessar PSF, mas não faturamento;
- `pharmacist` pode acessar estoque/farmácia, mas não escrever prontuário;
- `municipal_manager` enxerga operação e indicadores, mas não recebe escrita clínica;
- `clinician` escreve prontuário, mas não administra faturamento;
- rota interna sem política explícita é bloqueada por default-deny.

## Frases que não devem ser usadas

Não afirmar:

- `integração RNDS homologada`;
- `CadSUS em produção`;
- `arquivo BPA oficial validado pelo DATASUS`;
- `BNAFAR operacional`;
- `produção pronta`;
- `segurança certificada`.

Enquanto não houver evidência externa, usar:

- `adapter preparado`;
- `fluxo demonstrável`;
- `integração condicionada à credencial/homologação`;
- `exportação demonstrativa versionável`;
- `fundação funcional da POC`.
