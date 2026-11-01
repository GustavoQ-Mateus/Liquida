# ADR 0007: Dashboard em React + Vite + uPlot

- **Status:** Aceita (spec-v2.1.0)
- **Data:** 2026-10-31
- **Substitui:** a escolha de Angular + SCSS para `web/liquida-dashboard` feita na spec-v1.1.0.
- **Contexto:** O dashboard da 1.1.0 (Angular 20, ApexCharts, lucide-angular) será excluído e refeito do zero como a única interface do conjunto BankCore + Liquida: fundo preto, faixa de KPIs, grade de gráficos de série temporal e um botão que inicia a simulação. Na definição da spec-v2.1.0 o usuário pediu para sair do Angular e escolheu React + Vite + TypeScript, com uma biblioteca leve de gráficos.

## Decisão
- **React 19 + Vite + TypeScript estrito + SCSS** em `web/liquida-dashboard`, dev server na porta 4200.
- **uPlot** para os gráficos de série temporal; composições que não são plot (faixa do pipeline, barra de status) em HTML/SCSS.
- **Vitest** para as funções puras de dados (janela móvel, taxas derivadas, estado das fontes).
- Nenhuma outra dependência de runtime: sem biblioteca de animação, ícones, UI kit, roteador ou gerenciador de estado. Estado em hooks; movimento em CSS.

## Justificativa
- **Decisão do usuário** para o front; o dashboard não é contrato de nenhum outro sistema, então a troca não afeta API nem BankCore.
- **Tamanho proporcional ao problema:** uma tela, sem rotas nem formulários. Vite + React entrega isso com pouco boilerplate; o Angular trazia zone.js, router e forms sem uso.
- **uPlot** é feito para séries temporais atualizadas em tempo real: canvas, ~50 KB, `setData` sem reconstruir o gráfico, sem animação embutida e com eixos, grade e cores totalmente controláveis pelos tokens. Três painéis de série temporal atualizando a 1 Hz justificam não escrever escalas, ticks e tooltip à mão.
- **Vitest** reaproveita a configuração do Vite; a regra de que mudança de comportamento vem com teste vale também no front.

## Alternativas consideradas
- **Manter Angular (refeito):** descartada pelo usuário.
- **Svelte 5 ou TypeScript puro:** viáveis; React foi a escolha do usuário.
- **SVG próprio:** zero dependência, mas reescreveria escalas, ticks e redesenho eficiente para três gráficos. Preterido.
- **ApexCharts / Chart.js / Recharts:** mais pesados, animam por padrão e têm cromo difícil de calar no estilo true-black-flat. Rejeitadas.

## Consequências
- Comandos de verificação do dashboard mudam: `npm run build` (inclui `tsc -b`) e `npm test`, no lugar de `npx ng build` e `npx tsc -p tsconfig.app.json --noEmit`. O `CLAUDE.md` da raiz e o `README.md` precisam ser atualizados por quem tem esse escopo.
- A regra "TypeScript/Angular" do `CLAUDE.md` passa a valer como TypeScript/React: componente por arquivo em `kebab-case`, sem `any`, dados em hooks fora do JSX, SCSS com tokens em variáveis CSS.
- O histórico do dashboard Angular fica preservado no commit `05330be`, levado para a branch antes da exclusão.
