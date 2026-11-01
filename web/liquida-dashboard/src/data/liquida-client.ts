import { LIQUIDA_API_URL, RESET_TIMEOUT_MS } from '../config';
import type { ResetFailure } from './clean-sequence';

interface LiquidaError {
  error?: string;
  message?: string;
}

export async function resetLiquida(): Promise<ResetFailure | null> {
  const response = await fetch(`${LIQUIDA_API_URL}/simulacao/reset`, {
    method: 'POST',
    signal: AbortSignal.timeout(RESET_TIMEOUT_MS),
  });
  if (response.ok) {
    return null;
  }
  const body = (await response.json().catch(() => ({}))) as LiquidaError;
  return { status: response.status, code: body.error ?? null, message: body.message ?? null };
}
