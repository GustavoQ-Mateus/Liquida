import type { LiquidaTransport } from '../data/liquida-stream';
import type { SimulationPhase, SourceHealth } from '../data/types';
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
  onStart: () => void;
  onStop: () => void;
}

export function HeaderBar(props: HeaderBarProps) {
  const running = props.phase === 'running' || props.phase === 'seeding';
  const transportNote = props.liquida.state === 'ao-vivo' && props.liquidaTransport === 'polling' ? 'polling' : undefined;
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
        <span className={`phase ${running ? 'phase--running' : ''}`}>
          <span className="status-dot" aria-hidden="true" />
          <span aria-live="polite">{props.phaseText}</span>
        </span>
        <SimulationControl phase={props.phase} busy={props.busy} onStart={props.onStart} onStop={props.onStop} />
      </div>
    </header>
  );
}
