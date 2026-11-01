import { BacklogPanel } from './components/backlog-panel';
import { HeaderBar } from './components/header-bar';
import { KpiStrip } from './components/kpi-strip';
import { LatencyPanel } from './components/latency-panel';
import { PipelineStrip } from './components/pipeline-strip';
import { ThroughputPanel } from './components/throughput-panel';
import { CustomScenarioForm } from './components/custom-scenario-form';
import { scenarioSummary } from './data/scenarios';
import { useDashboard } from './data/use-dashboard';
import { useScenario } from './data/use-scenario';
import { buildExits, buildFinding, buildKpis, buildPipeline, buildStatusShare, phaseLabel } from './data/view-model';

export function App() {
  const dashboard = useDashboard();
  const scenario = useScenario();
  const inputs = {
    stats: dashboard.stats,
    snapshot: dashboard.snapshot,
    run: dashboard.run,
    receivedPerSec: dashboard.receivedPerSec,
    bankcoreState: dashboard.bankcore.state,
    cleaning: dashboard.cleaning,
    scenarioSummary: scenarioSummary(scenario.scenarioId, scenario.custom),
  };
  const phase = dashboard.stats?.simulation.phase ?? null;
  const running = phase === 'seeding' || phase === 'running';
  return (
    <div className="dashboard">
      <HeaderBar
        liquida={dashboard.liquida}
        liquidaTransport={dashboard.liquidaTransport}
        bankcore={dashboard.bankcore}
        phase={phase}
        phaseText={phaseLabel(inputs, dashboard.now)}
        busy={dashboard.busy}
        cleaning={dashboard.cleaning}
        scenario={scenario}
        onStart={() => dashboard.start(scenario.config, scenario.cleanFirst)}
        onStop={dashboard.stop}
        onClean={dashboard.clean}
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
        {scenario.scenarioId === 'personalizado' ? (
          <CustomScenarioForm scenario={scenario} disabled={running || dashboard.busy} />
        ) : null}
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
