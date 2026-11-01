import type { SimulationPhase } from '../data/types';
import { isActive } from '../data/view-model';

interface SimulationControlProps {
  phase: SimulationPhase | null;
  busy: boolean;
  onStart: () => void;
  onStop: () => void;
}

export function SimulationControl({ phase, busy, onStart, onStop }: SimulationControlProps) {
  const running = phase !== null && isActive(phase);
  const disabled = busy || phase === null;
  return (
    <button
      type="button"
      className={`simulation-control ${running ? 'simulation-control--stop' : 'simulation-control--start'}`}
      disabled={disabled}
      aria-busy={busy}
      onClick={running ? onStop : onStart}
    >
      <span className={`simulation-control__label ${running ? '' : 'is-visible'}`}>Iniciar simulação</span>
      <span className={`simulation-control__label ${running ? 'is-visible' : ''}`}>Parar simulação</span>
    </button>
  );
}
