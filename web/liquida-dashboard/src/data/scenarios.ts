import { formatInteger } from './format';

export type ScenarioId = 'tranquilo' | 'padrao' | 'estresse' | 'personalizado';

export interface SimulationConfig {
  accounts: number;
  transfers: number;
  rate: number;
  workers: number;
}

export type ConfigField = keyof SimulationConfig;

export interface Scenario {
  id: ScenarioId;
  label: string;
  story: string;
}

export interface FieldLimit {
  field: ConfigField;
  label: string;
  min: number;
  max: number;
}

export type ConfigErrors = Partial<Record<ConfigField, string>>;

export const DEFAULT_SCENARIO: ScenarioId = 'padrao';

export const SCENARIOS: Scenario[] = [
  { id: 'tranquilo', label: 'Tranquilo', story: 'Abaixo do teto: tudo é liquidado na hora, sem fila.' },
  { id: 'padrao', label: 'Padrão', story: 'Acima do teto: a fila cresce e drena a 25/s.' },
  { id: 'estresse', label: 'Estresse', story: 'Muitas colisões de lock, fila grande e 429.' },
  { id: 'personalizado', label: 'Personalizado', story: 'Os números que você escolher.' },
];

export const PRESETS: Record<Exclude<ScenarioId, 'personalizado'>, SimulationConfig> = {
  tranquilo: { accounts: 40, transfers: 500, rate: 20, workers: 8 },
  padrao: { accounts: 40, transfers: 1500, rate: 60, workers: 16 },
  estresse: { accounts: 15, transfers: 5000, rate: 200, workers: 32 },
};

export const FIELD_LIMITS: FieldLimit[] = [
  { field: 'accounts', label: 'Contas', min: 2, max: 500 },
  { field: 'transfers', label: 'Transferências', min: 1, max: 100000 },
  { field: 'rate', label: 'Taxa por segundo', min: 1, max: 500 },
  { field: 'workers', label: 'Workers', min: 1, max: 64 },
];

export function scenarioById(id: ScenarioId): Scenario {
  return SCENARIOS.find((scenario) => scenario.id === id) ?? SCENARIOS[1];
}

export function limitText(limit: FieldLimit): string {
  return `${formatInteger(limit.min)} a ${formatInteger(limit.max)}`;
}

export function validateConfig(config: SimulationConfig): ConfigErrors {
  const errors: ConfigErrors = {};
  for (const limit of FIELD_LIMITS) {
    const value = config[limit.field];
    if (!Number.isInteger(value) || value < limit.min || value > limit.max) {
      errors[limit.field] = `Use um inteiro de ${limitText(limit)}.`;
    }
  }
  return errors;
}

export function configFor(id: ScenarioId, custom: SimulationConfig): SimulationConfig {
  return id === 'personalizado' ? custom : PRESETS[id];
}

export function scenarioSummary(id: ScenarioId, custom: SimulationConfig): string {
  const scenario = scenarioById(id);
  const config = configFor(id, custom);
  return `Cenário ${scenario.label}: ${formatInteger(config.transfers)} transferências a ${formatInteger(config.rate)}/s. ${scenario.story}`;
}
