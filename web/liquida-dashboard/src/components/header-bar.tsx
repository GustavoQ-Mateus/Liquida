import type { LiquidaTransport } from '../data/liquida-stream';
import type { SimulationPhase, SourceHealth } from '../data/types';
import type { ScenarioState } from '../data/use-scenario';
import { isActive } from '../data/view-model';
import { CleanControl } from './clean-control';
import { ScenarioControl } from './scenario-control';
import { SimulationControl } from './simulation-control';
import { SourceStatus } from './source-status';
import './header-bar.scss';

interface HeaderBarProps {
  liquida: SourceHealth;
  liquidaTransport: LiquidaTransport;
  bankcore: SourceHealth;
  phase: SimulationPhase | null;
  phaseText: string;
  busy: boolean;
  cleaning: boolean;
  scenario: ScenarioState;
  onStart: () => void;
  onStop: () => void;
  onClean: () => void;
}

export function HeaderBar(props: HeaderBarProps) {
  const running = props.phase !== null && isActive(props.phase);
  const transportNote = props.liquida.state === 'ao-vivo' && props.liquidaTransport === 'polling' ? 'polling' : undefined;
  const idleControlsDisabled = running || props.busy || props.phase === null;
  return (
    <header className="header-bar">
      <div className="header-bar__brand">
        <svg className="header-bar__mark" viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="5" cy="10" r="3" />
          <rect x="8" y="9" width="5" height="2" />
          <circle cx="15" cy="10" r="3" />
        </svg>
        <span className="header-bar__name">Liquida</span>
        <span className="header-bar__scope">com BankCore</span>
      </div>
      <div className="header-bar__sources">
        <SourceStatus name="Liquida" health={props.liquida} note={transportNote} />
        <SourceStatus name="BankCore" health={props.bankcore} />
      </div>
      <div className="header-bar__simulation">
        <span className={`phase ${running || props.cleaning ? 'phase--running' : ''}`}>
          <span className="status-dot" aria-hidden="true" />
          <span aria-live="polite">{props.phaseText}</span>
        </span>
        <ScenarioControl scenario={props.scenario} disabled={idleControlsDisabled} />
        <div className="header-bar__actions">
          <SimulationControl
            phase={props.phase}
            busy={props.busy}
            startBlocked={!props.scenario.valid}
            onStart={props.onStart}
            onStop={props.onStop}
          />
          <CleanControl
            disabled={idleControlsDisabled}
            disabledReason={props.phase === null ? 'BankCore indisponível' : 'Pare a simulação para limpar'}
            onConfirm={props.onClean}
          />
        </div>
      </div>
    </header>
  );
}
