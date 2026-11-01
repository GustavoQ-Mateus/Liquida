import { useCallback, useEffect, useRef, useState } from 'react';
import { ERROR_VISIBLE_MS, TICK_MS, WINDOW_SECONDS } from '../config';
import { BankCoreError, SIMULATION_RUNNING, fetchStats, startSimulation, stopSimulation } from './bankcore-client';
import { connectLiquida, type LiquidaTransport } from './liquida-stream';
import {
  INITIAL_BASELINE,
  advanceBaseline,
  nextHealth,
  pushWindow,
  ratePerSecond,
  runTotals,
  type BaselineState,
  type CounterSample,
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
  error: string | null;
  start: () => void;
  stop: () => void;
}

export function useDashboard(): DashboardState {
  const [snapshot, setSnapshot] = useState<MetricsSnapshot | null>(null);
  const [stats, setStats] = useState<SimulationStats | null>(null);
  const [liquida, setLiquida] = useState<SourceHealth>(CONNECTING);
  const [liquidaTransport, setLiquidaTransport] = useState<LiquidaTransport>('sse');
  const [bankcore, setBankcore] = useState<SourceHealth>(CONNECTING);
  const [receivedPerSec, setReceivedPerSec] = useState<number | null>(null);
  const [baseline, setBaseline] = useState<BaselineState>(INITIAL_BASELINE);
  const [timeline, setTimeline] = useState<TimelinePoint[]>([]);
  const [now, setNow] = useState(() => Date.now());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const latest = useRef({ snapshot, stats, liquida, bankcore, receivedPerSec });
  latest.current = { snapshot, stats, liquida, bankcore, receivedPerSec };

  useEffect(() => {
    let previous: CounterSample | null = null;
    return connectLiquida({
      onSnapshot: (next) => {
        const sample = { value: next.recebidas, at: Date.parse(next.capturadoEm) };
        const rate = ratePerSecond(previous, sample);
        previous = sample;
        setSnapshot(next);
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
        setBaseline((state) => advanceBaseline(state, next, latest.current.snapshot?.rateLimited ?? null));
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

  const act = useCallback(async (action: () => Promise<SimulationStatus>) => {
    setBusy(true);
    try {
      const status = await action();
      setStats((current) => (current === null ? current : { ...current, simulation: status }));
    } catch (failure) {
      if (!(failure instanceof BankCoreError && failure.code === SIMULATION_RUNNING)) {
        setError(failure instanceof Error ? failure.message : 'Falha ao falar com o BankCore.');
      }
    } finally {
      setBusy(false);
    }
  }, []);

  const liquidaUp = liquida.state === 'ao-vivo';
  const bankcoreUp = bankcore.state === 'ao-vivo' && stats !== null;
  const rateLimited = liquidaUp ? (snapshot?.rateLimited ?? null) : null;

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
    error,
    start: () => void act(startSimulation),
    stop: () => void act(stopSimulation),
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
