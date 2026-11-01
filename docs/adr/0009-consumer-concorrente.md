# ADR 0009: Consumer com processamento concorrente controlado

- **Status:** Aceita (spec-v2.2.0)
- **Data:** 2026-10-31
- **Contexto:** Na 2.1.0 o Consumer processava uma mensagem por vez (`prefetchCount: 1` e semáforo de 1). Cada mensagem grava em `liquidacoes` e confirma no BankCore por HTTP (`PATCH /settle`), então a vazão ficou em ~10 a 17/s contra 25/s recebidas. No cenário padrão a fila cresceu e a latência p95 de liquidação chegou a ~110s (achado A4 da 2.1.0).

## Decisão
O Consumer processa até `ProcessamentoParalelo = 8` mensagens ao mesmo tempo: `BasicQos(prefetchCount: 8)`, `ConsumerDispatchConcurrency = 8` e semáforo de 8. O desligamento espera os 8 slots. A lógica de uma mensagem sai para `LiquidacaoProcessor`, sem mudar o comportamento.

## Justificativa
- O gargalo é latência de I/O (Postgres e HTTP), não CPU. Oito em paralelo cobrem 25/s com folga mesmo com settle de ~250ms.
- **Idempotência intacta:** a barreira é o `insert ... on conflict do nothing` com `settle_confirmado_em`. Duas entregas concorrentes do mesmo id produzem uma linha. No pior caso, os dois chamam `settle`, e o BankCore responde `200` idempotente (§5 da 2.0.0).
- Concorrência limitada por constante, não ilimitada: a pressão no BankCore e no Postgres fica previsível.

## Alternativas consideradas
- **Várias réplicas do Consumer:** mesmo efeito, mas mais containers e mais conexões para um ganho que uma constante resolve. Rejeitada por ora.
- **Settle em lote:** exigiria endpoint novo no BankCore. Rejeitada.

## Consequências
- A ordem de liquidação dentro da fila deixa de ser FIFO estrita. A ordem não é requisito; a leitura do BankCore continua FIFO (R6).
- Logs de mensagens diferentes se intercalam.
- Até 8 mensagens ficam em voo; a janela de silêncio do reset (ADR 0008) cobre esse caso.
