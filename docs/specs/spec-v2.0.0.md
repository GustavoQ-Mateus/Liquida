# Liquida — Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 2.0.0 |
| **Status** | Draft — contrato de fronteira **fechado**; 2 dependências no BankCore bloqueiam a implementação (R1 leitura, R2 orquestração) |
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

### 4.2 Origem — leitura de pendentes  ⛔ depende de **R1**
- O relatório do BankCore confirmou que a role `SETTLEMENT` **não** consegue ler pendentes hoje: `GET /transfers?status=PENDING` filtra por owner e `/admin/transfers` exige ADMIN. **Bloqueia o Producer.**
- **Decisão de contrato (pedido R1 ao BankCore):** criar `GET /settlement/transfers?status=PENDING`, acessível à role `SETTLEMENT` (least-privilege, sem dar ADMIN), com a **mesma** paginação/schema já usados.
- **Paginação:** offset-based, param `page` (1-based, default 1), page size fixo **50**, sem `total`/`hasNext`. O Producer **pagina até vir `transfers` vazio**.
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
- **Tabela de respostas → ação do Consumer:**

| Resposta do BankCore | Significado | Ação do Consumer |
|---|---|---|
| `200` | liquidada (ou já estava) | **sucesso** (ack) |
| `401` | token ausente/expirado | renova token e repete 1x; persistindo → retry Polly |
| `409` (transfer `FAILED`) | conflito terminal de estado | **DLQ** (não reprocessa — não liquidar algo que falhou) |
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
1. **Passo 9** ✅ Fechar o contrato (esta revisão). Contrato de dados/auth/callback **definido**; restam as dependências R1/R2 no BankCore.
2. **Passo 10** `BankCoreTokenProvider` + `ITransferSource` (`Local` = seed atual; `BankCore` = cliente paginado com auth). Selecionável por `Origem`. Testes de mapeamento (RNF14) e de auth/renovação. **Bloqueado por R1** para o E2E, mas a abstração + `Local` + o `TokenProvider` podem ser feitos já.
3. **Passo 11** Callback `PATCH /settle` no Consumer, disparado só na primeira inserção; tabela de erros §4.3; DLQ. Testes CA11–CA12.
4. **Passo 12** E2E com BankCore + Liquida no mesmo compose (**R2**); provar CA10–CA13; README; tag `v2.0.0`.

## 9. Dependências no BankCore (bloqueiam a implementação)
- **R1 (bloqueia Passo 10 E2E) — leitura de pendentes para a role `SETTLEMENT`.** Hoje nenhuma rota serve o backlog global a essa role. Pedido: `GET /settlement/transfers?status=PENDING`, escopado à role `SETTLEMENT`, mesma paginação (offset `page`, size 50) e mesmo schema de `Transfer`. (Alternativa aceitável: permitir a role `SETTLEMENT` em `GET /admin/transfers` — menos limpo por dar alcance de admin.)
- **R2 (bloqueia Passo 12) — orquestração.** Adicionar a API do BankCore ao `docker-compose` com rede compartilhada (base URL `http://bankcore-api:8080`). Atenção a colisão de porta: BankCore e `Liquida.Api` escutam ambos `:8080` no container — no host, mapear portas distintas (ex.: BankCore `8081:8080`, Liquida `8080:8080`).
- **R3 (opcional, nice-to-have)** — metadados de paginação (`hasNext`/`total`) na resposta de pendentes. Sem eles o Producer pagina até vir lista vazia; funciona, só é um round-trip a mais no fim.
- **Resolvido, não precisa de mudança no BankCore:** expor `idempotency_key` — descartado; adotamos o `id` do BankCore como `transacao_id` (§5).

## 10. Decisões tomadas do lado do Liquida (não exigem o BankCore)
- **D1** `transacao_id := Transfer.id` do BankCore (chave ponta-a-ponta).
- **D2** `valor := amount_cents/100m`; `moeda := "BRL"`; `tipo` omitido (vira opcional na API — RNF14).
- **D3** Em `Origem=BankCore`, o Producer ignora `transacoes_pendentes` (lê BankCore → `POST /liquidacoes`). `Origem=Local` mantém o seed da v1 para dev/demo.
- **D4** `settle` envia `settlement_ref = "liquida:" + transacaoId` (opcional, rastreio).
- **D5** Tratamento de erros do `settle` conforme tabela §4.3 (200 sucesso, 401 renova, 409/404/400/403 → DLQ, 5xx → retry).

## 11. Changelog
- **2.0.0 (2026-09-02, Draft)** — Fronteira com o BankCore fechada contra o relatório v1.1.0: auth JWT service-role `SETTLEMENT` (claim `role`, TTL 15min, `POST /auth/token`), origem paginada, callback `PATCH /settle` idempotente, `transacao_id` = `Transfer.id`, mapeamento de dados (cents→decimal, `tipo` opcional). Pipeline interno (25 rps, fila, consumer idempotente) preservado. Restam as dependências R1 (leitura para a role de settlement) e R2 (orquestração no compose) no BankCore.
