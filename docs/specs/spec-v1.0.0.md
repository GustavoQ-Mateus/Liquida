# Liquida — Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 1.0.0 |
| **Status** | Draft |
| **Data** | 2026-08-31 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |

> Versionamento: esta spec segue **SemVer**. MAJOR muda contrato/arquitetura de forma incompatível, MINOR adiciona escopo compatível, PATCH corrige texto/detalhe. Cada versão tem seu arquivo em `docs/specs/spec-vX.Y.Z.md`. Mudanças relevantes ficam no Changelog no fim deste arquivo e viram ADRs em `docs/adr/` quando são decisões de arquitetura.

---

## 1. Contexto de negócio
Um banco/instituição de pagamento acumula **transações pendentes** de liquidação (PIX, TED, boletos) em um banco de dados. Um processo de **batch** lê essas transações e as envia para uma **API de liquidação** que simula um adquirente/gateway com **limite de 25 requisições por segundo**. A API **enfileira** cada transação e responde imediatamente, e um **consumer** faz a **liquidação** de cada uma de forma assíncrona, idempotente e resiliente, registrando o resultado no ledger.

Objetivo de engenharia: provar controle de **vazão (rate limit)**, **desacoplamento via fila** e **resiliência** (idempotência, retry, DLQ) num fluxo realista de pagamentos.

## 2. Objetivo
Entregar um pipeline em **C# (.NET 8)** que leve transações pendentes do banco até a liquidação registrada, respeitando o limite de 25 rps e garantindo que nenhuma transação seja liquidada em duplicidade nem perdida.

## 3. Escopo
Quatro projetos numa solution .NET 8:

1. **Liquida.Api** (ASP.NET Core Minimal API): recebe a transação, aplica o rate limit de 25 rps, publica na fila e responde `202 Accepted`. Não liquida nada no request.
2. **Liquida.Producer** (Worker Service): lê `TransacoesPendentes` em páginas e faz `POST /liquidacoes`, respeitando 25 rps do lado do cliente e reagindo a `429`.
3. **Liquida.Consumer** (BackgroundService): consome a fila e **liquida** cada transação de forma idempotente, com retry e DLQ, gravando em `Liquidacoes`.
4. **Liquida.Shared**: DTOs, contratos de fila e chave de idempotência.

## 4. Stack
**Backend (núcleo do desafio):** .NET 8 · ASP.NET Core Minimal API · `AddRateLimiter` (Token Bucket 25/s) · RabbitMQ com DLQ · `BackgroundService` (IHostedService) · Dapper + PostgreSQL · Polly (resiliência no HttpClient) · Serilog (logging estruturado) · docker-compose (API + RabbitMQ + PostgreSQL).

**Frontend (extensão de portfólio, v1.1):** **Angular** + **SCSS** — dashboard que visualiza o pipeline em tempo quase real: transações pendentes, enviadas, liquidadas, `429` e itens na DLQ, com métricas de vazão (rps) para provar visualmente o rate limit. Consome endpoints de leitura da `Liquida.Api`. Não faz parte do núcleo do desafio; entra depois que o backend estiver provando os 25 rps.

## 5. Modelo de dados (PostgreSQL)
- **`transacoes_pendentes`**: `id (uuid)`, `valor (numeric)`, `moeda (char3)`, `conta_origem`, `conta_destino`, `tipo (PIX|TED|BOLETO)`, `status (PENDENTE|ENVIADA)`, `criado_em`.
- **`liquidacoes`** (resultado idempotente): `transacao_id (uuid, PK)`, `status (LIQUIDADA)`, `valor`, `liquidado_em`. A PK em `transacao_id` garante idempotência no banco.

## 6. Contrato da API
- **`POST /liquidacoes`**
  - Request: `{ "id": "uuid", "valor": 100.50, "moeda": "BRL", "contaOrigem": "...", "contaDestino": "...", "tipo": "PIX" }`
  - `202 Accepted` quando enfileirado.
  - `429 Too Many Requests` + header `Retry-After: 1` quando excede 25 rps, corpo `{ "error": "rate_limited", "message": "Limite de 25 req/s excedido." }`.
- **`GET /health`**: liveness simples.

## 7. Requisitos funcionais
- **RF1** A API expõe `POST /liquidacoes` que valida o payload, enfileira e responde `202`.
- **RF2** O Producer lê `transacoes_pendentes` em lotes paginados (status `PENDENTE`) e envia todas para a API, marcando `ENVIADA`.
- **RF3** O Consumer lê a fila continuamente e grava a liquidação em `liquidacoes`.

## 8. Requisitos não funcionais (foco da avaliação)
- **RNF1 Rate limit de 25 rps** no servidor via Token Bucket (`TokenLimit=25`, `TokensPerPeriod=25`, `ReplenishmentPeriod=1s`, `QueueLimit=0`). Excedente recebe `429` + `Retry-After`. Ver ADR 0001.
- **RNF2 Auto-limitação no Producer** a 25 rps (client-side pacing) com Polly fazendo retry com backoff exponencial em `429`/`5xx`, honrando `Retry-After`.
- **RNF3 Desacoplamento**: a API só enfileira e devolve `202` rápido; a liquidação pesada fica no Consumer. Ver ADR 0002.
- **RNF4 At-least-once + idempotência**: a chave de idempotência é o `transacao_id`; reprocessar a mesma transação não gera segunda liquidação.
- **RNF5 DLQ**: mensagem que falha N vezes vai para a Dead Letter Queue, sem travar a fila.
- **RNF6 Graceful shutdown**: o Consumer respeita o `CancellationToken` e conclui a mensagem atual antes de desligar.
- **RNF7 Observabilidade**: logging estruturado com contadores de lidas, enviadas, `429`, enfileiradas, liquidadas e DLQ.

## 9. Suposições (documentar no README)
1. Banco de origem: **PostgreSQL**.
2. Fila: **RabbitMQ real** com DLQ.
3. Rate limit: **global** na API (não por cliente/API key).
4. "Liquidar" = **persistir em `liquidacoes`** de forma idempotente (sem chamar sistemas externos reais).

## 10. Estrutura da solução
```
Liquida.sln
├─ src/
│  ├─ Liquida.Api/          # Minimal API + RateLimiter + publisher RabbitMQ
│  ├─ Liquida.Producer/     # Worker: le transacoes_pendentes (Dapper) -> POST /liquidacoes
│  ├─ Liquida.Consumer/     # BackgroundService: consome fila, liquida idempotente + DLQ
│  └─ Liquida.Shared/       # DTOs, contratos de fila, chave de idempotencia
├─ tests/
│  └─ Liquida.Tests/        # rate limit respeita 25/s; consumer idempotente
├─ web/
│  └─ liquida-dashboard/    # Angular + SCSS: dashboard do pipeline (v1.1)
├─ docs/
│  ├─ specs/                # specs versionadas (esta)
│  └─ adr/                  # decisoes de arquitetura
├─ docker-compose.yml       # rabbitmq + postgresql
├─ .gitignore  .editorconfig  .env.example
└─ README.md
```

## 11. Critérios de aceitação
- **CA1** 100 chamadas sequenciais ao `/liquidacoes` levam ~4s (25/s respeitado); excedente retorna `429` + `Retry-After`.
- **CA2** Enviar a mesma transação duas vezes resulta em um único registro em `liquidacoes` (idempotência).
- **CA3** Transação que falha repetidamente termina na DLQ, sem travar a fila.
- **CA4** `docker-compose up` sobe API, RabbitMQ e PostgreSQL; o fluxo roda ponta a ponta.
- **CA5** README explica arquitetura, como rodar e as decisões (token bucket, `202`, idempotência, DLQ), linkando as ADRs.

## 12. Ordem de execução
1. **Passo 0** Ambiente: .NET 8 SDK (`dotnet --version` = 8.0.x), Docker, Git.
2. **Passo 1** Solution + infra: `.sln` com 4 projetos, `docker-compose`, `.gitignore`/`.editorconfig`/`.env.example`, `git init`.
3. **Passo 2** API: `POST /liquidacoes` com rate limit 25/s publicando no RabbitMQ e devolvendo `202`.
4. **Passo 3** Consumer: consumo do RabbitMQ, idempotência (`liquidacoes`), DLQ, graceful shutdown, Serilog.
5. **Passo 4** Producer + banco: seed no Postgres, leitura paginada com Dapper, envio com pacing 25/s + Polly.
6. **Passo 5** Prova + entrega: teste de carga provando os 25 rps, testes básicos, README, tag `v1.0.0`.

## 13. Integração com o BankCore (fronteira, v-next)
O **BankCore** (`PRD_BankCore_Go.md`, Go) é o núcleo bancário: contas, transferências atômicas e ledger. Ele é a **origem natural** das transações que o Liquida liquida. Na v1 o Liquida é autônomo (lê da própria `transacoes_pendentes`), mas o modelo de dados é deliberadamente compatível com o `Transfer` do BankCore para a integração futura:
- **v-next:** o Producer lê transferências pendentes do BankCore (`GET /transfers?status=PENDING`), e o Consumer confirma a liquidação de volta (`PATCH /transfers/{id}/settle`), com idempotência ponta a ponta (`transferId` = `transacao_id`).
- Detalhes e justificativa em `docs/adr/0003-fronteira-com-bankcore.md`. Contrato entrará em `spec-v1.1.0.md`/`spec-v2.0.0.md`.

## 14. Fora de escopo (v1)
Autenticação real da API (mock/API key opcional), múltiplas moedas com conversão, integração com adquirente real, integração ao vivo com o BankCore. O **dashboard Angular/SCSS** é a primeira extensão planejada (`spec-v1.1.0`), depois que o backend provar os 25 rps. Integração com o BankCore fica para `spec-v2.0.0`.

---

## Changelog
- **1.0.0 (2026-08-31)** — Primeira spec. Define domínio de liquidação de pagamentos, contrato `POST /liquidacoes`, rate limit 25 rps (Token Bucket), fila RabbitMQ + DLQ, consumer idempotente, modelo de dados e critérios de aceitação.
