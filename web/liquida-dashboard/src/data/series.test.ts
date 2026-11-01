import { describe, expect, it } from 'vitest';
import {
  INITIAL_BASELINE,
  INITIAL_COUNTER,
  accumulate,
  advanceBaseline,
  nextHealth,
  pushWindow,
  ratePerSecond,
  runTotals,
} from './series';
import type { SimulationPhase, SimulationStats } from './types';

function stats(runId: string | null, phase: SimulationPhase, settled: number, volume: number): SimulationStats {
  return {
    captured_at: '2026-10-31T12:00:00Z',
    transfers: { pending: 10, settled, failed: 1 },
    volume_cents: { pending: 0, settled: volume },
    throughput: { created_per_sec: 0, settled_per_sec: 0 },
    settlement_latency_ms: { p50: 0, p95: 0 },
    ledger: { accounts: 40, divergent_accounts: 0 },
    version_conflicts: 0,
    simulation: {
      phase,
      run_id: runId,
      started_at: null,
      finished_at: null,
      config: { accounts: 40, transfers: 1500, rate: 60, workers: 16 },
      counters: { dispatched: 0, accepted: 1500, rejected_insufficient: 0, conflict_exhausted: 0, errors: 0 },
    },
  };
}

describe('pushWindow', () => {
  it('descarta os pontos mais antigos além do tamanho da janela', () => {
    const window = Array.from({ length: 121 }, (_, index) => index);

    const next = pushWindow(window, 121, 121);

    expect(next).toHaveLength(121);
    expect(next[0]).toBe(1);
    expect(next.at(-1)).toBe(121);
  });
});

describe('ratePerSecond', () => {
  it('divide o delta do contador pelo tempo decorrido', () => {
    expect(ratePerSecond({ value: 100, at: 0 }, { value: 125, at: 1000 })).toBe(25);
  });

  it('não gera taxa negativa quando o contador zera no restart da api', () => {
    expect(ratePerSecond({ value: 500, at: 0 }, { value: 3, at: 1000 })).toBeNull();
  });

  it('sem amostra anterior não há taxa', () => {
    expect(ratePerSecond(null, { value: 10, at: 1000 })).toBeNull();
  });
});

describe('nextHealth', () => {
  it('fica indisponível após três falhas seguidas e volta na primeira resposta boa', () => {
    let health = nextHealth({ state: 'ao-vivo', failures: 0 }, false);
    health = nextHealth(health, false);
    expect(health.state).toBe('reconectando');

    health = nextHealth(health, false);
    expect(health.state).toBe('indisponivel');

    expect(nextHealth(health, true)).toEqual({ state: 'ao-vivo', failures: 0 });
  });
});

describe('linha de base por execução', () => {
  it('sem linha de base mostra o acumulado do banco', () => {
    const state = advanceBaseline(INITIAL_BASELINE, stats('run-1', 'running', 3000, 900000), 40);

    const totals = runTotals(stats('run-1', 'running', 3000, 900000), state.baseline, 40);

    expect(state.baseline).toBeNull();
    expect(totals).toMatchObject({ scoped: false, settled: 3000, volumeSettledCents: 900000, rateLimited: 40 });
  });

  it('troca de run_id grava nova linha de base e os deltas recomeçam', () => {
    let state = advanceBaseline(INITIAL_BASELINE, stats('run-1', 'finished', 1500, 450000), 10);
    state = advanceBaseline(state, stats('run-2', 'running', 1500, 450000), 10);

    const totals = runTotals(stats('run-2', 'running', 1620, 486000), state.baseline, 14);

    expect(state.baseline?.runId).toBe('run-2');
    expect(totals).toMatchObject({ scoped: true, settled: 120, failed: 0, volumeSettledCents: 36000, rateLimited: 4 });
  });

  it('fase seeding na primeira leitura já grava a linha de base', () => {
    const state = advanceBaseline(INITIAL_BASELINE, stats('run-1', 'seeding', 1500, 450000), 0);

    expect(state.baseline).toMatchObject({ runId: 'run-1', settled: 1500 });
  });

  it('a mesma execução mantém a linha de base', () => {
    let state = advanceBaseline(INITIAL_BASELINE, stats('run-1', 'seeding', 0, 0), 0);
    state = advanceBaseline(state, stats('run-1', 'running', 700, 210000), 3);

    expect(state.baseline).toMatchObject({ runId: 'run-1', settled: 0, rateLimited: 0 });
  });
});

describe('acumulador monotônico de 429', () => {
  it('soma o crescimento e não perde o acumulado quando o contador cai', () => {
    let counter = accumulate(INITIAL_COUNTER, 10);
    counter = accumulate(counter, 14);
    counter = accumulate(counter, 3);
    counter = accumulate(counter, 5);

    expect(counter.total).toBe(19);
  });

  it('linha de base com o acumulador mantém o 429 da execução após restart da api', () => {
    const baseline = { runId: 'run-1', settled: 0, failed: 0, volumeSettledCents: 0, rateLimited: 10 };
    let counter = accumulate(INITIAL_COUNTER, 10);
    counter = accumulate(counter, 16);
    counter = accumulate(counter, 2);

    const totals = runTotals(stats('run-1', 'running', 0, 0), baseline, counter.total);

    expect(totals.rateLimited).toBe(8);
  });
});
