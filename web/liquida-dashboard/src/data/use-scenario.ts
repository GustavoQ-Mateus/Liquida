import { useState } from 'react';
import {
  DEFAULT_SCENARIO,
  PRESETS,
  configFor,
  validateConfig,
  type ConfigErrors,
  type ConfigField,
  type ScenarioId,
  type SimulationConfig,
} from './scenarios';

export interface ScenarioState {
  scenarioId: ScenarioId;
  custom: SimulationConfig;
  cleanFirst: boolean;
  config: SimulationConfig;
  errors: ConfigErrors;
  valid: boolean;
  select: (id: ScenarioId) => void;
  setField: (field: ConfigField, value: number) => void;
  setCleanFirst: (value: boolean) => void;
}

export function useScenario(): ScenarioState {
  const [scenarioId, setScenarioId] = useState<ScenarioId>(DEFAULT_SCENARIO);
  const [custom, setCustom] = useState<SimulationConfig>(PRESETS.padrao);
  const [cleanFirst, setCleanFirst] = useState(true);
  const config = configFor(scenarioId, custom);
  const errors = scenarioId === 'personalizado' ? validateConfig(custom) : {};
  return {
    scenarioId,
    custom,
    cleanFirst,
    config,
    errors,
    valid: Object.keys(errors).length === 0,
    select: setScenarioId,
    setField: (field, value) => setCustom((current) => ({ ...current, [field]: value })),
    setCleanFirst,
  };
}
