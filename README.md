# Liquida

Pipeline de **liquidação de transações de pagamento** em C# (.NET 8). Um batch lê transações pendentes de um banco, envia para uma API de liquidação com **rate limit de 25 req/s**, que enfileira cada uma e responde `202`; um consumer liquida de forma assíncrona, idempotente e resiliente (retry + DLQ).

> Projeto-vitrine de backend distribuído. Companheiro do [BankCore](../PRD_BankCore_Go.md) (núcleo bancário em Go): o BankCore origina as transações, o Liquida as liquida com backend .NET e um dashboard de tempo real em React. Juntos formam um portfólio poliglota (Go + .NET + React) de sistemas bancários.

## Arquitetura
```
banco (PostgreSQL) --batch--> API /liquidacoes (<=25 rps) --202--> fila (RabbitMQ) --> consumer (liquida idempotente)
                                   ^  GET /metrics/stream (SSE 1s)
                                   |
                        dashboard React (tempo real) --POST /simulation/start, GET /simulation/stats--> BankCore
```
- **Liquida.Api** — Minimal API + rate limit Token Bucket 25/s + publisher RabbitMQ; e (v1.1) endpoints de leitura para o dashboard.
- **Liquida.Producer** — Worker que lê `transacoes_pendentes` (Dapper) e faz POST com pacing 25/s + Polly.
- **Liquida.Consumer** — BackgroundService que consome a fila e grava em `liquidacoes` (idempotente) + DLQ.
- **Liquida.Shared** — DTOs e contratos.
- **web/liquida-dashboard** (v2.1) React + Vite + TypeScript + SCSS + uPlot: a única interface do conjunto BankCore + Liquida. Fundo preto, linha de KPIs, gráficos em tempo real e o botão que inicia a simulação no BankCore.

### Endpoints de leitura (v1.1, para o dashboard)
- `GET /metrics/stream` (v2.1): Server-Sent Events, um evento `metrics` por segundo com o mesmo snapshot do `GET /metrics`. Ver ADR 0006.
- `GET /metrics`: snapshot `{ pendentes, enviadas, liquidadas, rpsLiquidacao, fila, dlq, recebidas, rateLimited }`; `recebidas` conta os `POST /liquidacoes` aceitos com `202`. `fila`/`dlq` viram `null` se o RabbitMQ Management API estiver fora (degradação graciosa).
- `GET /liquidacoes/recentes?limite=50` e `GET /transacoes/recentes?limite=50` — feeds. Todos são `GET` **fora** do rate limiter. Fontes de cada métrica em ADR 0004.

## Documentação (spec-driven)
- **Specs (versionadas):** [`spec-v1.0.0`](docs/specs/spec-v1.0.0.md) (núcleo backend) · [`spec-v1.1.0`](docs/specs/spec-v1.1.0.md) (endpoints de leitura + dashboard) · [`spec-v2.0.0`](docs/specs/spec-v2.0.0.md) (fronteira BankCore, E2E verde) · [`spec-v2.1.0`](docs/specs/spec-v2.1.0.md) (SSE e dashboard de tempo real)
- **Decisões de arquitetura (ADR):**
  - [0001 — Token Bucket vs Fixed Window](docs/adr/0001-token-bucket-vs-fixed-window.md)
  - [0002 — API enfileira e responde 202](docs/adr/0002-api-enfileira-e-responde-202.md)
  - [0003 — Fronteira com o BankCore](docs/adr/0003-fronteira-com-bankcore.md)
  - [0004 — Endpoints de leitura e fontes das métricas](docs/adr/0004-endpoints-de-leitura-e-fontes-de-metrica.md)
  - [0005 — Auth serviço-a-serviço com o BankCore (JWT service-role)](docs/adr/0005-auth-servico-a-servico-com-bankcore.md)
  - [0006: Push das métricas por Server-Sent Events](docs/adr/0006-push-de-metricas-por-sse.md)
  - [0007: Dashboard em React + Vite + uPlot](docs/adr/0007-dashboard-react-vite-uplot.md)

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

### Dashboard de tempo real (v2.1)
Com os dois stacks no ar (BankCore com o simulador ligado e o Liquida com o override do BankCore):
```bash
cd web/liquida-dashboard
npm install
npm run dev                        # http://localhost:4200
```
Clique em **Iniciar simulação**: o BankCore gera as transferências, o Producer as drena e o Consumer confirma o settle; KPIs e gráficos se movem a cada segundo. O dashboard recebe as métricas do Liquida por SSE (`/metrics/stream`, com `GET /metrics` como fallback) e consulta `GET /simulation/stats` do BankCore a cada 1s. Se uma das fontes cair, os indicadores dela mostram `indisponível` e o resto segue.

URLs padrão `http://localhost:8080` (Liquida) e `http://localhost:8081` (BankCore); para outras, defina `VITE_LIQUIDA_API_URL` e `VITE_BANKCORE_API_URL`. Verificação: `npm run build` e `npm test`.

### Fluxo integrado com o BankCore (v2.0)
Liquida como serviço de settlement do [BankCore](docs/specs/spec-v2.0.0.md): as transferências nascem `PENDING` no BankCore, o Liquida as liquida a 25 rps e confirma de volta (`PATCH /transfers/{id}/settle`), idempotente ponta a ponta.

Pré-requisito: o stack do BankCore no ar na rede externa `bankcore-net` (com `LIQUIDA_INTEGRATION=external`). O BankCore semeia no boot a credencial `SETTLEMENT` do Liquida (`svc_liquida_dev`), que o override já usa por padrão; `BankCore__ClientId`/`BankCore__ClientSecret` no `.env` só servem para sobrescrevê-la (um `.env` com credencial antiga passa por cima do padrão).
Subir o Liquida anexado à `bankcore-net`, com origem BankCore e callback ativo (override):
```bash
docker compose -f docker-compose.yml -f docker-compose.bankcore.yml up -d --build
```
O Producer lê os pendentes paginados (`GET /settlement/transfers?status=PENDING`, FIFO por `created_at ASC, id ASC`) e faz `POST /liquidacoes`; o Consumer grava em `liquidacoes` (barreira de idempotência) e, na primeira inserção, confirma no BankCore. Erros de settle seguem a tabela §4.3 da spec (200 sucesso, 401 renova token, 409/404/400/403 → DLQ, 5xx → retry). Detalhes de contrato/auth em [ADR 0003](docs/adr/0003-fronteira-com-bankcore.md) e [ADR 0005](docs/adr/0005-auth-servico-a-servico-com-bankcore.md).

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
- **v2.1.0** (em andamento): métricas por SSE, contador `recebidas`, credencial semeada pelo BankCore e dashboard de tempo real em React + Vite + uPlot.
- **v2.0.0** — integração com o [BankCore](docs/specs/spec-v2.0.0.md): origem via `GET /settlement/transfers?status=PENDING`, callback `PATCH /transfers/{id}/settle` idempotente, auth JWT service-role (ADR 0005). E2E ponta a ponta verde (CA10–CA13): 8 `PENDING`→0 `SETTLED` a 25 rps, reexecução idempotente, `FAILED`→`409`→DLQ, refresh proativo do token. Tag `v2.0.0`.
