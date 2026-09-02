# ADR 0004 — Endpoints de leitura e fontes das métricas

- **Status:** Aceita (spec-v1.1.0)
- **Data:** 2026-09-02
- **Contexto:** A 1.1.0 adiciona um dashboard (Angular/SCSS) que visualiza o pipeline entregue na v1.0.0. Ele precisa de números — pendentes, enviadas, liquidadas, `429`, backlog da fila e itens na DLQ, além da vazão (rps). A questão é **de onde** cada métrica vem e **quem** as serve.

## Decisão
A `Liquida.Api` — que já é o ponto de entrada do sistema — passa a servir os endpoints de leitura (`GET /metrics`, `/liquidacoes/recentes`, `/transacoes/recentes`), agregando cada métrica da sua **fonte de verdade**, sem criar uma tabela de contadores nem um serviço novo:

- **Contadores de negócio** (`pendentes`, `enviadas`, `liquidadas`, `rpsLiquidacao`): consultados direto no **PostgreSQL** com `count(*)`/`SELECT ... LIMIT`. `rpsLiquidacao` sai de `liquidado_em > now() - interval '1 second'` — a própria tabela `liquidacoes` é o relógio.
- **Backlog de filas** (`fila`, `dlq`): lidos do **RabbitMQ Management API** (`GET /api/queues/%2F/{queue}` → campo `messages`). É a verdade sobre o broker sem precisar consumir mensagens.
- **`rateLimited`**: contador **em memória** na API, incrementado no `OnRejected` do rate limiter. É um dado que só a API conhece (a rejeição acontece nela) e é aceitável perdê-lo num restart — é observabilidade, não estado de negócio.

## Justificativa
- **Cada métrica na sua fonte**: nada de espelhar contadores num lugar único que precisa ser mantido em sincronia (fonte de bugs). O custo é consultar 3 origens; o ganho é não ter estado derivado para invalidar.
- **Leitura barata e read-only**: `count(*)` e `LIMIT n` não competem com o caminho de escrita; os `GET` ficam **fora** do rate limiter (RNF8), então o dashboard nunca rouba dos 25 rps nem toma `429`.
- **Degradação graciosa**: se o Management API cair, `fila`/`dlq` viram `null`/`—` e o resto do snapshot continua (RNF9). O dashboard tolera campos ausentes.
- **Sem serviço novo**: reusar a API evita mais um processo/porta/deploy só para leitura. O dashboard tem um único backend para falar.

## Alternativas consideradas
- **Consumer expõe as métricas**: ele conhece liquidadas/DLQ, mas não os `429` (que só a API vê) nem os pendentes de forma natural; e é um `BackgroundService`, não um host HTTP. Rejeitada.
- **Tabela `metricas` com contadores incrementais**: estado derivado que duplica o que já está em `transacoes_pendentes`/`liquidacoes`/broker; exige transação/consistência extra no hot path. Rejeitada — viola "cada métrica na sua fonte".
- **Prometheus/OpenTelemetry**: seria o caminho "de produção", mas adiciona stack de observabilidade fora do escopo do desafio. Pode entrar numa versão futura; a 1.1.0 fica no mínimo que prova o pipeline visualmente.
- **Push por WebSocket/SSE**: polling de 1s já casa com a janela de `rpsLiquidacao` e é trivial de operar. Push fica para depois se a latência incomodar.

## Consequências
- A `Liquida.Api` ganha dependência de leitura no PostgreSQL (`Npgsql`/`Dapper`) e um cliente HTTP para o Management API — antes ela só publicava no broker.
- `rateLimited` zera a cada restart da API (aceitável: é métrica de observabilidade, não ledger).
- `rpsLiquidacao` é uma janela deslizante de 1s calculada no banco; é uma aproximação instantânea, não uma média — coerente com "provar visualmente os 25 rps".
- O serviço `api` no compose passa a depender do `postgres` (`service_healthy`) e a receber a connection string, já que agora lê do banco. O `HEALTHCHECK` do `api` (via `/health`) continua vindo do `Dockerfile`, como antes.
