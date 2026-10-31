# Liquida, Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 2.1.0 |
| **Status** | Entregue (2026-10-31). Aprovada com os ajustes A1 e A2; CA18 corrigido por decisão do usuário; CA14 a CA21 verificados |
| **Data** | 2026-10-31 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |
| **Base** | Estende `spec-v2.0.0.md` (fronteira BankCore, entregue e tagueada `v2.0.0`) |

> Versionamento **SemVer**. Esta é uma **MINOR**: adiciona escopo compatível. O `GET /metrics` ganha um campo (`recebidas`) sem remover nem renomear nenhum; nasce o `GET /metrics/stream` (SSE); o compose de integração ganha defaults de credencial. O contrato de escrita (`POST /liquidacoes`), o pipeline e a fronteira com o BankCore não mudam. O dashboard é **excluído e refeito do zero** em outra stack (React + Vite + TypeScript, ADR 0007); como ele não é contrato de nenhum outro sistema, a troca não quebra compatibilidade. Referências: ADR 0006 (push por SSE) e ADR 0007 (stack do dashboard).

---

## 1. Objetivo da 2.1.0
Não existe interface de usuário final. A única interface do conjunto BankCore + Liquida é um **dashboard de tempo real**: fundo preto, uma linha de KPIs, uma grade de gráficos e um botão **Iniciar simulação** que dispara o fluxo nos dois sistemas. O BankCore gera transferências reais; o Producer do Liquida já drena `PENDING` sozinho a cada 5s (D7 da 2.0.0) e o Consumer faz o `settle`. O dashboard mostra, ao vivo, a simulação inteira sendo liquidada dentro do teto de 25 rps, sem perda e com o ledger íntegro.

Para isso a 2.1.0:
1. troca o polling do Liquida por **push via Server-Sent Events** (`GET /metrics/stream`);
2. conta as liquidações **recebidas** pela API (202), para mostrar recebidas/s contra o teto de 25;
3. deixa a integração **zero-touch** (credencial semeada pelo BankCore como default do compose);
4. refaz o dashboard do zero, consumindo o SSE do Liquida e o simulador do BankCore.

## 2. Escopo
1. **Liquida.Api:** `GET /metrics/stream` (SSE, 1 evento/s) e contador em memória `recebidas` no `MetricsSnapshot`. `GET /metrics` mantido como fallback.
2. **Compose de integração:** defaults de `BankCore__ClientId`/`BankCore__ClientSecret` em `docker-compose.bankcore.yml`; `.env.example` atualizado.
3. **Dashboard:** `web/liquida-dashboard` excluído por completo e recriado em React + Vite + TypeScript + SCSS, gráficos com uPlot (§8).
4. **ADRs:** 0006 (push por SSE, substitui o trecho de polling da 0004) e 0007 (stack do dashboard).

Fora de escopo: mudar o rate limit (segue 25 rps), mudar Producer, Consumer ou a fronteira com o BankCore, autenticação dos endpoints de leitura, Docker do dashboard (roda em `npm run dev` na porta 4200, como o anterior rodava em `ng serve`), os endpoints `GET /liquidacoes/recentes` e `GET /transacoes/recentes` (ficam como estão; o novo dashboard não os consome).

## 3. Arquitetura (delta)
```
                         POST /simulation/start|stop, GET /simulation/stats (polling 1s)
dashboard (4200) ───────────────────────────────────────────────────────────► BankCore API (8081)
      │                                                                              │ PENDING
      │  EventSource GET /metrics/stream (push 1s)                                   ▼
      └──────────────────────────────────────────► Liquida.Api (8080) ◄── Producer (drena a cada 5s, 25 rps)
                                                        │ fila                       
                                                        ▼                            
                                                    Consumer ──PATCH /settle──► BankCore
```
- O dashboard fala com **duas fontes independentes**. A queda de uma não derruba a outra (§8.6).
- O Liquida não passa a chamar o simulador do BankCore: quem dispara a simulação é o navegador, direto no BankCore (CORS do BankCore liberado para `http://localhost:4200`, entregue pela sessão bankcore).

## 4. Liquida.Api

### 4.1 Contador `recebidas`
- Novo `ReceivedCounter` em `Reading/`, mesmo padrão do `RateLimitCounter` (singleton, `Interlocked`, zera no restart por design, ADR 0004).
- Incrementado no `POST /liquidacoes` **somente** quando a resposta é `202`: depois da validação e da publicação na fila, imediatamente antes do `Results.Accepted`. `400` (validação), `429` (rate limiter, nem chega no handler) e falha de publicação não contam.
- `MetricsSnapshot` ganha `Recebidas` (`long`), serializado como `recebidas`, posicionado antes de `RateLimited`. Campo novo em record posicional: os testes que constroem o snapshot são ajustados.

`GET /metrics` passa a responder:
```json
{
  "capturadoEm": "2026-10-31T12:34:56.789Z",
  "pendentes": 0,
  "enviadas": 0,
  "liquidadas": 1210,
  "rpsLiquidacao": 25,
  "fila": 14,
  "dlq": 0,
  "recebidas": 1224,
  "rateLimited": 0
}
```

### 4.2 `GET /metrics/stream` (Server-Sent Events)
- `Content-Type: text/event-stream`, `Cache-Control: no-cache`, sem buffering de resposta (`IHttpResponseBodyFeature.DisableBuffering`).
- A cada **1s** (`PeriodicTimer`, intervalo como constante nomeada), monta o snapshot com o **mesmo** `MetricsService.SnapshotAsync` do `GET /metrics` e escreve:
  ```
  event: metrics
  data: {"capturadoEm":"…","pendentes":0,…,"recebidas":1224,"rateLimited":0}

  ```
  O primeiro evento sai imediatamente ao conectar (o dashboard não espera 1s para pintar).
- JSON com as mesmas opções de serialização do `GET /metrics` (camelCase, enums como string).
- **Fora do rate limiter** (não usa `RequireRateLimiting`), como os demais `GET` (RNF8).
- **Encerramento limpo:** o loop termina quando `HttpContext.RequestAborted` é cancelado (cliente fechou a aba ou o `EventSource`). `OperationCanceledException` desse token é o fim normal: não é logado como erro e não vira `500`.
- Falha de uma fonte dentro do snapshot segue a degradação da 1.1.0 (`fila`/`dlq` `null`); exceção inesperada ao montar um snapshot encerra o stream (o `EventSource` do navegador reconecta sozinho).
- O net8.0 não tem `TypedResults.ServerSentEvents` (é do .NET 10); a escrita é manual no `HttpResponse`, sem pacote novo.
- **CORS:** o `EventSource` sem credenciais faz um `GET` simples com `Origin`, sem preflight. A política `dashboard` atual (`WithOrigins(http://localhost:4200)`, `WithMethods("GET")`, `AllowAnyHeader`) já cobre o stream; nenhuma mudança de CORS. Confirmado por teste (§9, CA2).

### 4.3 Origem BankCore e os campos `pendentes`/`enviadas`
Em `Origem=BankCore` (fluxo integrado), o Producer não usa a tabela local `transacoes_pendentes` (D3 da 2.0.0), então `pendentes` e `enviadas` do snapshot ficam em `0` e **não** representam o backlog real. **Decisão D10:** o dashboard **não exibe** `pendentes`/`enviadas` do Liquida; o backlog pendente vem do BankCore (`transfers.pending` de `GET /simulation/stats`). Os campos continuam no snapshot (compatibilidade e `Origem=Local`).

## 5. Integração zero-touch
- O BankCore passa a semear no boot um service-client `SETTLEMENT` a partir de `SETTLEMENT_CLIENT_ID`/`SETTLEMENT_CLIENT_SECRET` (entrega da sessão bankcore, atrás de flag de dev).
- `docker-compose.bankcore.yml`, nos serviços `consumer` e `producer`:
  ```yaml
  BankCore__ClientId: ${BankCore__ClientId:-svc_liquida_dev}
  BankCore__ClientSecret: ${BankCore__ClientSecret:-dev-liquida-secret-change-me}
  ```
- `.env.example`: as linhas `BankCore__ClientId=svc_...`/`BankCore__ClientSecret=...` e o roteiro de provisionamento manual saem; fica a nota de que o compose já usa a credencial semeada pelo BankCore e que as variáveis só são definidas para sobrescrevê-la.
- **Atenção operacional:** um `.env` local que ainda tenha `BankCore__ClientId`/`BankCore__ClientSecret` de um provisionamento antigo **sobrescreve** o default. Para o fluxo zero-touch, essas duas linhas devem sair do `.env` local (que não é versionado e não será commitado).

## 6. Contrato consumido do BankCore (fechado pelo orquestrador)
`http://localhost:8081`, sem auth, CORS liberado para `http://localhost:4200`, JSON `snake_case`, dinheiro em centavos `int64`.

| Endpoint | Uso no dashboard |
|---|---|
| `POST /simulation/start` | botão Iniciar. Enviado **sem body** (defaults do BankCore: 40 contas, 1500 transferências, 60/s, 16 workers). `202` → status; `409 SIMULATION_RUNNING` → só atualiza o status, sem erro na tela. |
| `POST /simulation/stop` | botão Parar. `200` → status. |
| `GET /simulation/status` | não é chamado à parte: o `simulation` embutido no `stats` já traz `phase`, `config` e `counters`. |
| `GET /simulation/stats` | polling 1s: `transfers.{pending,settled,failed}`, `volume_cents.{pending,settled}`, `throughput.{created_per_sec,settled_per_sec}`, `settlement_latency_ms.{p50,p95}`, `ledger.{accounts,divergent_accounts}`, `version_conflicts`, `simulation.{phase,run_id,started_at,finished_at,config,counters}`. |

`phase` ∈ `idle | seeding | running | finished | stopped`. Nomes e shapes não são alterados pelo Liquida; divergência vai para o orquestrador.

## 7. Requisitos (delta)
- **RF9** `GET /metrics/stream` emite um evento `metrics` por segundo com o mesmo `MetricsSnapshot` do `GET /metrics` e encerra limpo no cancelamento da conexão.
- **RF10** O snapshot traz `recebidas`: total de `POST /liquidacoes` respondidos com `202` desde o start da API.
- **RF11** O dashboard inicia e para a simulação do BankCore e mostra a fase atual.
- **RF12** O dashboard mostra os KPIs e gráficos da §8.3 e §8.4, atualizados a cada segundo, em janela móvel de 120s.
- **RNF15** Push por SSE para o Liquida; polling só para o BankCore (que não oferece push) e como fallback do SSE.
- **RNF16** Degradação graciosa: cada fonte tem estado próprio; KPI de fonte caída mostra `indisponível`, o resto segue.
- **RNF17** Dashboard sem layout shift a cada atualização (números tabulares, larguras estáveis) e a 60 fps com redesenho de 1 Hz.

## 8. Dashboard

### 8.1 Stack (ADR 0007)
React 19 + Vite + TypeScript estrito + SCSS, em `web/liquida-dashboard` (mesma pasta, conteúdo novo). Dev server na porta **4200** com `strictPort` (o CORS dos dois backends já aponta para ela).

Dependências, todas justificadas:

| Pacote | Por quê |
|---|---|
| `react`, `react-dom` | framework escolhido pelo usuário no lugar do Angular (ADR 0007) |
| `uplot` | gráficos de série temporal em tempo real, ~50 KB, canvas, feito para atualizar a 1 Hz sem custo; escolhido pelo usuário (§8.5) |
| `vite`, `@vitejs/plugin-react`, `typescript`, `@types/react`, `@types/react-dom` | build e tipos |
| `sass` | mantém SCSS com tokens em variáveis CSS (regra do projeto) |
| `vitest` | testes das funções puras (janela móvel, derivação de taxas, estado das fontes); a stack nova não traz test runner |

Sem biblioteca de animação, ícones, UI kit, roteador ou gerenciador de estado: tudo isso seria dependência sem necessidade real. Versões exatas fixadas no `package.json` na implementação.

Configuração por variável de ambiente do Vite: `VITE_LIQUIDA_API_URL` (default `http://localhost:8080`) e `VITE_BANKCORE_API_URL` (default `http://localhost:8081`).

Estrutura (arquivos em `kebab-case`, um componente por arquivo, sem `any`, dados em hooks/serviços fora do JSX):
```
web/liquida-dashboard/
  index.html  package.json  vite.config.ts  tsconfig.json  tsconfig.app.json
  src/
    main.tsx  app.tsx
    styles/tokens.scss  styles/global.scss
    config.ts                 URLs, janela de 120s, teto 25, intervalo 1s
    data/types.ts             MetricsSnapshot (Liquida) e SimulationStats (BankCore)
    data/liquida-stream.ts    EventSource + fallback GET /metrics
    data/bankcore-client.ts   start, stop, stats
    data/series.ts            janela móvel e derivação de taxas (puro, testado)
    data/use-dashboard.ts     hook que junta as duas fontes
    components/header-bar.tsx  simulation-control.tsx  source-status.tsx
    components/kpi-strip.tsx  pipeline-strip.tsx
    components/time-series-chart.tsx  (wrapper do uPlot)
    components/throughput-panel.tsx  backlog-panel.tsx  latency-panel.tsx
```

### 8.2 Direção visual e qual skill embasou cada decisão
Pergunta que a tela responde primeiro: **"a simulação está sendo liquidada dentro do teto de 25/s, sem perder nada?"** Achado lido primeiro (linha sob o título, montada dos dados): por exemplo "1.210 de 1.500 liquidadas · backlog drenando a 25/s · ledger íntegro".

| Decisão | Skill | Detalhe |
|---|---|---|
| Tela de trabalho, não página: segue as regras de *app screens* | taste-skill (`references/app-screens.md`) | modo Operate no impeccable: precisão e contenção acima de expressão |
| Estilo **true-black-flat** (sorteio `draw.mjs --app --kind markets --theme dark`, seed 30394011), escolhido contra `data-terminal` | taste-skill | é uma tela assistida numa demonstração, não sessão longa de terminal; o próprio estilo pede fundo `#000` e cor confinada a dados, que é o pedido do produto |
| Elevação só por luminosidade: chão `#000000`, painel `#111111`, aninhado `#2a2a2a`; sem borda nem sombra em painel | taste-skill (sheet true-black-flat) | |
| **Uma cor só, nos dados: verde Supabase `#3ECF8E`**, no lugar do lima sorteado. Todo o resto é preto, cinzas do sheet e detalhes em branco puro `#FFFFFF` | escolha do usuário, aplicada pela regra "one data hue" da taste-skill | o verde marca o elemento principal: **liquidadas** (série liquidadas/s, segmento liquidado do funil, KPI de liquidadas). Também é `--positive` |
| Vermelho `#E5484D` e âmbar `#E5A93B` só como **estado** (DLQ > 0, `divergent_accounts` > 0, fonte indisponível, `failed`) | taste-skill ("status colours are for status") | nunca decoração |
| Botão primário por **inversão**: pílula branca 36px, texto preto; Parar é pílula `#2a2a2a` com texto branco | taste-skill (sheet) + pedido do usuário (detalhes em branco puro) | a cor de dado nunca toca botão |
| Tipografia Inter 400/500/600, `tabular-nums` em todo número que atualiza, sentence case, sem caixa alta espaçada | taste-skill (sheet + house rules) | sem layout shift a 1 Hz (RNF17) |
| KPIs como **tipo, não cartões**: uma faixa única dividida por filetes de 1px; rótulo cinza `#b4b4b4` 12-14px acima, valor 24px; o KPI-resposta (**liquidadas**) a 32px | taste-skill (§5 KPIs) | sem ícone, sem sparkline, sem delta colorido sem base |
| Sem sidebar: o sheet prevê T-frame com sidebar, mas há uma tela só e nada a navegar. Fica a barra superior de 64px com marca, estado das fontes, fase da simulação e o botão | taste-skill (redraw justificado: o produto não tem destinos de navegação) | a barra carrega conteúdo real de ponta a ponta |
| **Elemento-assinatura: faixa do pipeline** BankCore pendentes → recebidas pela API (portão de 25/s) → fila → liquidadas, com DLQ e 429 como saídas laterais. Só este produto tem esse caminho | taste-skill ("one element only this product has") + impeccable shape (o que é verdade só aqui) | substitui o "funil por status" genérico |
| Gráficos com quase nenhum cromo: grade horizontal 3-5 linhas `#2b2b2b`, sem eixo Y desenhado, ticks redondos, linhas de 2px em segmentos retos, sem preenchimento, sem legenda em série única, legenda de ponto + rótulo em séries múltiplas (até 3) | taste-skill (§6 Charts) | o teto de 25/s é linha tracejada branca a 40%, rotulada "teto 25/s" |
| Estados honestos: zero é `0`; fonte caída é `indisponível` em cinza; painel mantém o quadro vazio antes do primeiro dado | taste-skill (§11) + impeccable (harden) | |
| Movimento mínimo: números e gráficos **não** animam (dado funcional a 1 Hz); só feedback de controle e transições de estado | remove-animations / animate | detalhes em §8.7 |
| Polimento: alvos de clique ≥ 36px, foco visível, hover só em `(hover: hover)`, botão desabilitado durante a requisição sem saltar largura | emil-design-eng | |

Tokens (`styles/tokens.scss`, como variáveis CSS; nada de cor ou espaçamento solto):
```scss
:root {
  --ground: #000000;
  --surface: #111111;
  --raised: #2a2a2a;
  --rule: #1a1a1a;
  --grid: #2b2b2b;
  --ink: #ffffff;
  --muted: #b4b4b4;
  --faint: #949494;
  --data: #3ecf8e;
  --data-2: #e6e6e6;
  --data-3: #6b6b6b;
  --positive: #3ecf8e;
  --negative: #e5484d;
  --warning: #e5a93b;
  --radius-panel: 20px;
  --radius-control: 999px;
  --gap: 14px;
  --pad: 22px;
  --control-height: 36px;
  --fs-title: 20px;
  --fs-section: 16px;
  --fs-kpi: 24px;
  --fs-kpi-lead: 32px;
  --fs-body: 14px;
  --fs-small: 12px;
}
```

### 8.3 Layout (1440×900, tudo na primeira tela)
```
┌ barra 64px ─────────────────────────────────────────────────────────────────────────┐
│ ◆ Liquida + BankCore   Liquida ● ao vivo   BankCore ● ao vivo   Fase: running 0:42  [Parar] │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ achado: 1.210 de 1.500 liquidadas · backlog drenando a 25/s · ledger íntegro         │
├ faixa de KPIs (um painel, 8 células com filete) ────────────────────────────────────┤
│ Liquidadas | Volume liquidado | Recebidas/s | Latência p95 | Backlog | DLQ | Ledger | Colisões │
├──────────────────────────────────────────────────────┬──────────────────────────────┤
│ Vazão (2/3)                                          │ Faixa do pipeline (1/3)      │
│ criadas/s · recebidas/s · liquidadas/s · teto 25     │ pendentes → 25/s → fila → ✓  │
├──────────────────────────────┬───────────────────────┴──────────────────────────────┤
│ Backlog ao longo do tempo    │ Latência p50/p95                                     │
└──────────────────────────────┴──────────────────────────────────────────────────────┘
```
- Grade de 12 colunas, um único `--gap`, margens externas iguais (29px). Painéis `--surface`, raio 20px.
- Abaixo de 1100px: KPIs em 4×2, gráficos em uma coluna. Abaixo de 640px: KPIs em 2×4, barra superior com o botão em largura total abaixo da marca.

**KPIs** (ordem da faixa):

| KPI | Fonte | Valor |
|---|---|---|
| Liquidadas (resposta, 32px, verde) | BankCore `transfers.settled`, como delta da execução (§8.9) | inteiro com separador de milhar; subtítulo "de N aceitas" com `simulation.counters.accepted` |
| Volume liquidado | BankCore `volume_cents.settled`, como delta da execução (§8.9) | `R$` formatado `pt-BR` a partir de centavos (conversão só na fronteira, ADR 0002 do BankCore) |
| Recebidas/s | Liquida, Δ`recebidas`/Δt entre eventos | "24 /25"; `429` da execução como subtítulo (Δ`rateLimited` desde a linha de base, §8.9) |
| Latência p95 | BankCore `settlement_latency_ms.p95` | ms ou s com uma casa |
| Backlog | BankCore `transfers.pending` + Liquida `fila` | soma; subtítulo "pendentes X · fila Y"; se uma fonte cair, mostra só a parte disponível e marca a outra `indisponível` |
| DLQ | Liquida `dlq` | `0` em branco; > 0 em `--negative` |
| Integridade do ledger | BankCore `ledger.divergent_accounts` | "íntegro" (0) ou "N contas divergentes" em `--negative`; subtítulo com `ledger.accounts` |
| Colisões de lock otimista | BankCore `version_conflicts` | inteiro; subtítulo `conflict_exhausted` se > 0 |

### 8.4 Gráficos
Janela móvel de **120 pontos** (120s), um ponto por segundo, mantida no cliente em `data/series.ts`. A série é amostrada no relógio do cliente a cada 1s com o último valor de cada fonte (o tick do SSE e o do polling não precisam coincidir). Fonte ausente no instante vira `null` (lacuna na linha, não zero falso).

| Painel | Séries | Cor |
|---|---|---|
| **Vazão** (maior, 2/3 da largura, ~240px de plot) | criadas/s (BankCore `created_per_sec`), recebidas/s (Liquida Δ`recebidas`), liquidadas/s (Liquida `rpsLiquidacao`), linha fixa do teto 25 | liquidadas `--data`; criadas `--data-2`; recebidas `--data-3`; teto tracejado branco 40% |
| **Faixa do pipeline** (1/3, HTML/SCSS, não uPlot) | pendentes BankCore → recebidas (portão 25/s) → fila → liquidadas, saídas laterais 429 e DLQ; barra 100% de `pending/settled/failed` com seams de 2px | liquidado `--data`; pendente `--data-2`; failed `--negative` |
| **Backlog** | pendentes BankCore e fila RabbitMQ | pendentes `--data-2`; fila `--data-3` |
| **Latência de liquidação** | p50 e p95 (BankCore) | p95 `--data-2`; p50 `--data-3` |

Eixo Y com ticks redondos (contagens inteiras, zero como base), topo acima do máximo; vazão com teto de escala em `max(30, 1.2 × máximo)` para o teto 25 sempre caber. Eixo X em tempo relativo ("-120s" … "agora"), 5 rótulos.

### 8.4.1 Barra de status do funil
A barra 100% da faixa do pipeline usa `pending` do BankCore e `settled`/`failed` da execução (§8.9).

### 8.5 Biblioteca de gráfico vs SVG próprio
Avaliado e decidido com o usuário:
- **SVG próprio:** zero dependência e estilo exato, mas exige escrever escalas, ticks, tooltip e redesenho eficiente; 3 painéis de série temporal justificam não reinventar isso.
- **ApexCharts (o dashboard anterior):** pesado (~500 KB), anima por padrão, cromo difícil de calar. Descartado com a exclusão do dashboard.
- **uPlot (escolhido):** ~50 KB, canvas, feito para séries temporais em tempo real, `setData` a 1 Hz sem reconstruir o gráfico, sem animação embutida, estilo controlável (grade, eixos, cores por token). Necessidade real: três gráficos de série temporal atualizando a cada segundo. A faixa do pipeline continua em HTML/SCSS por ser composição, não plot.

### 8.6 Fontes, estados e degradação
- **Liquida:** `EventSource(${LIQUIDA}/metrics/stream)`, ouvindo `metrics`. Estados da fonte: `conectando`, `ao vivo`, `reconectando`, `indisponível`. Em erro, o próprio `EventSource` reconecta; se não houver evento por **3s**, a fonte vira `reconectando` e o hook passa a fazer polling de `GET /metrics` a cada 1s até o SSE voltar a entregar evento (fallback, ADR 0006).
- **BankCore:** polling de `GET /simulation/stats` a cada 1s, sem sobrepor requisições (a próxima só parte após a anterior terminar ou estourar timeout de 2s). 3 falhas seguidas → `indisponível`; a primeira resposta boa volta a `ao vivo`.
- **Degradação:** KPI cuja fonte está fora mostra `indisponível` em `--faint`; série ganha lacuna; o resto da tela segue. BankCore fora desabilita o botão de simulação com o motivo ao lado ("BankCore indisponível").
- **Botão:** `idle`, `finished`, `stopped` → **Iniciar simulação**; `seeding`, `running` → **Parar**. Durante a requisição, o botão fica desabilitado com a mesma largura. `409 SIMULATION_RUNNING` não é erro visível: o próximo `stats` corrige a fase. Outro erro → mensagem curta abaixo da barra por 5s.
- **Fase** visível na barra: "Parada", "Semeando contas", "Rodando 0:42", "Concluída em 1:12", "Interrompida" (tempo de `started_at`/`finished_at`).
- **Taxas derivadas** (`recebidas/s`, `429` na corrida) usam Δvalor/Δ`capturadoEm`; restart da API (contador menor que o anterior) zera a referência em vez de gerar taxa negativa.

### 8.9 Linha de base por execução (ajuste A1)
`transfers.settled`, `transfers.failed`, `volume_cents.settled` (BankCore) e `rateLimited` (Liquida) são acumulados do banco ou do processo; `simulation.counters.accepted` é da execução atual. Sem correção, a segunda simulação mostraria "3.000 de 1.500 aceitas".
- **Captura:** quando o `stats` traz um `run_id` diferente do da linha de base atual **e** (a fase é `seeding` **ou** o dashboard já tinha visto outro `run_id`/`null` antes nesta sessão), o dashboard grava a linha de base: `settled`, `failed`, `volume_cents.settled` desse `stats` e o `rateLimited` do último snapshot do Liquida.
- **Exibição:** liquidadas, failed, volume liquidado e 429 da execução = valor atual menos a linha de base, nunca abaixo de 0.
- **Antes da primeira execução** (nenhuma linha de base: fase `idle` ou dashboard aberto no meio/fim de uma corrida que ele não viu começar): mostra o **acumulado**, com o subtítulo "acumulado no banco" no lugar de "de N aceitas". Decisão: o acumulado é dado real e honesto; 0 esconderia o que já está no banco.
- A linha de base vive só em memória da aba; recarregar a página no meio de uma corrida volta ao modo acumulado.
- Teste `vitest`: troca de `run_id` grava nova linha de base e os deltas recomeçam; sem linha de base, os valores são o acumulado.

### 8.7 Movimento (animate, review-animations, animation-performance)
- **Sem animação** em números, KPIs e gráficos: dado funcional atualizado a 1 Hz; animar a cada segundo cansa e mente sobre o instante (remove-animations). uPlot sem transição.
- **Botão:** `transform: scale(0.97)` no `:active`, 120ms `ease-out`; troca Iniciar ↔ Parar com cross-fade de opacidade 150ms.
- **Indicador de fase/fonte:** ponto de 8px; em `ao vivo`/`running` um pulso de opacidade 1 → 0.4 em 2s, `ease-in-out`, infinito. Só `opacity` (composite-only, animation-performance).
- **Mensagem de erro:** entra com `opacity` + `translateY(4px)` 160ms; sai em 120ms.
- `prefers-reduced-motion: reduce`: pulso desligado (ponto estático), sem translate; só cor e opacidade instantâneas.
- Nada de `will-change` permanente; nenhuma propriedade de layout animada.
- Revisão final com `review-animations` e, se houver queda de quadro, `animation-performance`.

### 8.8 Processo de design na implementação
1. `/impeccable shape` (feito nesta spec: brief em §8.2-8.7; decisões abertas respondidas pelo usuário em 2026-10-31: cor única verde Supabase, saída do Angular para React, biblioteca leve de gráficos).
2. Construção seguindo o sheet `true-black-flat` com os ajustes acima.
3. `taste-skill`: `lint.mjs` e `inspect.mjs --app` sobre o build de produção (`vite build && vite preview`), 0 FAIL em 1440, 768 e 390.
4. `impeccable audit` e `impeccable polish`; detector `impeccable detect` nos arquivos alterados.
5. `emil-design-eng` para o passe de detalhes; `review-animations` no movimento.
6. Verificação no navegador com os dois composes no ar (CA5).

## 9. Testes (padrão do repo: xUnit + `WebApplicationFactory`, nomes cenário_resultado)
- `Metrics_Devolve_Recebidas_Apos_Posts_Aceitos`: N `POST /liquidacoes` válidos → `recebidas == N`.
- `Recebidas_Nao_Conta_Requisicao_Invalida`: `POST` inválido (`400`) não incrementa.
- `Recebidas_Nao_Conta_Rate_Limited`: rajada acima de 25 → `recebidas` igual ao número de `202`.
- `MetricsStream_Emite_Evento_Metrics_Por_Segundo`: lê 2 eventos `metrics` do stream, `data` desserializa em `MetricsSnapshot`, intervalo entre eles ~1s; `Content-Type` `text/event-stream`.
- `MetricsStream_Encerra_Ao_Cancelar_Conexao`: cancela o cliente; o endpoint termina sem exceção não tratada.
- `MetricsStream_Responde_Com_Cabecalho_Cors_Do_Dashboard`: `Origin: http://localhost:4200` → `Access-Control-Allow-Origin` presente.
- `MetricsStream_Nao_E_Rate_Limited`: várias conexões simultâneas, nenhuma `429`.
- Dashboard (`vitest`): janela móvel descarta pontos além de 120; taxa derivada de Δcontador/Δt; reset de contador não gera taxa negativa; fonte vira `indisponível` após 3 falhas e volta na primeira resposta boa.

## 10. Critérios de aceitação (delta)
- **CA14** `GET /metrics/stream` emite 1 evento `metrics` por segundo com o mesmo shape do `GET /metrics` e fecha limpo ao cancelar a conexão (teste + log sem erro).
- **CA15** `recebidas` conta exatamente os `202` do `POST /liquidacoes` (testes da §9).
- **CA16** Com os dois composes no ar (BankCore primeiro, depois `docker compose -f docker-compose.yml -f docker-compose.bankcore.yml up`) **sem `.env` com credencial**, o Liquida autentica no BankCore com a credencial semeada.
- **CA17** Clicar em **Iniciar simulação** faz KPIs e gráficos se moverem em tempo real até as **liquidadas da execução** (§8.9) alcançarem `simulation.counters.accepted`, com `pending` e fila em 0, DLQ em 0 e `divergent_accounts` em 0. Vale também na segunda execução seguida, sem limpar o banco.
- **CA18** Recebidas/s sustentado: média em qualquer janela de 30s ≤ 25 mais o erro de amostragem de 1s; rajada acima de 25 limitada à capacidade do token bucket (25, ADR 0001).
- **CA19** Derrubar o BankCore ou a `Liquida.Api` durante a corrida: os KPIs da fonte caída mostram `indisponível`, o resto continua atualizando; a fonte volta sozinha quando sobe de novo.
- **CA20** Dashboard verificado no navegador (desktop 1440, 768 e 390), `inspect.mjs --app` com 0 FAIL, `impeccable detect` sem achados abertos.
- **CA21** `dotnet build` sem avisos, `dotnet test` verde, `npm run build` (inclui `tsc -b`) sem avisos e `npm test` verde em `web/liquida-dashboard`.

## 11. Ordem de execução (2.1.0)
Branch `feature/dashboard-tempo-real` a partir de `origin/main`.
1. **Passo 13** Levar o snapshot: `cherry-pick` de `05330be` como primeiro commit da branch (preserva o estado do dashboard Angular no histórico do PR), depois `chore(dashboard): remove dashboard angular`.
2. **Passo 14** API: `ReceivedCounter` + campo `recebidas` + testes; `GET /metrics/stream` + testes.
3. **Passo 15** Compose zero-touch + `.env.example`.
4. **Passo 16** Dashboard: scaffold React + Vite, tokens, fontes de dados e testes `vitest`, KPIs, painéis, controle de simulação.
5. **Passo 17** Gate de design (§8.8) e fluxo integrado (CA16-CA19); resultado registrado no changelog.

## 12. Decisões tomadas nesta versão
- **D10** Em `Origem=BankCore`, o dashboard ignora `pendentes`/`enviadas` do Liquida; backlog vem do BankCore (§4.3).
- **D11** Push por SSE manual no net8.0, sem pacote novo; `GET /metrics` vira fallback (ADR 0006).
- **D12** Dashboard sai do Angular para React + Vite + TS + SCSS, por decisão do usuário (ADR 0007).
- **D13** Gráficos com uPlot; faixa do pipeline em HTML/SCSS (§8.5).
- **D14** Cor única de dado verde Supabase `#3ECF8E`; resto preto com detalhes em branco puro (§8.2).
- **D16** Linha de base por execução no front; antes da primeira execução observada, acumulado com rótulo (§8.9).
- **D15** A simulação é disparada pelo navegador direto no BankCore; o Liquida não ganha cliente do simulador.

## 12.1 Verificado no BankCore 1.3.0 (ajuste A2)
- `transfers.pending` e `volume_cents.pending` de `GET /simulation/stats` contam só `PENDING` **com lançamentos**, o mesmo conjunto que o Producer lê em `GET /settlement/transfers`. Backlog do dashboard e o que o Producer drena são o mesmo número.
- `PATCH /transfers/{id}/settle` em `PENDING` sem lançamentos responde `409 TRANSFER_NOT_BOOKED`. O `BankCoreSettleClient` já classifica qualquer `409` diferente de sucesso como `Terminal` → DLQ (tabela §4.3 da 2.0.0); nenhuma mudança de código no Liquida.

## 13. Pendências fora deste repo (para o orquestrador)
- `CLAUDE.md` da raiz: atualizado pelo orquestrador para React + Vite + uPlot.
- `README.md` do Liquida: atualização autorizada; entra nesta versão (dashboard novo e credencial semeada).
- BankCore: seed de `SETTLEMENT_CLIENT_ID`/`SECRET` com os mesmos valores dos defaults da §5 e CORS para `http://localhost:4200` em `/simulation/*`.

## 14. Changelog
- **2.1.0 (2026-10-31, Entregue)** Correção de critério por decisão do usuário: o CA18 original (média ≤ 25 em qualquer janela de 5s) era mais rígido que o desenho da ADR 0001, cujo token bucket admite rajada de até 25 além da reposição de 25/s (máximo teórico de 30/s em 5s). O texto novo exige média em 30s ≤ 25 mais o erro de amostragem e rajada limitada à capacidade do bucket. Com os números medidos na execução `6cee89d0` (regime de 23 a 26/s, média máxima em 30s de 25,4), o **CA18 ✅**. Os achados A3 (Producer encerra com a API fora), A4 (Consumer abaixo de 25/s) e A5 (restart da API zera os contadores da execução) viram escopo da 2.2.0. CA14 a CA21 verificados.
- **2.1.0 (2026-10-31, Aprovada, Passo 17: fluxo integrado)** BankCore `feature/simulador` + Liquida com override, dashboard servido do build de produção em 4200 e aberto em Chrome real (headless via DevTools, a extensão do navegador não estava conectada). Execução `6cee89d0`: 1500 despachadas, 1463 aceitas, 37 recusadas por saldo, iniciada 23:33:17Z e concluída 23:33:45Z; backlog e fila zerados às 23:35:37Z.
  - **CA16 ✅** Producer e Consumer obtêm token com `svc_liquida_dev` sem credencial no `.env` (log "Token do BankCore renovado; válido por ~900s").
  - **CA17 ✅** Liquidadas da execução 3042 − 1579 = **1463 = accepted**; failed +37 = rejected_insufficient; `pending` 0, fila 0, DLQ 0, `divergent_accounts` 0. Os 1571 `PENDING` antigos foram drenados antes do clique. Dashboard: "Execução concluída: 1.463 de 1.463 liquidadas. Ledger íntegro."
  - **CA18 ❌ como escrito** Em regime a API recebe 23 a 26/s por segundo; médias máximas: 30s 25,4, 20s 25,8, 10s 27,1, **5s 29,6**. O excesso é a rajada do token bucket (capacidade 25, ADR 0001) após uma pausa do Producer, somada ao ruído de amostragem de 1s; o máximo teórico do desenho em 5s é (25 + 125)/5 = 30. O CA conflita com a ADR 0001; reescrita pendente de decisão (proposta: média sustentada em 30s ≤ 25 + erro de amostragem, rajada limitada à capacidade do bucket).
  - **CA19 ✅** `liquida-api` parada por ~15s durante a corrida: fonte Liquida "indisponível", KPIs dela "indisponível", lacuna nas séries, BankCore seguiu ao vivo; voltou sozinha a "ao vivo" sem recarregar.
  - **CA20 ✅** Navegador com dados reais em 1440, 768 e 390; `inspect.mjs --app` 0 FAIL 0 WARN, `lint.mjs` 0 FAIL, `impeccable detect` sem achados. Correções feitas no olhar: valores e detalhes de KPI truncados (colunas de liquidadas e volume mais largas, detalhes encurtados, escala menor para valores longos abaixo de 1100px).
  - **CA21 ✅** reverificado após as correções.
  - **Achados fora do escopo da 2.1.0 (para nova versão):** A3 o Producer encerra o processo quando a API fica fora além dos retries do Polly (`HttpRequestException` não tratada em `ProducerWorker.ExecuteAsync`, `restart: "no"`); foi religado à mão às 23:34:46Z para concluir o CA17. A4 o Consumer liquida cerca de 10 a 17/s contra 25/s recebidas; a fila cresce durante a corrida e a latência p95 chega a ~110s. A5 restart da API zera `recebidas`/`rateLimited`; o "429 da execução" passa a 0 até a próxima execução (limitação do contador em memória, ADR 0004).
- **2.1.0 (2026-10-31, Aprovada, Passos 13 a 16 entregues)** Branch `feature/dashboard-tempo-real` com `05330be` levado e o dashboard Angular removido. API: `ReceivedCounter`, `recebidas` no snapshot e `GET /metrics/stream` (SSE manual, net8.0). Compose com credencial semeada como default e `.env.example` atualizado. Dashboard React 19.3 + Vite 8.3 + uPlot 1.6.32, sheet true-black-flat com verde `#3ECF8E`. Verificação: `dotnet build` 0 avisos, `dotnet test` 33/33, `npm run build` sem avisos, `npm test` 9/9. Gate de design (§8.8) no estado com as duas fontes fora: `lint.mjs` 0 FAIL (1 WARN defensável: `padStart` em duração m:ss), `inspect.mjs --app` 0 FAIL em 1440/768/390 (1 WARN `no-data-hue`, esperado sem dados), `impeccable detect` sem achados; primeira tela cabe em 1440×900. Vazio de 85px no painel de vazão e dos rótulos de KPI em duas linhas corrigidos após o primeiro olhar. Pendente: fluxo integrado (CA16 a CA19) e verificação visual com dados reais, junto com o orquestrador.
- **2.1.0 (2026-10-31, Aprovada)** Aprovada pelo usuário via orquestrador com dois ajustes: A1 linha de base por execução no front (§8.9, D16, CA17 reescrito) e A2 semântica de `pending` do BankCore 1.3.0 e `409 TRANSFER_NOT_BOOKED` (§12.1). `CLAUDE.md` da raiz atualizado pelo orquestrador; `README.md` autorizado.
- **2.1.0 (2026-10-31, Draft)** SSE de métricas, contador `recebidas`, integração zero-touch e dashboard de tempo real refeito do zero em React + Vite + uPlot, com direção visual true-black-flat e cor única verde Supabase. Aguardando aprovação.
