import { FIELD_LIMITS, limitText } from '../data/scenarios';
import type { ScenarioState } from '../data/use-scenario';
import './custom-scenario-form.scss';

interface CustomScenarioFormProps {
  scenario: ScenarioState;
  disabled: boolean;
}

export function CustomScenarioForm({ scenario, disabled }: CustomScenarioFormProps) {
  return (
    <fieldset className="custom-scenario" disabled={disabled}>
      <legend className="custom-scenario__legend">Cenário personalizado</legend>
      {FIELD_LIMITS.map((limit) => {
        const error = scenario.errors[limit.field];
        const hintId = `limite-${limit.field}`;
        return (
          <label key={limit.field} className={`custom-scenario__field ${error ? 'is-invalid' : ''}`}>
            <span className="custom-scenario__name">{limit.label}</span>
            <input
              type="number"
              inputMode="numeric"
              min={limit.min}
              max={limit.max}
              value={Number.isNaN(scenario.custom[limit.field]) ? '' : scenario.custom[limit.field]}
              aria-invalid={error !== undefined}
              aria-describedby={hintId}
              onChange={(event) => scenario.setField(limit.field, event.target.valueAsNumber)}
            />
            <span id={hintId} className="custom-scenario__hint">
              {error ?? limitText(limit)}
            </span>
          </label>
        );
      })}
    </fieldset>
  );
}
