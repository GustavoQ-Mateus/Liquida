import { FAILURES_UNTIL_UNAVAILABLE } from '../config';
import type { SimulationStats, SourceHealth } from './types';

export interface CounterSample {
  value: number;
  at: number;
}

export interface RunBaseline {
  runId: string;
  settled: number;
  failed: number;
  volumeSettledCents: number;
  rateLimited: number | null;
}

export interface BaselineState {
  lastSeenRunId: string | null | undefined;
  baseline: RunBaseline | null;
}

export interface RunTotals {
  scoped: boolean;
  settled: number;
  failed: number;
  volumeSettledCents: number;
  rateLimited: number | null;
}

export const INITIAL_BASELINE: BaselineState = { lastSeenRunId: undefined, baseline: null };

export function pushWindow<T>(window: readonly T[], point: T, size: number): T[] {
  const next = [...window, point];
  return next.length > size ? next.slice(next.length - size) : next;
}

export function ratePerSecond(previous: CounterSample | null, current: CounterSample): number | null {
  if (previous === null) {
    return null;
  }
  const elapsedSeconds = (current.at - previous.at) / 1000;
  if (elapsedSeconds <= 0 || current.value < previous.value) {
    return null;
  }
  return (current.value - previous.value) / elapsedSeconds;
}

export function nextHealth(previous: SourceHealth, succeeded: boolean): SourceHealth {
  if (succeeded) {
    return { state: 'ao-vivo', failures: 0 };
  }
  const failures = previous.failures + 1;
  if (failures >= FAILURES_UNTIL_UNAVAILABLE) {
    return { state: 'indisponivel', failures };
  }
  return { state: previous.state === 'conectando' ? 'conectando' : 'reconectando', failures };
}

export function advanceBaseline(
  state: BaselineState,
  stats: SimulationStats,
  rateLimited: number | null,
): BaselineState {
  const runId = stats.simulation.run_id;
  if (runId === null || runId === state.baseline?.runId) {
    return { ...state, lastSeenRunId: runId };
  }
  const sawRunStart = stats.simulation.phase === 'seeding';
  const sawRunChange = state.lastSeenRunId !== undefined && state.lastSeenRunId !== runId;
  if (!sawRunStart && !sawRunChange) {
    return { ...state, lastSeenRunId: runId };
  }
  return {
    lastSeenRunId: runId,
    baseline: {
      runId,
      settled: stats.transfers.settled,
      failed: stats.transfers.failed,
      volumeSettledCents: stats.volume_cents.settled,
      rateLimited,
    },
  };
}

export function runTotals(stats: SimulationStats, baseline: RunBaseline | null, rateLimited: number | null): RunTotals {
  const scoped = baseline !== null && baseline.runId === stats.simulation.run_id;
  if (!scoped || baseline === null) {
    return {
      scoped: false,
      settled: stats.transfers.settled,
      failed: stats.transfers.failed,
      volumeSettledCents: stats.volume_cents.settled,
      rateLimited,
    };
  }
  return {
    scoped: true,
    settled: sinceBaseline(stats.transfers.settled, baseline.settled),
    failed: sinceBaseline(stats.transfers.failed, baseline.failed),
    volumeSettledCents: sinceBaseline(stats.volume_cents.settled, baseline.volumeSettledCents),
    rateLimited:
      rateLimited === null || baseline.rateLimited === null ? null : sinceBaseline(rateLimited, baseline.rateLimited),
  };
}

function sinceBaseline(current: number, base: number): number {
  return Math.max(0, current - base);
}

export interface MonotonicCounter {
  total: number;
  last: number | null;
}

export const INITIAL_COUNTER: MonotonicCounter = { total: 0, last: null };

export function accumulate(counter: MonotonicCounter, current: number): MonotonicCounter {
  if (counter.last === null) {
    return { total: current, last: current };
  }
  const delta = current >= counter.last ? current - counter.last : current;
  return { total: counter.total + delta, last: current };
}
