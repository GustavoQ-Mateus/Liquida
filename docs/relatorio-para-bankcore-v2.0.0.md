# Relatório de fechamento Liquida → BankCore (v2.0.0)

| Campo | Valor |
|---|---|
| **De** | Liquida (serviço de settlement, .NET 8) |
| **Para** | BankCore (núcleo bancário, Go) |
| **Data** | 2026-09-07 |
| **Spec de referência** | Liquida `spec-v2.0.0` (**Entregue** — E2E verde, tag `v2.0.0`) |
| **Base BankCore** | v1.1.0 (R1–R6 entregues) |
| **Responde a** | `handoff-bankcore-v1.1.0.md` + `relatorio-para-bankcore.md` (2026-09-05) |

## TL;DR
**Ciclo integrado fechado e provado ponta a ponta.** O Passo 12 (E2E) rodou ao vivo com os dois stacks na `bankcore-net`: **CA10–CA13 verdes**. O Liquida está **tagueado `v2.0.0`**. Nenhuma mudança de contrato ou de código do BankCore ficou pendente para esta entrega — as duas correções do E2E foram do lado do Liquida (infra/compose e serialização HTTP). Fica **1 hardening sugerido** ao BankCore (não bloqueante) e **1 tema de segurança** ainda aberto do lado de vocês (`register` público com `role`), ambos já levantados antes e reconfirmados aqui.

---

## 1. E2E (Passo 12) — resultado ao vivo

Ambiente: BankCore com `LIQUIDA_INTEGRATION=external` na `bankcore-net`; Liquida anexado via `docker-compose.bankcore.yml` (`Origem=BankCore`, `BankCore:CallbackAtivo=true`), base URL `http://bankcore-api:8080`.

| Critério | Cenário | Resultado |
|---|---|---|
| **CA10** | 8 transferências `PENDING` no BankCore → Liquida liquida a 25 rps | ✅ 8 `PENDING` → 0; todas `SETTLED` no BankCore |
| **CA11** | Reexecutar o mesmo batch | ✅ `JaConfirmada` na barreira local; **sem re-settle** (idempotência ponta a ponta) |
| **CA12** | `settle` sobre transfer `FAILED` | ✅ `409 SETTLE_ON_FAILED` → **DLQ** (profundidade 1), terminal, não reprocessa |
| **CA13** | Token > TTL | ✅ refresh proativo por `expires_in` observado nos logs; batch não caiu |

Extras confirmados: `settlement_ref = "liquida:<id>"` **persistido** no BankCore após a correção (ver §2.2); roteamento de DLQ keyed por `error.code` exato (`SETTLE_ON_FAILED`), nunca pela `message`.

---

## 2. Achados do E2E e correções (ambos do lado Liquida)

Dois bugs só apareceram com os dois stacks juntos. **Nenhum exigiu mudança no BankCore.**

### 2.1 Colisão de DNS entre os composes (corrigido no Liquida)
Producer/Consumer do Liquida ficam **nas duas redes** (`default` + `bankcore-net`). Os nomes de serviço `api`/`postgres` resolviam para os containers do **BankCore**: o Producer postava `POST /liquidacoes` no `bankcore-api` → `404`; o Consumer conectava no `bankcore-postgres` (user `bankcore`) → `28P01 password authentication failed for user "liquida"`. **Correção:** fixar hosts **únicos** no override — `Producer__ApiBaseUrl=http://liquida-api:8080` e `ConnectionStrings__Postgres=Host=liquida-postgres`. O alvo do BankCore (`bankcore-api`) já é único. **Sem ação para o BankCore** — só registro pra vocês saberem que os nomes `api`/`postgres` genéricos são ambíguos quando um serviço externo entra na mesma rede.

### 2.2 `settlement_ref` não persistia — body chunked (hardening sugerido ao BankCore)
Reconfirmando o achado já enviado no relatório anterior (§0), agora **exercitado no E2E**: o `PatchAsJsonAsync` do .NET envia o corpo **chunked** (sem `Content-Length`); o handler de settle lê o `settlement_ref` só `if r.ContentLength > 0` (`internal/transfer/handler.go`), então gravava `settlement_ref` vazio mesmo respondendo `200`. **Correção (lado Liquida):** serializar com `StringContent` explícito, garantindo `Content-Length`. O E2E já persiste `settlement_ref=liquida:<id>` corretamente.
- **Sugestão de robustez pro BankCore (não bloqueante):** decodificar o corpo quando presente independente do `Content-Length` (tratar EOF como body vazio), em vez de gatear por `ContentLength > 0`. Chunked é HTTP válido; qualquer cliente que use transfer-encoding chunked cai no mesmo silêncio. Mesmo padrão vale pro `/fail` se um dia aceitar body.

---

## 3. Contrato consumido — estado final (sem ação do BankCore)

Tudo abaixo está **implementado, testado e provado no E2E** do lado do Liquida:

| Item | Contrato | Status |
|---|---|---|
| Auth | `POST /auth/token` JSON estrito, resposta `{token, token_type, expires_in:900}` | ✅ `BankCoreTokenProvider` (cache + refresh proativo + renovação em 401) |
| Leitura (R1) | `GET /settlement/transfers?status=PENDING`, role `SETTLEMENT`, envelope `{page,page_size,has_next,status,transfers[]}` | ✅ paginação por `has_next` (R3), FIFO `created_at ASC, id ASC` (R6) |
| Callback | `PATCH /transfers/{id}/settle`, body `{settlement_ref}` | ✅ idempotente (barreira `settle_confirmado_em` + `200` no-op do BankCore) |
| Códigos 409 (R4) | `SETTLE_ON_FAILED` / `FAIL_ON_SETTLED` | ✅ roteamento de DLQ por `error.code` — **exercido no CA12** |
| Mapeamento | `amount_cents/100m`→`valor`; `id`→`transacaoId`; sem `type`/`moeda` | ✅ `tipo` opcional, `moeda` default `BRL` |
| `settled_at`/`settlement_ref` sempre presentes (possível `null`) | §4.2 do handoff | ✅ `TransferDto` ignora — indiferente a `null`/omitido |

---

## 4. Único tema ainda aberto do lado do BankCore (não bloqueou o E2E)

Reconfirmando o que segue em aberto (segurança, não contrato):

> `POST /auth/register` (rota **pública**) aceita `role` no corpo e promove qualquer um a `ADMIN`. Usamos esse fluxo pra provisionar a credencial de service-client `SETTLEMENT` do E2E (rodado na máquina do Liquida; o segredo não trafegou pelo terminal de vocês). Funciona, mas é escalada de privilégio trivial em produção.

**Recomendação do Liquida (inalterada):** fechar com **gate por env** (`register` público recusa `role != CUSTOMER`) + **seed do 1º admin** a partir de `ADMIN_EMAIL`/`ADMIN_PASSWORD`. Isso é melhoria interna do BankCore — o E2E já está fechado independente disso.

---

## 5. Pedido ao BankCore

Nada bloqueante. Para fechar a coordenação da v2.0.0:

1. **Confirmar** se acatam o hardening do body chunked no `/settle` (§2.2) — se sim, num release futuro; se não, o Liquida segue com o `Content-Length` explícito e nada muda.
2. **Confirmar** o plano do `register`/`role` (§4) — quando fecharem, o único ajuste possível do nosso lado é o roteiro de provisionamento de credencial (seed vs register); avisem no bump.
3. **Manter estáveis** (bump + aviso se mudar): strings `SETTLE_ON_FAILED`/`FAIL_ON_SETTLED`, nome da rede `bankcore-net`, host interno `bankcore-api:8080`, envelope de `/settlement/transfers`.

Do lado do Liquida, a **v2.0.0 está entregue e tagueada**. Obrigado pela coordenação — R1–R6 vieram redondos e o ciclo fechou no primeiro E2E integrado (fora os dois bugs nossos de infra/serialização).
