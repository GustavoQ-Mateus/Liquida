const INTEGER = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 });
const ONE_DECIMAL = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const CURRENCY = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const CENTS_PER_REAL = 100;
const MS_PER_SECOND = 1000;
const SECONDS_PER_MINUTE = 60;

export function formatInteger(value: number): string {
  return INTEGER.format(Math.round(value));
}

export function formatCents(cents: number): string {
  return CURRENCY.format(cents / CENTS_PER_REAL);
}

export function formatLatency(ms: number): string {
  if (ms < MS_PER_SECOND) {
    return `${formatInteger(ms)} ms`;
  }
  return `${ONE_DECIMAL.format(ms / MS_PER_SECOND)} s`;
}

export function formatDuration(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / MS_PER_SECOND));
  const minutes = Math.floor(totalSeconds / SECONDS_PER_MINUTE);
  const seconds = totalSeconds % SECONDS_PER_MINUTE;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}
