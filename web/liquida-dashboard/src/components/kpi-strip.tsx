import type { Kpi } from '../data/view-model';
import './kpi-strip.scss';

const LONG_VALUE_CHARS = 11;

interface KpiStripProps {
  kpis: Kpi[];
}

export function KpiStrip({ kpis }: KpiStripProps) {
  return (
    <section className="panel kpi-strip" aria-label="Indicadores">
      {kpis.map((kpi) => (
        <div key={kpi.id} className={`kpi ${kpi.lead ? 'kpi--lead' : ''}`}>
          <span className="kpi__label">{kpi.label}</span>
          <span className={`kpi__value tone-${kpi.tone} ${kpi.value.length > LONG_VALUE_CHARS ? 'kpi__value--long' : ''}`}>
            {kpi.value}
            {kpi.unit ? <span className="kpi__unit">{kpi.unit}</span> : null}
          </span>
          <span className="kpi__detail">{kpi.detail}</span>
        </div>
      ))}
    </section>
  );
}
