# ADR 0005 — Auth serviço-a-serviço com o BankCore (JWT service-role)

- **Status:** Aceita e confirmada com o BankCore (v1.1.0 já implementa a emissão e o RBAC)
- **Data:** 2026-09-02
- **Contexto:** Na v2.0.0 o Liquida deixa de ser autônomo e passa a falar com o [BankCore](0003-fronteira-com-bankcore.md): o Producer lê transferências pendentes (`GET /transfers?status=PENDING`) e o Consumer confirma a liquidação de volta (`PATCH /transfers/{id}/settle`). Essas chamadas são **serviço-a-serviço** (sem usuário humano no meio) e precisam de credencial. O BankCore introduz a role **`SETTLEMENT`** para essa fronteira. A pergunta: a credencial do Liquida é um **JWT com role de serviço** (reaproveitando o auth existente do BankCore) ou uma **API key dedicada**?

## Decisão
O Liquida autentica com **JWT portando a role `SETTLEMENT`**, obtido via **client-credentials**. O BankCore valida esse JWT com o **mesmo middleware/RBAC** que já usa; a role vem numa claim. Confirmado com o BankCore (v1.1.0), a condição que hedgeava esta ADR (existência de issuer com expiração) **está satisfeita** — não é API key estática.

### Contrato de auth (BankCore v1.1.0)
- **Emissão:** `POST /auth/token` (pública), body `{"client_id","client_secret"}` → `{"token":"<jwt>","token_type":"Bearer"}`.
- **TTL:** 15min default (`SERVICE_JWT_TTL`). **Sem refresh token** — o Liquida re-troca `client_id/secret` quando expira.
- **Claim da role:** `role` (string **singular**), não `roles` (array). Ex.: `{ "role":"SETTLEMENT", "sub":"<uuid service_client>", "iat":..., "exp":... }`. O Liquida é apenas portador do token (não valida assinatura), então **HS256 (segredo simétrico)** do lado do BankCore é transparente para nós.
- **Sem `aud`.** Provisionamento via `POST /admin/service-clients` (role ADMIN) → `client_id` (`svc_<hex>`) + `client_secret` (mostrado 1x; persistido só como bcrypt hash). Rotável/revogável.
- **Escopo da role `SETTLEMENT`:** hoje só `PATCH /transfers/{id}/settle` e `/fail`. **Não** dá acesso útil às rotas de leitura de pendentes (`GET /transfers` filtra por owner; `/admin/transfers` exige ADMIN). Isso gera a dependência R1 na spec-v2.0.0 (endpoint de leitura para a role de settlement).

## Justificativa
- **Reaproveita o auth atual:** mesmo pipeline de validação e RBAC do BankCore; `requireRole("SETTLEMENT")` nos endpoints de fronteira é o mesmo código do RBAC de usuário. API key exigiria um **segundo** mecanismo de auth para construir, testar, auditar e revogar em paralelo.
- **Padrão M2M:** service-to-service moderno = OAuth2 *client credentials grant*, que produz exatamente "JWT com identidade de serviço + roles". Alinhado ao padrão da indústria.
- **Expiração embutida:** TTL curto + renovação, em vez de segredo estático eterno.

## Consequências
- O Liquida precisa de um `BankCoreTokenProvider`: troca `client_id/secret` em `POST /auth/token`, cacheia o access token até perto do `exp` (15min), e re-obtém em `401` (Polly), repetindo a chamada 1x.
- `client_secret` vive em env/secret store, **nunca** no código nem em query string.
- A claim é `role` (singular) — qualquer parsing/asserção nossa deve usar esse nome, não `roles`.
- O escopo mínimo da role `SETTLEMENT` (só settle/fail) é a decisão certa de least-privilege, mas exige um endpoint de leitura de pendentes acessível a ela (dependência R1 da spec-v2.0.0).

## Questões abertas
- Nenhuma no auth em si (contrato fechado). Pendências relacionadas viraram dependências no BankCore: leitura de pendentes para a role `SETTLEMENT` (R1) e orquestração no compose (R2) — ver spec-v2.0.0 §9.
