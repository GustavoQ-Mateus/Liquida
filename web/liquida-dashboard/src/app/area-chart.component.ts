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
  selector: 'app-area-chart',
  standalone: true,
  template: `<div #host class="host"></div>`,
  styles: [':host,.host{display:block;width:100%;min-width:0;}'],
})
export class AreaChart implements AfterViewInit, OnDestroy {
  readonly data = input<number[]>([]);
  readonly labels = input<string[]>([]);
  readonly color = input('#16794a');
  readonly height = input(44);
  readonly axis = input(false);
  readonly currency = input(false);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private chart?: ApexCharts;
  private ready = false;

  constructor() {
    effect(() => {
      const data = this.data();
      const labels = this.labels();
      if (this.ready && this.chart) {
        this.chart.updateOptions(
          { series: [{ data: [...data] }], xaxis: { categories: [...labels] } },
          false,
          true,
        );
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

  private options(): ApexCharts.ApexOptions {
    const withAxis = this.axis();
    const hasLabels = this.labels().length > 0;
    const brl = (v: number) =>
      v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    return {
      chart: {
        type: 'area',
        height: this.height(),
        fontFamily: 'Inter, system-ui, sans-serif',
        sparkline: { enabled: !withAxis },
        toolbar: { show: false },
        zoom: { enabled: false },
        animations: {
          enabled: true,
          easing: 'easeinout',
          speed: 500,
          dynamicAnimation: { enabled: true, speed: 400 },
        },
      },
      series: [{ name: 'valor', data: [...this.data()] }],
      colors: [this.color()],
      stroke: { curve: 'smooth', width: withAxis ? 2.5 : 2 },
      markers: { size: 0, hover: { size: withAxis ? 5 : 0 } },
      fill: {
        type: 'gradient',
        gradient: { shadeIntensity: 1, opacityFrom: 0.32, opacityTo: 0.02, stops: [0, 100] },
      },
      dataLabels: { enabled: false },
      grid: withAxis
        ? { borderColor: '#ededee', strokeDashArray: 4, padding: { left: 8, right: 8 } }
        : { show: false, padding: { left: 0, right: 0, top: 0, bottom: 0 } },
      xaxis: {
        categories: [...this.labels()],
        labels: { show: withAxis && hasLabels, style: { colors: '#a8a8ae', fontSize: '10px' } },
        axisBorder: { show: false },
        axisTicks: { show: false },
        tooltip: { enabled: false },
      },
      yaxis: {
        show: withAxis,
        labels: {
          style: { colors: '#a8a8ae', fontSize: '11px' },
          formatter: this.currency() ? (v: number) => this.compact(v) : undefined,
        },
      },
      tooltip: withAxis
        ? {
            theme: 'light',
            x: { show: hasLabels },
            y: { formatter: this.currency() ? brl : undefined },
          }
        : { enabled: false },
    };
  }

  private compact(v: number): string {
    if (v >= 1000) {
      return `${(v / 1000).toFixed(0)}k`;
    }
    return `${Math.round(v)}`;
  }
}
