export const SIMULATION_RUNNING = 'SIMULATION_RUNNING';

export class CleanError extends Error {}

export interface ResetFailure {
  status: number;
  code: string | null;
  message: string | null;
}

export interface CleanSteps {
  resetBankCore: () => Promise<ResetFailure | null>;
  resetLiquida: () => Promise<ResetFailure | null>;
}

const HTTP_NOT_FOUND = 404;

export async function cleanEnvironment(steps: CleanSteps): Promise<void> {
  const bankcore = await steps.resetBankCore();
  if (bankcore !== null) {
    throw new CleanError(bankCoreMessage(bankcore));
  }
  const liquida = await steps.resetLiquida();
  if (liquida !== null) {
    throw new CleanError(liquidaMessage(liquida));
  }
}

function bankCoreMessage(failure: ResetFailure): string {
  if (failure.code === SIMULATION_RUNNING) {
    return 'Pare a simulação para limpar.';
  }
  if (failure.status === HTTP_NOT_FOUND) {
    return 'O BankCore no ar não tem limpeza (precisa da 1.4.0).';
  }
  return `Falha ao limpar o BankCore: ${failure.message ?? `HTTP ${failure.status}`}.`;
}

function liquidaMessage(failure: ResetFailure): string {
  if (failure.status === HTTP_NOT_FOUND) {
    return 'A limpeza do Liquida está desligada (Simulacao:ResetHabilitado).';
  }
  if (failure.code === 'reset_nao_estabilizou') {
    return 'Limpeza incompleta: liquidações continuam chegando. Pare a simulação e tente de novo.';
  }
  return `Falha ao limpar o Liquida: ${failure.message ?? `HTTP ${failure.status}`}.`;
}
