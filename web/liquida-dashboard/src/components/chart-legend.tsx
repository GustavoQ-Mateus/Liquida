export interface LegendEntry {
  label: string;
  colorToken: string;
  value: string;
}

interface ChartLegendProps {
  entries: LegendEntry[];
}

export function ChartLegend({ entries }: ChartLegendProps) {
  return (
    <ul className="legend">
      {entries.map((entry) => (
        <li key={entry.label} className="legend__item">
          <span className="legend__swatch" style={{ background: `var(${entry.colorToken})` }} aria-hidden="true" />
          {entry.label} <span className="legend__value">{entry.value}</span>
        </li>
      ))}
    </ul>
  );
}
