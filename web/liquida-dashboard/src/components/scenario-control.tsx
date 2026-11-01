import { SCENARIOS, type ScenarioId } from '../data/scenarios';
import type { ScenarioState } from '../data/use-scenario';
import './scenario-control.scss';

interface ScenarioControlProps {
  scenario: ScenarioState;
  disabled: boolean;
}

export function ScenarioControl({ scenario, disabled }: ScenarioControlProps) {
  return (
    <div className="scenario-control">
      <label className="scenario-control__select">
        <span className="scenario-control__label">Cenário</span>
        <select
          value={scenario.scenarioId}
          disabled={disabled}
          onChange={(event) => scenario.select(event.target.value as ScenarioId)}
        >
          {SCENARIOS.map((item) => (
            <option key={item.id} value={item.id}>
              {item.label}
            </option>
          ))}
        </select>
      </label>
      <label className="scenario-control__check">
        <input
          type="checkbox"
          checked={scenario.cleanFirst}
          disabled={disabled}
          onChange={(event) => scenario.setCleanFirst(event.target.checked)}
        />
        Limpar antes de iniciar
      </label>
    </div>
  );
}
