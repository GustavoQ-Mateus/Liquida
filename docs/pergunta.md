2. Perguntas pra levar pro terminal do Liquida

A spec do BankCore (v1.1.0/ADR 0004) já fixou o lado servidor, mas o contrato entre os dois tem pontos que precisam bater dos dois lados antes de fechar a spec do Liquida. Estes são os que eu levaria:

Auth / token
1. POST /auth/token — o grant é JSON {client_id, client_secret} (como está na spec) ou OAuth2 form-encoded grant_type=client_credentials? E o shape da resposta: access_token + expires_in + token_type? O Liquida precisa saber exatamente pra montar o handler.
2. O Liquida vai fazer cache do token até expirar e re-emitir só perto do TTL (15m), ou pega token a cada polling? (Recomendo cache com refresh no fim do TTL.)

Backlog / polling
3. Ordenação de GET /settlement/transfers?status=PENDING — FIFO (mais antigo primeiro) pra liquidar em ordem? A spec fixou paginação offset + has_next, mas não a ordem.
4. Cadência de polling do lado do Liquida — de quanto em quanto tempo? (Afeta latência de liquidação e

settle / fail
5. settlement_ref no /settle é opcional ou obrigatório? A spec diz opcional — confirma que o Liquida vai sempre mandar (rastreabilidade ponta a ponta).
6. /fail aceita motivo nonão previu um campo reason — vale decidir agora se entra.
7. Idempotency-Key nos PATCH: o Liquida reusa o transfer_id (chave ponta a ponta da ADR 0003) como header,

Contrato de erro (o mais importante pro Liquida tratar determinístico)
8. Códigos: settle sobre ettle sobre FAILED → 409?fail sobre SETTLED → 409? A spec deixou "no-op seguro ou erro de conflito conforme o estado" em aberto (RF8) — o Liquida precisa do mapa exato de status
HTTP pra cada transição i

Orquestração
9. Confirmar que o Liquidomo external e alcançahttp://bankcore-api:8080 (host :8081).

Os itens 8 (mapa de erros do token) são os quetravam a implementação do Liquida — começaria por eles. Quer que eu já feche a decisão do item 8 no lado BankCore (proponho: transição inválida = 409 Conflict, exceto repetir a transição de destino que é 200 idempotente) e deixe registrado na spec antes de você abrir o outro terminal?


## respostas

Respostas do lado BankCore, com o que está implementado (não o que a spec promete):

1. Token ADMIN + provisionar o svc-client

Achado importante e feio: não existe seed/bootstrap de admin, nem ADMIN_* env, nem target no Makefile. Hoje o jeito de virar ADMIN é o POST /auth/register aceitar role no corpo — auth.go:53 só rebaixa pra CUSTOMER se a role não for ADMIN/CUSTOMER. Ou seja, qualquer um se registra como ADMIN pela rota pública. Funciona pro teu E2E, mas é um buraco de segurança que vou querer fechar (gate por env ou primeiro-admin-via-seed). Por ora, o fluxo é:

# 1. cria um admin (rota pública — aceita role no body)
curl -X POST http://localhost:8081/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"name":"root","email":"admin@bankcore.dev","password":"admin123","role":"ADMIN"}'

# 2. login → pega o JWT admin
curl -X POST http://localhost:8081/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@bankcore.dev","password":"admin123"}'                 # resposta: {"token":"<JW
                                                                            # 3. provisiona o serviceTTLEMENT é fixa noservidor)                                                                   curl -X POST http://localts \
  -H 'Authorization: Bearer <JWT_ADMIN>'
                                                                            Resposta do passo 3:
{
  "client":                                                                 {"id":"<uuid>","client_idENT","created_at":"..."},
  "client_secret": "<hex-48>",
  "warning": "guarde o client_secret agora: ele não será exibido novamente" }

Sobre "provisiona já e me passa": eu poderia subir o compose e gerar, mas o client_secret cairia nestar os 3 comandos aí (via ! curl ...) e colar o svc_.../secret direto no .env do Liquida — o segredo nunca sai da tua máquina. Se preferir, eu subo o stack e rodo, mas te aviso que o secret fica registrado aq

2. Envelope de GET /settlement/transfers?status=PENDING

Array sob transfers; has_next/page/page_size/status no root, ao lado (handler.go:178). Exemplo real de uma página:

{
  "page": 1,
  "page_size": 50,
  "has_next": false,
  "status": "PENDING",
  "transfers": [
    {
      "id": "3f2b...",
      "from_account_id": "a1...",
      "to_account_id": "b
      "amount_cents": 15000,
      "status": "PENDING",
      "created_at": "2026
    }
  ]
}

Atenção ao desserializar: valor é amount_cents (int64, centavos), não decimal.
settled_at/settlement_ref enquanto PENDING; sósurgem depois do settle.

3. Path dos callbacks

Confirmado, e é assimétrico de propósito:
- Leitura: GET /settlement/transfers (sob /settlement).
- Escrita: PATCH /transfers/{id}/settle e PATCH /transfers/{id}/fail — sob
/transfers, não sob /settbos protegidos porRequireRole(SETTLEMENT).

Então bate com a §4.3: os callbacks são /transfers/{id}/settle|fail. Só a
listagem que fica sob /se

4. Erros do settle — e um problema pro teu DLQ

Shape do erro (padrão de toda a API, httpx.go:52):
{"error": {"code": "CONFLICT", "message": "transferência falhou e não pode ser liquidada"}}
                                                                       Mapa real das transições
                                                                       ┌─────────────┬──────┬─────────────────────────┐
│   Estado    │ HTTP │   code    │                 message             │    atual    │      │                         │
├─────────────┼──────┼───────────┼──────────────────────────────────────────┤
│ PENDING     │ 200  │ —         │ (Transfer com status SETTLED)       ├─────────────┼──────┼─────────────────────────┤
│ SETTLED     │ 200  │ —         │ idempotente, no-op, devolve o atual      │
├─────────────┼──────┼───────────┼──────────────────────────────────────────┤
│ FAILED      │ 409  │ CONFLICT  │ transferência falhou e não pode ser │             │      │                         │
├─────────────┼──────┼───────────┼─────────────────────────────────────│ inexistente │ 404  │ NOado                   │
└─────────────┴──────┴───────────┴─────────────────────────────────────
(fail é espelhado: PENDING→200, FAILED→200 no-op, SETTLED→409 transferência já liquidada e não pode falhar, inexistente→404.)                         
O problema que você levantou é real: o code do 409 é sempre "CONFLICT" — não dá pra distinguir "transfer FAILED" de qualquer outro 409 pelo code. Hoje message (string em PT-BR)ra rotear DLQ. Recomendoeu adicionar códigos distintos no lado BankCore, tipo SETTLE_ON_FAILED / FAIL_ON_SETTLED, antes de você amarrar o roteamento. Quer que eu já faça essa mudança e regenere o Swagger?
                                                                       5. POST /auth/token
                                                                       Confirmado, e com uma peg
- Content-Type: application/json (usa json.NewDecoder — form-urlencoded quebra).
- Sem grant_type. Mais que isso: o decoder usa DisallowUnknownFields() (httpx.go:32), então mandmpo extra) retorna 400
5. POST /auth/token

Confirmado, e com uma pegadinha:
- Content-Type: application/json (usa json.NewDecoder — form-urlencoded quebra).
- Sem grant_type. Mais quwUnknownFields()(httpx.go:32), então mandar grant_type (ou qualquer campo extra) retorna 4VALIDATION. Manda só {"cl
- Resposta: {"token":"<jwt>","token_type":"Bearer"} — nota que a chave é tnão access_token; e não t de 15m está só no exp doJWT). Se você precisa saber quando renovar, decodifica o exp do JWT ou eu adiciono expires_in na re

curl -X POST http://localhost:8081/auth/token \
  -H 'Content-Type: appli
  -d '{"client_id":"svc_...","client_secret":"..."}'