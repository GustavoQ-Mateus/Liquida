import { formatInteger } from '../data/format';
import { UNAVAILABLE } from '../data/view-model';
import { ChartLegend } from './chart-legend';
import { TimeSeriesChart, type ChartSeries, type ReferenceLine } from './time-series-chart';
import './chart-panel.scss';

interface ChartPanelProps {
  title: string;
  className: string;
  series: ChartSeries[];
  height: number;
  yFloor: number;
  reference?: ReferenceLine;
  formatValue?: (value: number) => string;
  formatPoint?: (value: number) => string;
}

const HEADROOM = 1.2;

export function ChartPanel({
  title,
  className,
  series,
  height,
  yFloor,
  reference,
  formatValue = formatInteger,
  formatPoint = formatValue,
}: ChartPanelProps) {
  const entries = series.map((item) => {
    const last = item.values.at(-1) ?? null;
    return { label: item.label, colorToken: item.colorToken, value: last === null ? UNAVAILABLE : formatValue(last) };
  });
  return (
    <section className={`panel chart-panel ${className}`}>
      <div className="chart-panel__header">
        <h2 className="panel__title">{title}</h2>
        <ChartLegend entries={entries} />
      </div>
      <TimeSeriesChart
        series={series}
        height={height}
        yFloor={yFloor}
        headroom={HEADROOM}
        reference={reference}
        formatValue={formatValue}
        formatPoint={formatPoint}
        ariaLabel={`${title}: ${entries.map((entry) => `${entry.label} ${entry.value}`).join(', ')}`}
      />
    </section>
  );
}
