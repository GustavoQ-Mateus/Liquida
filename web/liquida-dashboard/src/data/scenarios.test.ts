import { describe, expect, it } from 'vitest';
import { PRESETS, configFor, scenarioSummary, validateConfig } from './scenarios';

describe('cenários', () => {
  it('cada preset envia o body combinado com o BankCore', () => {
    expect(configFor('tranquilo', PRESETS.padrao)).toEqual({ accounts: 40, transfers: 500, rate: 20, workers: 8 });
    expect(configFor('padrao', PRESETS.padrao)).toEqual({ accounts: 40, transfers: 1500, rate: 60, workers: 16 });
    expect(configFor('estresse', PRESETS.padrao)).toEqual({ accounts: 15, transfers: 5000, rate: 200, workers: 32 });
  });

  it('personalizado envia os campos do formulário', () => {
    const custom = { accounts: 3, transfers: 10, rate: 5, workers: 2 };

    expect(configFor('personalizado', custom)).toBe(custom);
  });

  it('aceita os limites inclusivos do BankCore', () => {
    expect(validateConfig({ accounts: 2, transfers: 1, rate: 1, workers: 1 })).toEqual({});
    expect(validateConfig({ accounts: 500, transfers: 100000, rate: 500, workers: 64 })).toEqual({});
  });

  it('recusa valor fora do limite, vazio ou fracionário', () => {
    const errors = validateConfig({ accounts: 1, transfers: 100001, rate: Number.NaN, workers: 2.5 });

    expect(Object.keys(errors).sort()).toEqual(['accounts', 'rate', 'transfers', 'workers']);
    expect(errors.accounts).toBe('Use um inteiro de 2 a 500.');
  });

  it('resume o cenário com a história que ele conta', () => {
    expect(scenarioSummary('padrao', PRESETS.padrao)).toBe(
      'Cenário Padrão: 1.500 transferências a 60/s. Acima do teto: a fila cresce e drena a 25/s.',
    );
  });
});
