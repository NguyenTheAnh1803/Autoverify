import { ProductionFeatureCharts } from '../components/analytics/ProductionFeatureCharts'
import { SpcEarlyWarningCenter } from '../components/analytics/SpcEarlyWarningCenter'
import type { Notice } from '../types'

const ANALYSIS_STAGES = [
  {
    index: '01',
    title: 'Baseline & Stability',
    status: 'Available now',
    description: 'SPC, EWMA and CUSUM establish normal feature behavior, control limits and small process shifts.',
    outputs: ['Control limits', 'Drift score', 'Out-of-control points'],
  },
  {
    index: '02',
    title: 'Defect Correlation',
    status: 'Available now',
    description: 'Measure which features move before Real Defect, Overkill and Leakage classifications increase.',
    outputs: ['Feature vs defect rate', 'Lag correlation', 'Top risk features'],
  },
  {
    index: '03',
    title: 'Line & Machine Benchmark',
    status: 'Available now',
    description: 'Compare the same feature distribution across Lines, Machines, Models, VPs and recipe revisions.',
    outputs: ['Distribution shift', 'Best-line baseline', 'Recipe delta'],
  },
  {
    index: '04',
    title: 'Predictive Alerts',
    status: 'Available now',
    description: 'Combine drift, threshold proximity and defect correlation into early-warning risk alerts.',
    outputs: ['Risk forecast', 'Alert severity', 'Recommended inspection'],
  },
]

export function FeatureTrendsTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  return (
    <div className="feature-trends-workspace">
      <section className="feature-trends-hero">
        <div>
          <p className="eyebrow">FEATURE INTELLIGENCE WORKSPACE</p>
          <h2>Feature Trends & Process Drift</h2>
          <p>
            Analyze how numerical AOI features change over time, identify early drift, and connect feature movement
            with Real Defect, Overkill and Leakage outcomes.
          </p>
        </div>
        <div className="feature-trends-objective">
          <span>Primary objective</span>
          <strong>Detect process change before defect rate rises</strong>
        </div>
      </section>

      {/* Real-time SPC Early Warning & Capability Center */}
      <SpcEarlyWarningCenter onNotice={onNotice} />

      <section className="feature-roadmap-panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">ANALYSIS ROADMAP</p>
            <h3>Recommended Feature Trend Analysis Flow</h3>
          </div>
          <span className="feature-roadmap-note">Baseline → Correlate → Compare → Predict</span>
        </div>

        <div className="feature-roadmap-grid">
          {ANALYSIS_STAGES.map((stage) => (
            <article key={stage.index} className="feature-roadmap-card">
              <div className="feature-roadmap-card-head">
                <span className="feature-stage-index">{stage.index}</span>
                <span className={`feature-stage-status ${stage.status === 'Available now' ? 'available' : ''}`}>{stage.status}</span>
              </div>
              <h4>{stage.title}</h4>
              <p>{stage.description}</p>
              <div className="feature-output-list">
                {stage.outputs.map((output) => <span key={output}>{output}</span>)}
              </div>
            </article>
          ))}
        </div>
      </section>

      <ProductionFeatureCharts onNotice={onNotice} />
    </div>
  )
}
