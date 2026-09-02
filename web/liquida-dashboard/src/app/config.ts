// Base da Liquida.Api. Pode ser sobrescrita em runtime sem rebuild:
//   <script>window.LIQUIDA_API_BASE = 'http://host:8080'</script> no index.html.
declare global {
  interface Window { LIQUIDA_API_BASE?: string; }
}

export const API_BASE_URL: string =
  (typeof window !== 'undefined' && window.LIQUIDA_API_BASE) || 'http://localhost:8080';

// Intervalo de polling do dashboard (ms). Coerente com a janela de rpsLiquidacao (1s). RNF11.
export const POLL_INTERVAL_MS = 1000;

// Alvo de vazão que o pipeline prova (25 rps). Usado nas barras/indicadores.
export const RPS_ALVO = 25;
