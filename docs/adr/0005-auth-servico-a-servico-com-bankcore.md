# ADR 0005 — Auth serviço-a-serviço com o BankCore (JWT service-role)

- **Status:** Aceita (decisão default; confirmada com o time do BankCore)
- **Data:** 2026-09-02
- **Contexto:** Na v2.0.0 o Liquida deixa de ser autônomo e passa a falar com o [BankCore](0003-fronteira-com-bankcore.md): o Producer lê transferências pendentes (`GET /transfers?status=PENDING`) e o Consumer confirma a liquidação de volta (`PATCH /transfers/{id}/settle`). Essas chamadas são **serviço-a-serviço** (sem usuário humano no meio) e precisam de credencial. O BankCore introduz a role **`SETTLEMENT`** para essa fronteira. A pergunta: a credencial do Liquida é um **JWT com role de serviço** (reaproveitando o auth existente do BankCore) ou uma **API key dedicada**?

## Decisão
O Liquida autentica com **JWT portando a role `SETTLEMENT`**, obtido via **client-credentials** (o Liquida guarda `client_id`/`client_secret` e troca por um token de vida curta). O BankCore valida esse JWT com o **mesmo middleware/RBAC** que já usa para usuários; `SETTLEMENT` é apenas mais uma claim de `roles`.

**Condição explícita:** esta decisão só é superior a uma API key **se houver emissão real com expiração** (endpoint de client-credentials, TTL curto). Se o BankCore optar por **não** ter issuer e o token de serviço for estático/sem expiração, a decisão correta passa a ser uma **API key dedicada** (validada contra hash, escopada a `SETTLEMENT`, rotacionável) — não um "JWT eterno", que seria uma API key fantasiada com mais cerimônia e o mesmo risco.

## Justificativa
- **Reaproveita o auth atual:** mesmo pipeline de validação e RBAC do BankCore; `requireRole("SETTLEMENT")` nos endpoints de fronteira é o mesmo código do RBAC de usuário. API key exigiria um **segundo** mecanismo de auth para construir, testar, auditar e revogar em paralelo.
- **Padrão M2M:** service-to-service moderno = OAuth2 *client credentials grant*, que produz exatamente "JWT com identidade de serviço + roles". Alinhado ao padrão da indústria.
- **Expiração embutida:** TTL curto + renovação, em vez de segredo estático eterno.

## Consequências
- O Liquida precisa de um pequeno fluxo de obtenção/renovação de token (cache do access token até expirar; re-obtém em `401`).
- `client_secret` (ou a API key, se for o caso) vive em env/secret store, **nunca** no código nem em query string.
- A role `SETTLEMENT` deve ter escopo mínimo no BankCore: ler transferências pendentes e confirmar liquidação — nada além disso.
- Ortogonal ao auth, mas obrigatório na fronteira: **idempotência ponta a ponta** com `Idempotency-Key`/`transferId` = `transacao_id` (ver ADR 0003 e spec-v2.0.0 §5).

## Questões abertas (alinhar com a spec do BankCore)
- Formato exato do endpoint de token (client-credentials): rota, `audience`, TTL, claim de `roles`.
- Comportamento em `401`/`403`: o Liquida deve renovar o token e repetir (Polly), sem derrubar o batch.
- Se o BankCore ficar **sem issuer**, migrar esta ADR para "API key dedicada" antes de implementar.
