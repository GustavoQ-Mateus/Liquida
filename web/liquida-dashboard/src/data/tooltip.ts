import { UNAVAILABLE } from './view-model';

export interface TooltipSeries {
  label: string;
  colorToken: string;
  values: (number | null)[];
}

export interface TooltipRow {
  label: string;
  colorToken: string;
  value: string;
  available: boolean;
}

export interface TooltipContent {
  time: string;
  rows: TooltipRow[];
}

export function relativeTimeLabel(offsetSeconds: number): string {
  return offsetSeconds === 0 ? 'agora' : `${offsetSeconds}s`;
}

export function buildTooltip(
  series: TooltipSeries[],
  index: number,
  format: (value: number) => string,
): TooltipContent | null {
  const length = series[0]?.values.length ?? 0;
  if (index < 0 || index >= length) {
    return null;
  }
  return {
    time: relativeTimeLabel(index - (length - 1)),
    rows: series.map(({ label, colorToken, values }) => {
      const value = values[index] ?? null;
      return value === null
        ? { label, colorToken, value: UNAVAILABLE, available: false }
        : { label, colorToken, value: format(value), available: true };
    }),
  };
}
