import { Component, computed, effect, input, signal } from '@angular/core';

@Component({
  selector: 'app-counter',
  standalone: true,
  template: `{{ display() }}`,
})
export class Counter {
  readonly value = input<number | null | undefined>(0);
  readonly format = input<'int' | 'brl'>('int');
  readonly duration = input(650);

  private readonly current = signal(0);
  private raf = 0;

  readonly display = computed(() => this.render(this.current()));

  constructor() {
    effect(() => {
      const target = this.value() ?? 0;
      this.animateTo(target);
    });
  }

  private animateTo(target: number): void {
    cancelAnimationFrame(this.raf);
    const from = this.current();
    const delta = target - from;
    if (delta === 0) {
      return;
    }
    const dur = this.duration();
    const t0 = performance.now();
    const tick = (now: number) => {
      const p = Math.min(1, (now - t0) / dur);
      const eased = 1 - Math.pow(1 - p, 3);
      this.current.set(from + delta * eased);
      if (p < 1) {
        this.raf = requestAnimationFrame(tick);
      }
    };
    this.raf = requestAnimationFrame(tick);
  }

  private render(v: number): string {
    if (this.format() === 'brl') {
      return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    }
    return Math.round(v).toLocaleString('pt-BR');
  }
}
