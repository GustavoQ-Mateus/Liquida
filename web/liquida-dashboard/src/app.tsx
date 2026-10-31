import { BacklogPanel } from './components/backlog-panel';
import { HeaderBar } from './components/header-bar';
import { KpiStrip } from './components/kpi-strip';
import { LatencyPanel } from './components/latency-panel';
import { PipelineStrip } from './components/pipeline-strip';
import { ThroughputPanel } from './components/throughput-panel';
import { useDashboard } from './data/use-dashboard';
import { buildExits, buildFinding, buildKpis, buildPipeline, buildStatusShare, phaseLabel } from './data/view-model';

export function App() {
  const dashboard = useDashboard();
  const inputs = {
    stats: dashboard.stats,
    snapshot: dashboard.snapshot,
    run: dashboard.run,
    receivedPerSec: dashboard.receivedPerSec,
    bankcoreState: dashboard.bankcore.state,
  };
  return (
    <div className="dashboard">
      <HeaderBar
        liquida={dashboard.liquida}
        liquidaTransport={dashboard.liquidaTransport}
        bankcore={dashboard.bankcore}
        phase={dashboard.stats?.simulation.phase ?? null}
        phaseText={phaseLabel(inputs, dashboard.now)}
        busy={dashboard.busy}
        onStart={dashboard.start}
        onStop={dashboard.stop}
      />
      <main className="dashboard__body">
        <div className="title-row">
          <h1 className="title-row__title">Simulação de liquidação</h1>
          <p className="title-row__finding">{buildFinding(inputs)}</p>
          {dashboard.error ? (
            <p className="title-row__error" role="alert">
              {dashboard.error}
            </p>
          ) : null}
        </div>
        <KpiStrip kpis={buildKpis(inputs)} />
        <div className="board">
          <ThroughputPanel timeline={dashboard.timeline} />
          <PipelineStrip stages={buildPipeline(inputs)} exits={buildExits(inputs)} share={buildStatusShare(inputs)} />
          <BacklogPanel timeline={dashboard.timeline} />
          <LatencyPanel timeline={dashboard.timeline} />
        </div>
      </main>
    </div>
  );
}
