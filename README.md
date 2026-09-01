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

### Tudo via Docker (fluxo ponta a ponta)
Sobe RabbitMQ, PostgreSQL, API, Consumer e roda o Producer uma vez (seed + envio):
```bash
docker compose up -d --build
```
O Producer semeia 100 transações, envia todas a 25 rps, marca `ENVIADA`, e o Consumer liquida cada uma de forma idempotente. Ao terminar, o container `producer` encerra (exit 0); API e Consumer seguem no ar.

Verificar o resultado:
```bash
docker logs liquida-producer            # resumo: enviadas=100 rate_limited=0 falhas=0
docker exec liquida-postgres psql -U liquida -d liquida -c "SELECT count(*) FROM liquidacoes;"   # 100
docker exec liquida-rabbitmq rabbitmqctl list_queues name messages                                # filas em 0
```
UI do RabbitMQ: http://localhost:15672 (guest/guest). Health da API: http://localhost:8080/health.

Derrubar (com volumes):
```bash
docker compose down -v
```

### Desenvolvimento local (só a infra em Docker, serviços via dotnet)
```bash
docker compose up -d rabbitmq postgres      # só a infra
dotnet run --project src/Liquida.Api         # API em http://localhost:5058
dotnet run --project src/Liquida.Consumer     # consumer
dotnet run --project src/Liquida.Producer     # seed + envio
```

### Testes
```bash
dotnet test        # validação, rate limit (WebApplicationFactory) e idempotência (Testcontainers)
```

## Decisões-chave (resumo)
- **Rate limit de 25 rps** no servidor (Token Bucket), com `429` + `Retry-After`. Ver ADR 0001.
- **API só enfileira** e devolve `202`; liquidação assíncrona no consumer. Ver ADR 0002.
- **Idempotência** pela chave `transacao_id` (PK em `liquidacoes`); **DLQ** para poison messages; **graceful shutdown** respeitando o `CancellationToken`.
- **Pacing client-side** no Producer (25 rps) + Polly com backoff honrando `Retry-After` em `429`/`5xx`.

## Suposições (spec §9)
1. Banco de origem: PostgreSQL. 2. Fila: RabbitMQ real com DLQ. 3. Rate limit **global** na API (não por cliente). 4. "Liquidar" = persistir em `liquidacoes` de forma idempotente, sem sistemas externos reais.

## Status
Backend v1.0.0 completo e validado (CA1–CA5). Próximo: dashboard Angular/SCSS (`spec-v1.1.0`).
