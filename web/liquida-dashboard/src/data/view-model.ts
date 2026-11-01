import { RATE_CEILING } from '../config';
import { formatCents, formatDuration, formatInteger, formatLatency } from './format';
import type { RunTotals } from './series';
import type { MetricsSnapshot, SimulationPhase, SimulationStats, SourceState } from './types';

export const UNAVAILABLE = 'indisponível';

const CONNECTING = 'Conectando';

export type Tone = 'ink' | 'data' | 'negative' | 'faint';

export interface Kpi {
  id: string;
  label: string;
  value: string;
  unit: string | null;
  detail: string;
  tone: Tone;
  lead: boolean;
}

export interface DashboardInputs {
  stats: SimulationStats | null;
  snapshot: MetricsSnapshot | null;
  run: RunTotals | null;
  receivedPerSec: number | null;
  bankcoreState: SourceState;
}

export interface PipelineStage {
  id: string;
  label: string;
  value: string;
  tone: Tone;
}

export interface StatusShare {
  settled: number;
  pending: number;
  failed: number;
}

const PHASE_LABELS: Record<SimulationPhase, string> = {
  idle: 'Parada',
  seeding: 'Semeando contas',
  running: 'Rodando',
  finished: 'Concluída em',
  stopped: 'Interrompida',
};

export function isActive(phase: SimulationPhase): boolean {
  return phase === 'seeding' || phase === 'running';
}

export function phaseLabel({ stats, bankcoreState }: DashboardInputs, now: number): string {
  if (stats === null) {
    return bankcoreState === 'conectando' ? CONNECTING : 'Fase indisponível';
  }
  const { phase, started_at: startedAt, finished_at: finishedAt } = stats.simulation;
  const label = PHASE_LABELS[phase];
  if ((phase === 'running' || phase === 'finished') && startedAt !== null) {
    const end = finishedAt !== null ? Date.parse(finishedAt) : now;
    return `${label} ${formatDuration(end - Date.parse(startedAt))}`;
  }
  return label;
}

export function buildKpis(inputs: DashboardInputs): Kpi[] {
  return [
    settledKpi(inputs),
    volumeKpi(inputs),
    receivedKpi(inputs),
    latencyKpi(inputs.stats),
    backlogKpi(inputs),
    dlqKpi(inputs.snapshot),
    ledgerKpi(inputs.stats),
    conflictsKpi(inputs.stats),
  ];
}

export function buildFinding(inputs: DashboardInputs): string {
  const { stats, run, snapshot } = inputs;
  if (stats === null || run === null) {
    return inputs.bankcoreState === 'conectando'
      ? 'Conectando ao BankCore e ao Liquida.'
      : 'BankCore indisponível: a simulação não pode ser iniciada nem acompanhada.';
  }
  const ledger = ledgerPhrase(stats);
  if (!run.scoped) {
    const transfers = formatInteger(stats.simulation.config.transfers);
    return `Nenhuma execução acompanhada nesta aba. Iniciar dispara ${transfers} transferências no BankCore. ${ledger}.`;
  }
  const settled = `${formatInteger(run.settled)} de ${formatInteger(stats.simulation.counters.accepted)} liquidadas`;
  const total = backlog(stats, snapshot) ?? stats.transfers.pending;
  if (stats.simulation.phase === 'finished' && total === 0) {
    return `Execução concluída: ${settled}. ${ledger}.`;
  }
  const received =
    inputs.receivedPerSec === null
      ? 'Liquida indisponível'
      : `${formatInteger(inputs.receivedPerSec)}/s de ${RATE_CEILING} recebidas`;
  return `${settled} · backlog ${formatInteger(total)} · ${received} · ${ledger}`;
}

export function buildPipeline(inputs: DashboardInputs): PipelineStage[] {
  const { stats, snapshot, run, receivedPerSec } = inputs;
  return [
    stage('pending', 'Pendentes no BankCore', stats ? formatInteger(stats.transfers.pending) : null),
    stage('gate', `Recebidas pela API, teto ${RATE_CEILING}/s`, receivedPerSec === null ? null : `${formatInteger(receivedPerSec)}/s`),
    stage('queue', 'Na fila', snapshot?.fila == null ? null : formatInteger(snapshot.fila)),
    stage('settled', run?.scoped ? 'Liquidadas na execução' : 'Liquidadas no banco', run ? formatInteger(run.settled) : null, 'data'),
  ];
}

export function buildExits(inputs: DashboardInputs): PipelineStage[] {
  const { snapshot, run } = inputs;
  const rejected = run?.rateLimited ?? null;
  const dlq = snapshot?.dlq ?? null;
  return [
    stage('rate-limited', run?.scoped ? 'Recusadas com 429 na execução' : 'Recusadas com 429', rejected === null ? null : formatInteger(rejected)),
    stage('dlq', 'Na DLQ', dlq === null ? null : formatInteger(dlq), dlq !== null && dlq > 0 ? 'negative' : 'ink'),
  ];
}

export function buildStatusShare(inputs: DashboardInputs): StatusShare | null {
  if (inputs.stats === null || inputs.run === null) {
    return null;
  }
  return { settled: inputs.run.settled, pending: inputs.stats.transfers.pending, failed: inputs.run.failed };
}

function stage(id: string, label: string, value: string | null, tone: Tone = 'ink'): PipelineStage {
  return value === null ? { id, label, value: UNAVAILABLE, tone: 'faint' } : { id, label, value, tone };
}

function settledKpi({ stats, run }: DashboardInputs): Kpi {
  if (stats === null || run === null) {
    return unavailable('settled', 'Liquidadas', true);
  }
  const detail = run.scoped ? `de ${formatInteger(stats.simulation.counters.accepted)} aceitas` : 'acumulado no banco';
  return kpi('settled', 'Liquidadas', formatInteger(run.settled), detail, { tone: 'data', lead: true });
}

function volumeKpi({ run }: DashboardInputs): Kpi {
  if (run === null) {
    return unavailable('volume', 'Volume liquidado');
  }
  const detail = run.scoped ? 'nesta execução' : 'acumulado no banco';
  return kpi('volume', 'Volume liquidado', formatCents(run.volumeSettledCents), detail);
}

function receivedKpi({ receivedPerSec, run }: DashboardInputs): Kpi {
  if (receivedPerSec === null) {
    return unavailable('received', 'Recebidas/s');
  }
  const rejected = run?.rateLimited ?? null;
  const detail = rejected === null ? '429 indisponível' : `${formatInteger(rejected)} com 429`;
  return kpi('received', 'Recebidas/s', formatInteger(receivedPerSec), detail, { unit: `/${RATE_CEILING}` });
}

function latencyKpi(stats: SimulationStats | null): Kpi {
  if (stats === null) {
    return unavailable('latency', 'Latência p95');
  }
  const { p50, p95 } = stats.settlement_latency_ms;
  return kpi('latency', 'Latência p95', formatLatency(p95), `p50 ${formatLatency(p50)}`);
}

function backlogKpi({ stats, snapshot }: DashboardInputs): Kpi {
  const total = backlog(stats, snapshot);
  if (total === null) {
    return unavailable('backlog', 'Backlog');
  }
  const pending = stats ? formatInteger(stats.transfers.pending) : UNAVAILABLE;
  const queue = snapshot?.fila == null ? UNAVAILABLE : formatInteger(snapshot.fila);
  return kpi('backlog', 'Backlog', formatInteger(total), `BankCore ${pending} · fila ${queue}`);
}

function dlqKpi(snapshot: MetricsSnapshot | null): Kpi {
  if (snapshot?.dlq == null) {
    return unavailable('dlq', 'DLQ');
  }
  const retained = snapshot.dlq > 0;
  const detail = retained ? 'para reconciliar' : 'nenhuma retida';
  return kpi('dlq', 'DLQ', formatInteger(snapshot.dlq), detail, { tone: retained ? 'negative' : 'ink' });
}

function ledgerKpi(stats: SimulationStats | null): Kpi {
  if (stats === null) {
    return unavailable('ledger', 'Ledger');
  }
  const { accounts, divergent_accounts: divergent } = stats.ledger;
  const value = divergent === 0 ? 'Íntegro' : `${formatInteger(divergent)} divergentes`;
  const detail = `${formatInteger(accounts)} contas`;
  return kpi('ledger', 'Ledger', value, detail, { tone: divergent === 0 ? 'ink' : 'negative' });
}

function conflictsKpi(stats: SimulationStats | null): Kpi {
  if (stats === null) {
    return unavailable('conflicts', 'Colisões de lock');
  }
  const exhausted = stats.simulation.counters.conflict_exhausted;
  const detail = exhausted === 0 ? 'nenhuma esgotou' : `${formatInteger(exhausted)} esgotaram`;
  const tone = exhausted === 0 ? 'ink' : 'negative';
  return kpi('conflicts', 'Colisões de lock', formatInteger(stats.version_conflicts), detail, { tone });
}

function kpi(
  id: string,
  label: string,
  value: string,
  detail: string,
  options: { tone?: Tone; lead?: boolean; unit?: string } = {},
): Kpi {
  return { id, label, value, detail, unit: options.unit ?? null, tone: options.tone ?? 'ink', lead: options.lead ?? false };
}

function unavailable(id: string, label: string, lead = false): Kpi {
  return kpi(id, label, UNAVAILABLE, 'fonte fora do ar', { tone: 'faint', lead });
}

function backlog(stats: SimulationStats | null, snapshot: MetricsSnapshot | null): number | null {
  const pending = stats?.transfers.pending ?? null;
  const queue = snapshot?.fila ?? null;
  if (pending === null && queue === null) {
    return null;
  }
  return (pending ?? 0) + (queue ?? 0);
}

function ledgerPhrase(stats: SimulationStats): string {
  const divergent = stats.ledger.divergent_accounts;
  return divergent === 0 ? 'Ledger íntegro' : `Ledger com ${formatInteger(divergent)} contas divergentes`;
}
