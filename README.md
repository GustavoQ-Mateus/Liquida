# Liquida

Pipeline de **liquidação de transações de pagamento** em C# (.NET 8). Um batch lê transações pendentes de um banco, envia para uma API de liquidação com **rate limit de 25 req/s**, que enfileira cada uma e responde `202`; um consumer liquida de forma assíncrona, idempotente e resiliente (retry + DLQ).

> Projeto-vitrine de backend distribuído. Companheiro do [BankCore](../PRD_BankCore_Go.md) (núcleo bancário em Go): o BankCore origina as transações, o Liquida as liquida com backend .NET e dashboard Angular/SCSS. Juntos formam um portfólio poliglota (Go + .NET + Angular) de sistemas bancários.

## Arquitetura
```
banco (PostgreSQL) --batch--> API /liquidacoes (<=25 rps) --202--> fila (RabbitMQ) --> consumer (liquida idempotente)
```
- **Liquida.Api** — Minimal API + rate limit Token Bucket 25/s + publisher RabbitMQ.
- **Liquida.Producer** — Worker que lê `transacoes_pendentes` (Dapper) e faz POST com pacing 25/s + Polly.
- **Liquida.Consumer** — BackgroundService que consome a fila e grava em `liquidacoes` (idempotente) + DLQ.
- **Liquida.Shared** — DTOs e contratos.

## Documentação (spec-driven)
- **Spec (versionada):** [`docs/specs/spec-v1.0.0.md`](docs/specs/spec-v1.0.0.md)
- **Decisões de arquitetura (ADR):**
  - [0001 — Token Bucket vs Fixed Window](docs/adr/0001-token-bucket-vs-fixed-window.md)
  - [0002 — API enfileira e responde 202](docs/adr/0002-api-enfileira-e-responde-202.md)
  - [0003 — Fronteira com o BankCore](docs/adr/0003-fronteira-com-bankcore.md)

## Como rodar
_(preenchido ao longo do desenvolvimento)_
```bash
docker-compose up -d      # sobe API, RabbitMQ e PostgreSQL
# ...
```

## Decisões-chave (resumo)
- **Rate limit de 25 rps** no servidor (Token Bucket), com `429` + `Retry-After`. Ver ADR 0001.
- **API só enfileira** e devolve `202`; liquidação assíncrona no consumer. Ver ADR 0002.
- **Idempotência** pela chave `transacao_id`; **DLQ** para poison messages; **graceful shutdown**.

## Status
Em desenvolvimento. Spec v1.0.0 (Draft).
