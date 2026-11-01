# Liquida, Especificação Técnica

| Campo | Valor |
|---|---|
| **Versão** | 2.1.1 |
| **Status** | Entregue (2026-10-31). Pedido explícito do usuário; CA22 a CA27 verificados |
| **Data** | 2026-10-31 |
| **Autor** | Gustavo Queiroz Mateus |
| **Domínio** | Liquidação/compensação de transações de pagamento |
| **Base** | Estende `spec-v2.1.0.md` (entregue). Arquivo novo porque a 2.1.0 já está entregue e spec entregue não é reescrita |

> Versionamento **SemVer**. Esta é uma **PATCH**: detalhe de interação no dashboard, sem mudança de API, contrato, dependência ou arquitetura.

---

## 1. Objetivo
Nos três gráficos de série temporal (vazão, backlog, latência), ao passar o mouse ou tocar, mostrar o valor de cada série naquele instante.

## 2. Comportamento
- **Cursor do uPlot** (sem dependência nova): linha vertical de 1px em `--muted` no instante sob o ponteiro e um ponto de 8px em cada série, na cor da série. Série sem dado no instante não ganha ponto.
- **Tooltip** pequeno sobre o gráfico, fundo `--raised`, texto `--ink`, raio de 8px, sem sombra:
  - primeira linha: tempo relativo, no mesmo rótulo do eixo X ("-42s" ou "agora");
  - uma linha por série, na ordem da legenda, com o ponto de cor da série, o nome e o valor em `tabular-nums`;
  - formatação igual à dos KPIs: vazão em inteiro com "/s", backlog em inteiro, latência em ms ou s;
  - valor `null` aparece como "indisponível" em `--faint`, nunca como zero;
  - a linha do teto 25/s não entra no tooltip.
- **Posição:** à direita do cursor; passando de 60% da largura do plot, vira para a esquerda. Fica sempre dentro do painel.
- **Fora dos dados:** com a janela ainda incompleta, o cursor numa região sem pontos não mostra tooltip (o uPlot pularia para o ponto mais próximo e o tempo exibido não bateria com a linha); o tooltip só aparece a até 1s de um ponto real.
- **Movimento:** nenhum. O tooltip aparece e some de imediato (dado funcional, `remove-animations` e `emil-design-eng`).
- **Atualização a 1 Hz:** com o ponteiro parado, o tooltip mostra o valor atualizado do índice sob o cursor a cada novo ponto (a série desliza sob o cursor). Largura mínima e números tabulares evitam salto de layout.
- **Saída:** some ao sair do gráfico com o mouse.
- **Toque:** tocar no gráfico posiciona o cursor e mostra o tooltip daquele ponto; tocar fora do gráfico esconde.

## 3. Estrutura
- `data/tooltip.ts`: função pura que monta o conteúdo (tempo relativo e linhas) a partir das séries, do índice e do formatador.
- `components/chart-tooltip.tsx` e `.scss`: renderização.
- `time-series-chart.tsx`: cursor e hook `setCursor` do uPlot; `chart-panel.tsx` recebe o formatador do ponto.

## 4. Critérios de aceitação
- **CA22** Nos três painéis, passar o mouse mostra a linha vertical, os pontos e o tooltip com tempo relativo e o valor de cada série na formatação dos KPIs; o teto não aparece.
- **CA23** Valor ausente aparece como "indisponível", não como 0 (teste `vitest`).
- **CA24** Perto da borda direita o tooltip vira para a esquerda e não sai do painel.
- **CA25** Com o ponteiro parado, o tooltip acompanha a atualização de 1 Hz sem salto de layout; some ao sair do gráfico.
- **CA26** Em 390px, tocar mostra o tooltip e tocar fora esconde.
- **CA27** `npm run build` sem avisos e `npm test` verde; verificado no navegador com dados reais.

## 5. Changelog
- **2.1.1 (2026-10-31, Entregue)** Verificado no build de produção em Chrome real (headless via DevTools) com dados de uma simulação nova:
  - **CA22 ✅** vazão mostra "-4s, criadas 0/s, recebidas 25/s, liquidadas 14/s"; latência mostra "p95 35,7 s, p50 25,5 s"; backlog mostra inteiros; o teto não entra.
  - **CA23 ✅** `null` aparece como "indisponível" em `--faint` (visto no navegador e em 4 testes `vitest`).
  - **CA24 ✅** a 97% da largura o tooltip vira para a esquerda (652 a 853px num painel de 29 a 936px); em 390px a posição é presa ao painel por medição.
  - **CA25 ✅** com o cursor parado durante a corrida, os valores mudaram a cada segundo (17, 18, 17, 15/s) com posição idêntica (533 a 720px); some ao sair do gráfico.
  - **CA26 ✅** em 390px com toque: tocar mostra o tooltip dentro do painel (60 a 284px num painel de 16 a 374px) e tocar fora esconde.
  - **CA27 ✅** `npm run build` sem avisos, `npm test` 13/13, `lint.mjs` 0 FAIL, `impeccable detect` sem achados.
  - Ajustes feitos no olhar: espaço entre rótulo e valor no tooltip; tooltip suprimido fora dos dados.
- **2.1.1 (2026-10-31, Aprovada)** Valor do ponto ao passar o mouse nos gráficos, pedido explícito do usuário antes do PR da 2.1.0.
