import { useMemo } from 'react';
import type { TimelinePoint } from '../data/types';
import { ChartPanel } from './chart-panel';

interface BacklogPanelProps {
  timeline: TimelinePoint[];
}

const PLOT_HEIGHT = 156;
const Y_FLOOR = 10;

export function BacklogPanel({ timeline }: BacklogPanelProps) {
  const series = useMemo(
    () => [
      { label: 'pendentes no BankCore', colorToken: '--data-2', values: timeline.map((point) => point.pending) },
      { label: 'na fila', colorToken: '--data-3', values: timeline.map((point) => point.queue) },
    ],
    [timeline],
  );
  return <ChartPanel title="Backlog" className="backlog-panel" series={series} height={PLOT_HEIGHT} yFloor={Y_FLOOR} />;
}
