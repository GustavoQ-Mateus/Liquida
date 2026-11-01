# ADR 0008: Reset de demonstração na Liquida.Api

- **Status:** Aceita (spec-v2.2.0)
- **Data:** 2026-10-31
- **Contexto:** O dashboard é a única interface do conjunto BankCore + Liquida, e toda demonstração deve começar limpa. O BankCore 1.4.0 ganhou `POST /simulation/reset` como exceção de demonstração. O Liquida precisa do par: esvaziar filas, apagar o que liquidou e zerar os contadores em memória. A dificuldade é que Producer e Consumer são processos separados, com trabalho em voo no momento da limpeza: uma página lida antes do reset do BankCore e mensagens entregues ao Consumer cujo `settle` vai receber `404` e cair na DLQ.

## Decisão
- `POST /simulacao/reset` na `Liquida.Api`, mapeado só com `Simulacao:ResetHabilitado=true` (desligado por padrão, ligado no compose de integração), com CORS de `POST` só nessa rota.
- A limpeza usa **portão e janela de silêncio**:
  1. `POST /liquidacoes` passa a responder `409 simulacao_reiniciando`, que o Polly do Producer não repete;
  2. a API esvazia a fila principal e observa tentativas barradas, linhas em `liquidacoes` e profundidade da DLQ (AMQP passivo) até ficarem paradas por 3s;
  3. só então esvazia fila e DLQ, apaga as tabelas, zera os contadores e reabre o portão.
- Timeout de 20s: limpa mesmo assim e responde `503 reset_nao_estabilizou`.

## Justificativa
- **Exceção de demonstração, igual ao BankCore:** atrás de flag, desligada por padrão, sem efeito em produção.
- **Sem coordenação entre processos:** o trabalho em voo sempre termina num dos três sinais observados (publicar, gravar, mandar para a DLQ). Esperar o silêncio desses sinais garante o estado estável sem pausar Producer ou Consumer por mensagem, banco ou endpoint novo.
- **`409` em vez de `503` no portão:** `5xx` seria repetido pelo Polly por até ~6s e reapareceria depois da reabertura. `409` faz o Producer descartar o item na hora; o item não faz falta, porque a transfer já não existe no BankCore.
- **AMQP passivo para contar:** a Management API atualiza estatísticas com atraso de alguns segundos, o que falsearia a janela de silêncio.
- **Contrato do BankCore intacto:** nada pedido ao outro lado.

## Alternativas consideradas
- **Purge simples:** deixa fila, DLQ e `liquidacoes` crescendo de novo segundos depois. Rejeitada.
- **Pausar Producer e Consumer por sinal (tabela de controle ou endpoint em cada worker):** mais estado e mais superfície em dois processos que hoje não expõem nada. Rejeitada.
- **Não mandar para a DLQ o `404` pós-reset:** mudaria a regra D5 da 2.0.0 (erro terminal vai para a DLQ) por causa de um caso de demonstração. Rejeitada.
- **Reset feito pelo BankCore chamando o Liquida:** acoplaria o BankCore ao Liquida e mudaria o contrato. Rejeitada.

## Consequências
- Durante a limpeza (alguns segundos), `POST /liquidacoes` responde `409`. Só acontece com a flag ligada.
- A API ganha uma conexão AMQP curta por reset e acesso de escrita às duas tabelas (até aqui só lia).
- Com a simulação ainda rodando, o reset não estabiliza e responde `503` em até 20s; a UI impede esse caso.
