import { BANKCORE_API_URL, REQUEST_TIMEOUT_MS } from '../config';
import type { SimulationStats, SimulationStatus } from './types';

export const SIMULATION_RUNNING = 'SIMULATION_RUNNING';

interface ErrorEnvelope {
  error?: { code?: string; message?: string };
}

export class BankCoreError extends Error {
  readonly code: string | null;

  constructor(code: string | null, message: string) {
    super(message);
    this.code = code;
  }
}

export function fetchStats(): Promise<SimulationStats> {
  return request<SimulationStats>('/simulation/stats', 'GET');
}

export function startSimulation(): Promise<SimulationStatus> {
  return request<SimulationStatus>('/simulation/start', 'POST');
}

export function stopSimulation(): Promise<SimulationStatus> {
  return request<SimulationStatus>('/simulation/stop', 'POST');
}

async function request<T>(path: string, method: 'GET' | 'POST'): Promise<T> {
  const response = await fetch(`${BANKCORE_API_URL}${path}`, {
    method,
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  if (response.ok) {
    return (await response.json()) as T;
  }
  const envelope = (await response.json().catch(() => ({}))) as ErrorEnvelope;
  throw new BankCoreError(envelope.error?.code ?? null, envelope.error?.message ?? `HTTP ${response.status}`);
}
