import { useEffect, useRef } from 'react';
import uPlot, { type AlignedData, type Options } from 'uplot';
import 'uplot/dist/uPlot.min.css';
import { WINDOW_SECONDS } from '../config';

export interface ChartSeries {
  label: string;
  colorToken: string;
  values: (number | null)[];
}

export interface ReferenceLine {
  value: number;
  label: string;
}

interface TimeSeriesChartProps {
  series: ChartSeries[];
  height: number;
  yFloor: number;
  headroom: number;
  reference?: ReferenceLine;
  formatValue: (value: number) => string;
  ariaLabel: string;
}

const X_SPLITS = [-90, -60, -30, 0];
const NICE_STEPS = [1, 2, 5, 10];
const Y_INTERVALS = 4;
const AXIS_FONT = '12px Inter, system-ui, sans-serif';
const Y_AXIS_SIZE = 56;
const LINE_WIDTH = 2;
const DASH = [6, 4];
const LABEL_OFFSET = 6;

export function TimeSeriesChart(props: TimeSeriesChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const plotRef = useRef<uPlot | null>(null);
  const propsRef = useRef(props);
  propsRef.current = props;
  const seriesKey = props.series.map((item) => `${item.label}:${item.colorToken}`).join('|');

  useEffect(() => {
    const container = containerRef.current;
    if (container === null) {
      return;
    }
    const plot = new uPlot(buildOptions(propsRef, container.clientWidth), toAligned(propsRef.current.series), container);
    plotRef.current = plot;
    const observer = new ResizeObserver(() => plot.setSize({ width: container.clientWidth, height: propsRef.current.height }));
    observer.observe(container);
    return () => {
      observer.disconnect();
      plot.destroy();
      plotRef.current = null;
    };
  }, [seriesKey]);

  useEffect(() => {
    plotRef.current?.setData(toAligned(props.series));
  }, [props.series]);

  return <div className="time-series-chart" ref={containerRef} role="img" aria-label={props.ariaLabel} />;
}

function buildOptions(propsRef: { current: TimeSeriesChartProps }, width: number): Options {
  const faint = cssVar('--faint');
  const grid = cssVar('--grid');
  const { series, height } = propsRef.current;
  return {
    width,
    height,
    legend: { show: false },
    cursor: { show: false },
    scales: {
      x: { time: false, range: [-WINDOW_SECONDS, 0] },
      y: {
        range: (_plot, _min, max) => {
          const { yFloor, headroom } = propsRef.current;
          return [0, niceScale(Math.max(yFloor, (max ?? 0) * headroom)).top];
        },
      },
    },
    axes: [
      {
        stroke: faint,
        font: AXIS_FONT,
        grid: { show: false },
        ticks: { show: false },
        splits: () => X_SPLITS,
        values: (_plot, splits) => splits.map((value) => (value === 0 ? 'agora' : `${value}s`)),
      },
      {
        stroke: faint,
        font: AXIS_FONT,
        size: Y_AXIS_SIZE,
        grid: { stroke: grid, width: 1 },
        ticks: { show: false },
        splits: (plot) => ySplits(plot.scales.y.max ?? 1),
        values: (_plot, splits) => splits.map((value) => propsRef.current.formatValue(value)),
      },
    ],
    series: [
      {},
      ...series.map((item) => ({ label: item.label, stroke: cssVar(item.colorToken), width: LINE_WIDTH, spanGaps: false })),
    ],
    hooks: { draw: [(plot: uPlot) => drawReference(plot, propsRef.current.reference)] },
  };
}

function drawReference(plot: uPlot, reference: ReferenceLine | undefined): void {
  if (reference === undefined) {
    return;
  }
  const ratio = window.devicePixelRatio || 1;
  const y = Math.round(plot.valToPos(reference.value, 'y', true));
  const { ctx, bbox } = plot;
  ctx.save();
  ctx.strokeStyle = cssVar('--reference');
  ctx.lineWidth = ratio;
  ctx.setLineDash(DASH.map((segment) => segment * ratio));
  ctx.beginPath();
  ctx.moveTo(bbox.left, y);
  ctx.lineTo(bbox.left + bbox.width, y);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.fillStyle = cssVar('--muted');
  ctx.font = `${12 * ratio}px Inter, system-ui, sans-serif`;
  ctx.fillText(reference.label, bbox.left + LABEL_OFFSET * ratio, y - LABEL_OFFSET * ratio);
  ctx.restore();
}

function toAligned(series: ChartSeries[]): AlignedData {
  const length = series[0]?.values.length ?? 0;
  const x = Array.from({ length }, (_, index) => index - (length - 1));
  return [x, ...series.map((item) => item.values)];
}

function niceScale(target: number): { top: number; step: number } {
  const raw = Math.max(target, 1) / Y_INTERVALS;
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const step = (NICE_STEPS.find((candidate) => candidate * magnitude >= raw) ?? 10) * magnitude;
  return { top: Math.ceil(target / step) * step, step };
}

function ySplits(top: number): number[] {
  const { step } = niceScale(top);
  return Array.from({ length: Math.round(top / step) + 1 }, (_, index) => index * step);
}

function cssVar(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}
