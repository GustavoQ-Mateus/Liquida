# Liquida — Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 2.0.0 |
| **Status** | Draft (fronteira; depende do contrato do BankCore em desenvolvimento) |
| **Data** | 2026-09-02 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |
| **Base** | Estende `spec-v1.0.0` (núcleo) + `spec-v1.1.0` (leitura/dashboard) |

> Versionamento **SemVer**. Esta é uma **MAJOR**: muda a **origem** das transações de forma incompatível — o Producer deixa de ler a tabela local `transacoes_pendentes` (seed autônomo da v1) e passa a consumir o **BankCore** como fonte da verdade, confirmando a liquidação de volta. O pipeline interno (API 25 rps → fila → consumer idempotente) é preservado; o que muda é a **fronteira** de entrada e o **callback** de saída. Referências: ADR 0003 (fronteira) e ADR 0005 (auth).

---

## 1. Objetivo da 2.0.0
Fechar o ciclo com o [BankCore](../adr/0003-fronteira-com-bankcore.md): as transferências pendentes de liquidação nascem no BankCore (núcleo bancário em Go, contas + ledger), o Liquida as liquida com seu pipeline resiliente de 25 rps, e **confirma de volta** no BankCore, com idempotência ponta a ponta. O Liquida deixa de ser autônomo e vira o **serviço de settlement** do BankCore.

## 2. Escopo
1. **Producer — origem BankCore:** substitui o seed + leitura de `transacoes_pendentes` por leitura de transferências pendentes do BankCore (`GET /transfers?status=PENDING`, paginado). Continua com pacing 25 rps + Polly ao chamar a `Liquida.Api`.
2. **Consumer — callback BankCore:** além de gravar em `liquidacoes` (idempotente, como hoje), confirma a liquidação no BankCore (`PATCH /transfers/{id}/settle`), com retry e DLQ. A gravação local vira o **registro idempotente** que evita callback duplicado.
3. **Auth serviço-a-serviço:** JWT com role `SETTLEMENT` via client-credentials (ADR 0005).
4. **Leitura/dashboard (v1.1):** preservados; ganham, se aplicável, um contador de "confirmadas no BankCore".

Fora de escopo: mudar o rate limit (segue 25 rps), reescrever o Consumer/idempotência local, UI nova (dashboard da v1.1 continua).

## 3. Mudança de arquitetura (delta)
```
v1.x:  transacoes_pendentes (seed local) --Producer--> Liquida.Api --fila--> Consumer --> liquidacoes
v2.0:  BankCore GET /transfers?status=PENDING --Producer--> Liquida.Api --fila--> Consumer --> liquidacoes
                                                                                        └--PATCH /settle--> BankCore
```
- A tabela `transacoes_pendentes` e o seed passam a ser **modo de desenvolvimento/demonstração** (flag), não a fonte de produção. Decidir via configuração `Origem = BankCore | Local` (ver §9, questão aberta).
- `liquidacoes` (PK `transacao_id`) continua sendo a **barreira de idempotência**: só quem insere de fato (`ON CONFLICT DO NOTHING` retornou linha) dispara o callback `PATCH /settle`; duplicatas não reconfirmam.

## 4. Contrato de fronteira (BankCore) — a confirmar com a spec do BankCore
> Campos e rotas abaixo são o **entendimento atual** (ADR 0003). Cada item marcado ⚠️ precisa ser confirmado contra a spec do BankCore antes de implementar.

### 4.1 Origem — `GET /transfers?status=PENDING`
- Paginação ⚠️ (cursor vs offset; tamanho de página).
- Item de transferência ⚠️: espera-se algo compatível com `transacoes_pendentes` — `id/transferId`, `amount/valor`, `currency/moeda`, contas origem/destino, `type/tipo`, `createdAt`. Mapear nomes reais → DTO do Liquida.
- `transferId` (BankCore) **=** `transacao_id` (Liquida): a mesma chave atravessa os dois sistemas.

### 4.2 Callback — `PATCH /transfers/{id}/settle`
- Semântica: marca a transferência como liquidada no ledger do BankCore.
- **Idempotente no BankCore** ⚠️: reenvio do mesmo `settle` (mesma `Idempotency-Key`/`transferId`) não deve gerar efeito duplo. Confirmar header/contrato de idempotência do lado de lá.
- Corpo/resposta ⚠️: o que o Liquida envia (valor liquidado? timestamp?) e o que recebe (novo status?).
- Erros ⚠️: `409` (já liquidada) deve ser tratado como **sucesso idempotente**; `4xx` de negócio → DLQ; `5xx`/`401` → retry (Polly), com renovação de token em `401`.

### 4.3 Auth — JWT `SETTLEMENT` (ADR 0005)
- Client-credentials: `client_id`/`client_secret` do Liquida (env/secret store) → access token de vida curta.
- Todas as chamadas ao BankCore levam `Authorization: Bearer <jwt>`; em `401`, renova e repete uma vez.
- Se o BankCore optar por não ter issuer → migrar para API key dedicada (ADR 0005), sem mudar o resto da spec.

## 5. Idempotência ponta a ponta
1. **Entrada:** o Producer pode reenviar a mesma transferência (batch reexecutado); a `Liquida.Api` enfileira, mas o Consumer só liquida uma vez (PK em `liquidacoes`).
2. **Callback:** só a **primeira** inserção bem-sucedida em `liquidacoes` dispara `PATCH /settle`; reprocessos não reconfirmam. Além disso, o `settle` carrega `Idempotency-Key = transacao_id`, então mesmo um callback repetido (ex.: falha após gravar e antes de confirmar, seguida de reprocesso) é absorvido pelo BankCore.
3. **Chave única:** `transferId` = `transacao_id` do início ao fim.

## 6. Requisitos (delta)
- **RF7** O Producer lê transferências pendentes do BankCore (paginado) e as envia à `Liquida.Api`, respeitando 25 rps.
- **RF8** O Consumer, ao liquidar (primeira inserção em `liquidacoes`), confirma no BankCore via `PATCH /settle`; falha no callback segue a política de retry/DLQ.
- **RNF12 Auth resiliente:** token de serviço com cache + renovação em `401`, sem derrubar o batch (Polly).
- **RNF13 Callback idempotente:** reenvio de `settle` (mesma chave) é seguro; `409` do BankCore = sucesso.
- **RNF14 Compat de dados:** o mapeamento `Transfer` (BankCore) → `LiquidacaoMessage` (Liquida) é explícito e testado.

## 7. Critérios de aceitação (delta)
- **CA10** Com o BankCore no ar com N transferências `PENDING`, `docker compose up` roda o ciclo: Producer lê do BankCore, pipeline liquida a 25 rps, e as N transferências ficam `SETTLED` no BankCore.
- **CA11** Reexecutar o batch (mesmas transferências) **não** gera segunda liquidação nem segundo `settle` efetivo (idempotência ponta a ponta).
- **CA12** `PATCH /settle` que retorna `409` (já liquidada) é tratado como sucesso, sem ir para a DLQ.
- **CA13** Token de serviço expirado → o Liquida renova e conclui, sem falhar o batch.

## 8. Ordem de execução (2.0.0)
1. **Passo 9** Fechar o contrato de fronteira com a spec do BankCore (resolver os ⚠️ da §4); atualizar esta spec de Draft → definido.
2. **Passo 10** Cliente do BankCore no Producer (origem paginada) atrás de `Origem = BankCore | Local`; auth JWT service-role. Testes de mapeamento (RNF14).
3. **Passo 11** Callback `PATCH /settle` no Consumer, disparado só na primeira inserção; tratamento de `409`/erros; DLQ. Testes CA11–CA12.
4. **Passo 12** E2E com BankCore + Liquida no mesmo compose; provar CA10–CA13; README; tag `v2.0.0`.

## 9. Questões abertas
- **Origem em dev:** manter o seed local atrás de `Origem=Local` para rodar o Liquida isolado, ou exigir o BankCore sempre? (default sugerido: manter `Local` para dev/demo.)
- **Contrato do BankCore (§4 ⚠️):** paginação, schema do `Transfer`, corpo/resposta e idempotência do `settle`, formato do token. Bloqueiam o Passo 10/11.
- **Orquestração:** um único `docker-compose` cobrindo BankCore + Liquida, ou composes separados com rede compartilhada?

## 10. Changelog
- **2.0.0 (2026-09-02, Draft)** — Fronteira com o BankCore: Producer lê `GET /transfers?status=PENDING`, Consumer confirma `PATCH /transfers/{id}/settle`, idempotência ponta a ponta (`transferId` = `transacao_id`), auth JWT service-role `SETTLEMENT` (ADR 0005). Pipeline interno (25 rps, fila, consumer idempotente) preservado. Contrato de fronteira pendente de alinhamento com a spec do BankCore.
