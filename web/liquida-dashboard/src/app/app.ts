import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ApiService } from './api.service';
import { RPS_ALVO } from './config';

const MAX_HISTORICO = 60;

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly api = inject(ApiService);

  readonly RPS_ALVO = RPS_ALVO;

  readonly metrics = toSignal(this.api.metricsStream(), { initialValue: null });
  readonly liquidacoes = toSignal(this.api.liquidacoesRecentesStream(), { initialValue: [] });
  readonly transacoes = toSignal(this.api.transacoesRecentesStream(), { initialValue: [] });

  readonly online = computed(() => this.metrics() !== null);
  readonly rps = computed(() => this.metrics()?.rpsLiquidacao ?? 0);
  readonly rpsPct = computed(() => Math.min(100, Math.round((this.rps() / RPS_ALVO) * 100)));

  // Histórico deslizante de rps para a sparkline — prova visual dos 25 rps.
  readonly historico = signal<number[]>([]);
  readonly sparkPath = computed(() => this.pontosSparkline(this.historico()));

  private ultimoInstante: string | null = null;

  constructor() {
    effect(() => {
      const m = this.metrics();
      if (!m || m.capturadoEm === this.ultimoInstante) {
        return;
      }
      this.ultimoInstante = m.capturadoEm;
      this.historico.update((h) => [...h, m.rpsLiquidacao].slice(-MAX_HISTORICO));
    });
  }

  // ---- helpers de apresentação ----

  shortId(id: string): string {
    return id ? id.slice(0, 8) : '—';
  }

  valor(v: number | null | undefined): string {
    if (v === null || v === undefined) {
      return '—';
    }
    return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }

  numero(v: number | null | undefined): string {
    return v === null || v === undefined ? '—' : v.toLocaleString('pt-BR');
  }

  hora(iso: string): string {
    if (!iso) {
      return '—';
    }
    const d = new Date(iso);
    return d.toLocaleTimeString('pt-BR', { hour12: false });
  }

  private pontosSparkline(valores: number[]): string {
    if (valores.length < 2) {
      return '';
    }
    const w = 100;
    const h = 28;
    const max = Math.max(RPS_ALVO, ...valores);
    const step = w / (MAX_HISTORICO - 1);
    return valores
      .map((v, i) => {
        const x = i * step;
        const y = h - (v / max) * h;
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  }
}
