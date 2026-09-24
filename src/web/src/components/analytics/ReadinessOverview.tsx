import React from 'react'
import type { FeatureSchemaReadinessResponse, AnalyticsJobType } from '../../types'

export type ReadinessOverviewProps = {
  readinessData: FeatureSchemaReadinessResponse
  onSelectFamily: (family: AnalyticsJobType) => void
}

const FAMILY_CONFIG: Array<{
  id: AnalyticsJobType
  label: string
  desc: string
}> = [
  { id: 'SPC', label: 'SPC & Capability', desc: 'I-MR / Xbar-R/S control charts, Nelson rules 1-8, and Cp/Cpk process capability.' },
  { id: 'EWMA', label: 'EWMA Control', desc: 'Exponentially weighted moving average for early small mean drift detection (0.5σ - 2.0σ).' },
  { id: 'CUSUM', label: 'CUSUM Cumulative Sum', desc: 'High/low cumulative sum tracking for persistent shift and run length monitoring.' },
  { id: 'PCA', label: 'PCA & Multivariate', desc: 'Principal Component Analysis with Hotelling T² and SPE/Q multivariate anomaly isolation.' },
  { id: 'MSA', label: 'MSA / Gage R&R', desc: 'Two-way ANOVA variance decomposition for Part, Operator, Repeatability & NDC.' },
  { id: 'DOE', label: 'DOE Pareto Effects', desc: 'Factorial analysis, standardized effect significance Pareto chart, and residual diagnostics.' },
  { id: 'FEATURE_RELATIONSHIP', label: 'Feature Relationships', desc: 'Feature-to-feature Pearson/Spearman matrix, overlap counts, and pairwise scatter drill-down.' },
  { id: 'KMEANS', label: 'K-Means Clustering', desc: 'Unsupervised geometric partitioning, standardized 2D projection, cluster centroids, and inertia.' },
  { id: 'GMM', label: 'Gaussian Mixture (GMM)', desc: 'Probabilistic component decomposition, soft cluster posterior probability, entropy, and AIC/BIC.' },
  { id: 'ISOLATION_FOREST', label: 'Isolation Forest', desc: 'Multi-feature recursive partitioning for anomalous defect isolation, score ranking, and feature contributions.' },
]

export const ReadinessOverview: React.FC<ReadinessOverviewProps> = ({
  readinessData,
  onSelectFamily,
}) => {
  const { totalRecords, isNgOnlyDataset, features, readiness, detectedRuleVersions, scopeWarnings } = readinessData

  return (
    <div className="analytics-readiness-overview">
      {isNgOnlyDataset && (
        <aside
          className="analytics-alert-banner warning"
          role="alert"
          aria-live="polite"
        >
          <div className="alert-icon" aria-hidden="true">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <div className="alert-content">
            <header>
              <h4 className="alert-title">NG-Only Dataset Safeguard Warning</h4>
            </header>
            <p>
              This dataset is filtered to <strong>NG (Defect)</strong> records only. Under statistical process control (SPC) and quality engineering principles, 
              evaluating process capability indices (<strong>Cp / Cpk / Pp / Ppk</strong>) on truncated non-conforming distributions is theoretically invalid and will produce severely distorted metrics. 
              Normal distribution tests are suppressed or flagged, and capability badges must be treated strictly as non-standard diagnostics.
            </p>
          </div>
        </aside>
      )}

      <div className="analytics-summary-cards">
        <div className="analytics-stat-card">
          <span className="stat-label">Total Preview Records</span>
          <span className="stat-value">{totalRecords.toLocaleString()}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Numeric Defect Features</span>
          <span className="stat-value">{features.length}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Population Class</span>
          <span className={`stat-badge ${isNgOnlyDataset ? 'badge-warning' : 'badge-info'}`}>
            {isNgOnlyDataset ? 'NG-Only Filtered' : 'Unfiltered / Mixed'}
          </span>
        </div>
        {detectedRuleVersions && detectedRuleVersions.length > 0 && (
          <div className="analytics-stat-card">
            <span className="stat-label">Detected Rule Versions</span>
            <span className="stat-value font-mono text-xs">
              {detectedRuleVersions.join(', ')}
            </span>
          </div>
        )}
      </div>

      <section className="analytics-section" aria-labelledby="section-families-heading">
        <h3 id="section-families-heading" className="analytics-section-title">
          Analysis Method Readiness Matrix
        </h3>
        <div className="analytics-families-grid">
          {scopeWarnings && scopeWarnings.length > 0 && (
            <div className="analytics-alert-banner warning my-2 text-xs" role="status">
              <strong>Scope Warnings:</strong> {scopeWarnings.join(' | ')}
            </div>
          )}

          {FAMILY_CONFIG.map(({ id, label, desc }) => {
            const lookupKey = id as string
            const familyReadiness =
              readiness[lookupKey] ||
              readiness[lookupKey.toUpperCase()] ||
              readiness[lookupKey.toLowerCase()]
            const isReady = familyReadiness?.isReady ?? false
            const blockers = familyReadiness?.blockers ?? []
            const warnings = familyReadiness?.warnings ?? []
            const score = familyReadiness?.readinessScore ?? 0

            return (
              <div
                key={id}
                className={`analytics-family-card ${isReady ? 'ready' : 'blocked'}`}
              >
                <div className="family-card-header">
                  <div>
                    <h4 className="family-name">{label}</h4>
                    <span className="family-desc">{desc}</span>
                  </div>
                  <div className="family-status-group">
                    <span
                      className={`status-pill ${
                        isReady
                          ? warnings.length > 0
                            ? 'pill-warning'
                            : 'pill-success'
                          : 'pill-danger'
                      }`}
                    >
                      {isReady ? (warnings.length > 0 ? 'Ready (Warn)' : 'Ready') : 'Blocked'}
                    </span>
                    <span className="score-label">{Math.round(score * 100)}%</span>
                  </div>
                </div>

                {blockers.length > 0 && (
                  <div className="family-messages blockers">
                    <span className="msg-title">Blockers:</span>
                    <ul>
                      {blockers.map((b, i) => (
                        <li key={i}>{b}</li>
                      ))}
                    </ul>
                  </div>
                )}

                {warnings.length > 0 && (
                  <div className="family-messages warnings">
                    <span className="msg-title">Warnings:</span>
                    <ul>
                      {warnings.map((w, i) => (
                        <li key={i}>{w}</li>
                      ))}
                    </ul>
                  </div>
                )}

                {familyReadiness?.recommendedFeatures && familyReadiness.recommendedFeatures.length > 0 && (
                  <div className="family-recommended">
                    <span className="rec-title">Recommended:</span>{' '}
                    <code>{familyReadiness.recommendedFeatures.join(', ')}</code>
                  </div>
                )}

                <div className="family-card-footer">
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={!isReady}
                    onClick={() => onSelectFamily(id)}
                    title={!isReady ? 'Resolution required before execution' : `Configure ${label}`}
                  >
                    Configure & Run
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      </section>

      <section className="analytics-section" aria-labelledby="section-features-heading">
        <h3 id="section-features-heading" className="analytics-section-title">
          Extracted Feature Summary (Numeric Telemetry)
        </h3>
        {features.length === 0 ? (
          <div className="analytics-empty-note">
            No numeric continuous defect features found in preview records. Check preview data mappings or ingestion schema.
          </div>
        ) : (
          <div className="table-responsive">
            <table className="analytics-data-table" role="table">
              <thead>
                <tr>
                  <th>Feature Name</th>
                  <th className="num">Count</th>
                  <th className="num">Missing Count</th>
                  <th className="num">Missing %</th>
                  <th className="num">Min</th>
                  <th className="num">Max</th>
                  <th className="num">Mean</th>
                  <th className="num">StdDev (σ)</th>
                </tr>
              </thead>
              <tbody>
                {features.map((f) => (
                  <tr key={f.name}>
                    <td className="font-mono">{f.name}</td>
                    <td className="num">{f.count.toLocaleString()}</td>
                    <td className="num">{f.missingCount.toLocaleString()}</td>
                    <td className="num">{(f.missingRatio * 100).toFixed(1)}%</td>
                    <td className="num font-mono">{f.min !== null ? f.min.toFixed(4) : '-'}</td>
                    <td className="num font-mono">{f.max !== null ? f.max.toFixed(4) : '-'}</td>
                    <td className="num font-mono">{f.mean !== null ? f.mean.toFixed(4) : '-'}</td>
                    <td className="num font-mono">{f.stdDev !== null ? f.stdDev.toFixed(4) : '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
