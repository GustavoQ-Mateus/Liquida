import { BANKCORE_API_URL, REQUEST_TIMEOUT_MS } from '../config';
import type { ResetFailure } from './clean-sequence';
import type { SimulationConfig } from './scenarios';
import type { SimulationStats, SimulationStatus } from './types';

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

class BankCoreRequestError extends BankCoreError {
  readonly status: number;

  constructor(status: number, code: string | null, message: string) {
    super(code, message);
    this.status = status;
  }
}

export function fetchStats(): Promise<SimulationStats> {
  return request<SimulationStats>('/simulation/stats', 'GET');
}

export function startSimulation(config: SimulationConfig): Promise<SimulationStatus> {
  return request<SimulationStatus>('/simulation/start', 'POST', config);
}

export function stopSimulation(): Promise<SimulationStatus> {
  return request<SimulationStatus>('/simulation/stop', 'POST');
}

export async function resetBankCore(): Promise<ResetFailure | null> {
  try {
    await request<SimulationStatus>('/simulation/reset', 'POST');
    return null;
  } catch (failure) {
    if (failure instanceof BankCoreRequestError) {
      return { status: failure.status, code: failure.code, message: failure.message };
    }
    throw failure;
  }
}

async function request<T>(path: string, method: 'GET' | 'POST', body?: SimulationConfig): Promise<T> {
  const response = await fetch(`${BANKCORE_API_URL}${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  if (response.ok) {
    return (await response.json()) as T;
  }
  const envelope = (await response.json().catch(() => ({}))) as ErrorEnvelope;
  throw new BankCoreRequestError(
    response.status,
    envelope.error?.code ?? null,
    envelope.error?.message ?? `HTTP ${response.status}`,
  );
}
