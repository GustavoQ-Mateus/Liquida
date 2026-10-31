import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { LucideAngularModule } from 'lucide-angular';
import { ApiService } from './api.service';
import { RPS_ALVO } from './config';
import { AreaChart } from './area-chart.component';
import { Counter } from './counter.component';
import { Gauge } from './gauge.component';

const MAX_HISTORICO = 60;

interface Historico {
  rps: number[];
  pendentes: number[];
  enviadas: number[];
  liquidadas: number[];
  rateLimited: number[];
  fila: number[];
  dlq: number[];
}

const HISTORICO_VAZIO: Historico = {
  rps: [],
  pendentes: [],
  enviadas: [],
  liquidadas: [],
  rateLimited: [],
  fila: [],
  dlq: [],
};

@Component({
  selector: 'app-root',
  imports: [Counter, AreaChart, Gauge, LucideAngularModule],
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

  readonly historico = signal<Historico>(HISTORICO_VAZIO);

  readonly liquidacoesValores = computed(() => this.liquidacoes().map((l) => l.valor));
  readonly liquidacoesLabels = computed(() => this.liquidacoes().map((l) => this.hora(l.liquidadoEm)));
  readonly transacoesValores = computed(() => this.transacoes().map((t) => t.valor));
  readonly transacoesLabels = computed(() => this.transacoes().map((t) => this.hora(t.criadoEm)));

  private ultimoInstante: string | null = null;

  constructor() {
    effect(() => {
      const m = this.metrics();
      if (!m || m.capturadoEm === this.ultimoInstante) {
        return;
      }
      this.ultimoInstante = m.capturadoEm;
      this.historico.update((h) => ({
        rps: this.push(h.rps, m.rpsLiquidacao),
        pendentes: this.push(h.pendentes, m.pendentes),
        enviadas: this.push(h.enviadas, m.enviadas),
        liquidadas: this.push(h.liquidadas, m.liquidadas),
        rateLimited: this.push(h.rateLimited, m.rateLimited),
        fila: this.push(h.fila, m.fila ?? 0),
        dlq: this.push(h.dlq, m.dlq ?? 0),
      }));
    });
  }

  private push(serie: number[], valor: number): number[] {
    return [...serie, valor].slice(-MAX_HISTORICO);
  }

  hora(iso: string): string {
    if (!iso) {
      return '—';
    }
    const d = new Date(iso);
    return d.toLocaleTimeString('pt-BR', { hour12: false });
  }
}
