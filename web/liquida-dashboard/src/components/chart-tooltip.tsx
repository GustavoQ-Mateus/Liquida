import { useLayoutEffect, useRef } from 'react';
import type { TooltipContent } from '../data/tooltip';
import './chart-tooltip.scss';

export interface TooltipPosition {
  left: number;
  top: number;
  flipped: boolean;
}

interface ChartTooltipProps {
  content: TooltipContent;
  position: TooltipPosition;
}

const CURSOR_GAP = 12;

export function ChartTooltip({ content, position }: ChartTooltipProps) {
  const tooltipRef = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    const tooltip = tooltipRef.current;
    const area = tooltip?.parentElement;
    if (!tooltip || !area) {
      return;
    }
    const width = tooltip.offsetWidth;
    const preferred = position.flipped ? position.left - CURSOR_GAP - width : position.left + CURSOR_GAP;
    tooltip.style.left = `${Math.min(Math.max(0, preferred), area.clientWidth - width)}px`;
  });

  return (
    <div ref={tooltipRef} className="chart-tooltip" style={{ top: position.top }} role="status">
      <span className="chart-tooltip__time">{content.time}</span>
      <ul className="chart-tooltip__rows">
        {content.rows.map((row) => (
          <li key={row.label} className="chart-tooltip__row">
            <span className="legend__swatch" style={{ background: `var(${row.colorToken})` }} aria-hidden="true" />
            <span className="chart-tooltip__label">{row.label}</span>
            <span className={`chart-tooltip__value ${row.available ? '' : 'tone-faint'}`}>{row.value}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
