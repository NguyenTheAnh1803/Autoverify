import React, { useState } from 'react'
import { classifierRulesApi } from '../api'
import { useConfirm } from '../confirm'
import { formatPercent } from '../format'
import { Icon, Modal } from '../ui'
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

  const confirm = useConfirm()

  async function handleApplyTunedRule() {
    if (!candidateScript.trim()) return
    if (simResult && !simResult.isSafeToDeploy) {
      const ok = await confirm({
        title: 'Save a script that lets defects escape?',
        message: 'The simulation found that this candidate script introduces defect escape risk (Leakage).',
        confirmLabel: 'Save anyway',
        tone: 'danger',
      })
      if (!ok) return
    }

    setSavingRule(true)
    try {
      await classifierRulesApi.updateRule(rule.id, {
        scriptContent: candidateScript,
      })
      onNotice({ tone: 'success', message: `Applied tuned script to Rule [${rule.ruleCode}] successfully!` })
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
      title={`Rule Optimizer & What-If Simulation · ${rule.ruleName} (Priority ${rule.priority ?? 0})`}
      description="Automatically analyzes Overkill data, calculates Gold Thresholds, and runs regression simulations to verify false alarm reduction before deploying to machines."
      open={open}
      onClose={onClose}
      wide
    >
      <div className="vstack gap-xl">
        {/* Executive Summary Card */}
        {loadingTuning ? (
          <div className="dashboard-report-empty">
            <div className="spinner" />
            <span>Mining Overkill data and calculating optimal thresholds...</span>
          </div>
        ) : tuningData ? (
          <div
            style={{
              background: 'var(--status-info-bg)',
              border: '1px solid var(--status-info-border)',
              borderRadius: '8px',
              padding: '14px',
            }}
          >
            <div className="hstack justify-between">
              <div>
                <span className="text-xs font-bold text-info">
                  OVERKILL DATA MINING SUMMARY:
                </span>
                <p className="text-sm text-strong" style={{ margin: '4px 0 0 0', lineHeight: 1.4 }}>
                  {tuningData.executiveSummary}
                </p>
              </div>
              <div className="text-right" style={{ minWidth: '160px' }}>
                <div className="text-xs text-muted">Overkill / Real Defect Samples:</div>
                <strong className="text-base text-overkill">
                  {tuningData.totalOverkillSamples} Overkill
                </strong>{' '}
                / <strong className="text-real">{tuningData.totalRealDefectSamples} Real NG</strong>
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
                    <th className="text-overkill">Overkill [Min - Max]</th>
                    <th className="text-real">Real Defect [Min - Max]</th>
                    <th>Projected Overkill Cut</th>
                    <th>Algorithmic Explanation</th>
                  </tr>
                </thead>
                <tbody>
                  {tuningData.recommendations.map((rec) => {
                    const isGold = rec.separationKind === 'CompletelySeparated'
                    return (
                      <tr key={rec.featureName} style={{ background: isGold ? 'var(--status-ok-bg)' : undefined }}>
                        <td className="font-mono font-bold" style={{ color: isGold ? 'var(--status-ok-text)' : 'var(--slate-900)' }}>
                          {isGold && ''}
                          {rec.featureName}
                        </td>
                        <td className="font-mono text-muted">
                          {rec.currentThresholdInScript != null ? rec.currentThresholdInScript : '—'}
                        </td>
                        <td>
                          <span
                            className="tag-badge font-mono font-bold"
                            style={{
                              background: isGold ? 'var(--status-ok-bg)' : 'var(--status-info-bg)',
                              color: isGold ? 'var(--status-ok-text)' : 'var(--status-info-text)',
                            }}
                          >
                            {rec.comparisonOperator} {rec.proposedThreshold}
                          </span>
                        </td>
                        <td className="font-mono text-xs text-overkill">
                          [{rec.overkillMin} - {rec.overkillMax}]
                        </td>
                        <td className="font-mono text-xs text-real">
                          [{rec.realMin} - {rec.realMax}]
                        </td>
                        <td>
                          <strong style={{ color: isGold ? 'var(--status-ok-text)' : 'var(--status-overkill-text)' }}>
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
        <div className="gap-xl" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr' }}>
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px' }}>
              <label className="text-xs font-bold text-muted">
                Current Script (Baseline v1.0):
              </label>
              <span className="text-xs font-mono text-muted">Read-only</span>
            </div>
            <textarea
              className="font-mono text-xs w-full text-muted"
              value={baselineScript}
              readOnly
              rows={14}
              style={{
                background: 'var(--slate-50)',
                padding: '10px',
                borderRadius: '6px',
                border: '1px solid var(--slate-200)',
                whiteSpace: 'pre',
              }}
            />
          </div>

          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px' }}>
              <label className="text-xs font-bold text-ok">
                Proposed Tuned Script (Candidate Script):
              </label>
              {tuningData?.autoTunedScript && (
                <button
                  type="button"
                  className="ghost-button compact text-xs"
                  style={{ padding: '1px 6px' }}
                  onClick={() => setCandidateScript(tuningData.autoTunedScript)}
                  title="Reload auto-tuned script"
                >
                  <Icon name="refresh" className="icon inline-icon" />Reload auto-tuned script
                </button>
              )}
            </div>
            <textarea
              className="font-mono text-xs w-full text-strong"
              value={candidateScript}
              onChange={(e) => setCandidateScript(e.target.value)}
              rows={14}
              style={{
                background: 'var(--slate-50)',
                padding: '10px',
                borderRadius: '6px',
                border: '1px solid var(--status-ok-border)',
                whiteSpace: 'pre',
              }}
            />
          </div>
        </div>

        {/* Action Button: Run What-If Simulation */}
        <div className="hstack justify-between">
          <span className="text-xs text-muted">
            The system executes both scripts in parallel across historical defect observations to evaluate safety and overkill reduction.
          </span>
          <button
            type="button"
            className="primary font-semibold"
            style={{ background: 'var(--blue-600)', padding: '8px 24px' }}
            onClick={() => void handleRunSimulation()}
            disabled={simulating || !candidateScript.trim()}
          >
            {simulating ? 'Simulating...' : 'Run What-If Simulation'}
          </button>
        </div>

        {/* Simulation Results Card */}
        {simResult && (
          <div
            style={{
              background: simResult.isSafeToDeploy ? 'var(--status-ok-bg)' : 'var(--status-ng-bg)',
              border: `1px solid ${simResult.isSafeToDeploy ? 'var(--status-ok-border)' : 'var(--status-ng-border)'}`,
              borderRadius: '8px',
              padding: '16px',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '12px' }}>
              <div>
                <span className="text-xs font-bold" style={{ color: simResult.isSafeToDeploy ? 'var(--status-ok-text)' : 'var(--status-ng-text)' }}>
                  REGRESSION SIMULATION ON {simResult.totalEvaluated} PRODUCTS:
                </span>
                <p className="text-sm text-strong" style={{ margin: '4px 0 0 0', fontWeight: 500, lineHeight: 1.4 }}>
                  {simResult.detailedExplanation}
                </p>
              </div>
              <span
                className="tag-badge font-bold text-xs"
                style={{
                  background: simResult.isSafeToDeploy ? 'var(--status-ok)' : 'var(--status-ng)',
                  color: '#fff',
                  padding: '4px 10px',
                }}
              >
                {simResult.isSafeToDeploy ? <><Icon name="check" className="icon inline-icon" />QUALIFIED FOR PRODUCTION</> : <><Icon name="warning" className="icon inline-icon" />LEAKAGE RISK DETECTED</>}
              </span>
            </div>

            {/* Metrics Comparison Grid */}
            <div className="gap-lg mt-3" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(180px, 100%), 1fr))' }}>
              <div style={{ background: 'var(--slate-50)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Overkill Alarms:</span>
                <div className="text-base" style={{ marginTop: '2px' }}>
                  <span className="text-muted">{simResult.baselineOverkillCount}</span>
                  <span style={{ margin: '0 6px' }}>→</span>
                  <strong className="text-ok">{simResult.candidateOverkillCount}</strong>
                </div>
                <small className="text-ok">Cut by {simResult.overkillReductionPercent}%</small>
              </div>

              <div style={{ background: 'var(--slate-50)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Overkill Rate:</span>
                <div className="text-base" style={{ marginTop: '2px' }}>
                  <span className="text-muted">{formatPercent(simResult.baselineOverkillRate)}</span>
                  <span style={{ margin: '0 6px' }}>→</span>
                  <strong className="text-ok">{formatPercent(simResult.candidateOverkillRate)}</strong>
                </div>
              </div>

              <div style={{ background: 'var(--slate-50)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Defect Escapes (Leakage):</span>
                <div className="text-base" style={{ marginTop: '2px' }}>
                  <strong style={{ color: simResult.candidateLeakageCount > 0 ? 'var(--status-leakage-text)' : 'var(--status-ok-text)' }}>
                    {simResult.candidateLeakageCount} cases
                  </strong>
                </div>
                <small style={{ color: simResult.candidateLeakageCount > 0 ? 'var(--status-leakage-text)' : 'var(--status-ok-text)' }}>
                  {simResult.candidateLeakageCount === 0 ? 'Absolute Zero Leakage' : 'Escapes introduced!'}
                </small>
              </div>

              <div style={{ background: 'var(--slate-50)', padding: '10px', borderRadius: '6px' }}>
                <span className="text-xs text-muted">Operator Time Saved:</span>
                <div className="text-base" style={{ marginTop: '2px' }}>
                  <strong className="text-info">~{simResult.estimatedSavedOpHoursPerMonth} hrs/mo</strong>
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
                <div className="vstack gap-xs" style={{ maxHeight: '140px', overflowY: 'auto' }}>
                  {simResult.divergences.slice(0, 10).map((d) => (
                    <div
                      className="hstack justify-between text-xs"
                      key={d.keyId}
                      style={{
                        padding: '4px 8px',
                        borderRadius: '4px',
                        background: 'var(--slate-50)',
                      }}
                    >
                      <strong className="mono-code">{d.keyId}</strong>
                      <span
                        className="font-semibold"
                        style={{
                          color: d.divergenceKind === 'OverkillEliminated' ? 'var(--status-ok-text)' : 'var(--status-ng-text)',
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
        <div className="actions gap-md" style={{ justifyContent: 'flex-end', borderTop: '1px solid var(--slate-200)', paddingTop: '12px' }}>
          <button type="button" className="ghost-button" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="primary font-semibold"
            style={{ background: 'var(--status-ok)', borderColor: 'var(--status-ok-border)' }}
            onClick={() => void handleApplyTunedRule()}
            disabled={savingRule || !candidateScript.trim()}
          >
            {savingRule ? 'Saving...' : `Apply Tuned Script to Rule [${rule.ruleCode}]`}
          </button>
        </div>
      </div>
    </Modal>
  )
}
