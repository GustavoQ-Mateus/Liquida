# Liquida — Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 1.1.0 |
| **Status** | Draft |
| **Data** | 2026-09-02 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |
| **Base** | Estende `spec-v1.0.0.md` (núcleo backend, entregue e tagueado `v1.0.0`) |

> Versionamento **SemVer**. Esta é uma **MINOR**: adiciona escopo compatível (endpoints de leitura + dashboard) sem alterar o contrato de escrita (`POST /liquidacoes`) nem a arquitetura do pipeline. Tudo em `spec-v1.0.0.md` continua valendo; este arquivo descreve **apenas o delta** da 1.1.0.

---

## 1. Objetivo da 1.1.0
Dar **visibilidade** ao pipeline já entregue na v1.0.0. O backend prova os 25 rps, o desacoplamento por fila e a idempotência/DLQ; a 1.1.0 expõe esses números por **endpoints de leitura** na `Liquida.Api` e os apresenta num **dashboard Angular + SCSS** que atualiza em tempo quase real. Não muda o comportamento de liquidação; só observa.

## 2. Escopo
1. **Liquida.Api — endpoints de leitura** (`GET`, fora do rate limiter): um snapshot de métricas do pipeline e feeds das transações/liquidações recentes. A API passa a ler do PostgreSQL (contadores de `transacoes_pendentes`/`liquidacoes`) e do **RabbitMQ Management API** (profundidade das filas `liquidacoes` e `liquidacoes.dlq`), além de um contador em memória de respostas `429`.
2. **web/liquida-dashboard — Angular + SCSS**: consome os endpoints acima em polling e mostra cartões (pendentes, enviadas, liquidadas, `429`, fila, DLQ), a vazão de liquidação (rps) e feeds recentes.

Fora de escopo da 1.1.0: autenticação dos endpoints de leitura (mock/aberto em dev), WebSocket/SSE (polling basta para provar o conceito), integração com o BankCore (continua `v2.0.0`, ver ADR 0003).

## 3. Fontes das métricas
As métricas são heterogêneas por natureza — cada uma vem de onde a verdade vive:

| Métrica | Fonte | Cálculo |
|---|---|---|
| `pendentes` | PostgreSQL | `count(*) transacoes_pendentes where status='PENDENTE'` |
| `enviadas` | PostgreSQL | `count(*) transacoes_pendentes where status='ENVIADA'` |
| `liquidadas` | PostgreSQL | `count(*) liquidacoes` |
| `rpsLiquidacao` | PostgreSQL | `count(*) liquidacoes where liquidado_em > now() - interval '1 second'` |
| `fila` | RabbitMQ Mgmt API | profundidade (`messages`) da fila `liquidacoes` |
| `dlq` | RabbitMQ Mgmt API | profundidade (`messages`) da fila `liquidacoes.dlq` |
| `rateLimited` | API (memória) | contador acumulado de respostas `429` desde o start da API |

Decisões de origem em **ADR 0004**.

## 4. Contrato dos endpoints de leitura (delta da §6 da v1.0.0)
Todos são `GET`, **sem** o rate limiter de escrita, resposta `application/json`.

### 4.1 `GET /metrics`
Snapshot do pipeline num instante.
```json
{
  "capturadoEm": "2026-09-02T12:34:56.789Z",
  "pendentes": 940,
  "enviadas": 60,
  "liquidadas": 55,
  "rpsLiquidacao": 24,
  "fila": 5,
  "dlq": 2,
  "rateLimited": 210
}
```
- `fila`/`dlq` são `null` quando o RabbitMQ Management API está indisponível (o dashboard exibe `—`); os demais campos nunca são `null`.
- Retorna sempre `200`. Não bloqueia: falha em uma fonte degrada só o campo dela.

### 4.2 `GET /liquidacoes/recentes?limite=50`
Últimas liquidações, mais recentes primeiro. `limite` padrão `50`, máximo `200`.
```json
[
  { "transacaoId": "uuid", "valor": 100.50, "liquidadoEm": "2026-09-02T12:34:56Z" }
]
```

### 4.3 `GET /transacoes/recentes?limite=50`
Últimas transações da origem, mais recentes primeiro. `limite` padrão `50`, máximo `200`.
```json
[
  { "id": "uuid", "valor": 100.50, "moeda": "BRL", "tipo": "PIX", "status": "ENVIADA", "criadoEm": "2026-09-02T12:34:00Z" }
]
```

### 4.4 `GET /health`
Inalterado (liveness). Passa a ser usado também como **healthcheck** do serviço `api` no `docker-compose` (corrige a dependência `service_healthy` que o `producer` já declarava).

## 5. Requisitos funcionais (delta)
- **RF4** A API expõe `GET /metrics` agregando contadores de Postgres, profundidade de filas do RabbitMQ e o contador de `429` em memória, sem bloquear quando uma fonte falha.
- **RF5** A API expõe `GET /liquidacoes/recentes` e `GET /transacoes/recentes` para os feeds do dashboard.
- **RF6** O dashboard Angular consome esses endpoints em polling e visualiza cartões, vazão (rps) e feeds recentes.

## 6. Requisitos não funcionais (delta)
- **RNF8 Leitura não interfere na escrita**: endpoints `GET` ficam fora da policy de rate limit; consultas são `count`/`SELECT ... LIMIT` baratas e read-only. A leitura nunca deve degradar os 25 rps do `POST`.
- **RNF9 Degradação graciosa**: fonte indisponível (ex.: Management API fora) vira `null`/`—` no lugar de erro `5xx`.
- **RNF10 CORS**: a API libera CORS para a origem do dashboard (em dev, `http://localhost:4200`), só para os métodos de leitura.
- **RNF11 Polling configurável**: intervalo de atualização do dashboard configurável (padrão 1s), coerente com a janela de `rpsLiquidacao`.

## 7. Estrutura (delta da §10 da v1.0.0)
```
web/
└─ liquida-dashboard/       # Angular + SCSS: dashboard do pipeline (ativado na 1.1.0)
docs/adr/
└─ 0004-endpoints-de-leitura-e-fontes-de-metrica.md
```
A `Liquida.Api` ganha dependência de `Npgsql`/`Dapper` (leitura Postgres) e um cliente HTTP para o RabbitMQ Management API. Connection string `Postgres` passa a existir também na API (já existe no Consumer/Producer).

## 8. Critérios de aceitação (delta)
- **CA6** Com o pipeline rodando (`docker-compose up` + seed), `GET /metrics` reflete a evolução: `pendentes` cai, `enviadas`/`liquidadas` sobem, `rpsLiquidacao` fica em torno de 25 durante o processamento.
- **CA7** Forçar uma transação a falhar repetidamente faz `dlq` de `/metrics` refletir o item na DLQ (consistente com CA3).
- **CA8** O dashboard sobe (`npm start`), consome a API e mostra os cartões e a vazão atualizando ~1×/s; derrubar o RabbitMQ Management API degrada `fila`/`dlq` para `—` sem quebrar a página.
- **CA9** Endpoints de leitura não passam pelo rate limiter: chamar `/metrics` em rajada não retorna `429` (regressão de CA1 preservada só para `POST /liquidacoes`).

## 9. Ordem de execução (1.1.0)
1. **Passo 6** ADR 0004 + endpoints de leitura na `Liquida.Api` (Postgres + Management API + contador `429`), CORS, healthcheck do `api` no compose. Testes de leitura e de que `GET` não sofre rate limit.
2. **Passo 7** Dashboard Angular + SCSS consumindo os endpoints; polling; cartões + vazão + feeds.
3. **Passo 8** Prova + entrega: validar CA6–CA9 ponta a ponta, atualizar README, tag `v1.1.0`.

## 10. Changelog
- **1.1.0 (2026-09-02)** — Adiciona endpoints de leitura (`GET /metrics`, `/liquidacoes/recentes`, `/transacoes/recentes`) na `Liquida.Api` e o dashboard Angular/SCSS do pipeline. Sem mudança no contrato de escrita nem na arquitetura de liquidação. Introduz ADR 0004 (fontes das métricas). BankCore permanece `v2.0.0` (ADR 0003).
