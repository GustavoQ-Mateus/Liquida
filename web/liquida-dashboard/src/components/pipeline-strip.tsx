import type { PipelineStage, StatusShare } from '../data/view-model';
import { StatusBar } from './status-bar';
import './pipeline-strip.scss';

interface PipelineStripProps {
  stages: PipelineStage[];
  exits: PipelineStage[];
  share: StatusShare | null;
}

export function PipelineStrip({ stages, exits, share }: PipelineStripProps) {
  return (
    <section className="panel pipeline-strip">
      <h2 className="panel__title">Caminho de uma transferência</h2>
      <ol className="pipeline-strip__stages">
        {stages.map((stage) => (
          <li key={stage.id} className="pipeline-stage">
            <span className="pipeline-stage__label">{stage.label}</span>
            <span className={`pipeline-stage__value tone-${stage.tone}`}>{stage.value}</span>
          </li>
        ))}
      </ol>
      <dl className="pipeline-strip__exits">
        {exits.map((exit) => (
          <div key={exit.id} className="pipeline-exit">
            <dt>{exit.label}</dt>
            <dd className={`tone-${exit.tone}`}>{exit.value}</dd>
          </div>
        ))}
      </dl>
      <StatusBar share={share} />
    </section>
  );
}
