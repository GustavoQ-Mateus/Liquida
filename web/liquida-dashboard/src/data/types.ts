export interface MetricsSnapshot {
  capturadoEm: string;
  pendentes: number;
  enviadas: number;
  liquidadas: number;
  rpsLiquidacao: number;
  fila: number | null;
  dlq: number | null;
  recebidas: number;
  rateLimited: number;
}

export type SimulationPhase = 'idle' | 'seeding' | 'running' | 'finished' | 'stopped';

export interface SimulationStatus {
  phase: SimulationPhase;
  run_id: string | null;
  started_at: string | null;
  finished_at: string | null;
  config: { accounts: number; transfers: number; rate: number; workers: number };
  counters: {
    dispatched: number;
    accepted: number;
    rejected_insufficient: number;
    conflict_exhausted: number;
    errors: number;
  };
}

export interface SimulationStats {
  captured_at: string;
  transfers: { pending: number; settled: number; failed: number };
  volume_cents: { pending: number; settled: number };
  throughput: { created_per_sec: number; settled_per_sec: number };
  settlement_latency_ms: { p50: number; p95: number };
  ledger: { accounts: number; divergent_accounts: number };
  version_conflicts: number;
  simulation: SimulationStatus;
}

export type SourceState = 'conectando' | 'ao-vivo' | 'reconectando' | 'indisponivel';

export interface SourceHealth {
  state: SourceState;
  failures: number;
}

export interface TimelinePoint {
  createdPerSec: number | null;
  receivedPerSec: number | null;
  settledPerSec: number | null;
  pending: number | null;
  queue: number | null;
  latencyP50: number | null;
  latencyP95: number | null;
}
