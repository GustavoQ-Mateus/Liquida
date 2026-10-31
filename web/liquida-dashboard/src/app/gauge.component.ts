import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  input,
  viewChild,
} from '@angular/core';
import ApexCharts from 'apexcharts';

@Component({
  selector: 'app-gauge',
  standalone: true,
  template: `<div #host class="host"></div>`,
  styles: [':host,.host{display:block;width:100%;}'],
})
export class Gauge implements AfterViewInit, OnDestroy {
  readonly value = input(0);
  readonly max = input(100);
  readonly unit = input('');
  readonly color = input('#16794a');
  readonly height = input(240);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private chart?: ApexCharts;
  private ready = false;
  private display = 0;

  constructor() {
    effect(() => {
      const v = this.value();
      this.display = v;
      if (this.ready && this.chart) {
        this.chart.updateSeries([this.pct(v)], true);
      }
    });
  }

  async ngAfterViewInit(): Promise<void> {
    this.chart = new ApexCharts(this.host().nativeElement, this.options());
    await this.chart.render();
    this.ready = true;
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }

  private pct(v: number): number {
    const max = this.max() || 1;
    return Math.min(100, Math.round((v / max) * 100));
  }

  private options(): ApexCharts.ApexOptions {
    return {
      chart: {
        type: 'radialBar',
        height: this.height(),
        fontFamily: 'Inter, system-ui, sans-serif',
        animations: { enabled: true, easing: 'easeinout', speed: 600 },
        sparkline: { enabled: true },
      },
      series: [this.pct(this.value())],
      colors: [this.color()],
      plotOptions: {
        radialBar: {
          startAngle: -135,
          endAngle: 135,
          hollow: { size: '62%' },
          track: { background: '#eef0ef', strokeWidth: '100%', margin: 4 },
          dataLabels: {
            name: { offsetY: 22, color: '#79797f', fontSize: '13px', fontWeight: 500 },
            value: {
              offsetY: -18,
              color: '#101012',
              fontSize: '40px',
              fontWeight: 700,
              formatter: () => `${Math.round(this.display)}`,
            },
          },
        },
      },
      stroke: { lineCap: 'round' },
      fill: {
        type: 'gradient',
        gradient: {
          shade: 'dark',
          type: 'horizontal',
          gradientToColors: ['#37c483'],
          stops: [0, 100],
        },
      },
      labels: [this.unit()],
    };
  }
}
