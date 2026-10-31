import { LIQUIDA_API_URL, REQUEST_TIMEOUT_MS, STALE_STREAM_MS, TICK_MS } from '../config';
import { nextHealth } from './series';
import type { MetricsSnapshot, SourceHealth } from './types';

export interface LiquidaHandlers {
  onSnapshot: (snapshot: MetricsSnapshot) => void;
  onHealth: (health: SourceHealth, transport: LiquidaTransport) => void;
}

export type LiquidaTransport = 'sse' | 'polling';

const STREAM_EVENT = 'metrics';

export function connectLiquida(handlers: LiquidaHandlers): () => void {
  let health: SourceHealth = { state: 'conectando', failures: 0 };
  let lastEventAt = Date.now();
  let fallbackInFlight = false;
  let source = openStream();

  function openStream(): EventSource {
    const stream = new EventSource(`${LIQUIDA_API_URL}/metrics/stream`);
    stream.addEventListener(STREAM_EVENT, (event: MessageEvent<string>) => {
      lastEventAt = Date.now();
      report(true, 'sse');
      handlers.onSnapshot(JSON.parse(event.data) as MetricsSnapshot);
    });
    return stream;
  }

  function report(succeeded: boolean, transport: LiquidaTransport): void {
    health = nextHealth(health, succeeded);
    handlers.onHealth(health, transport);
  }

  async function pollFallback(): Promise<void> {
    fallbackInFlight = true;
    try {
      const response = await fetch(`${LIQUIDA_API_URL}/metrics`, { signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const snapshot = (await response.json()) as MetricsSnapshot;
      report(true, 'polling');
      handlers.onSnapshot(snapshot);
    } catch {
      report(false, 'polling');
    } finally {
      fallbackInFlight = false;
    }
  }

  const watchdog = window.setInterval(() => {
    if (source.readyState === EventSource.CLOSED) {
      source = openStream();
    }
    if (Date.now() - lastEventAt > STALE_STREAM_MS && !fallbackInFlight) {
      void pollFallback();
    }
  }, TICK_MS);

  return () => {
    window.clearInterval(watchdog);
    source.close();
  };
}
