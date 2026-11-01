import type { SourceHealth } from '../data/types';

interface SourceStatusProps {
  name: string;
  health: SourceHealth;
  note?: string;
}

const STATE_LABELS: Record<SourceHealth['state'], string> = {
  conectando: 'conectando',
  'ao-vivo': 'ao vivo',
  reconectando: 'reconectando',
  indisponivel: 'indisponível',
};

export function SourceStatus({ name, health, note }: SourceStatusProps) {
  return (
    <span className={`source-status source-status--${health.state}`}>
      <span className="status-dot" aria-hidden="true" />
      <span className="source-status__name">{name}</span>
      <span className="source-status__state">
        {STATE_LABELS[health.state]}
        {note ? ` · ${note}` : ''}
      </span>
    </span>
  );
}
