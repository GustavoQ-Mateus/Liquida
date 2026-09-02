# ADR 0003 — Fronteira com o BankCore (origem das transações)

- **Status:** Aceita (v1 desacoplado, integração planejada para v-next)
- **Data:** 2026-08-31
- **Contexto:** O [BankCore](../../BankCore/docs/PRD_BankCore_Go.md) é a API de núcleo bancário (Go): contas, transferências atômicas e ledger append-only. Ele é a origem natural das transações que o **Liquida** liquida. Existe a tentação de acoplar os dois já na v1.

## Decisão
Na **v1**, o Liquida é **autônomo**: lê de sua própria tabela `transacoes_pendentes` (com seed), sem depender do BankCore. A integração com o BankCore fica **desenhada como fronteira** e adiada para uma versão futura da spec.

## Justificativa
- **Prazo e foco:** o núcleo do desafio (batch → API 25 rps → fila → consumer) precisa ser entregável sem depender de subir outro sistema em outra stack.
- **Baixo acoplamento:** manter a fronteira explícita permite plugar o BankCore depois sem reescrever o pipeline.
- **Narrativa de portfólio:** os dois projetos juntos contam uma história poliglota (core banking em Java + settlement em .NET), mas cada um roda e é avaliável isoladamente.

## Integração planejada (v-next)
- **Origem:** o Producer do Liquida passa a ler as transferências pendentes de liquidação do BankCore, seja consultando um endpoint (`GET /transfers?status=PENDING`) ou lendo a tabela `Transfer`.
- **Callback:** ao liquidar, o Consumer do Liquida confirma no BankCore (ex.: `PATCH /transfers/{id}/settle`), fechando o ciclo no ledger.
- **Idempotência ponta a ponta:** a chave de idempotência do BankCore (`Idempotency-Key`/`transferId`) é a mesma usada pelo Liquida (`transacao_id`), garantindo consistência entre os dois serviços.
- **Contrato:** definido em [`spec-v2.0.0.md`](../specs/spec-v2.0.0.md) (Draft), com esta ADR e a [ADR 0005](0005-auth-servico-a-servico-com-bankcore.md) (auth) referenciadas.

## Consequências
- v1 não tem dependência de runtime do BankCore.
- O modelo de dados do Liquida (`transacoes_pendentes`) é deliberadamente compatível com o `Transfer` do BankCore (id, valor, contas, tipo) para facilitar a integração futura.
