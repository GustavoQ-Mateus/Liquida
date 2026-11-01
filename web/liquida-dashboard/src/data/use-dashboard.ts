import { useCallback, useEffect, useRef, useState } from 'react';
import { ERROR_VISIBLE_MS, TICK_MS, WINDOW_SECONDS } from '../config';
import { BankCoreError, fetchStats, resetBankCore, startSimulation, stopSimulation } from './bankcore-client';
import { SIMULATION_RUNNING, cleanEnvironment } from './clean-sequence';
import { resetLiquida } from './liquida-client';
import { connectLiquida, type LiquidaTransport } from './liquida-stream';
import type { SimulationConfig } from './scenarios';
import {
  INITIAL_BASELINE,
  INITIAL_COUNTER,
  accumulate,
  advanceBaseline,
  nextHealth,
  pushWindow,
  ratePerSecond,
  runTotals,
  type BaselineState,
  type CounterSample,
  type MonotonicCounter,
  type RunTotals,
} from './series';
import type { MetricsSnapshot, SimulationStats, SimulationStatus, SourceHealth, TimelinePoint } from './types';

const WINDOW_POINTS = WINDOW_SECONDS + 1;
const CONNECTING: SourceHealth = { state: 'conectando', failures: 0 };

export interface DashboardState {
  snapshot: MetricsSnapshot | null;
  stats: SimulationStats | null;
  liquida: SourceHealth;
  liquidaTransport: LiquidaTransport;
  bankcore: SourceHealth;
  receivedPerSec: number | null;
  run: RunTotals | null;
  timeline: TimelinePoint[];
  now: number;
  busy: boolean;
  cleaning: boolean;
  error: string | null;
  start: (config: SimulationConfig, cleanFirst: boolean) => void;
  stop: () => void;
  clean: () => void;
}

export function useDashboard(): DashboardState {
  const [snapshot, setSnapshot] = useState<MetricsSnapshot | null>(null);
  const [stats, setStats] = useState<SimulationStats | null>(null);
  const [liquida, setLiquida] = useState<SourceHealth>(CONNECTING);
  const [liquidaTransport, setLiquidaTransport] = useState<LiquidaTransport>('sse');
  const [bankcore, setBankcore] = useState<SourceHealth>(CONNECTING);
  const [receivedPerSec, setReceivedPerSec] = useState<number | null>(null);
  const [rateLimitedTotal, setRateLimitedTotal] = useState<number | null>(null);
  const [baseline, setBaseline] = useState<BaselineState>(INITIAL_BASELINE);
  const [timeline, setTimeline] = useState<TimelinePoint[]>([]);
  const [now, setNow] = useState(() => Date.now());
  const [busy, setBusy] = useState(false);
  const [cleaning, setCleaning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const previousSample = useRef<CounterSample | null>(null);
  const rateLimitedCounter = useRef<MonotonicCounter>(INITIAL_COUNTER);
  const latest = useRef({ snapshot, stats, liquida, bankcore, receivedPerSec, rateLimitedTotal });
  latest.current = { snapshot, stats, liquida, bankcore, receivedPerSec, rateLimitedTotal };

  useEffect(() => {
    return connectLiquida({
      onSnapshot: (next) => {
        const sample = { value: next.recebidas, at: Date.parse(next.capturadoEm) };
        const rate = ratePerSecond(previousSample.current, sample);
        previousSample.current = sample;
        rateLimitedCounter.current = accumulate(rateLimitedCounter.current, next.rateLimited);
        setSnapshot(next);
        setRateLimitedTotal(rateLimitedCounter.current.total);
        if (rate !== null) {
          setReceivedPerSec(rate);
        }
      },
      onHealth: (health, transport) => {
        setLiquida(health);
        setLiquidaTransport(transport);
      },
    });
  }, []);

  useEffect(() => {
    let active = true;
    let timer = 0;
    let health = CONNECTING;
    const poll = async () => {
      const startedAt = Date.now();
      try {
        const next = await fetchStats();
        health = nextHealth(health, true);
        setStats(next);
        setBaseline((state) => advanceBaseline(state, next, latest.current.rateLimitedTotal));
      } catch {
        health = nextHealth(health, false);
      }
      if (!active) {
        return;
      }
      setBankcore(health);
      timer = window.setTimeout(poll, Math.max(0, TICK_MS - (Date.now() - startedAt)));
    };
    void poll();
    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, []);

  useEffect(() => {
    const sampler = window.setInterval(() => {
      setNow(Date.now());
      setTimeline((points) => pushWindow(points, samplePoint(latest.current), WINDOW_POINTS));
    }, TICK_MS);
    return () => window.clearInterval(sampler);
  }, []);

  useEffect(() => {
    if (error === null) {
      return;
    }
    const timer = window.setTimeout(() => setError(null), ERROR_VISIBLE_MS);
    return () => window.clearTimeout(timer);
  }, [error]);

  const forgetHistory = useCallback(() => {
    previousSample.current = null;
    rateLimitedCounter.current = INITIAL_COUNTER;
    setRateLimitedTotal(null);
    setReceivedPerSec(null);
    setBaseline(INITIAL_BASELINE);
    setTimeline([]);
  }, []);

  const runClean = useCallback(async (): Promise<boolean> => {
    setCleaning(true);
    try {
      await cleanEnvironment({ resetBankCore, resetLiquida });
      forgetHistory();
      return true;
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Falha ao limpar o ambiente.');
      return false;
    } finally {
      setCleaning(false);
    }
  }, [forgetHistory]);

  const act = useCallback(async (action: () => Promise<SimulationStatus>) => {
    try {
      const status = await action();
      setStats((current) => (current === null ? current : { ...current, simulation: status }));
    } catch (failure) {
      if (!(failure instanceof BankCoreError && failure.code === SIMULATION_RUNNING)) {
        setError(failure instanceof Error ? failure.message : 'Falha ao falar com o BankCore.');
      }
    }
  }, []);

  const withBusy = useCallback(async (work: () => Promise<void>) => {
    setBusy(true);
    try {
      await work();
    } finally {
      setBusy(false);
    }
  }, []);

  const liquidaUp = liquida.state === 'ao-vivo';
  const bankcoreUp = bankcore.state === 'ao-vivo' && stats !== null;
  const rateLimited = liquidaUp ? rateLimitedTotal : null;

  return {
    snapshot: liquidaUp ? snapshot : null,
    stats: bankcoreUp ? stats : null,
    liquida,
    liquidaTransport,
    bankcore,
    receivedPerSec: liquidaUp ? receivedPerSec : null,
    run: bankcoreUp && stats !== null ? runTotals(stats, baseline.baseline, rateLimited) : null,
    timeline,
    now,
    busy,
    cleaning,
    error,
    start: (config, cleanFirst) =>
      void withBusy(async () => {
        if (cleanFirst && !(await runClean())) {
          return;
        }
        await act(() => startSimulation(config));
      }),
    stop: () => void withBusy(() => act(stopSimulation)),
    clean: () =>
      void withBusy(async () => {
        await runClean();
      }),
  };
}

interface Latest {
  snapshot: MetricsSnapshot | null;
  stats: SimulationStats | null;
  liquida: SourceHealth;
  bankcore: SourceHealth;
  receivedPerSec: number | null;
}

function samplePoint({ snapshot, stats, liquida, bankcore, receivedPerSec }: Latest): TimelinePoint {
  const liquidaSnapshot = liquida.state === 'ao-vivo' ? snapshot : null;
  const bankcoreStats = bankcore.state === 'ao-vivo' ? stats : null;
  return {
    createdPerSec: bankcoreStats?.throughput.created_per_sec ?? null,
    receivedPerSec: liquidaSnapshot === null ? null : receivedPerSec,
    settledPerSec: liquidaSnapshot?.rpsLiquidacao ?? null,
    pending: bankcoreStats?.transfers.pending ?? null,
    queue: liquidaSnapshot?.fila ?? null,
    latencyP50: bankcoreStats?.settlement_latency_ms.p50 ?? null,
    latencyP95: bankcoreStats?.settlement_latency_ms.p95 ?? null,
  };
}
