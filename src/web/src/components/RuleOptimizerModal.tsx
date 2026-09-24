import React, { useState } from 'react'
import { classifierRulesApi } from '../api'
import { formatPercent } from '../format'
import { Modal } from '../ui'
import type {
  ClassifierRuleDto,
  Notice,
  RuleTuningAnalysisResponse,
  SimulateWhatIfResponse,
} from '../types'

export type RuleOptimizerModalProps = {
  rule: ClassifierRuleDto
  open: boolean
  onClose: () => void
  onNotice: (notice: Notice) => void
  onRuleUpdated: () => void
}

export const RuleOptimizerModal: React.FC<RuleOptimizerModalProps> = ({
  rule,
  open,
  onClose,
  onNotice,
  onRuleUpdated,
}) => {
  const [tuningData, setTuningData] = useState<RuleTuningAnalysisResponse | null>(null)
  const [loadingTuning, setLoadingTuning] = useState(false)

  // Candidate Script under evaluation
  const [candidateScript, setCandidateScript] = useState<string>(rule.scriptContent || '')
  const [baselineScript, setBaselineScript] = useState<string>(rule.scriptContent || '')

  // Simulation state
  const [simulating, setSimulating] = useState(false)
  const [simResult, setSimResult] = useState<SimulateWhatIfResponse | null>(null)

  // Applying new rule state
  const [savingRule, setSavingRule] = useState(false)

  // Initial load of tuning analysis
  React.useEffect(() => {
    if (open && rule.id) {
      void runTuningAnalysis()
    }
  }, [open, rule.id])

  async function runTuningAnalysis() {
    setLoadingTuning(true)
    try {
      const res = await classifierRulesApi.getTuningAnalysis(rule.id)
      setTuningData(res)
      setBaselineScript(res.originalScript || rule.scriptContent || '')
      if (res.autoTunedScript) {
        setCandidateScript(res.autoTunedScript)
      }
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to analyze rule thresholds.' })
    } finally {
      setLoadingTuning(false)
    }
  }

  async function handleRunSimulation() {
    setSimulating(true)
    setSimResult(null)
    try {
      const res = await classifierRulesApi.simulateWhatIf({
        ruleId: rule.id,
        baselineScript,
        candidateScript,
        defectName: rule.defectClass || rule.ruleCode,
      })
      setSimResult(res)
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'What-If Simulation failed.' })
    } finally {
      setSimulating(false)
    }
  }

  async function handleApplyTunedRule() {
    if (!candidateScript.trim()) return
    if (simResult && !simResult.isSafeToDeploy) {
      if (!window.confirm('WARNING: This candidate script introduces defect escape risk (Leakage)! Are you sure you want to save?')) {
        return
      }
    }

    setSavingRule(true)
    try {
      await classifierRulesApi.updateRule(rule.id, {
        scriptContent: candidateScript,
      })
      onNotice({ tone: 'success', message: `✓ Applied tuned script to Rule [${rule.ruleCode}] successfully!` })
      onRuleUpdated()
      onClose()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to apply rule update.' })
    } finally {
      setSavingRule(false)
    }
  }

  return (
    <Modal
      title={`🎯 Rule Optimizer & What-If Simulation · ${rule.ruleName} (Priority ${rule.priority ?? 0})`}
      description="Automatically analyzes Overkill data, calculates Gold Thresholds, and runs regression simulations to verify false alarm reduction before deploying to machines."
      open={open}
      onClose={onClose}
      wide
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        {/* Executive Summary Card */}
        {loadingTuning ? (
          <div className="dashboard-report-empty">
            <div className="spinner" />
            <span>Mining Overkill data and calculating optimal thresholds...</span>
          </div>
        ) : tuningData ? (
          <div
            style={{
              background: 'rgba(59, 130, 246, 0.1)',
              border: '1px solid rgba(59, 130, 246, 0.3)',
              borderRadius: '8px',
              padding: '14px',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div>
                <span className="text-xs font-bold" style={{ color: '#93c5fd' }}>
                  OVERKILL DATA MINING SUMMARY:
                </span>
                <p style={{ margin: '4px 0 0 0', fontSize: '13px', color: '#e2e8f0', lineHeight: 1.4 }}>
                  {tuningData.executiveSummary}
                </p>
              </div>
              <div style={{ textAlign: 'right', minWidth: '160px' }}>
                <div className="text-xs text-muted">Overkill / Real Defect Samples:</div>
                <strong style={{ fontSize: '14px', color: '#f59e0b' }}>
                  {tuningData.totalOverkillSamples} Overkill
                </strong>{' '}
                / <strong style={{ color: '#a855f7' }}>{tuningData.totalRealDefectSamples} Real NG</strong>
              </div>
            </div>
          </div>
        ) : null}

        {/* Recommended Gold Thresholds Table */}
        {tuningData?.recommendations && tuningData.recommendations.length > 0 && (
          <div>
            <span className="text-xs font-bold text-muted" style={{ display: 'block', marginBottom: '6px' }}>
              RECOMMENDED THRESHOLD TUNING FROM FEATURE DATA:
            </span>
            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Feature Parameter</th>
                    <th>Current Threshold</th>
                    <th>Proposed Gold Threshold</th>
                    <th style={{ color: '#f59e0b' }}>Overkill [Min - Max]</th>
                    <th style={{ color: '#a855f7' }}>Real Defect [Min - Max]</th>
                    <th>Projected Overkill Cut</th>
                    <th>Algorithmic Explanation</th>
                  </tr>
                </thead>
                <tbody>
                  {tuningData.recommendations.map((rec) => {
                    const isGold = rec.separationKind === 'CompletelySeparated'
                    return (
                      <tr key={rec.featureName} style={{ background: isGold ? 'rgba(16, 185, 129, 0.08)' : undefined }}>
                        <td className="font-mono font-bold" style={{ color: isGold ? '#34d399' : '#e2e8f0' }}>
                          {isGold && '⭐ '}
                          {rec.featureName}
                        </td>
                        <td className="font-mono text-muted">
                          {rec.currentThresholdInScript != null ? rec.currentThresholdInScript : '—'}
                        </td>
                        <td>
                          <span
                            className="tag-badge font-mono"
                            style={{
                              background: isGold ? 'rgba(16, 185, 129, 0.2)' : 'rgba(59, 130, 246, 0.2)',
                              color: isGold ? '#10b981' : '#60a5fa',
                              fontWeight: 700,
                            }}
                          >
                            {rec.comparisonOperator} {rec.proposedThreshold}
                          </span>
                        </td>
                        <td className="font-mono text-xs" style={{ color: '#f59e0b' }}>
                          [{rec.overkillMin} - {rec.overkillMax}]
                        </td>
                        <td className="font-mono text-xs" style={{ color: '#a855f7' }}>
                          [{rec.realMin} - {rec.realMax}]
                        </td>
                        <td>
                          <strong style={{ color: isGold ? '#10b981' : '#f59e0b' }}>
                            {rec.projectedOverkillCutPercent}%
                          </strong>
                        </td>
                        <td className="text-xs" style={{ maxWidth: 280 }}>
                          {rec.explanation}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Side-by-side comparison editors */}
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px' }}>
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px' }}>
              <label className="text-xs font-bold" style={{ color: '#94a3b8' }}>
                Current Script (Baseline v1.0):
              </label>
              <span className="text-xs font-mono text-muted">Read-only</span>
            </div>
            <textarea
              value={baselineScript}
              readOnly
              rows={14}
              style={{
                fontFamily: 'monospace',
                fontSize: '11px',
                width: '100%',
                background: '#090d16',
                color: '#94a3b8',
                padding: '10px',
                borderRadius: '6px',
                border: '1px solid rgba(255,255,255,0.08)',
                whiteSpace: 'pre',
              }}
            />
          </div>

          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px' }}>
              <label className="text-xs font-bold" style={{ color: '#34d399' }}>
                Proposed Tuned Script (Candidate Script):
              </label>
              {tuningData?.autoTunedScript && (
                <button
                  type="button"
                  className="ghost-button compact"
                  style={{ fontSize: '10px', padding: '1px 6px' }}
                  onClick={() => setCandidateScript(tuningData.autoTunedScript)}
                  title="Reload auto-tuned script"
                >
                  ↺ Reload auto-tuned script
                </button>
              )}
            </div>
            <textarea
              value={candidateScript}
              onChange={(e) => setCandidateScript(e.target.value)}
              rows={14}
              style={{
                fontFamily: 'monospace',
                fontSize: '11px',
                width: '100%',
                background: '#090d16',
                color: '#e2e8f0',
                padding: '10px',
                borderRadius: '6px',
                border: '1px solid rgba(16, 185, 129, 0.4)',
                whiteSpace: 'pre',
              }}
            />
          </div>
        </div>

        {/* Action Button: Run What-If Simulation */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <span className="text-xs text-muted">
            The system executes both scripts in parallel across historical defect observations to evaluate safety and overkill reduction.
          </span>
          <button
            type="button"
            className="primary"
            style={{ background: '#2563eb', padding: '8px 24px', fontWeight: 600 }}
            onClick={() => void handleRunSimulation()}
            disabled={simulating || !candidateScript.trim()}
          >
            {simulating ? 'Simulating...' : '🚀 Run What-If Simulation'}
          </button>
        </div>

        {/* Simulation Results Card */}
        {simResult && (
          <div
            style={{
              background: simResult.isSafeToDeploy ? 'rgba(16, 185, 129, 0.1)' : 'rgba(239, 68, 68, 0.15)',
              border: `1px solid ${simResult.isSafeToDeploy ? '#10b981' : '#ef4444'}`,
              borderRadius: '8px',
              padding: '16px',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '12px' }}>
              <div>
                <span className="text-xs font-bold" style={{ color: simResult.isSafeToDeploy ? '#10b981' : '#ef4444' }}>
                  REGRESSION SIMULATION ON {simResult.totalEvaluated} PRODUCTS:
                </span>
                <p style={{ margin: '4px 0 0 0', fontSize: '13px', color: '#e2e8f0', fontWeight: 500, lineHeight: 1.4 }}>
                  {simResult.detailedExplanation}
                </p>
              </div>
              <span
                className="tag-badge"
                style={{
                  background: simResult.isSafeToDeploy ? '#10b981' : '#ef4444',
                  color: '#fff',
                  fontWeight: 700,
                  fontSize: '12px',
                  padding: '4px 10px',
                }}
              >
                {simResult.isSafeToDeploy ? '✓ QUALIFIED FOR PRODUCTION' : '🚨 LEAKAGE RISK DETECTED'}
              </span>
            </div>

            {/* Metrics Comparison Grid */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px', marginTop: '12px' }}>
              <div style={{ background: 'rgba(255,255,255,0.03)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Overkill Alarms:</span>
                <div style={{ fontSize: '14px', marginTop: '2px' }}>
                  <span style={{ color: '#94a3b8' }}>{simResult.baselineOverkillCount}</span>
                  <span style={{ margin: '0 6px' }}>→</span>
                  <strong style={{ color: '#10b981' }}>{simResult.candidateOverkillCount}</strong>
                </div>
                <small style={{ color: '#10b981' }}>Cut by {simResult.overkillReductionPercent}%</small>
              </div>

              <div style={{ background: 'rgba(255,255,255,0.03)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Overkill Rate:</span>
                <div style={{ fontSize: '14px', marginTop: '2px' }}>
                  <span style={{ color: '#94a3b8' }}>{formatPercent(simResult.baselineOverkillRate)}</span>
                  <span style={{ margin: '0 6px' }}>→</span>
                  <strong style={{ color: '#10b981' }}>{formatPercent(simResult.candidateOverkillRate)}</strong>
                </div>
              </div>

              <div style={{ background: 'rgba(255,255,255,0.03)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Defect Escapes (Leakage):</span>
                <div style={{ fontSize: '14px', marginTop: '2px' }}>
                  <strong style={{ color: simResult.candidateLeakageCount > 0 ? '#ef4444' : '#10b981' }}>
                    {simResult.candidateLeakageCount} cases
                  </strong>
                </div>
                <small style={{ color: simResult.candidateLeakageCount > 0 ? '#ef4444' : '#10b981' }}>
                  {simResult.candidateLeakageCount === 0 ? 'Absolute Zero Leakage' : 'Escapes introduced!'}
                </small>
              </div>

              <div style={{ background: 'rgba(255,255,255,0.03)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Operator Time Saved:</span>
                <div style={{ fontSize: '14px', marginTop: '2px' }}>
                  <strong style={{ color: '#38bdf8' }}>~{simResult.estimatedSavedOpHoursPerMonth} hrs/mo</strong>
                </div>
                <small className="text-muted">Estimated throughput savings</small>
              </div>
            </div>

            {/* Divergence Samples List */}
            {simResult.divergences.length > 0 && (
              <div style={{ marginTop: '14px' }}>
                <span className="text-xs text-muted" style={{ display: 'block', marginBottom: '6px' }}>
                  SAMPLE OBSERVATIONS WITH CHANGED VERDICTS:
                </span>
                <div style={{ maxHeight: '140px', overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: '4px' }}>
                  {simResult.divergences.slice(0, 10).map((d) => (
                    <div
                      key={d.keyId}
                      style={{
                        padding: '4px 8px',
                        borderRadius: '4px',
                        background: 'rgba(255,255,255,0.03)',
                        display: 'flex',
                        justifyContent: 'space-between',
                        fontSize: '11px',
                        alignItems: 'center',
                      }}
                    >
                      <strong className="mono-code">{d.keyId}</strong>
                      <span
                        style={{
                          color: d.divergenceKind === 'OverkillEliminated' ? '#10b981' : '#ef4444',
                          fontWeight: 600,
                        }}
                      >
                        {d.explanation}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {/* Modal Bottom Actions */}
        <div className="actions" style={{ justifyContent: 'flex-end', gap: '8px', borderTop: '1px solid rgba(255,255,255,0.08)', paddingTop: '12px' }}>
          <button type="button" className="ghost-button" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="primary"
            style={{ background: '#059669', borderColor: '#10b981', fontWeight: 600 }}
            onClick={() => void handleApplyTunedRule()}
            disabled={savingRule || !candidateScript.trim()}
          >
            {savingRule ? 'Saving...' : `✓ Apply Tuned Script to Rule [${rule.ruleCode}]`}
          </button>
        </div>
      </div>
    </Modal>
  )
}
