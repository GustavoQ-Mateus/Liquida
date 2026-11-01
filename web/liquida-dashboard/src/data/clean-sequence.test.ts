import { describe, expect, it } from 'vitest';
import { CleanError, cleanEnvironment, type ResetFailure } from './clean-sequence';

function steps(bankcore: ResetFailure | null, liquida: ResetFailure | null) {
  const calls: string[] = [];
  return {
    calls,
    steps: {
      resetBankCore: async () => {
        calls.push('bankcore');
        return bankcore;
      },
      resetLiquida: async () => {
        calls.push('liquida');
        return liquida;
      },
    },
  };
}

describe('cleanEnvironment', () => {
  it('limpa o BankCore antes do Liquida', async () => {
    const fake = steps(null, null);

    await cleanEnvironment(fake.steps);

    expect(fake.calls).toEqual(['bankcore', 'liquida']);
  });

  it('com a simulação rodando para no BankCore e não toca no Liquida', async () => {
    const fake = steps({ status: 409, code: 'SIMULATION_RUNNING', message: null }, null);

    await expect(cleanEnvironment(fake.steps)).rejects.toThrow('Pare a simulação para limpar.');
    expect(fake.calls).toEqual(['bankcore']);
  });

  it('BankCore sem a rota de reset vira mensagem clara', async () => {
    const fake = steps({ status: 404, code: null, message: null }, null);

    await expect(cleanEnvironment(fake.steps)).rejects.toThrow('O BankCore no ar não tem limpeza (precisa da 1.4.0).');
  });

  it('Liquida que não estabilizou avisa limpeza incompleta', async () => {
    const fake = steps(null, { status: 503, code: 'reset_nao_estabilizou', message: 'x' });

    const failure = await cleanEnvironment(fake.steps).catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(CleanError);
    expect((failure as CleanError).message).toMatch(/^Limpeza incompleta/);
  });
});
