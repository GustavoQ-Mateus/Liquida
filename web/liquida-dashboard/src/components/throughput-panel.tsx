import { useMemo } from 'react';
import { RATE_CEILING } from '../config';
import { formatInteger } from '../data/format';
import type { TimelinePoint } from '../data/types';
import { ChartPanel } from './chart-panel';

interface ThroughputPanelProps {
  timeline: TimelinePoint[];
}

const PLOT_HEIGHT = 258;
const Y_FLOOR = 30;
const CEILING = { value: RATE_CEILING, label: `teto ${RATE_CEILING}/s` };
const formatRate = (value: number) => `${formatInteger(value)}/s`;

export function ThroughputPanel({ timeline }: ThroughputPanelProps) {
  const series = useMemo(
    () => [
      { label: 'criadas/s no BankCore', colorToken: '--data-2', values: timeline.map((point) => point.createdPerSec) },
      { label: 'recebidas/s pela API', colorToken: '--data-3', values: timeline.map((point) => point.receivedPerSec) },
      { label: 'liquidadas/s', colorToken: '--data', values: timeline.map((point) => point.settledPerSec) },
    ],
    [timeline],
  );
  return <ChartPanel title="Vazão por segundo" className="throughput-panel" series={series} height={PLOT_HEIGHT} yFloor={Y_FLOOR} reference={CEILING} formatPoint={formatRate} />;
}
