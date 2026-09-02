# Liquida

Pipeline de **liquidação de transações de pagamento** em C# (.NET 8). Um batch lê transações pendentes de um banco, envia para uma API de liquidação com **rate limit de 25 req/s**, que enfileira cada uma e responde `202`; um consumer liquida de forma assíncrona, idempotente e resiliente (retry + DLQ).

> Projeto-vitrine de backend distribuído. Companheiro do [BankCore](../PRD_BankCore_Go.md) (núcleo bancário em Go): o BankCore origina as transações, o Liquida as liquida com backend .NET e dashboard Angular/SCSS. Juntos formam um portfólio poliglota (Go + .NET + Angular) de sistemas bancários.

## Arquitetura
```
banco (PostgreSQL) --batch--> API /liquidacoes (<=25 rps) --202--> fila (RabbitMQ) --> consumer (liquida idempotente)
                                   ^  GET /metrics, /*/recentes
                                   |
                        dashboard Angular/SCSS (polling 1s)
```
- **Liquida.Api** — Minimal API + rate limit Token Bucket 25/s + publisher RabbitMQ; e (v1.1) endpoints de leitura para o dashboard.
- **Liquida.Producer** — Worker que lê `transacoes_pendentes` (Dapper) e faz POST com pacing 25/s + Polly.
- **Liquida.Consumer** — BackgroundService que consome a fila e grava em `liquidacoes` (idempotente) + DLQ.
- **Liquida.Shared** — DTOs e contratos.
- **web/liquida-dashboard** (v1.1) — Angular 20 + SCSS: visualiza o pipeline em tempo quase real (pendentes, enviadas, liquidadas, `429`, fila, DLQ e vazão rps).

### Endpoints de leitura (v1.1, para o dashboard)
- `GET /metrics` — snapshot: `{ pendentes, enviadas, liquidadas, rpsLiquidacao, fila, dlq, rateLimited }`. `fila`/`dlq` viram `null` se o RabbitMQ Management API estiver fora (degradação graciosa).
- `GET /liquidacoes/recentes?limite=50` e `GET /transacoes/recentes?limite=50` — feeds. Todos são `GET` **fora** do rate limiter. Fontes de cada métrica em ADR 0004.

## Documentação (spec-driven)
- **Specs (versionadas):** [`spec-v1.0.0`](docs/specs/spec-v1.0.0.md) (núcleo backend) · [`spec-v1.1.0`](docs/specs/spec-v1.1.0.md) (endpoints de leitura + dashboard) · [`spec-v2.0.0`](docs/specs/spec-v2.0.0.md) (fronteira BankCore — Draft)
- **Decisões de arquitetura (ADR):**
  - [0001 — Token Bucket vs Fixed Window](docs/adr/0001-token-bucket-vs-fixed-window.md)
  - [0002 — API enfileira e responde 202](docs/adr/0002-api-enfileira-e-responde-202.md)
  - [0003 — Fronteira com o BankCore](docs/adr/0003-fronteira-com-bankcore.md)
  - [0004 — Endpoints de leitura e fontes das métricas](docs/adr/0004-endpoints-de-leitura-e-fontes-de-metrica.md)
  - [0005 — Auth serviço-a-serviço com o BankCore (JWT service-role)](docs/adr/0005-auth-servico-a-servico-com-bankcore.md)

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

### Dashboard (v1.1, Angular + SCSS)
Com a API no ar (Docker ou `dotnet run`), aponte o dashboard para ela e suba o dev server:
```bash
cd web/liquida-dashboard
npm install
npm start                          # http://localhost:4200
```
Por padrão consome `http://localhost:8080` (a API no Docker). Para outra URL, sobrescreva em runtime sem rebuild adicionando ao `src/index.html`:
```html
<script>window.LIQUIDA_API_BASE = 'http://localhost:5058'</script>
```
A API libera CORS para `http://localhost:4200` (configurável em `Cors:AllowedOrigins`). O dashboard faz polling a cada 1s; se a API cair, mostra "API offline" sem quebrar.

### Testes
```bash
dotnet test        # validação, rate limit + leitura fora do rate limiter (WebApplicationFactory) e idempotência (Testcontainers)
```

## Decisões-chave (resumo)
- **Rate limit de 25 rps** no servidor (Token Bucket), com `429` + `Retry-After`. Ver ADR 0001.
- **API só enfileira** e devolve `202`; liquidação assíncrona no consumer. Ver ADR 0002.
- **Idempotência** pela chave `transacao_id` (PK em `liquidacoes`); **DLQ** para poison messages; **graceful shutdown** respeitando o `CancellationToken`.
- **Pacing client-side** no Producer (25 rps) + Polly com backoff honrando `Retry-After` em `429`/`5xx`.

## Suposições (spec §9)
1. Banco de origem: PostgreSQL. 2. Fila: RabbitMQ real com DLQ. 3. Rate limit **global** na API (não por cliente). 4. "Liquidar" = persistir em `liquidacoes` de forma idempotente, sem sistemas externos reais.

## Status
- **v1.0.0** — backend completo e validado (CA1–CA5), tag `v1.0.0`.
- **v1.1.0** — endpoints de leitura + dashboard Angular/SCSS. Código completo; `dotnet test` verde (incl. CA9: leitura fora do rate limiter). Validação E2E do dashboard contra o pipeline vivo (CA6–CA8) roda com `docker compose up` + `npm start`.
- **v2.0.0 (Draft):** integração com o [BankCore](docs/specs/spec-v2.0.0.md) — origem via `GET /transfers?status=PENDING`, callback `PATCH /settle`, auth JWT service-role (ADR 0005). Contrato de fronteira pendente de alinhamento com a spec do BankCore.
