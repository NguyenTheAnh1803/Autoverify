import React, { useMemo, useState } from 'react'
import ReactECharts from '../ReactECharts'
import type { GaussianMixtureResult } from '../../types'

export type GmmChartProps = {
  result: GaussianMixtureResult
  onSelectRowIndex: (rowIndex: number) => void
}

const GMM_COLORS = [
  '#38bdf8', // sky
  '#f59e0b', // amber
  '#10b981', // emerald
  '#ec4899', // pink
  '#8b5cf6', // purple
  '#06b6d4', // cyan
  '#f97316', // orange
  '#64748b', // slate
]

export const GmmChart: React.FC<GmmChartProps> = ({
  result,
  onSelectRowIndex,
}) => {
  const {
    componentsCount,
    covarianceType,
    logLikelihood,
    aic,
    bic,
    converged,
    iterations,
    componentSummaries,
    assignments,
    selectedFeatures,
    means,
  } = result

  const [selectedComp, setSelectedComp] = useState<number | null>(null)
  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    xVal: number
    yVal: number
    compIndex: number
    maxProb?: number
    rowIndex?: number
    keyId?: string
    defectFamily?: string
  } | null>(null)

  const featX = selectedFeatures[0] || 'Feature 1'
  const featY = selectedFeatures[1] || (selectedFeatures.length > 1 ? selectedFeatures[1] : 'Feature 1 (Offset)')

  const scatterOption = useMemo(() => {
    if (!assignments || assignments.length === 0) return null

    // Group assignments by assigned component
    const seriesList = componentSummaries.map((comp) => {
      const compIdx = comp.componentIndex
      const compAssignments = assignments.filter((a) => a.assignedComponent === compIdx)
      const color = GMM_COLORS[compIdx % GMM_COLORS.length]

      const data = compAssignments.map((a) => {
        const meanX = comp.meanVector?.[0] ?? 0
        const meanY = comp.meanVector?.[1] ?? meanX

        return {
          value: [meanX, meanY],
          obs: a.observation,
          compIndex: compIdx,
          maxProb: a.maxProbability,
          entropy: a.entropy,
          logDensity: a.logDensity,
        }
      })

      return {
        name: `Component ${compIdx}`,
        type: 'scatter',
        symbolSize: (dataItem: { maxProb: number }) => Math.max(4, Math.min(12, dataItem.maxProb * 10)),
        data,
        itemStyle: {
          color,
          opacity: selectedComp === null || selectedComp === compIdx ? 0.8 : 0.15,
        },
      }
    })

    // Mean centers series
    const centersSeries = {
      name: 'Component Means',
      type: 'scatter',
      symbol: 'cross',
      symbolSize: 16,
      data: means.map((m, idx) => ({
        value: [m[0] ?? 0, m[1] ?? (m[0] ?? 0)],
        compIndex: idx,
        weight: componentSummaries[idx]?.weight ?? 0,
      })),
      itemStyle: {
        color: '#f8fafc',
        borderWidth: 2,
      },
    }

    return {
      title: {
        text: `Gaussian Mixture Components (${componentsCount} Components, ${covarianceType.toUpperCase()} Covariance)`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
        subtext: `Dimension Mapping: ${featX} vs ${featY} (Point size \u221d Posterior Probability)`,
        subtextStyle: { color: '#94a3b8', fontSize: 11 },
      },
      tooltip: {
        trigger: 'item',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: {
          data: {
            value: [number, number]
            obs?: {
              rowIndex: number
              keyId?: string
              lineCode?: string
              machineCode?: string
              stationCode?: string
              defectFamily?: string
            }
            compIndex: number
            maxProb?: number
            entropy?: number
            logDensity?: number
            weight?: number
          }
        }) => {
          const d = params.data
          if (!d.obs) {
            return `
              <div style="font-family:monospace;font-size:12px;">
                <div><strong>Component Mean ${d.compIndex}</strong></div>
                <div>Mixture Weight: ${((d.weight ?? 0) * 100).toFixed(1)}%</div>
              </div>
            `
          }
          return `
            <div style="font-family:monospace;font-size:12px;">
              <div><strong>Assigned Component:</strong> Comp ${d.compIndex}</div>
              <div><strong>Posterior Probability:</strong> ${((d.maxProb ?? 0) * 100).toFixed(1)}%</div>
              <div><strong>Entropy:</strong> ${(d.entropy ?? 0).toFixed(4)}</div>
              <div><strong>Log Density:</strong> ${(d.logDensity ?? 0).toFixed(4)}</div>
              <div><strong>Row:</strong> #${d.obs.rowIndex}</div>
              ${d.obs.keyId ? `<div><strong>Key:</strong> ${d.obs.keyId}</div>` : ''}
              ${d.obs.lineCode || d.obs.machineCode ? `<div><strong>Scope:</strong> ${d.obs.lineCode || '?'}/${d.obs.machineCode || '?'}</div>` : ''}
              ${d.obs.defectFamily ? `<div><strong>Family:</strong> ${d.obs.defectFamily}</div>` : ''}
              <div style="font-size:12px;color:#38bdf8;margin-top:4px;">(Click point to drill down in preview grid)</div>
            </div>
          `
        },
      },
      legend: {
        top: '25px',
        right: '10px',
        textStyle: { color: '#94a3b8', fontSize: 11 },
      },
      grid: { left: '50px', right: '30px', top: '70px', bottom: '40px' },
      xAxis: {
        type: 'value',
        name: featX,
        nameLocation: 'middle',
        nameGap: 25,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        name: featY,
        nameLocation: 'middle',
        nameGap: 35,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [...seriesList, centersSeries],
    }
  }, [assignments, componentSummaries, componentsCount, covarianceType, featX, featY, means, selectedComp])

  const chartEvents = useMemo(
    () => ({
      click: (params: { data?: { value?: [number, number]; compIndex?: number; maxProb?: number; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        const rowIndex = params?.data?.obs?.rowIndex
        if (rowIndex !== undefined) {
          onSelectRowIndex(rowIndex)
        }
        if (params?.data?.value && params?.data?.compIndex !== undefined) {
          setSelectedPointPreview({
            xVal: params.data.value[0],
            yVal: params.data.value[1],
            compIndex: params.data.compIndex,
            maxProb: params.data.maxProb,
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
          })
        }
      },
      mouseover: (params: { data?: { value?: [number, number]; compIndex?: number; maxProb?: number; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        if (params?.data?.value && params?.data?.compIndex !== undefined) {
          setSelectedPointPreview({
            xVal: params.data.value[0],
            yVal: params.data.value[1],
            compIndex: params.data.compIndex,
            maxProb: params.data.maxProb,
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
          })
        }
      },
    }),
    [onSelectRowIndex]
  )

  return (
    <div className="analytics-gmm-view">
      {/* Evidence Notice */}
      <aside className="analytics-alert-banner info my-2" role="region" aria-label="GMM Unsupervised Evidence">
        <div className="alert-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="16" x2="12" y2="12" />
            <line x1="12" y1="8" x2="12.01" y2="8" />
          </svg>
        </div>
        <div className="alert-content">
          <p className="text-xs">
            Gaussian Mixture Models model data as a mixture of continuous Gaussian distributions. Soft assignment probabilities and component parameters represent probabilistic density structures; they do <strong>not</strong> prove defect taxonomy correctness without ground-truth verification.
          </p>
        </div>
      </aside>

      {/* Metric Cards */}
      <div className="analytics-summary-cards">
        <div className="analytics-stat-card">
          <span className="stat-label">Components</span>
          <span className="stat-value">{componentsCount}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Covariance Model</span>
          <span className="stat-value font-mono">{covarianceType}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Log-Likelihood</span>
          <span className="stat-value font-mono">{logLikelihood.toFixed(2)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">AIC / BIC</span>
          <span className="stat-value font-mono text-xs">
            {aic.toFixed(1)} / {bic.toFixed(1)}
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">EM Convergence</span>
          <span className={`stat-badge ${converged ? 'badge-success' : 'badge-warning'}`}>
            {converged ? `Converged (${iterations} iters)` : `Max iters (${iterations})`}
          </span>
        </div>
      </div>

      {/* 2D Scatter Chart */}
      {scatterOption ? (
        <div className="echarts-container-card my-3">
          <ReactECharts
            option={scatterOption}
            onEvents={chartEvents}
            style={{ height: '360px', width: '100%' }}
          />
        </div>
      ) : (
        <div className="analytics-empty-note">Insufficient assignments for GMM component visualization.</div>
      )}

      {selectedPointPreview && (
        <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
          <div className="preview-header">
            <span className="badge info">GMM Component Point Preview</span>
            <span className="badge primary font-mono">Component #{selectedPointPreview.compIndex}</span>
            {selectedPointPreview.rowIndex !== undefined && (
              <span className="sample-name font-mono">Row #{selectedPointPreview.rowIndex}</span>
            )}
            {selectedPointPreview.keyId && (
              <span className="text-xs text-slate-400 font-mono">Key: {selectedPointPreview.keyId}</span>
            )}
          </div>
          <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
            <div>
              <span className="text-xs text-slate-400">{featX} (Mean): </span>
              <strong className="font-mono text-cyan-400">{selectedPointPreview.xVal.toFixed(4)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">{featY} (Mean): </span>
              <strong className="font-mono text-sky-400">{selectedPointPreview.yVal.toFixed(4)}</strong>
            </div>
            {selectedPointPreview.maxProb !== undefined && (
              <div>
                <span className="text-xs text-slate-400">Post Prob: </span>
                <strong className="font-mono text-emerald-400">{(selectedPointPreview.maxProb * 100).toFixed(1)}%</strong>
              </div>
            )}
            {selectedPointPreview.defectFamily && (
              <div>
                <span className="text-xs text-slate-400">Family: </span>
                <span className="badge warning text-xs">{selectedPointPreview.defectFamily}</span>
              </div>
            )}
            {selectedPointPreview.rowIndex !== undefined && (
              <button
                type="button"
                className="btn btn-xs btn-primary ml-auto"
                onClick={() => onSelectRowIndex(selectedPointPreview.rowIndex!)}
              >
                Locate in Defect Table
              </button>
            )}
          </div>
        </div>
      )}

      {/* Component Parameters Table */}
      <div className="component-summary-section mt-4">
        <div className="section-header-row">
          <h4 className="text-sm font-semibold">GMM Component Parameters</h4>
          {selectedComp !== null && (
            <button
              type="button"
              className="btn btn-xs btn-outline"
              onClick={() => setSelectedComp(null)}
            >
              Clear Component Filter
            </button>
          )}
        </div>

        <div className="table-responsive">
          <table className="analytics-data-table">
            <thead>
              <tr>
                <th>Component</th>
                <th>Mixture Weight</th>
                <th>Observation Size</th>
                <th>Mean Vector ({selectedFeatures.join(', ')})</th>
                <th>Variance Vector</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {componentSummaries.map((c) => {
                const isSelected = selectedComp === c.componentIndex
                const color = GMM_COLORS[c.componentIndex % GMM_COLORS.length]
                const meanStr = c.meanVector.map((v) => v.toFixed(3)).join(', ')
                const varStr = c.varianceVector.map((v) => v.toFixed(3)).join(', ')

                return (
                  <tr
                    key={c.componentIndex}
                    className={isSelected ? 'selected-row' : ''}
                    onClick={() => setSelectedComp(isSelected ? null : c.componentIndex)}
                    style={{ cursor: 'pointer' }}
                  >
                    <td>
                      <span
                        className="status-pill"
                        style={{
                          backgroundColor: `${color}20`,
                          color,
                          borderColor: color,
                          borderWidth: 1,
                          borderStyle: 'solid',
                        }}
                      >
                        Comp {c.componentIndex}
                      </span>
                    </td>
                    <td className="font-mono">{(c.weight * 100).toFixed(1)}%</td>
                    <td className="font-mono">{c.size.toLocaleString()}</td>
                    <td className="font-mono text-xs">{meanStr}</td>
                    <td className="font-mono text-xs">{varStr}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-xs btn-outline"
                        onClick={(e) => {
                          e.stopPropagation()
                          setSelectedComp(isSelected ? null : c.componentIndex)
                        }}
                      >
                        {isSelected ? 'Show All' : 'Highlight'}
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
