# Liquida, Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 2.2.0 |
| **Status** | Entregue (2026-11-01). Aprovada com as três divergências de contrato e ADRs 0008/0009; CA30 corrigido por decisão do usuário; CA28 a CA36 verificados |
| **Data** | 2026-10-31 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |
| **Base** | Estende `spec-v2.1.1.md` (entregue) |

> Versionamento **SemVer**. Esta é uma **MINOR**: adiciona `POST /simulacao/reset` (atrás de flag, desligada por padrão), cenários e limpeza no dashboard, e corrige resiliência e vazão do pipeline (A3, A4, A5 da 2.1.0). O contrato de escrita (`POST /liquidacoes`) ganha uma resposta nova (`409` durante a limpeza) que só aparece com a flag ligada. Referências: ADR 0008 (reset de demonstração) e ADR 0009 (Consumer concorrente).

---

## 1. Objetivo
Toda demonstração começa limpa e nada roda até clicar em **Iniciar**. O dashboard ganha:
- uma lixeira **Limpar ambiente**;
- a escolha do **cenário** da simulação;
- a opção **Limpar antes de iniciar**, marcada por padrão.

O pipeline passa a aguentar a demonstração inteira sem intervenção:
- o Producer não morre (A3);
- o Consumer acompanha os 25/s (A4);
- o "429 da execução" sobrevive a um restart da API (A5).

## 2. Escopo
1. **Liquida.Api:** `POST /simulacao/reset` com portão e janela de silêncio (§4); CORS de `POST` só nessa rota.
2. **Compose de integração:** liga o reset e põe `restart: unless-stopped` em Producer e Consumer.
3. **Producer (A3):** o loop trata falha, faz backoff e segue.
4. **Consumer (A4):** processamento concorrente controlado.
5. **Dashboard:**
   - lixeira com confirmação;
   - menu de cenários e opção "Limpar antes de iniciar";
   - sequência de limpeza;
   - correção do 429 da execução (A5).
6. **ADRs:** 0008 e 0009.

Fora de escopo: mudar o contrato do BankCore, o rate limit de 25 rps, a semântica de DLQ do settle (D5 da 2.0.0) ou autenticação dos endpoints de demonstração.

## 3. Contrato consumido do BankCore 1.4.0 (fechado pelo orquestrador)
- `POST /simulation/reset`, sem body, CORS para `http://localhost:4200`.
  - `409 SIMULATION_RUNNING` em `seeding`/`running`.
  - `200` com o status em `phase: idle` e contadores zerados.
  - Apaga `entries`, `transfers` e `accounts`; mantém `customers` e `service_clients` (a credencial do Liquida sobrevive).
- Depois do reset, a leitura de pendentes vem vazia, e `settle`/`fail` de uma transfer apagada responde `404 NOT_FOUND`. Pela tabela §4.3 da 2.0.0, isso vira `Terminal` e cai na DLQ.
- `POST /simulation/start` com body dos cenários (§6.2), dentro dos limites já publicados pelo BankCore.

## 4. `POST /simulacao/reset` (Liquida.Api)

### 4.1 Contrato
- **Flag:** `Simulacao:ResetHabilitado`, default `false`. Desligada, a rota não é mapeada (`404`), no mesmo padrão do simulador do BankCore. O `docker-compose.bankcore.yml` liga (`Simulacao__ResetHabilitado: "true"`).
- **Sem body.** Fora do rate limiter.
- **CORS:** política nova `dashboard-reset` (`WithOrigins` do dashboard, `WithMethods("POST")`), aplicada só nesta rota com `RequireCors`. A política `dashboard` dos `GET` não muda, e `POST /liquidacoes` continua sem CORS.

| Resposta | Quando | Corpo |
|---|---|---|
| `200` | limpeza concluída e estável | `MetricsSnapshot` zerado (`fila`/`dlq` 0, `liquidadas` 0, `recebidas` 0, `rateLimited` 0) |
| `409` | outro reset em andamento | `{"error":"reset_em_andamento","message":"..."}` |
| `503` | não estabilizou em 20s (liquidações continuam chegando, por exemplo com a simulação rodando) | `{"error":"reset_nao_estabilizou","message":"Liquidações continuam chegando; pare a simulação e tente de novo."}`; a limpeza é feita mesmo assim |

O formato de erro segue o padrão já usado pela API no `429` (`{"error","message"}`).

### 4.2 Efeito
Esvazia as filas `liquidacoes` e `liquidacoes.dlq`, apaga `liquidacoes` e `transacoes_pendentes` e zera `ReceivedCounter` e `RateLimitCounter`.

### 4.3 A corrida e a solução: portão + janela de silêncio
**Problema.** A ordem do dashboard é parar (se rodando) → reset do BankCore → reset do Liquida. Mesmo assim:
- o Producer pode estar no meio de uma página lida antes do reset do BankCore (até 50 itens a 25/s, ~2s) e continuar postando;
- o Consumer pode ter mensagens em voo (entregues e não confirmadas) cujo `settle` recebe `404` e cai na DLQ depois do purge.

Um purge simples deixaria fila, DLQ ou `liquidacoes` voltando a crescer segundos depois.

**Solução** (tudo dentro da API, sem sinalização entre processos e sem mudar o BankCore):
1. **Fecha o portão.** Enquanto o reset roda, `POST /liquidacoes` responde `409 {"error":"simulacao_reiniciando"}` sem publicar.
   - Não conta em `recebidas` nem em `rateLimited`.
   - `409` não é repetido pelo Polly do Producer (que só repete `429`/`5xx`): o item é registrado como falha e o Producer segue para o próximo. Assim, a página residual se esgota em segundos, sem fila de retentativas.
   - Esses itens não fazem falta: a transfer de origem já não existe no BankCore.
2. **Espera o silêncio.** A cada 250ms o reset:
   - esvazia a fila principal (`QueuePurge`);
   - observa três sinais: tentativas barradas no portão, contagem de linhas em `liquidacoes` e profundidade da DLQ (lida por AMQP, `QueueDeclarePassive`, que é instantânea; a Management API tem atraso de estatística).

   Quando nenhum sinal muda por **3s seguidos** e a fila principal está vazia, nada mais está em voo:
   - a página do Producer acabou;
   - as mensagens que o Consumer tinha em mãos terminaram (gravaram linha e foram para a DLQ ou confirmaram).
3. **Limpa.** Esvazia fila e DLQ, apaga as tabelas e zera os contadores.
4. **Abre o portão** e responde `200`.

**Limites.**
- **Timeout de 20s:** sem silêncio nesse prazo, faz a limpeza mesmo assim, abre o portão e responde `503 reset_nao_estabilizou`.
- **Sinais escolhidos:** os três sinais cobrem todas as saídas possíveis do trabalho em voo (publicar, gravar, mandar para a DLQ).
- **Tempo do silêncio:** 3s é mais que o maior tempo de uma mensagem em voo com o BankCore no ar (settle `404` é imediato; retentativa transitória ~0,6s). Constantes nomeadas: `JanelaDeSilencio`, `IntervaloDeObservacao`, `TempoMaximoDeReset`.
- **Teste:** a lógica fica numa classe com `TimeProvider`, testável com relógio falso.

### 4.4 Componentes
- `Reading/ResetGate`: estado do portão e contador de tentativas barradas.
- `Simulation/SimulationResetService`: orquestra §4.3.
- `Simulation/QueueMaintenance`: conexão AMQP curta só para o reset, com purge e contagem passiva.
- `Simulation/ResetRepository`: `count` e `truncate` de `liquidacoes, transacoes_pendentes`.
- `ReceivedCounter` e `RateLimitCounter` ganham `Reset()`.

## 5. Pipeline

### 5.1 A3: Producer resiliente
- Cada volta do loop do `ProducerWorker` (ler página, enviar itens) fica dentro de um `try/catch` que trata qualquer exceção que não seja o cancelamento do host:
  - loga em `Error`;
  - espera um backoff exponencial de 1s a 30s (constantes nomeadas);
  - segue.
- O backoff volta a 1s no primeiro envio aceito.
- Itens não aceitos não são marcados como despachados e são relidos na próxima varredura (comportamento atual).
- `docker-compose.bankcore.yml`: `restart: unless-stopped` em `producer` e `consumer`. O compose base mantém `restart: "no"` no Producer, que no modo `Local` encerra ao terminar o seed.

### 5.2 A4: Consumer concorrente (ADR 0009)
- `ProcessamentoParalelo = 8` (constante nomeada), usado em `BasicQos(prefetchCount)` e em `ConnectionFactory.ConsumerDispatchConcurrency`. O semáforo de 1 vira semáforo de `ProcessamentoParalelo`; o desligamento espera todos os slots.
- **Idempotência preservada:** a barreira continua sendo o `insert ... on conflict do nothing` em `liquidacoes` com `settle_confirmado_em`. Duas entregas concorrentes do mesmo id resultam em uma linha. No pior caso, os dois workers chamam `settle`, e o BankCore responde `200` idempotente (§5 da 2.0.0).
- **Ordem:** a liquidação deixa de ser FIFO estrita dentro da fila. A ordem não é requisito; o FIFO do BankCore continua valendo na leitura (R6).
- O processamento de uma mensagem sai do `LiquidacaoConsumer` para `LiquidacaoProcessor` (mesma lógica de hoje), testável contra Postgres real.
- **Meta medida no cenário Padrão:**
  - em regime, liquidadas/s acompanha recebidas/s (diferença média ≤ 2/s);
  - a fila fica abaixo de 50;
  - p95 de latência de liquidação abaixo de 70s, contra ~110s na 2.1.0.

  O piso físico é a drenagem do BankCore: 1500 transferências a 25/s levam 60s, então a latência das últimas é dominada pela espera no BankCore, não pelo Liquida. Os números medidos vão para o changelog.

### 5.3 A5: 429 da execução sobrevive ao restart
- O dashboard mantém um acumulador monotônico de `rateLimited`. A cada snapshot soma `atual − anterior` quando o contador cresce, e soma `atual` quando ele cai (restart ou reset).
- A linha de base da execução (§8.9 da 2.1.0) passa a guardar esse acumulador em vez do valor cru. O "429 da execução" fica correto depois de um restart.

## 6. Dashboard

### 6.1 Barra superior
- **Ordem, da esquerda para a direita:** marca, estado das fontes, e então o grupo de ação:
  - fase;
  - seletor de **Cenário**;
  - caixa **Limpar antes de iniciar**;
  - botão **Iniciar simulação** / **Parar simulação**;
  - lixeira.
- Abaixo de 1100px o grupo de ação ocupa uma linha própria; em 390px, duas linhas (cenário e caixa; botão e lixeira).
- Com a simulação em `seeding`/`running`, cenário, caixa e lixeira ficam desabilitados.

### 6.2 Cenários

| Cenário | Body do `POST /simulation/start` | O que mostra (frase exibida) |
|---|---|---|
| Tranquilo | `{"accounts":40,"transfers":500,"rate":20,"workers":8}` | Abaixo do teto: tudo é liquidado na hora, sem fila. |
| Padrão (pré-selecionado) | `{"accounts":40,"transfers":1500,"rate":60,"workers":16}` | Acima do teto: a fila cresce e drena a 25/s. |
| Estresse | `{"accounts":15,"transfers":5000,"rate":200,"workers":32}` | Muitas colisões de lock, fila grande e 429. |
| Personalizado | os quatro campos do formulário | Os números que você escolher. |

- **Controle:** `<select>` nativo (modo Operate do impeccable: controle conhecido, teclado e leitor de tela de graça). A frase do cenário aparece na linha de achado enquanto a simulação está parada ("Cenário Padrão: 1.500 transferências a 60/s. Acima do teto: a fila cresce e drena a 25/s.").
- **Personalizado:** abre uma linha logo abaixo do título com quatro campos numéricos:

  | Campo | Rótulo | Limite visível |
  |---|---|---|
  | `accounts` | Contas | 2 a 500 |
  | `transfers` | Transferências | 1 a 100.000 |
  | `rate` | Taxa (por segundo) | 1 a 500 |
  | `workers` | Workers | 1 a 64 |

  O limite fica ligado ao campo por `aria-describedby` (taste-skill: o limite que resta fica visível ao lado do campo). Valor fora do limite marca o campo com borda `--negative` e mensagem curta, e desabilita **Iniciar**. A validação é uma função pura testada.
- **Persistência:** o cenário e a caixa ficam só em memória da aba (YAGNI).

### 6.3 Lixeira e confirmação
- **Botão:** só ícone (lixeira em SVG próprio, sem biblioteca de ícones), 36px, redondo, fundo `--raised`, ícone `--ink`, `aria-label="Limpar ambiente"`.
  - Desabilitado fora de `idle`/`finished`/`stopped`, ou com o BankCore indisponível, com `title` explicando ("Pare a simulação para limpar").
  - Fiel à house rule da taste-skill: ícone sem quadrado colorido em volta, mesmo raio das pílulas.
- **Confirmação:** popover ancorado sob a lixeira (não modal), fundo `--raised` com sombra curta (é o único elemento que flutua de verdade, regra da taste-skill).
  - Texto: "Isso apaga todos os dados da simulação no BankCore e no Liquida."
  - Botões: **Cancelar** (fantasma) e **Apagar dados** (fundo `--negative`, texto `--ink`). Destrutivo é estado, então a cor de status é legítima; a cor de dado continua fora de botões.
  - O foco vai para **Cancelar** ao abrir (padrão seguro); `Esc` e clique fora fecham, e o foco volta para a lixeira (emil-design-eng).
- **Estado "Limpando…":**
  - a fase na barra vira "Limpando ambiente…" com o ponto pulsando;
  - todos os controles ficam desabilitados com largura fixa;
  - ao terminar, KPIs zerados e gráficos com a janela recomeçada.
  - Erro mostra a mensagem por 5s (mesmo mecanismo da 2.1.0), por exemplo "Limpeza incompleta: liquidações continuam chegando".

### 6.4 Sequência de limpeza (função pura de orquestração, testada)
1. `POST /simulation/reset` no BankCore. `409 SIMULATION_RUNNING` aborta com a mensagem "Pare a simulação para limpar".
2. `POST /simulacao/reset` no Liquida, com timeout de 25s, maior que os 20s do servidor.
3. Zera no cliente: janela de séries, linha de base, acumulador de 429, referência de taxa.

"Limpar ambiente" executa 1 a 3. **Iniciar** com "Limpar antes de iniciar" marcada executa 1 a 3 e depois `POST /simulation/start` com o body do cenário; desmarcada, só o start. A UI só permite iniciar com a simulação parada, então não há "parar" na sequência.

### 6.5 Movimento (animate, review-animations, animation-performance)
- **Popover:** entra com `opacity` 0 → 1 e `scale` 0,97 → 1, origem no canto da lixeira, 150ms `ease-out`; sai em 100ms.
- **Botões:** press `scale(0.97)`, como o Iniciar.
- **Proibido:** nenhum spinner e nenhuma animação em números ou gráficos.
- **`prefers-reduced-motion`:** popover sem escala, só opacidade instantânea.

### 6.6 Skills por decisão

| Decisão | Skill |
|---|---|
| `<select>` nativo, formulário com limites visíveis e `aria-describedby` | impeccable (Operate), taste-skill (forms) |
| Lixeira só ícone, sem quadrado colorido, `aria-label` | taste-skill (house rules app) |
| Confirmação em popover, foco em Cancelar, `Esc`, retorno de foco | emil-design-eng |
| Vermelho só em "Apagar dados" e erro | taste-skill (cor de status) |
| Movimento do popover e press | animate, animation-performance (só `opacity`/`transform`) |
| Revisão final | `impeccable audit` e `polish`, `review-animations`, `inspect.mjs --app` |

## 7. Testes
- **.NET (xUnit, `WebApplicationFactory` e Testcontainers no padrão do repo):**
  - `Reset_Nao_Existe_Com_Flag_Desligada` (404);
  - `Reset_Zera_Contadores_Filas_E_Tabelas` (fakes de fila e repositório);
  - `Liquidacoes_Durante_Reset_Responde_409_Sem_Publicar_Nem_Contar`;
  - `Reset_Concorrente_Responde_409`;
  - `Reset_Aceita_Post_Do_Dashboard_Com_Cors`;
  - `Liquidacoes_Continua_Sem_Cors`;
  - `Coordenador_Estabiliza_Apos_Janela_De_Silencio` e `Coordenador_Nao_Estabiliza_Com_Chegadas_Continuas` (relógio falso);
  - `Producer_Segue_Apos_Api_Indisponivel` (handler que falha e depois aceita);
  - `Processor_Concorrente_Mesmo_Id_Grava_Uma_Linha` (Postgres real).
- **vitest:**
  - validação dos campos do Personalizado nos limites;
  - body de cada cenário;
  - acumulador monotônico de 429 (cresce, cai por restart, cai por reset);
  - sequência de limpeza (ordem das chamadas, aborto em `409`, limpeza do estado do cliente).

## 8. Critérios de aceitação
- **CA28** Com a simulação em `seeding`/`running`, a UI não permite limpar (lixeira desabilitada com motivo). Chamado direto:
  - `POST /simulation/reset` do BankCore responde `409 SIMULATION_RUNNING`;
  - `POST /simulacao/reset` do Liquida responde `503 reset_nao_estabilizou` em até 20s.
- **CA29** Depois de **Limpar**, KPIs e gráficos ficam zerados e estáveis por 15s: fila 0, DLQ 0, `liquidacoes` 0, `recebidas` 0, pendentes no BankCore 0. Vale também quando o clique vem logo depois de uma execução que ainda tinha página do Producer e mensagens em voo.
- **CA30** Cada cenário roda e o painel conta a história da §6.2:
  - Tranquilo: sem fila sustentada (a fila volta a 0 entre rajadas e não cresce ao longo da execução); 429 ocasional aceito, sem perda;
  - Padrão: fila cresce e drena com recebidas em 25/s;
  - Estresse: colisões de lock altas, fila grande e 429 maior que 0.

  O Personalizado respeita os limites.
- **CA31** "Limpar antes de iniciar" funciona em três execuções seguidas: cada uma começa do zero, e as liquidadas da execução alcançam as aceitas.
- **CA32** A3: com a `liquida-api` parada por 30s durante a corrida, o Producer não encerra, segue sozinho quando a API volta e a execução termina com backlog 0.
- **CA33** A4: no Padrão, liquidadas/s acompanha recebidas/s em regime (diferença média ≤ 2/s), a fila fica abaixo de 50 e o p95 de latência fica abaixo de 70s. Números registrados.
- **CA34** A5: com a API reiniciada durante a corrida, o "429 da execução" não volta para 0 e termina igual à soma dos 429 antes e depois do restart.
- **CA35** Verificado no navegador em 1440, 768 e 390 (popover, Personalizado, estados desabilitados e "Limpando…"); `inspect.mjs --app` 0 FAIL; `impeccable detect` sem achados.
- **CA36** `dotnet build` sem avisos, `dotnet test` verde, `npm run build` sem avisos e `npm test` verde.

## 9. Ordem de execução
Branch `feature/limpeza-e-cenarios` a partir de `feature/dashboard-tempo-real`; depois do merge do PR #1, `git merge origin/main`.
1. **Passo 18:** A3 (Producer) e compose, com teste.
2. **Passo 19:** A4 (`LiquidacaoProcessor` e concorrência), com teste e medição.
3. **Passo 20:** `POST /simulacao/reset` (portão, coordenador, fila, repositório, CORS, flag), com testes.
4. **Passo 21:** dashboard (cenários, Personalizado, lixeira, confirmação, sequência, A5), com testes `vitest`.
5. **Passo 22:** gate de design e fluxo integrado com o BankCore 1.4.0 (CA28 a CA35).

## 10. Decisões
- **D17** Reset do Liquida por portão e janela de silêncio dentro da API (§4.3), em vez de coordenação entre processos. Não muda o BankCore.
- **D18** `409 simulacao_reiniciando` no `POST /liquidacoes` durante o reset, para o Polly não repetir.
- **D19** Contagem de filas no reset por AMQP passivo, não pela Management API (sem atraso de estatística).
- **D20** Consumer com `ProcessamentoParalelo = 8`; ordem FIFO estrita deixa de valer dentro da fila (ADR 0009).
- **D21** Acumulador monotônico de 429 no cliente (A5); o servidor continua zerando no restart (ADR 0004).
- **D22** Seletor de cenário nativo; frase do cenário na linha de achado; cenário e caixa só em memória.
- **D23** No compose de integração o Producer revarre pendentes a cada 1s (`Producer__PollDelaySeconds=1`), no lugar dos 5s da D7. O pacing segue em 25/s para o Estresse continuar mostrando 429.

## 11. Changelog
- **2.2.0 (2026-11-01, Entregue)** Reteste com o BankCore corrigido (4dd117e: `LOCK TABLE transfers, entries, accounts` na ordem do settle e retry em `40P01`/`55P03`):
  - **Limpeza com settles em voo:** três ciclos pela lixeira, cada um aos ~35s de um Padrão, com o Liquida confirmando 8 a 24 settles/s e fila de 9 a 11. Os seis `POST /simulation/reset` do período (três com settles em voo, três de "limpar antes de iniciar") responderam `200` em 0,5 a 0,8s; nenhum `500` no log do BankCore e nenhum erro na tela. O achado do deadlock está fechado.
  - **CA44 do BankCore** (execução `cf106095`, com limpeza antes): antes do start, BankCore e Liquida zerados (`pending`/`settled`/`failed` 0, contas 0, `liquidadas`/`fila`/`dlq`/`recebidas` 0). Ao fim, 1428/1428 `SETTLED`, `pending` 0, `divergent_accounts` 0, DLQ 0, p95 43,7s.
  - **Interferência registrada:** uma tentativa anterior (execução iniciada 05:05:25Z) foi apagada por resets, starts e stops de outros clientes (um navegador e outra sessão) entre 05:06:08Z e 05:08:04Z. O sistema se comportou como especificado: o portão barrou com `409` os envios em voo, a limpeza estabilizou e o estado ficou zerado. A execução foi refeita sem interferência.
  - **Verificação final:** `origin/main` sem novidades; `dotnet build` 0 avisos, `dotnet test` 44/44, `npm run build` sem avisos, `npm test` 25/25.
- **2.2.0 (2026-11-01, correção do CA30 por decisão do usuário)**
  - **Por que mudou:** o critério do Tranquilo ("recebidas ≤ 20/s, fila perto de 0, nenhum 429") era mais rígido que o desenho. Com o Producer esperando 5s quando o backlog zera (D7), o acumulado é drenado em rajadas a 25/s, e o balde de 25 do Producer e o balde de 25 da API (ADR 0001) se desalinham em alguns 429, todos reenviados pelo Polly e sem perda.
  - **O que mudou:** o critério virou "sem fila sustentada; 429 ocasional aceito, sem perda", e o compose de integração passou a revarrer a cada 1s (D23).
  - **Tranquilo de novo** (execução `9a3531de`, com limpeza antes): 470/470, **0 respostas 429**, recebidas 19,3/s em média, p95 1,66s (antes 7,8s), DLQ 0. A fila máxima foi 20; ela volta a 0 entre rajadas e não cresce ao longo da execução. Os patamares de 19 e 20 por ~5s vêm do intervalo de estatística da Management API, não de fila real. **CA30 ✅**.
  - **Teste da corrida do `RegistrarAsync`:** `Registro_Concorrente_Ainda_Invisivel_Retorna_PendenteConfirmacao` (Postgres real) segura a corrida de forma determinística. Uma transação insere a linha sem commit, o `RegistrarAsync` fica esperando no `on conflict`, e o commit deixa a linha invisível ao snapshot da instrução. Com o código antigo o teste falha ("Expected PendenteConfirmacao, Actual Inserida"); com a correção, passa.
- **2.2.0 (2026-11-01, Passos 18 a 22 implementados; fluxo integrado com o BankCore 1.4.0)** Dashboard servido do build de produção em 4200, Chrome real (headless via DevTools). Execuções `8a9c63bb` (Tranquilo), `137db3ea` (Padrão), `93ee657c` (Estresse), mais duas Padrão e uma Personalizado.
  - **CA28 ✅** Rodando: lixeira e seletor desabilitados com o motivo "Pare a simulação para limpar". Direto na API: BankCore `409 SIMULATION_RUNNING`; Liquida `503 reset_nao_estabilizou` em 20,4s.
  - **CA29 ✅** Lixeira confirmada com o pipeline ainda drenando (recebidas subindo de 787 para 862, fila 17, página do Producer em curso). A limpeza terminou em ~15s, e depois foram 25s com tudo em 0, conferido no snapshot, no RabbitMQ (`liquidacoes` 0, `liquidacoes.dlq` 0), no Postgres (`liquidacoes` 0) e no BankCore (`pending` 0, contas 0).
  - **CA30 ⚠️ parcial**
    - **Padrão ✅** fila cresce e drena com recebidas em 25/s.
    - **Estresse ✅** 4776/4776, 5022 colisões, fila até 111, 429 > 0.
    - **Personalizado ✅** campo fora do limite bloqueia o Iniciar; o body `{6,120,15,4}` chegou ao BankCore; 120/120.
    - **Tranquilo ❌ como escrito:** 497/497, sem DLQ e p95 7,8s, mas houve 7 respostas 429, fila até 25 e recebidas em rajadas de ~25/s, não ≤ 20. A causa vem de antes desta versão: o Producer espera 5s quando o backlog zera (D7 da 2.0.0) e então drena o acumulado a 25/s, e o balde do Producer e o da API se desalinham em alguns 429 (todos reenviados pelo Polly; recebidas = aceitas). Correção pendente de decisão: `Producer__PollDelaySeconds=1` no compose de integração e pacing do Producer em 24/s, ou relaxar o critério.
  - **CA31 ✅** Três execuções seguidas com "Limpar antes de iniciar" (Tranquilo 497/497, Padrão 1453/1453, Estresse 4776/4776), cada uma do zero (o `liquidadas` do Liquida bate com a execução), mais duas Padrão e uma Personalizado também limpas.
  - **CA32 ✅** `liquida-api` parada 30s no Estresse: o Producer seguiu de pé (backoff), voltou a 24/s e a execução fechou com backlog 0.
  - **CA33 ✅** Padrão: recebidas 24,06/s contra liquidadas 23,92/s em regime (diferença média absoluta 0,91/s), fila máxima 25, p95 57,95s (antes ~110s).
  - **CA34 ✅** Restart da API no Estresse: o "429 da execução" foi de 13 para 31, sem voltar a 0.
  - **CA35 ✅** 1440, 768 e 390 com popover, Personalizado, estados desabilitados e "Limpando ambiente…"; `inspect.mjs --app` 0 FAIL 0 WARN; `impeccable detect` sem achados; `lint.mjs` 0 FAIL (avisos defensáveis: sombra só no popover que flutua e zero à esquerda em m:ss).
  - **CA36 ✅** `dotnet build` 0 avisos, `dotnet test` 43/43, `npm run build` sem avisos, `npm test` 25/25.
  - **Correções feitas durante a verificação:**
    - (a) com a concorrência da ADR 0009, `RegistrarAsync` lia "sem linha" como `Inserida` quando a linha concorrente ainda não era visível ao snapshot; agora é `PendenteConfirmacao` (só afetava contagem de log; o settle duplo é idempotente);
    - (b) depois da limpeza o dashboard usa linha de base zerada para a próxima execução, em vez de "nunca vi execução", porque o rótulo caía em "acumulado no banco" e o 429 da execução ficava "indisponível";
    - (c) o popover passou a ancorar na linha de ações nas larguras estreitas, e o seletor e a caixa quebram linha em 390px.
  - **Achado no BankCore (para o orquestrador):** `POST /simulation/reset` chamado com settles do Liquida em andamento respondeu `500` depois de ~1s, junto com 6 `PATCH /settle` também `500` (padrão de deadlock do Postgres, `deadlock_timeout` 1s). Do lado do Liquida o settle `500` é transitório e é repetido; o dashboard mostra "Falha ao limpar o BankCore: erro interno", e a nova tentativa passa. Sugestão do lado do BankCore: repetir o reset em `40P01` ou travar as tabelas em ordem fixa.
- **2.2.0 (2026-10-31, Aprovada)** Aprovada pelo usuário via orquestrador, incluindo `503 reset_nao_estabilizou`, `409 simulacao_reiniciando` e o formato `{"error","message"}`. Até o BankCore 1.4.0 subir, o dashboard trata `404` do reset do BankCore com mensagem clara ("O BankCore no ar não tem limpeza (precisa da 1.4.0)").
- **2.2.0 (2026-10-31, Draft)** Limpeza de ambiente (reset do BankCore 1.4.0 e do Liquida com portão e janela de silêncio), cenários de simulação, "Limpar antes de iniciar", Producer resiliente (A3), Consumer concorrente (A4) e 429 da execução contínuo (A5). Aguardando aprovação.
