import React, { useMemo, useState } from 'react'
import ReactECharts from '../ReactECharts'
import { defectAnalyticsApi } from '../../api'
import type {
  ScriptConditionResult,
  ScriptObservationEvaluation,
} from '../../types'

export type ScriptConditionChartProps = {
  result: ScriptConditionResult
  sessionId?: string
  jobId?: string
  onSelectRowIndex: (rowIndex: number) => void
}

const escHtml = (s: string): string =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

export const ScriptConditionChart: React.FC<ScriptConditionChartProps> = ({
  result,
  sessionId,
  jobId,
  onSelectRowIndex,
}) => {
  const {
    script,
    totalObservations,
    guardFailedCount,
    finalTrueCount,
    finalFalseCount,
    finalTrueRatio,
    guardStats,
    conditionStats,
    evaluations,
    warnings,
  } = result

  const [selectedRow, setSelectedRow] = useState<ScriptObservationEvaluation | null>(
    evaluations.length > 0 ? evaluations[0] : null
  )
  const [viewMode, setViewMode] = useState<'overview' | 'breakdown' | 'rows'>('overview')

  // ---- OVERVIEW CHART: Final result pie ----
  const finalPieOption = useMemo(() => {
    return {
      title: {
        text: 'Script Evaluation Result',
        subtext: `${totalObservations} observations evaluated`,
        left: 'center',
        textStyle: { color: '#e2e8f0', fontSize: 14 },
        subtextStyle: { color: '#94a3b8', fontSize: 12 },
      },
      tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
      series: [
        {
          type: 'pie',
          radius: ['40%', '70%'],
          avoidLabelOverlap: true,
          label: {
            color: '#e2e8f0',
            fontSize: 12,
          },
          data: [
            {
              value: finalTrueCount,
              name: `NG Detected (True): ${finalTrueCount}`,
              itemStyle: { color: '#ef4444' },
            },
            {
              value: finalFalseCount,
              name: `Filtered Out (False): ${finalFalseCount}`,
              itemStyle: { color: '#22c55e' },
            },
            {
              value: guardFailedCount,
              name: `Guard Failed: ${guardFailedCount}`,
              itemStyle: { color: '#f59e0b' },
            },
          ],
        },
      ],
      backgroundColor: 'transparent',
    }
  }, [totalObservations, finalTrueCount, finalFalseCount, guardFailedCount])

  // ---- CONDITION BREAKDOWN CHART: Horizontal bar ----
  const condBreakdownOption = useMemo(() => {
    if (conditionStats.length === 0) return null

    const names = conditionStats.map((c) => c.conditionName)
    const trueRatios = conditionStats.map((c) => +(c.trueRatio * 100).toFixed(1))
    const falseRatios = conditionStats.map((c) => +(c.falseRatio * 100).toFixed(1))

    return {
      title: {
        text: 'Condition Pass/Fail Breakdown',
        left: 'center',
        textStyle: { color: '#e2e8f0', fontSize: 14 },
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: Array<{ seriesName: string; value: number; name: string }>) => {
          const name = params[0]?.name || ''
          const cond = conditionStats.find((c) => c.conditionName === name)
          let tip = `<b>${escHtml(name)}</b><br/>`
          if (cond) {
            tip += `Expression: <code>${escHtml(cond.expression.length > 60 ? cond.expression.slice(0, 57) + '...' : cond.expression)}</code><br/>`
          }
          params.forEach((p) => {
            tip += `${escHtml(p.seriesName)}: ${p.value}%<br/>`
          })
          if (cond) {
            tip += `Missing features: ${cond.missingFeatureCount}`
          }
          return tip
        },
      },
      grid: { left: '20%', right: '5%', bottom: '5%', top: '15%' },
      xAxis: {
        type: 'value',
        max: 100,
        name: '%',
        axisLabel: { color: '#94a3b8' },
        nameTextStyle: { color: '#94a3b8' },
      },
      yAxis: {
        type: 'category',
        data: names,
        axisLabel: { color: '#e2e8f0', fontSize: 11 },
        inverse: true,
      },
      series: [
        {
          name: 'True (Pass)',
          type: 'bar',
          stack: 'total',
          data: trueRatios,
          itemStyle: { color: '#22c55e' },
          barMaxWidth: 28,
        },
        {
          name: 'False (Fail)',
          type: 'bar',
          stack: 'total',
          data: falseRatios,
          itemStyle: { color: '#ef4444' },
          barMaxWidth: 28,
        },
      ],
      backgroundColor: 'transparent',
    }
  }, [conditionStats])

  // ---- GUARD BREAKDOWN CHART ----
  const guardBreakdownOption = useMemo(() => {
    if (guardStats.length === 0) return null

    const names = guardStats.map((g) => g.feature)
    const failRatios = guardStats.map((g) => +(g.failRatio * 100).toFixed(1))
    const passRatios = guardStats.map((g) => +(((1 - g.failRatio) * 100)).toFixed(1))

    return {
      title: {
        text: 'Guard Clauses (Early Return False)',
        left: 'center',
        textStyle: { color: '#e2e8f0', fontSize: 14 },
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: Array<{ seriesName: string; value: number; name: string }>) => {
          const name = params[0]?.name || ''
          const guard = guardStats.find((g) => g.feature === name)
          let tip = `<b>${escHtml(name)}</b><br/>`
          if (guard) {
            tip += `<code>${escHtml(guard.expression)}</code><br/>`
            tip += `Total: ${guard.totalEvaluated}, Fail: ${guard.failCount}<br/>`
          }
          params.forEach((p) => {
            tip += `${escHtml(p.seriesName)}: ${p.value}%<br/>`
          })
          return tip
        },
      },
      grid: { left: '25%', right: '5%', bottom: '5%', top: '15%' },
      xAxis: {
        type: 'value',
        max: 100,
        name: '%',
        axisLabel: { color: '#94a3b8' },
        nameTextStyle: { color: '#94a3b8' },
      },
      yAxis: {
        type: 'category',
        data: names,
        axisLabel: { color: '#e2e8f0', fontSize: 11 },
        inverse: true,
      },
      series: [
        {
          name: 'Pass',
          type: 'bar',
          stack: 'total',
          data: passRatios,
          itemStyle: { color: '#22c55e' },
          barMaxWidth: 28,
        },
        {
          name: 'Fail (Return False)',
          type: 'bar',
          stack: 'total',
          data: failRatios,
          itemStyle: { color: '#f59e0b' },
          barMaxWidth: 28,
        },
      ],
      backgroundColor: 'transparent',
    }
  }, [guardStats])

  // ---- ROW DETAIL: condition heatmap for selected row ----
  const renderRowDetail = (eval_: ScriptObservationEvaluation) => {
    return (
      <div className="script-row-detail" style={{ marginTop: 12 }}>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 8 }}>
          <span className="font-mono text-xs" style={{ color: '#94a3b8' }}>
            Row #{eval_.observation.rowIndex}
          </span>
          <span
            className={`job-status-pill ${eval_.finalResult ? 'status-failed' : 'status-completed'}`}
            style={{ fontSize: 11 }}
          >
            {eval_.finalResult ? 'NG (Script=True)' : 'OK (Script=False)'}
          </span>
          {eval_.guardFailed && (
            <span className="job-status-pill" style={{ background: '#f59e0b', color: '#000', fontSize: 11 }}>
              Guard Failed: {eval_.failedGuardFeature}
            </span>
          )}
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            style={{ marginLeft: 'auto', fontSize: 11 }}
            onClick={() => onSelectRowIndex(eval_.observation.rowIndex)}
          >
            View Defect
          </button>
        </div>

        {/* Feature values table */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 4, marginBottom: 8 }}>
          {Object.entries(eval_.featureValues).map(([feat, val]) => (
            <div
              key={feat}
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                padding: '2px 6px',
                background: 'rgba(255,255,255,0.03)',
                borderRadius: 3,
                fontSize: 11,
              }}
            >
              <span style={{ color: '#94a3b8' }}>{feat}</span>
              <span className="font-mono" style={{ color: val != null ? '#e2e8f0' : '#ef4444' }}>
                {val != null ? val.toFixed(2) : 'N/A'}
              </span>
            </div>
          ))}
        </div>

        {/* Condition results */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {Object.entries(eval_.conditionResults).map(([name, val]) => (
            <div
              key={name}
              style={{
                padding: '4px 10px',
                borderRadius: 4,
                fontSize: 12,
                fontWeight: 500,
                background: val ? 'rgba(34,197,94,0.15)' : 'rgba(239,68,68,0.15)',
                border: `1px solid ${val ? '#22c55e' : '#ef4444'}`,
                color: val ? '#22c55e' : '#ef4444',
              }}
            >
              {name}: {val ? 'TRUE' : 'FALSE'}
            </div>
          ))}
        </div>
      </div>
    )
  }

  return (
    <div className="script-condition-analysis">
      {/* Header summary */}
      <div className="analytics-summary-bar" style={{ marginBottom: 12 }}>
        <div className="summary-stat">
          <span className="stat-label">Total</span>
          <span className="stat-value font-mono">{totalObservations}</span>
        </div>
        <div className="summary-stat">
          <span className="stat-label">NG (True)</span>
          <span className="stat-value font-mono" style={{ color: '#ef4444' }}>
            {finalTrueCount} ({(finalTrueRatio * 100).toFixed(1)}%)
          </span>
        </div>
        <div className="summary-stat">
          <span className="stat-label">OK (False)</span>
          <span className="stat-value font-mono" style={{ color: '#22c55e' }}>
            {finalFalseCount}
          </span>
        </div>
        <div className="summary-stat">
          <span className="stat-label">Guard Failed</span>
          <span className="stat-value font-mono" style={{ color: '#f59e0b' }}>
            {guardFailedCount}
          </span>
        </div>
        <div className="summary-stat">
          <span className="stat-label">Features Used</span>
          <span className="stat-value font-mono">{script.allRequiredFeatures.length}</span>
        </div>
        <div className="summary-stat">
          <span className="stat-label">Conditions</span>
          <span className="stat-value font-mono">{script.conditions.length}</span>
        </div>
        {sessionId && jobId && (
          <div className="summary-stat" style={{ marginLeft: 'auto' }}>
            <a
              href={defectAnalyticsApi.getCsvExportUrl(sessionId, jobId)}
              download
              className="btn btn-secondary btn-sm"
              style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6 }}
              title="Export all script evaluation results to CSV for Excel review"
            >
              📥 Export CSV Report
            </a>
          </div>
        )}
      </div>

      {/* Warnings */}
      {warnings.length > 0 && (
        <div className="analytics-warnings" style={{ marginBottom: 8 }}>
          {warnings.map((w, i) => (
            <div key={i} className="text-amber-400 text-xs">
              {w}
            </div>
          ))}
        </div>
      )}

      {/* View mode tabs */}
      <nav className="analytics-nav-tabs" style={{ marginBottom: 12 }}>
        <button
          type="button"
          className={`tab-btn ${viewMode === 'overview' ? 'active' : ''}`}
          onClick={() => setViewMode('overview')}
        >
          Overview
        </button>
        <button
          type="button"
          className={`tab-btn ${viewMode === 'breakdown' ? 'active' : ''}`}
          onClick={() => setViewMode('breakdown')}
        >
          Condition Breakdown
        </button>
        <button
          type="button"
          className={`tab-btn ${viewMode === 'rows' ? 'active' : ''}`}
          onClick={() => setViewMode('rows')}
        >
          Per-Row Analysis ({evaluations.length})
        </button>
      </nav>

      {/* OVERVIEW */}
      {viewMode === 'overview' && (
        <div>
          <div style={{ display: 'grid', gridTemplateColumns: guardStats.length > 0 ? '1fr 1fr' : '1fr', gap: 16 }}>
            <ReactECharts
              option={finalPieOption}
              style={{ height: 320 }}
              opts={{ renderer: 'canvas' }}
            />
            {guardBreakdownOption && (
              <ReactECharts
                option={guardBreakdownOption}
                style={{ height: 320 }}
                opts={{ renderer: 'canvas' }}
              />
            )}
          </div>

          {/* Parsed script structure */}
          <div className="section-header" style={{ marginTop: 16 }}>
            <h4>Parsed Script Structure</h4>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div>
              <h5 style={{ color: '#94a3b8', fontSize: 12, marginBottom: 4 }}>Guard Clauses ({script.guards.length})</h5>
              {script.guards.map((g, i) => (
                <div
                  key={i}
                  style={{
                    padding: '4px 8px',
                    background: 'rgba(255,255,255,0.03)',
                    borderRadius: 3,
                    marginBottom: 2,
                    fontSize: 11,
                    fontFamily: 'monospace',
                    color: '#e2e8f0',
                  }}
                >
                  L{g.lineNumber}: {g.rawExpression}
                </div>
              ))}
            </div>
            <div>
              <h5 style={{ color: '#94a3b8', fontSize: 12, marginBottom: 4 }}>
                Named Conditions ({script.conditions.length})
              </h5>
              {script.conditions.map((c, i) => (
                <div
                  key={i}
                  style={{
                    padding: '4px 8px',
                    background: 'rgba(255,255,255,0.03)',
                    borderRadius: 3,
                    marginBottom: 2,
                    fontSize: 11,
                    fontFamily: 'monospace',
                    color: '#e2e8f0',
                  }}
                >
                  L{c.lineNumber}: <b style={{ color: '#60a5fa' }}>{c.name}</b> = {c.expression}
                </div>
              ))}
              {script.finalReturn && (
                <div
                  style={{
                    padding: '4px 8px',
                    background: 'rgba(96,165,250,0.08)',
                    borderRadius: 3,
                    marginTop: 6,
                    fontSize: 11,
                    fontFamily: 'monospace',
                    color: '#60a5fa',
                    border: '1px solid rgba(96,165,250,0.2)',
                  }}
                >
                  L{script.finalReturn.lineNumber}: return {script.finalReturn.rawExpression}
                </div>
              )}
            </div>
          </div>

          {/* Required features */}
          <div style={{ marginTop: 12 }}>
            <h5 style={{ color: '#94a3b8', fontSize: 12, marginBottom: 4 }}>
              Required Features ({script.allRequiredFeatures.length})
            </h5>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
              {script.allRequiredFeatures.map((f) => (
                <span
                  key={f}
                  style={{
                    padding: '2px 8px',
                    background: 'rgba(255,255,255,0.05)',
                    borderRadius: 3,
                    fontSize: 11,
                    fontFamily: 'monospace',
                    color: '#e2e8f0',
                  }}
                >
                  {f}
                </span>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* CONDITION BREAKDOWN */}
      {viewMode === 'breakdown' && (
        <div>
          {condBreakdownOption ? (
            <ReactECharts
              option={condBreakdownOption}
              style={{ height: Math.max(200, conditionStats.length * 45 + 80) }}
              opts={{ renderer: 'canvas' }}
            />
          ) : (
            <div className="analytics-empty-note">
              No named conditions were found in the script.
            </div>
          )}

          {/* Condition detail table */}
          {conditionStats.length > 0 && (
            <div className="table-responsive" style={{ marginTop: 12 }}>
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Condition</th>
                    <th>Expression</th>
                    <th>True</th>
                    <th>False</th>
                    <th>True %</th>
                    <th>Missing</th>
                  </tr>
                </thead>
                <tbody>
                  {conditionStats.map((c) => (
                    <tr key={c.conditionName}>
                      <td className="font-mono font-bold" style={{ color: '#60a5fa' }}>
                        {c.conditionName}
                      </td>
                      <td
                        className="font-mono text-xs"
                        style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis' }}
                        title={c.expression}
                      >
                        {c.expression}
                      </td>
                      <td className="font-mono" style={{ color: '#22c55e' }}>
                        {c.trueCount}
                      </td>
                      <td className="font-mono" style={{ color: '#ef4444' }}>
                        {c.falseCount}
                      </td>
                      <td className="font-mono">
                        {(c.trueRatio * 100).toFixed(1)}%
                      </td>
                      <td className="font-mono" style={{ color: c.missingFeatureCount > 0 ? '#f59e0b' : '#94a3b8' }}>
                        {c.missingFeatureCount}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Guard detail table */}
          {guardStats.length > 0 && (
            <div style={{ marginTop: 16 }}>
              <h4 style={{ color: '#e2e8f0', fontSize: 14, marginBottom: 8 }}>Guard Clauses</h4>
              <div className="table-responsive">
                <table className="analytics-data-table" role="table">
                  <thead>
                    <tr>
                      <th>Feature</th>
                      <th>Expression</th>
                      <th>Pass</th>
                      <th>Fail</th>
                      <th>Fail %</th>
                    </tr>
                  </thead>
                  <tbody>
                    {guardStats.map((g, i) => (
                      <tr key={i}>
                        <td className="font-mono font-bold" style={{ color: '#f59e0b' }}>
                          {g.feature}
                        </td>
                        <td className="font-mono text-xs" title={g.expression}>
                          {g.expression}
                        </td>
                        <td className="font-mono" style={{ color: '#22c55e' }}>
                          {g.passCount}
                        </td>
                        <td className="font-mono" style={{ color: '#ef4444' }}>
                          {g.failCount}
                        </td>
                        <td className="font-mono">
                          {(g.failRatio * 100).toFixed(1)}%
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* PER-ROW ANALYSIS */}
      {viewMode === 'rows' && (
        <div>
          <div className="table-responsive">
            <table className="analytics-data-table" role="table">
              <thead>
                <tr>
                  <th>Row</th>
                  <th>Result</th>
                  <th>Guard</th>
                  {conditionStats.map((c) => (
                    <th key={c.conditionName} title={c.expression}>
                      {c.conditionName}
                    </th>
                  ))}
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {evaluations.map((ev) => (
                  <tr
                    key={ev.observation.rowIndex}
                    className={selectedRow?.observation.rowIndex === ev.observation.rowIndex ? 'selected-row' : ''}
                    onClick={() => setSelectedRow(ev)}
                    style={{ cursor: 'pointer' }}
                  >
                    <td className="font-mono text-xs">{ev.observation.rowIndex}</td>
                    <td>
                      <span
                        className={`job-status-pill ${ev.finalResult ? 'status-failed' : 'status-completed'}`}
                        style={{ fontSize: 10 }}
                      >
                        {ev.finalResult ? 'TRUE' : 'FALSE'}
                      </span>
                    </td>
                    <td>
                      {ev.guardFailed ? (
                        <span style={{ color: '#f59e0b', fontSize: 11 }}>
                          FAIL ({ev.failedGuardFeature})
                        </span>
                      ) : (
                        <span style={{ color: '#22c55e', fontSize: 11 }}>PASS</span>
                      )}
                    </td>
                    {conditionStats.map((c) => {
                      const val = ev.conditionResults[c.conditionName]
                      return (
                        <td key={c.conditionName}>
                          <span
                            style={{
                              color: val ? '#22c55e' : '#ef4444',
                              fontWeight: 600,
                              fontSize: 11,
                            }}
                          >
                            {val ? 'T' : 'F'}
                          </span>
                        </td>
                      )
                    })}
                    <td>
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        style={{ fontSize: 10, padding: '2px 6px' }}
                        onClick={(e) => {
                          e.stopPropagation()
                          onSelectRowIndex(ev.observation.rowIndex)
                        }}
                      >
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Selected row detail */}
          {selectedRow && renderRowDetail(selectedRow)}
        </div>
      )}
    </div>
  )
}
