import { useMemo } from 'react';
import { formatLatency } from '../data/format';
import type { TimelinePoint } from '../data/types';
import { ChartPanel } from './chart-panel';

interface LatencyPanelProps {
  timeline: TimelinePoint[];
}

const PLOT_HEIGHT = 156;
const Y_FLOOR = 1000;

export function LatencyPanel({ timeline }: LatencyPanelProps) {
  const series = useMemo(
    () => [
      { label: 'p95', colorToken: '--data-2', values: timeline.map((point) => point.latencyP95) },
      { label: 'p50', colorToken: '--data-3', values: timeline.map((point) => point.latencyP50) },
    ],
    [timeline],
  );
  return (
    <ChartPanel
      title="Latência de liquidação"
      className="latency-panel"
      series={series}
      height={PLOT_HEIGHT}
      yFloor={Y_FLOOR}
      formatValue={formatLatency}
    />
  );
}
