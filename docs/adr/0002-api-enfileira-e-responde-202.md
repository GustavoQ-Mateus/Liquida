# ADR 0002 — API enfileira e responde 202 Accepted (não liquida no request)

- **Status:** Aceita
- **Data:** 2026-08-31
- **Contexto:** A API recebe transações a até 25 rps. A liquidação (gravar no ledger, futuramente chamar o BankCore) pode ser mais lenta e sujeita a falhas.

## Decisão
A API apenas **valida e publica na fila**, respondendo `202 Accepted`. A liquidação acontece de forma assíncrona no **Consumer**.

## Justificativa
- **Desacoplamento:** desacopla ingestão de processamento; a fila absorve picos e protege a liquidação de sobrecarga.
- **Latência baixa e previsível** no request, evitando timeouts sob carga.
- **Resiliência:** se o Consumer ou o ledger cair, as mensagens ficam na fila; nada se perde.
- `202 Accepted` comunica corretamente "aceito para processamento", não "processado".

## Consequências
- Entrega **at-least-once**: o Consumer precisa ser **idempotente** (chave `transacao_id`) para não liquidar duas vezes.
- Necessária **DLQ** para poison messages (ver spec RNF5).
- O status "liquidada" não é imediato; um consumidor da API que precise de confirmação deve consultar `liquidacoes` (fora de escopo na v1).
