import { formatInteger } from '../data/format';
import type { StatusShare } from '../data/view-model';
import './status-bar.scss';

interface StatusBarProps {
  share: StatusShare | null;
}

export function StatusBar({ share }: StatusBarProps) {
  const total = share === null ? 0 : share.settled + share.pending + share.failed;
  if (share === null || total === 0) {
    return <div className="status-bar status-bar--empty" aria-label="Sem transferências para dividir por status" />;
  }
  const parts = [
    { id: 'settled', label: 'liquidadas', value: share.settled },
    { id: 'pending', label: 'pendentes', value: share.pending },
    { id: 'failed', label: 'falhas', value: share.failed },
  ];
  return (
    <div className="status-bar-block">
      <div className="status-bar" role="img" aria-label={parts.map((part) => `${part.value} ${part.label}`).join(', ')}>
        {parts.filter((part) => part.value > 0).map((part) => (
          <span key={part.id} className={`status-bar__part status-bar__part--${part.id}`} style={{ flexGrow: part.value }} />
        ))}
      </div>
      <ul className="legend">
        {parts.map((part) => (
          <li key={part.id} className="legend__item">
            <span className={`legend__swatch legend__swatch--${part.id}`} aria-hidden="true" />
            {part.label} <span className="legend__value">{formatInteger(part.value)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
