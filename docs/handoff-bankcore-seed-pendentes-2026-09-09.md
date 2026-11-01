# Handoff para o BankCore — faltam transferências PENDING (2026-09-09)

**Para:** time/terminal do BankCore
**De:** Liquida (pipeline de liquidação)
**Assunto:** integração está ativa e autenticada; falta o BankCore ter transferências `PENDING` para o Liquida liquidar.

---

## TL;DR

A integração Liquida ↔ BankCore está **ligada e funcionando** (OAuth ok, endpoints de settlement respondendo 200). O único bloqueio para o fluxo ponta-a-ponta é: **o BankCore não tem nenhuma transferência com status `PENDING`**. Preciso que o BankCore **crie/seed transferências pendentes**. Assim que existir `PENDING`, o Liquida puxa em até ~5s, liquida e confirma via `settle`.

---

## O que já está validado (lado Liquida)

- Stack Liquida no ar: `liquida-api` (host 8080), `liquida-consumer`, `liquida-producer`, `rabbitmq`, `postgres`.
- Producer rodando com `Producer__Origem=BankCore`, na rede externa `bankcore-net`.
- **Auth OAuth service-to-service ok** — log do producer: `Token do BankCore renovado; válido por ~900s`.
- Chamada de settlement confirmada manualmente:
  - `POST http://localhost:8081/auth/token` (client-credentials) → `200`, retorna `{ "token", "token_type":"Bearer", ... }`.
  - `GET http://localhost:8081/settlement/transfers?status=PENDING&page=1` → `200`, corpo:
    ```json
    { "has_next": false, "page": 1, "page_size": 50, "status": "PENDING", "transfers": [] }
    ```
- Consumer conectado ao RabbitMQ/Postgres: `Consumer aguardando mensagens em 'liquidacoes'`.
  (Obs.: aparece o aviso `libgssapi_krb5.so.2: cannot open shared object file` do Npgsql — **não é fatal**, o consumer conecta normalmente.)

## O que falta (lado BankCore)  ← ação de amanhã

**Criar transferências no BankCore de forma que fiquem com status `PENDING`.**
Hoje `GET /settlement/transfers?status=PENDING` retorna lista vazia, então não há nada para o Liquida liquidar.

Isso é do domínio do BankCore (fluxo de cliente / seed) — não há endpoint de criação no contrato de settlement que o Liquida enxerga; o Liquida só **lê** `PENDING` e **confirma** `settle`.

### Pedido concreto ao BankCore
1. Gerar um lote de transferências pendentes (sugestão: **50–200** para ver o dashboard sustentar carga).
2. Garantir que elas entrem com status **`PENDING`** e apareçam em `GET /settlement/transfers?status=PENDING`.

---

## Como validar (do lado BankCore, após criar)

1. **Ver as pendentes** (role SETTLEMENT):
   ```
   GET http://localhost:8081/settlement/transfers?status=PENDING&page=1
   Authorization: Bearer <svcJwt>
   ```
   → deve vir `transfers: [ ... ]` com itens (antes estava `[]`).

2. **Confirmar que o Liquida consumiu** (em até ~5s, o poll do producer):
   - As mesmas transferências passam de `PENDING` → `SETTLED` no BankCore (o Liquida chama `PATCH /transfers/{id}/settle` com `{"settlement_ref":"liquida:<id>"}`).
   - `GET /settlement/transfers?status=PENDING` volta a esvaziar conforme são liquidadas.

3. **Dashboard do Liquida** (`http://localhost:4200`): `pendentes`/`enviadas`/`liquidadas` sobem e `rpsLiquidacao` encosta em ~25 (meta / token bucket).

---

## Dados técnicos da integração (já configurados no Liquida)

| Item | Valor |
|---|---|
| BankCore API (host) | `http://localhost:8081` |
| BankCore API (interno, na rede) | `http://bankcore-api:8080` |
| Rede docker compartilhada | `bankcore-net` (external) |
| Auth | `POST /auth/token` JSON estrito `{client_id, client_secret}` → `{token, token_type:"Bearer", expires_in:900}` |
| Role do service-client | `SETTLEMENT` |
| Listar pendentes | `GET /settlement/transfers?status=PENDING&page=N` |
| Confirmar liquidação | `PATCH /transfers/{id}/settle` body `{"settlement_ref":"liquida:{id}"}` |
| Credenciais | já provisionadas no `.env` do Liquida (`BankCore__ClientId` / `BankCore__ClientSecret`) — **não** versionadas |
| Poll do producer | a cada 5s (`Producer__PollDelaySeconds=5`) |

## Como o Liquida foi subido (referência)

```bash
# stack Liquida + override de integração BankCore (lê credenciais do .env)
docker compose -f docker-compose.yml -f docker-compose.bankcore.yml up -d
```

---

## Checklist para amanhã

- [ ] BankCore cria/seed de transferências (50–200) em status `PENDING`.
- [ ] `GET /settlement/transfers?status=PENDING` retorna itens.
- [ ] Em ~5s, itens migram para `SETTLED` (Liquida liquidou).
- [ ] Dashboard `http://localhost:4200` mostra as liquidações fluindo.
