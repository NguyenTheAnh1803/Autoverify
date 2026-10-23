import React, { useMemo, useState } from 'react'
import ReactECharts from '../ReactECharts'
import type { IsolationForestResult, TopAnomalyItem } from '../../types'

export type IsolationForestChartProps = {
  result: IsolationForestResult
  onSelectRowIndex: (rowIndex: number) => void
}

export const IsolationForestChart: React.FC<IsolationForestChartProps> = ({
  result,
  onSelectRowIndex,
}) => {
  const {
    treesCount,
    subsampleSize,
    contamination,
    scoreThreshold,
    totalObservations,
    anomalyCount,
    selectedFeatures,
    featureContributions,
    assignments,
    topAnomalies,
  } = result

  const [selectedAnomaly, setSelectedAnomaly] = useState<TopAnomalyItem | null>(
    topAnomalies.length > 0 ? topAnomalies[0] : null
  )
  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    sampleIdx: number
    anomalyScore: number
    isAnomaly?: boolean
    rowIndex?: number
    keyId?: string
    defectFamily?: string
  } | null>(null)

  // Chart: Anomaly Score Distribution Scatter / Line
  const scoreChartOption = useMemo(() => {
    if (!assignments || assignments.length === 0) return null

    // Downsample if more than 2,000 points to keep 60fps
    const step = Math.max(1, Math.floor(assignments.length / 1500))
    const sampled = assignments.filter((_, idx) => idx % step === 0)

    const normalData: Array<{ value: [number, number]; obs: unknown }> = []
    const anomalyData: Array<{ value: [number, number]; obs: unknown }> = []

    sampled.forEach((a, i) => {
      const pt = {
        value: [i + 1, a.anomalyScore] as [number, number],
        obs: a.observation,
      }
      if (a.isAnomaly) {
        anomalyData.push(pt)
      } else {
        normalData.push(pt)
      }
    })

    return {
      title: {
        text: `Isolation Forest Anomaly Scores (Contamination: ${(contamination * 100).toFixed(1)}%)`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
        subtext: `Score Semantics: Higher = More Anomalous | Threshold: ${scoreThreshold.toFixed(4)}`,
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
          const isAbove = value[1] >= scoreThreshold
          return `
            <div style="font-family:monospace;font-size:12px;">
              <div><strong>Observation:</strong> #${obs.rowIndex}</div>
              ${obs.keyId ? `<div><strong>Key:</strong> ${obs.keyId}</div>` : ''}
              ${obs.lineCode || obs.machineCode ? `<div><strong>Scope:</strong> ${obs.lineCode || '?'}/${obs.machineCode || '?'}</div>` : ''}
              ${obs.defectFamily ? `<div><strong>Family:</strong> ${obs.defectFamily}</div>` : ''}
              <div style="margin-top:4px;"><strong>Anomaly Score:</strong> ${value[1].toFixed(4)}</div>
              <div><strong style="color:${isAbove ? '#ef4444' : '#10b981'};">${isAbove ? 'Flagged Anomaly' : 'Nominal Distribution'}</strong></div>
              <div style="font-size:12px;color:#38bdf8;margin-top:4px;">(Click point to drill down in preview grid)</div>
            </div>
          `
        },
      },
      grid: { left: '50px', right: '40px', top: '65px', bottom: '40px' },
      xAxis: {
        type: 'value',
        name: 'Sample Index (Sequence)',
        nameLocation: 'middle',
        nameGap: 25,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        name: 'Anomaly Score',
        nameLocation: 'middle',
        nameGap: 35,
        min: 0,
        max: 1,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'Nominal Points',
          type: 'scatter',
          symbolSize: 5,
          data: normalData,
          itemStyle: { color: '#38bdf8', opacity: 0.5 },
        },
        {
          name: 'Anomalies',
          type: 'scatter',
          symbolSize: 8,
          data: anomalyData,
          itemStyle: { color: '#ef4444', opacity: 0.9 },
          markLine: {
            silent: true,
            symbol: 'none',
            data: [
              {
                yAxis: scoreThreshold,
                lineStyle: { color: '#f87171', type: 'dashed', width: 2 },
                label: {
                  formatter: `Threshold: ${scoreThreshold.toFixed(4)}`,
                  position: 'insideEndTop',
                  color: '#f87171',
                  fontSize: 10,
                },
              },
            ],
          },
        },
      ],
    }
  }, [assignments, contamination, scoreThreshold])

  const chartEvents = useMemo(
    () => ({
      click: (params: { data?: { value?: [number, number]; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        const rowIndex = params?.data?.obs?.rowIndex
        if (rowIndex !== undefined) {
          onSelectRowIndex(rowIndex)
        }
        if (params?.data?.value) {
          setSelectedPointPreview({
            sampleIdx: params.data.value[0],
            anomalyScore: params.data.value[1],
            isAnomaly: params.data.value[1] >= scoreThreshold,
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
          })
        }
      },
      mouseover: (params: { data?: { value?: [number, number]; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        if (params?.data?.value) {
          setSelectedPointPreview({
            sampleIdx: params.data.value[0],
            anomalyScore: params.data.value[1],
            isAnomaly: params.data.value[1] >= scoreThreshold,
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
          })
        }
      },
    }),
    [onSelectRowIndex, scoreThreshold]
  )

  // Global Feature Contribution bar option
  const contributionOption = useMemo(() => {
    if (!featureContributions || Object.keys(featureContributions).length === 0) return null

    const entries = Object.entries(featureContributions).sort((a, b) => b[1] - a[1])
    const categories = entries.map(([name]) => name)
    const values = entries.map(([, val]) => Math.round(val * 1000) / 1000)

    return {
      title: {
        text: 'Feature Anomaly Contribution (Isolation Importance)',
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: Array<{ name: string; value: number }>) => {
          const item = params[0]
          return `
            <div style="font-family:monospace;font-size:12px;">
              <div><strong>Feature:</strong> ${item.name}</div>
              <div><strong>Relative Importance:</strong> ${(item.value * 100).toFixed(1)}%</div>
            </div>
          `
        },
      },
      grid: { left: '120px', right: '30px', top: '40px', bottom: '25px' },
      xAxis: {
        type: 'value',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'category',
        data: categories,
        inverse: true,
        axisLine: { lineStyle: { color: '#475569' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'Contribution',
          type: 'bar',
          data: values,
          itemStyle: { color: '#f59e0b' },
        },
      ],
    }
  }, [featureContributions])

  return (
    <div className="analytics-isolation-forest-view">
      {/* Evidence Banner */}
      <aside className="analytics-alert-banner info my-2" role="region" aria-label="Isolation Forest Anomaly Evidence">
        <div className="alert-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="16" x2="12" y2="12" />
            <line x1="12" y1="8" x2="12.01" y2="8" />
          </svg>
        </div>
        <div className="alert-content">
          <p className="text-xs">
            Isolation Forest isolates anomalies via recursive random hyperplanes. Higher anomaly scores (closer to 1.0) indicate faster isolation and lower statistical density. Scores are relative diagnostic signals; they do <strong>not</strong> prove defect status or escape risk without verified engineer review.
          </p>
        </div>
      </aside>

      {/* Summary Cards */}
      <div className="analytics-summary-cards">
        <div className="analytics-stat-card">
          <span className="stat-label">Evaluated Observations</span>
          <span className="stat-value">{totalObservations.toLocaleString()}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Isolated Anomalies</span>
          <span className="stat-value font-mono" style={{ color: '#ef4444' }}>
            {anomalyCount.toLocaleString()} ({((anomalyCount / (totalObservations || 1)) * 100).toFixed(1)}%)
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Configured Contamination</span>
          <span className="stat-value font-mono">{(contamination * 100).toFixed(1)}%</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Score Threshold</span>
          <span className="stat-value font-mono">{scoreThreshold.toFixed(4)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Forest Geometry</span>
          <span className="stat-value font-mono text-xs">
            {treesCount} trees | {subsampleSize} subsample | {selectedFeatures.length} features
          </span>
        </div>
      </div>

      {/* Charts Grid */}
      <div className="relationship-split-layout my-3">
        {/* Left: Score Distribution */}
        <div className="relationship-scatter-panel">
          {scoreChartOption ? (
            <>
              <div className="echarts-container-card">
                <ReactECharts
                  option={scoreChartOption}
                  onEvents={chartEvents}
                  style={{ height: '340px', width: '100%' }}
                />
              </div>
              {selectedPointPreview && (
                <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
                  <div className="preview-header">
                    <span className="badge info">Isolation Forest Point Preview</span>
                    <span className="sample-name font-mono">Sample #{selectedPointPreview.sampleIdx}</span>
                    {selectedPointPreview.rowIndex !== undefined && (
                      <span className="text-xs text-slate-400 font-mono">Row #{selectedPointPreview.rowIndex}</span>
                    )}
                    {selectedPointPreview.isAnomaly && (
                      <span className="badge danger">Outlier Anomaly</span>
                    )}
                    {selectedPointPreview.keyId && (
                      <span className="text-xs text-slate-400 font-mono">Key: {selectedPointPreview.keyId}</span>
                    )}
                  </div>
                  <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
                    <div>
                      <span className="text-xs text-slate-400">Score: </span>
                      <strong className="font-mono text-rose-400">{selectedPointPreview.anomalyScore.toFixed(4)}</strong>
                    </div>
                    <div>
                      <span className="text-xs text-slate-400">Threshold: </span>
                      <span className="font-mono text-slate-300">{scoreThreshold.toFixed(4)}</span>
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
            <div className="analytics-empty-note">No anomaly score series available.</div>
          )}
        </div>

        {/* Right: Feature Contributions */}
        <div className="relationship-heatmap-panel">
          {contributionOption ? (
            <div className="echarts-container-card">
              <ReactECharts
                option={contributionOption}
                style={{ height: '340px', width: '100%' }}
              />
            </div>
          ) : (
            <div className="analytics-empty-note">No feature contribution data computed.</div>
          )}
        </div>
      </div>

      {/* Top Anomalies Ranked Table */}
      <div className="isolation-anomalies-section mt-4">
        <div className="section-header-row">
          <h4 className="text-sm font-semibold">
            Top Ranked Anomalies (Showing {topAnomalies.length} Most Isolated Observations)
          </h4>
        </div>

        <div className="table-responsive max-h-60 overflow-y-auto">
          <table className="analytics-data-table">
            <thead>
              <tr>
                <th>Rank</th>
                <th>Grid Row</th>
                <th>Anomaly Score</th>
                <th>Key / ID</th>
                <th>Line / Machine / Station</th>
                <th>Top Contributing Features</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {topAnomalies.map((item) => {
                const isSelected = selectedAnomaly?.observation.observationId === item.observation.observationId
                const topContribs = Object.entries(item.featureContributions || {})
                  .sort((a, b) => b[1] - a[1])
                  .slice(0, 3)
                  .map(([feat, contrib]) => `${feat} (${(contrib * 100).toFixed(0)}%)`)
                  .join(', ')

                return (
                  <tr
                    key={item.rank}
                    className={isSelected ? 'selected-row' : ''}
                    onClick={() => setSelectedAnomaly(item)}
                    style={{ cursor: 'pointer' }}
                  >
                    <td>
                      <span className="font-mono font-bold">#{item.rank}</span>
                    </td>
                    <td>
                      <span className="status-pill pill-danger font-mono">
                        Row #{item.observation.rowIndex}
                      </span>
                    </td>
                    <td className="font-mono font-bold text-danger">
                      {item.anomalyScore.toFixed(4)}
                    </td>
                    <td className="font-mono text-xs">{item.observation.keyId || 'N/A'}</td>
                    <td className="font-mono text-xs">
                      {item.observation.lineCode || '?'}/{item.observation.machineCode || '?'}/{item.observation.stationCode || '?'}
                    </td>
                    <td className="font-mono text-xs">{topContribs || 'N/A'}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-xs btn-primary"
                        onClick={(e) => {
                          e.stopPropagation()
                          onSelectRowIndex(item.observation.rowIndex)
                        }}
                        title={`Drill down to row #${item.observation.rowIndex} in Defect Preview Grid`}
                      >
                        Locate in Grid
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
