import { describe, expect, it } from 'vitest';
import { formatLatency } from './format';
import { buildTooltip } from './tooltip';

const SERIES = [
  { label: 'p95', colorToken: '--data-2', values: [820, 1460, null] },
  { label: 'p50', colorToken: '--data-3', values: [300, null, 610] },
];

describe('buildTooltip', () => {
  it('monta o tempo relativo e uma linha por série na ordem da legenda', () => {
    const content = buildTooltip(SERIES, 0, formatLatency);

    expect(content?.time).toBe('-2s');
    expect(content?.rows.map((row) => [row.label, row.value])).toEqual([
      ['p95', '820 ms'],
      ['p50', '300 ms'],
    ]);
  });

  it('mostra indisponível quando a série não tem dado no instante, não zero', () => {
    const content = buildTooltip(SERIES, 1, formatLatency);

    expect(content?.rows[1]).toEqual({ label: 'p50', colorToken: '--data-3', value: 'indisponível', available: false });
    expect(content?.rows[0].value).toBe('1,5 s');
  });

  it('o último ponto é agora', () => {
    expect(buildTooltip(SERIES, 2, formatLatency)?.time).toBe('agora');
  });

  it('índice fora da série não gera tooltip', () => {
    expect(buildTooltip(SERIES, 3, formatLatency)).toBeNull();
    expect(buildTooltip(SERIES, -1, formatLatency)).toBeNull();
  });
});
