import React, { useState, useMemo } from 'react'
import ReactECharts from 'echarts-for-react'
import type {
  FeatureRelationshipResult,
  FeatureRelationshipPair,
  SessionScopeFacetsResponse,
} from '../../types'

export type FeatureRelationshipChartProps = {
  result: FeatureRelationshipResult
  facets: SessionScopeFacetsResponse | null
  onSelectRowIndex: (rowIndex: number) => void
}

export const FeatureRelationshipChart: React.FC<FeatureRelationshipChartProps> = ({
  result,
  facets,
  onSelectRowIndex,
}) => {
  const {
    features,
    pearsonMatrix,
    spearmanMatrix,
    overlapCounts,
    rankedPairs,
    scatterPoints,
    totalObservations,
    includedObservations,
  } = result

  const [metric, setMetric] = useState<'pearson' | 'spearman'>('pearson')
  const [selectedPair, setSelectedPair] = useState<FeatureRelationshipPair | null>(
    rankedPairs.length > 0 ? rankedPairs[0] : null
  )
  const [filterStrength, setFilterStrength] = useState<string>('All')
  const [activeSubTab, setActiveSubTab] = useState<'pairs' | 'coverage'>('pairs')
  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    xVal: number
    yVal: number
    rowIndex?: number
    keyId?: string
    defectFamily?: string
    featureA: string
    featureB: string
  } | null>(null)

  // Heatmap option
  const heatmapOption = useMemo(() => {
    if (!features || features.length === 0) return null

    const matrix = metric === 'pearson' ? pearsonMatrix : spearmanMatrix
    const data: Array<[number, number, number | null, number]> = []

    for (let i = 0; i < features.length; i++) {
      for (let j = 0; j < features.length; j++) {
        const val = matrix[i]?.[j] ?? null
        const overlap = overlapCounts[i]?.[j] ?? 0
        data.push([j, i, val !== null ? Math.round(val * 1000) / 1000 : null, overlap])
      }
    }

    return {
      title: {
        text: `Feature Correlation Matrix (${metric.toUpperCase()})`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        position: 'top',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: { data: [number, number, number | null, number] }) => {
          const [colIdx, rowIdx, corrVal, overlap] = params.data
          const fA = features[rowIdx]
          const fB = features[colIdx]
          const corrText = corrVal !== null ? corrVal.toFixed(4) : 'N/A'
          return `
            <div style="font-family:monospace;font-size:12px;">
              <div><strong>Pair:</strong> ${fA} \u2194 ${fB}</div>
              <div><strong>${metric.toUpperCase()} Correlation:</strong> ${corrText}</div>
              <div><strong>Overlap Observations:</strong> ${overlap.toLocaleString()}</div>
              <div style="font-size:10px;color:#94a3b8;margin-top:4px;">(Click cell to select pair for scatter plot)</div>
            </div>
          `
        },
      },
      grid: {
        left: '120px',
        right: '60px',
        top: '40px',
        bottom: '80px',
      },
      xAxis: {
        type: 'category',
        data: features,
        axisLabel: {
          color: '#94a3b8',
          fontSize: 10,
          rotate: 45,
          interval: 0,
        },
        splitArea: { show: true },
      },
      yAxis: {
        type: 'category',
        data: features,
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        splitArea: { show: true },
      },
      visualMap: {
        min: -1,
        max: 1,
        calculable: true,
        orient: 'horizontal',
        left: 'center',
        bottom: '10px',
        textStyle: { color: '#94a3b8', fontSize: 10 },
        inRange: {
          color: ['#3b82f6', '#1e293b', '#ef4444'],
        },
      },
      series: [
        {
          name: `${metric.toUpperCase()} Correlation`,
          type: 'heatmap',
          data,
          label: {
            show: features.length <= 10,
            color: '#f8fafc',
            fontSize: 9,
            formatter: (p: { data: [number, number, number | null, number] }) =>
              p.data[2] !== null ? p.data[2].toFixed(2) : '',
          },
          emphasis: {
            itemStyle: {
              shadowBlur: 10,
              shadowColor: 'rgba(0, 0, 0, 0.5)',
            },
          },
        },
      ],
    }
  }, [features, metric, pearsonMatrix, spearmanMatrix, overlapCounts])

  // Heatmap click handler
  const heatmapEvents = useMemo(
    () => ({
      click: (params: { data?: [number, number, number | null, number] }) => {
        if (!params.data) return
        const [colIdx, rowIdx] = params.data
        const fA = features[rowIdx]
        const fB = features[colIdx]
        const pair = rankedPairs.find(
          (p) =>
            (p.featureA === fA && p.featureB === fB) ||
            (p.featureA === fB && p.featureB === fA)
        )
        if (pair) {
          setSelectedPair(pair)
        } else {
          setSelectedPair({
            featureA: fA,
            featureB: fB,
            pearson: pearsonMatrix[rowIdx]?.[colIdx],
            spearman: spearmanMatrix[rowIdx]?.[colIdx],
            overlapCount: overlapCounts[rowIdx]?.[colIdx] ?? 0,
            strength: 'Unknown',
          })
        }
      },
    }),
    [features, rankedPairs, pearsonMatrix, spearmanMatrix, overlapCounts]
  )

  // Bounded Scatter Plot for Selected Pair
  const scatterOption = useMemo(() => {
    if (!selectedPair) return null

    const points = scatterPoints || []
    const seriesData = points.map((pt) => ({
      value: [pt.x, pt.y],
      obs: pt.observation,
    }))

    return {
      title: {
        text: `Pair Scatter: ${selectedPair.featureA} vs ${selectedPair.featureB}`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
        subtext: `Overlap: ${selectedPair.overlapCount.toLocaleString()} obs | Pearson: ${selectedPair.pearson !== null && selectedPair.pearson !== undefined ? selectedPair.pearson.toFixed(4) : 'N/A'} | Spearman: ${selectedPair.spearman !== null && selectedPair.spearman !== undefined ? selectedPair.spearman.toFixed(4) : 'N/A'}`,
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
            obs: {
              rowIndex: number
              keyId?: string
              lineCode?: string
              machineCode?: string
              stationCode?: string
              defectFamily?: string
            }
          }
        }) => {
          const { value, obs } = params.data
          return `
            <div style="font-family:monospace;font-size:12px;">
              <div><strong>Row:</strong> #${obs.rowIndex}</div>
              ${obs.keyId ? `<div><strong>Key:</strong> ${obs.keyId}</div>` : ''}
              ${obs.lineCode || obs.machineCode ? `<div><strong>Scope:</strong> ${obs.lineCode || '?'}/${obs.machineCode || '?'}</div>` : ''}
              ${obs.defectFamily ? `<div><strong>Family:</strong> ${obs.defectFamily}</div>` : ''}
              <div style="margin-top:4px;"><strong>${selectedPair.featureA}:</strong> ${value[0].toFixed(4)}</div>
              <div><strong>${selectedPair.featureB}:</strong> ${value[1].toFixed(4)}</div>
              <div style="font-size:10px;color:#38bdf8;margin-top:4px;">(Click point to drill down in preview grid)</div>
            </div>
          `
        },
      },
      grid: { left: '60px', right: '40px', top: '55px', bottom: '40px' },
      xAxis: {
        type: 'value',
        name: selectedPair.featureA,
        nameLocation: 'middle',
        nameGap: 25,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        name: selectedPair.featureB,
        nameLocation: 'middle',
        nameGap: 35,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'Observations',
          type: 'scatter',
          symbolSize: 6,
          data: seriesData,
          itemStyle: {
            color: '#38bdf8',
            opacity: 0.7,
          },
        },
      ],
    }
  }, [selectedPair, scatterPoints])

  const scatterEvents = useMemo(
    () => ({
      click: (params: { data?: { value?: [number, number]; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        const rowIndex = params?.data?.obs?.rowIndex
        if (rowIndex !== undefined) {
          onSelectRowIndex(rowIndex)
        }
        if (params?.data?.value && selectedPair) {
          setSelectedPointPreview({
            xVal: params.data.value[0],
            yVal: params.data.value[1],
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
            featureA: selectedPair.featureA,
            featureB: selectedPair.featureB,
          })
        }
      },
      mouseover: (params: { data?: { value?: [number, number]; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        if (params?.data?.value && selectedPair) {
          setSelectedPointPreview({
            xVal: params.data.value[0],
            yVal: params.data.value[1],
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
            featureA: selectedPair.featureA,
            featureB: selectedPair.featureB,
          })
        }
      },
    }),
    [onSelectRowIndex, selectedPair]
  )

  const filteredRankedPairs = useMemo(() => {
    if (filterStrength === 'All') return rankedPairs
    return rankedPairs.filter((p) => p.strength.toLowerCase() === filterStrength.toLowerCase())
  }, [rankedPairs, filterStrength])

  return (
    <div className="analytics-feature-relationship-view">
      {/* Evidence Banner */}
      <aside className="analytics-alert-banner info my-2" role="region" aria-label="Feature Correlation Evidence">
        <div className="alert-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="16" x2="12" y2="12" />
            <line x1="12" y1="8" x2="12.01" y2="8" />
          </svg>
        </div>
        <div className="alert-content">
          <p className="text-xs">
            Correlation reflects statistical linear (Pearson) or monotonic (Spearman) co-variation across observation pairs ({includedObservations.toLocaleString()} of {totalObservations.toLocaleString()} total observations). High correlation does not imply causal defect mechanisms.
          </p>
        </div>
      </aside>

      {/* Toolbar */}
      <div className="analytics-chart-toolbar">
        <div className="toolbar-group">
          <label className="text-xs font-semibold mr-2">Correlation Metric:</label>
          <div className="btn-group">
            <button
              type="button"
              className={`btn btn-xs ${metric === 'pearson' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setMetric('pearson')}
            >
              Pearson (r)
            </button>
            <button
              type="button"
              className={`btn btn-xs ${metric === 'spearman' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setMetric('spearman')}
            >
              Spearman (ρ)
            </button>
          </div>
        </div>

        <div className="toolbar-group">
          <label className="text-xs font-semibold mr-2">View Mode:</label>
          <div className="btn-group">
            <button
              type="button"
              className={`btn btn-xs ${activeSubTab === 'pairs' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setActiveSubTab('pairs')}
            >
              Pair Ranking & Scatter
            </button>
            <button
              type="button"
              className={`btn btn-xs ${activeSubTab === 'coverage' ? 'btn-primary' : 'btn-outline'}`}
              onClick={() => setActiveSubTab('coverage')}
            >
              Feature \u00d7 Defect Coverage
            </button>
          </div>
        </div>
      </div>

      {activeSubTab === 'pairs' ? (
        <div className="relationship-split-layout">
          {/* Left: Heatmap Matrix */}
          <div className="relationship-heatmap-panel">
            {heatmapOption ? (
              <div className="echarts-container-card">
                <ReactECharts
                  option={heatmapOption}
                  onEvents={heatmapEvents}
                  style={{ height: '380px', width: '100%' }}
                />
              </div>
            ) : (
              <div className="analytics-empty-note">Insufficient features for correlation heatmap.</div>
            )}
          </div>

          {/* Right: Pair Scatter */}
          <div className="relationship-scatter-panel">
            {selectedPair ? (
              scatterOption ? (
                <>
                  <div className="echarts-container-card">
                    <ReactECharts
                      option={scatterOption}
                      onEvents={scatterEvents}
                      style={{ height: '380px', width: '100%' }}
                    />
                  </div>
                  {selectedPointPreview && (
                    <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
                      <div className="preview-header">
                        <span className="badge info">Pair Scatter Observation Preview</span>
                        {selectedPointPreview.rowIndex !== undefined && (
                          <span className="sample-name font-mono">Row #{selectedPointPreview.rowIndex}</span>
                        )}
                        {selectedPointPreview.keyId && (
                          <span className="text-xs text-slate-400 font-mono">Key: {selectedPointPreview.keyId}</span>
                        )}
                      </div>
                      <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
                        <div>
                          <span className="text-xs text-slate-400">{selectedPointPreview.featureA}: </span>
                          <strong className="font-mono text-cyan-400">{selectedPointPreview.xVal.toFixed(4)}</strong>
                        </div>
                        <div>
                          <span className="text-xs text-slate-400">{selectedPointPreview.featureB}: </span>
                          <strong className="font-mono text-sky-400">{selectedPointPreview.yVal.toFixed(4)}</strong>
                        </div>
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
                </>
              ) : (
                <div className="analytics-empty-note">No scatter points returned for this pair.</div>
              )
            ) : (
              <div className="analytics-empty-note">Select a cell in the correlation matrix or row in the table below to inspect pair scatter.</div>
            )}
          </div>
        </div>
      ) : (
        /* Feature x Defect Family Coverage View */
        <div className="analytics-coverage-panel">
          <div className="coverage-header mb-2">
            <h4 className="text-sm font-semibold">Scoped Feature \u00d7 Defect Family Observation Coverage</h4>
            <p className="text-xs text-muted">
              Honest representation of populated features across detected defect families based on session scope facets.
            </p>
          </div>

          {facets?.defectFamilies && facets.defectFamilies.length > 0 ? (
            <div className="table-responsive">
              <table className="analytics-data-table">
                <thead>
                  <tr>
                    <th>Defect Family</th>
                    <th>Observations</th>
                    <th>Available Features</th>
                    <th>Coverage Assessment</th>
                  </tr>
                </thead>
                <tbody>
                  {facets.defectFamilies.map((df) => (
                    <tr key={df.value || 'unknown'}>
                      <td className="font-mono">{df.value || 'Unknown / Unassigned'}</td>
                      <td>{df.count.toLocaleString()}</td>
                      <td>{features.length} continuous features</td>
                      <td>
                        <span className="status-pill pill-success">
                          {(df.count / (totalObservations || 1) * 100).toFixed(1)}% of session
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="analytics-empty-note">
              No distinct Defect Family facets recorded in this session.
            </div>
          )}
        </div>
      )}

      {/* Ranked Pair Table */}
      <div className="relationship-ranked-section mt-4">
        <div className="section-header-row">
          <h4 className="text-sm font-semibold">
            Ranked Feature Correlation Pairs ({filteredRankedPairs.length})
          </h4>
          <div className="filter-group">
            <label className="text-xs mr-2">Filter Strength:</label>
            <select
              className="form-control text-xs"
              value={filterStrength}
              onChange={(e) => setFilterStrength(e.target.value)}
            >
              <option value="All">All Strengths</option>
              <option value="Strong">{'Strong (|r| \u2265 0.7)'}</option>
              <option value="Moderate">{'Moderate (0.3 \u2264 |r| < 0.7)'}</option>
              <option value="Weak">{'Weak (|r| < 0.3)'}</option>
            </select>
          </div>
        </div>

        <div className="table-responsive max-h-60 overflow-y-auto">
          <table className="analytics-data-table">
            <thead>
              <tr>
                <th>Feature A</th>
                <th>Feature B</th>
                <th>Pearson (r)</th>
                <th>Spearman (ρ)</th>
                <th>Overlap Count</th>
                <th>P-Value</th>
                <th>Strength</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {filteredRankedPairs.map((p, idx) => {
                const isSelected =
                  selectedPair?.featureA === p.featureA && selectedPair?.featureB === p.featureB
                return (
                  <tr
                    key={idx}
                    className={isSelected ? 'selected-row' : ''}
                    onClick={() => setSelectedPair(p)}
                    style={{ cursor: 'pointer' }}
                  >
                    <td className="font-mono">{p.featureA}</td>
                    <td className="font-mono">{p.featureB}</td>
                    <td className="font-mono">
                      {p.pearson !== null && p.pearson !== undefined ? p.pearson.toFixed(4) : 'N/A'}
                    </td>
                    <td className="font-mono">
                      {p.spearman !== null && p.spearman !== undefined ? p.spearman.toFixed(4) : 'N/A'}
                    </td>
                    <td>{p.overlapCount.toLocaleString()}</td>
                    <td className="font-mono">
                      {p.pValue !== null && p.pValue !== undefined ? p.pValue.toExponential(2) : 'N/A'}
                    </td>
                    <td>
                      <span
                        className={`status-pill ${
                          p.strength === 'Strong'
                            ? 'pill-warning'
                            : p.strength === 'Moderate'
                            ? 'pill-info'
                            : 'pill-neutral'
                        }`}
                      >
                        {p.strength}
                      </span>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-xs btn-outline"
                        onClick={(e) => {
                          e.stopPropagation()
                          setSelectedPair(p)
                        }}
                      >
                        Inspect Scatter
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
