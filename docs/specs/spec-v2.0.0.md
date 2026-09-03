# Liquida — Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 2.0.0 |
| **Status** | Draft — contrato de fronteira **fechado**; R1/R2/R3 **resolvidos** pelo BankCore (v1.1.0). Shapes confirmados contra o código do BankCore. Pronto para implementação (Passo 10). Restam 3 ajustes menores solicitados ao BankCore (R4 códigos de erro distintos, R5 `expires_in`, R6 ordenação estável) e o pré-req operacional da credencial de service-client. |
| **Data** | 2026-09-02 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |
| **Base** | Estende `spec-v1.0.0` (núcleo) + `spec-v1.1.0` (leitura/dashboard) |

> Versionamento **SemVer**. Esta é uma **MAJOR**: muda a **origem** das transações de forma incompatível — o Producer deixa de ler a tabela local `transacoes_pendentes` (seed autônomo da v1) e passa a consumir o **BankCore** como fonte da verdade, confirmando a liquidação de volta. O pipeline interno (API 25 rps → fila → consumer idempotente) é preservado; o que muda é a **fronteira** de entrada e o **callback** de saída. Referências: ADR 0003 (fronteira) e ADR 0005 (auth). Contrato validado contra o relatório do BankCore v1.1.0 (2026-09-02).

---

## 1. Objetivo da 2.0.0
Fechar o ciclo com o [BankCore](../adr/0003-fronteira-com-bankcore.md): as transferências pendentes de liquidação nascem no BankCore (núcleo bancário em Go, contas + ledger), o Liquida as liquida com seu pipeline resiliente de 25 rps, e **confirma de volta** no BankCore, com idempotência ponta a ponta. O Liquida deixa de ser autônomo e vira o **serviço de settlement** do BankCore. O BankCore roda com `LIQUIDA_INTEGRATION=external` (deixa transferências em `PENDING` em vez de auto-liquidar).

## 2. Escopo
1. **Producer — origem BankCore:** substitui o seed + leitura de `transacoes_pendentes` por leitura paginada de transferências pendentes do BankCore. Continua com pacing 25 rps + Polly ao chamar a `Liquida.Api`.
2. **Consumer — callback BankCore:** além de gravar em `liquidacoes` (idempotente, como hoje), confirma a liquidação no BankCore (`PATCH /transfers/{id}/settle`), com retry e DLQ. A gravação local é o **registro idempotente** que evita callback duplicado.
3. **Auth serviço-a-serviço:** JWT com role `SETTLEMENT` via client-credentials (ADR 0005).
4. **Leitura/dashboard (v1.1):** preservados.

Fora de escopo: mudar o rate limit (segue 25 rps), reescrever a idempotência local, UI nova. O Liquida **não** chama `PATCH /fail` (falha de settlement fica na DLQ para reconciliação; a decisão de marcar `FAILED` no BankCore é de operação, não automática).

## 3. Mudança de arquitetura (delta)
```
v1.x:  transacoes_pendentes (seed local) --Producer--> Liquida.Api --fila--> Consumer --> liquidacoes
v2.0:  BankCore (pendentes) --Producer--> Liquida.Api --fila--> Consumer --> liquidacoes
                                                                       └--PATCH /settle--> BankCore
```
- A origem é selecionada por configuração **`Origem = Local | BankCore`** (default `Local` para dev/demo isolada; `BankCore` para o fluxo integrado). Em `BankCore`, o Producer **não** usa a tabela `transacoes_pendentes` — lê do BankCore e faz `POST /liquidacoes` direto.
- `liquidacoes` (PK `transacao_id`) continua sendo a **barreira de idempotência**: só quem insere de fato (`ON CONFLICT DO NOTHING` retornou linha) dispara o callback `PATCH /settle`; duplicatas não reconfirmam.

## 4. Contrato de fronteira (BankCore v1.1.0)

### 4.1 Auth — `POST /auth/token` → JWT `SETTLEMENT`
Detalhado na **ADR 0005**. Resumo: client-credentials (`client_id`/`client_secret` em env) → `{"token","token_type":"Bearer"}`, TTL 15min, sem refresh, claim **`role`** (singular). O Liquida cacheia o token e re-obtém em `401`. Todas as chamadas ao BankCore levam `Authorization: Bearer <jwt>`.
- **`DisallowUnknownFields`:** o `POST /auth/token` rejeita campo extra com `400 VALIDATION`. Enviar **só** `{"client_id","client_secret"}` — nada de `grant_type`. Content-Type `application/json` (form-urlencoded quebra). A chave da resposta é **`token`** (não `access_token`).
- **Renovação:** a resposta **não** traz `expires_in` hoje (o TTL vive só no `exp` do JWT) — solicitado ao BankCore adicioná-lo (**R5**). Enquanto não vier, o `BankCoreTokenProvider` renova proativamente com margem antes de 15min, além da renovação reativa em `401`.

### 4.2 Origem — leitura de pendentes  ✅ **R1 entregue**
- O BankCore criou o endpoint dedicado **`GET /settlement/transfers?status=PENDING`**, protegido por `RequireRole(SETTLEMENT)` (least-privilege — não abriu `/admin`, então a role de settlement não ganha listagem de contas). `CUSTOMER` recebe `403`; `SETTLEMENT` lê o backlog global. Confirmado E2E pelo BankCore.
- **Paginação:** offset-based, params `page` (1-based, default 1) e page size **fixo 50** (sem query param para alterar — a spec já assumia isso, sem conflito). A resposta traz **`has_next`** (R3 entregue, via fetch de `limit+1`, custo ~zero), além de `page` e `page_size`. O Producer **pagina enquanto `has_next=true`** (sem o round-trip extra de "até vir vazio").
- **Envelope da resposta (confirmado no código, `handler.go:178`):** o array de transfers vem sob a chave **`transfers`**; `page`, `page_size`, `has_next` e `status` ficam na **raiz**, ao lado. `settled_at`/`settlement_ref` **não** aparecem enquanto `PENDING` (só após o `settle`). Exemplo:
  ```json
  { "page": 1, "page_size": 50, "has_next": false, "status": "PENDING",
    "transfers": [ { "id": "3f2b…", "from_account_id": "a1…", "to_account_id": "b…",
                     "amount_cents": 15000, "status": "PENDING", "created_at": "2026-…" } ] }
  ```
- **Ordenação (R6, solicitado):** paginação offset exige `ORDER BY` determinístico — sem ele, páginas repetem/pulam linhas entre requests. Pedido ao BankCore garantir **`created_at ASC, id ASC`** (FIFO — liquida na ordem de chegada; `id` desempata). O Producer assume essa ordem estável.
- **Schema do `Transfer` (JSON real) e mapeamento → `LiquidacaoMessage`:**

| BankCore | tipo | → Liquida | observação |
|---|---|---|---|
| `id` | UUID | `transacaoId` | **é a chave ponta-a-ponta** (ver §5) |
| `amount_cents` | int64 (centavos) | `valor` = `amount_cents / 100m` | BankCore é cents; Liquida é decimal |
| — (não existe) | | `moeda` = `"BRL"` | single-currency implícito no BankCore |
| — (não existe) | | `tipo` = *omitido* | BankCore não tem `type`; ver §6 (tipo vira opcional) |
| `from_account_id` | UUID | `contaOrigem` | |
| `to_account_id` | UUID | `contaDestino` | |
| `status` | `PENDING\|SETTLED\|FAILED` | (filtro) | lê só `PENDING` |
| `created_at` | RFC3339 | (não propagado) | |

### 4.3 Callback — `PATCH /transfers/{id}/settle`
- `{id}` = `id` do BankCore (= `transacaoId` do Liquida). Body opcional `{"settlement_ref":"<string>"}`; o Liquida envia `settlement_ref = "liquida:" + transacaoId` para rastreio. Resposta `200` com o `Transfer` já `SETTLED`.
- **Idempotência natural** pelo `transferId` + state machine (o BankCore ignora `Idempotency-Key` no settle, e não precisamos dele): `settle` repetido numa transfer já `SETTLED` → `200` no-op (mantém o `settlement_ref` original). Combinado com a barreira local (`liquidacoes`), o callback duplicado é seguro em duas camadas.
- **Shape do erro (confirmado, `httpx.go:52`):** `{"error":{"code":"<CODE>","message":"<pt-BR>"}}`. O Consumer roteia pelo **`error.code`**, nunca pela `message`.
- **Códigos distintos no 409 (R4, solicitado):** hoje todo 409 traz `code:"CONFLICT"` — indistinguível. Pedido ao BankCore emitir **`SETTLE_ON_FAILED`** (settle sobre `FAILED`) e **`FAIL_ON_SETTLED`** (fail sobre `SETTLED`), mantendo `CONFLICT` genérico. No caminho de `settle` o único 409 hoje é `FAILED` → `DLQ` já é determinístico; o code distinto blinda contra 409s futuros de outra origem.
- **Tabela de respostas → ação do Consumer:**

| Resposta do BankCore | Significado | Ação do Consumer |
|---|---|---|
| `200` | liquidada (ou já estava `SETTLED`, no-op) | **sucesso** (ack) |
| `401` | token ausente/expirado | renova token e repete 1x; persistindo → retry Polly |
| `409` `SETTLE_ON_FAILED` (transfer `FAILED`) | conflito terminal de estado | **DLQ** (não reprocessa — não liquidar algo que falhou) |
| `404` id inexistente / `400` malformado | erro terminal | **DLQ** |
| `403` | token sem role `SETTLEMENT` | erro de config → falha alta, **DLQ** + alerta |
| `5xx` | falha transitória | retry Polly (backoff); esgotou → DLQ |

## 5. Idempotência ponta a ponta
- **Chave única:** o **`id` da transfer do BankCore vira o `transacao_id` do Liquida**. O Liquida deixa de gerar `transacao_id` (não semeia mais em modo BankCore); adota o id da origem. Isso dispensa expor o `idempotency_key` do BankCore (ponto que o relatório levantou) — o `id` já é a correlação natural, e o `settle` é keyed por ele, funcionando **sem mudança** no BankCore.
- **Barreira local:** só a **primeira** inserção bem-sucedida em `liquidacoes` dispara `PATCH /settle`; reprocessos (batch reexecutado, redelivery da fila) não reconfirmam.
- **Dupla proteção:** mesmo que o Consumer grave em `liquidacoes` e caia antes do `settle`, o reprocesso vê a linha já existente **e** o BankCore responde `200` idempotente — nenhum efeito duplo em nenhum dos lados.

## 6. Requisitos (delta)
- **RF7** Em `Origem=BankCore`, o Producer lê pendentes do BankCore (paginado, até esvaziar) e envia à `Liquida.Api` a 25 rps.
- **RF8** O Consumer, na primeira inserção em `liquidacoes`, confirma via `PATCH /settle`; erros seguem a tabela §4.3.
- **RNF12 Auth resiliente:** `BankCoreTokenProvider` com cache do token e renovação em `401`, sem derrubar o batch.
- **RNF13 Callback idempotente:** `settle` repetido é seguro; `200` em transfer já `SETTLED` é sucesso.
- **RNF14 Compat de dados:** mapeamento §4.2 explícito e testado; `valor = amount_cents/100`. **`tipo` passa a ser opcional** em `LiquidacaoRequest`/`LiquidacaoMessage` (BankCore não fornece); a validação da API aceita ausência de `tipo` e `moeda` default `BRL`.

## 7. Critérios de aceitação (delta)
- **CA10** Com o BankCore (`LIQUIDA_INTEGRATION=external`) tendo N transferências `PENDING`, o ciclo integrado liquida a 25 rps e as N ficam `SETTLED` no BankCore.
- **CA11** Reexecutar o batch (mesmas transfers) **não** gera segunda liquidação local nem segundo efeito de `settle` (idempotência ponta a ponta).
- **CA12** `settle` numa transfer já `SETTLED` → `200`, tratado como sucesso (não DLQ). `settle` numa `FAILED` → `409`, tratado como **terminal/DLQ**.
- **CA13** Token expirado (>15min) → o Liquida renova via `POST /auth/token` e conclui, sem falhar o batch.

## 8. Ordem de execução (2.0.0)
1. **Passo 9** ✅ Fechar o contrato + resolver dependências. Contrato de dados/auth/callback **definido**; R1/R2/R3 **entregues** pelo BankCore.
2. **Passo 10** `BankCoreTokenProvider` + `ITransferSource` (`Local` = seed atual; `BankCore` = cliente paginado por `has_next` com auth). Selecionável por `Origem`. Testes de mapeamento (RNF14) e de auth/renovação. **Desbloqueado** (R1 entregue).
3. **Passo 11** Callback `PATCH /settle` no Consumer, disparado só na primeira inserção; tabela de erros §4.3; DLQ. Testes CA11–CA12.
4. **Passo 12** E2E com BankCore + Liquida na rede compartilhada (**R2 entregue**); provar CA10–CA13; README; tag `v2.0.0`. Pré-requisito operacional: provisionar a credencial de service-client (§9).

## 9. Dependências no BankCore
- **R1 ✅ entregue — leitura de pendentes para a role `SETTLEMENT`.** `GET /settlement/transfers?status=PENDING`, protegido por `RequireRole(SETTLEMENT)` (dedicado, não abriu `/admin`). Paginação offset (`page`, size 50) + `has_next`/`page`/`page_size`. E2E confirmado (SETTLEMENT lê; CUSTOMER → 403). Commits BankCore `d0aeb80`/`9f63ebe`.
- **R2 ✅ entregue — orquestração.** `docker-compose` do BankCore sobe `postgres → migrate → bankcore-api` na rede **`bankcore-net`**. Alcançável em `http://bankcore-api:8080` dentro da rede; no host mapeado **`8081:8080`** (não colide com `Liquida.Api` em `8080`). O Postgres do BankCore foi para o host **`5433`** (o `liquida-postgres` já ocupa `5432`) — coexistem. Para o E2E, o compose do Liquida anexa a rede como **externa**:
  ```yaml
  networks:
    bankcore-net:
      external: true
  ```
  e usa `http://bankcore-api:8080` como base URL do BankCore.
- **R3 ✅ entregue** — `has_next` na resposta de pendentes (via `limit+1`). O Producer pagina por ele, sem round-trip extra.
- **Resolvido, sem mudança no BankCore:** expor `idempotency_key` — descartado; adotamos o `id` do BankCore como `transacao_id` (§5).

**Solicitados ao BankCore (ajustes menores, não bloqueiam o Passo 10):**
- **R4 — códigos de erro distintos no 409.** Hoje `code:"CONFLICT"` para qualquer conflito. Pedido: `SETTLE_ON_FAILED` e `FAIL_ON_SETTLED` (§4.3), + Swagger regenerado. Sem isso, o Consumer roteia por `message` (PT-BR) — funcional, porém frágil.
- **R5 — `expires_in` na resposta do `/auth/token`.** Hoje o TTL vive só no `exp` do JWT (§4.1). Fallback do Liquida: renovação proativa por margem + reativa em `401`.
- **R6 — ordenação estável `created_at ASC, id ASC`** em `GET /settlement/transfers` (§4.2). Necessário para a paginação offset não repetir/pular linhas; o Producer assume FIFO.

### Pré-requisito operacional do E2E (não é mudança de código do BankCore)
Provisionar uma credencial de service-client com role `SETTLEMENT`. **Não há seed/bootstrap de admin** no BankCore hoje: o admin é criado pela rota pública `POST /auth/register` aceitando `role` no corpo (buraco de segurança conhecido do BankCore, a ser fechado depois com gate por env / seed do 1º admin — não bloqueia o E2E). Fluxo:
1. `POST /auth/register` `{"name","email","password","role":"ADMIN"}` (rota pública).
2. `POST /auth/login` → JWT admin.
3. `POST /admin/service-clients` (Bearer admin) → `{ "client": { "client_id":"svc_…", "role":"SETTLEMENT", … }, "client_secret":"<hex-48>", "warning":"guarde agora" }` — a role `SETTLEMENT` é fixa no servidor; o `client_secret` é mostrado **1x**.

O Liquida guarda `client_id`/`client_secret` em `.env` (fora do git) e os injeta como `BankCore__ClientId`/`BankCore__ClientSecret`. Os 3 comandos rodam na máquina do Liquida (o segredo não trafega pelo terminal do BankCore).

## 10. Decisões tomadas do lado do Liquida (não exigem o BankCore)
- **D1** `transacao_id := Transfer.id` do BankCore (chave ponta-a-ponta).
- **D2** `valor := amount_cents/100m`; `moeda := "BRL"`; `tipo` omitido (vira opcional na API — RNF14).
- **D3** Em `Origem=BankCore`, o Producer ignora `transacoes_pendentes` (lê BankCore → `POST /liquidacoes`). `Origem=Local` mantém o seed da v1 para dev/demo.
- **D4** `settle` envia `settlement_ref = "liquida:" + transacaoId` (opcional, rastreio).
- **D5** Tratamento de erros do `settle` conforme tabela §4.3 (200 sucesso, 401 renova, 409/404/400/403 → DLQ, 5xx → retry).
- **D6** Producer pagina a origem por `has_next` (não por "até vir vazio").

## 11. Changelog
- **2.0.0 (2026-09-03, Draft — shapes confirmados)** — Contrato validado contra o **código** do BankCore: envelope de pendentes (`transfers` na chave, `page/page_size/has_next/status` na raiz), `/auth/token` JSON estrito (`DisallowUnknownFields`, chave `token`, sem `expires_in`), callbacks assimétricos (`/transfers/{id}/settle|fail` fora de `/settlement`), shape de erro `{"error":{"code","message"}}`. Abertos 3 ajustes menores solicitados: R4 (códigos 409 distintos), R5 (`expires_in`), R6 (ordenação estável FIFO). Documentado o fluxo de provisionamento de admin/service-client (sem seed no BankCore — register público aceita `role`, buraco conhecido).
- **2.0.0 (2026-09-03, Draft)** — Dependências de fronteira R1/R2/R3 resolvidas pelo BankCore (v1.1.0): endpoint dedicado `GET /settlement/transfers` (role SETTLEMENT, `has_next`), compose compartilhado (`bankcore-net`, host `8081`, postgres `5433`). Spec pronta para implementação (Passo 10). E2E requer apenas provisionar a credencial de service-client.
- **2.0.0 (2026-09-02, Draft)** — Fronteira com o BankCore fechada contra o relatório v1.1.0: auth JWT service-role `SETTLEMENT` (claim `role`, TTL 15min, `POST /auth/token`), origem paginada, callback `PATCH /settle` idempotente, `transacao_id` = `Transfer.id`, mapeamento de dados (cents→decimal, `tipo` opcional). Pipeline interno (25 rps, fila, consumer idempotente) preservado.
