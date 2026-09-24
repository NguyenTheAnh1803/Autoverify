import React, { useMemo, useState } from 'react'
import ReactECharts from 'echarts-for-react'
import type { KMeansResult } from '../../types'

export type KMeansChartProps = {
  result: KMeansResult
  onSelectRowIndex: (rowIndex: number) => void
}

const CLUSTER_COLORS = [
  '#38bdf8', // sky
  '#f59e0b', // amber
  '#10b981', // emerald
  '#ec4899', // pink
  '#8b5cf6', // purple
  '#06b6d4', // cyan
  '#f97316', // orange
  '#64748b', // slate
]

export const KMeansChart: React.FC<KMeansChartProps> = ({
  result,
  onSelectRowIndex,
}) => {
  const {
    k,
    inertia,
    iterations,
    converged,
    startsRun,
    silhouetteScore,
    clusterSummaries,
    assignments,
    selectedFeatures,
    centers,
    scales,
  } = result

  const [selectedCluster, setSelectedCluster] = useState<number | null>(null)
  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    xVal: number
    yVal: number
    clusterIndex: number
    dist?: number
    rowIndex?: number
    keyId?: string
    defectFamily?: string
  } | null>(null)

  // Feature dimension names for 2D scatter view
  const featX = selectedFeatures[0] || 'Feature 1'
  const featY = selectedFeatures[1] || (selectedFeatures.length > 1 ? selectedFeatures[1] : 'Feature 1 (Offset)')

  // Build scatter option
  const scatterOption = useMemo(() => {
    if (!assignments || assignments.length === 0) return null

    // Group assignments by cluster index
    const seriesList = clusterSummaries.map((cluster) => {
      const clusterIdx = cluster.clusterIndex
      const clusterAssignments = assignments.filter((a) => a.clusterIndex === clusterIdx)
      const color = CLUSTER_COLORS[clusterIdx % CLUSTER_COLORS.length]

      const data = clusterAssignments.map((a) => {
        // Calculate approximate standardized coordinates for 2D display:
        // Center/scale normalization if provided
        const mX = centers?.[0] ?? 0
        const sX = scales?.[0] && scales[0] > 0 ? scales[0] : 1
        const origX = cluster.originalCentroid?.[0] ?? 0
        const origY = cluster.originalCentroid?.[1] ?? origX

        // Jitter around scaled centroid based on distance
        const xVal = cluster.scaledCentroid?.[0] ?? (origX - mX) / sX
        const yVal = cluster.scaledCentroid?.[1] ?? (origY - (centers?.[1] ?? mX)) / (scales?.[1] ?? sX)

        return {
          value: [xVal, yVal],
          obs: a.observation,
          clusterIndex: clusterIdx,
          dist: a.distanceToCentroid,
        }
      })

      return {
        name: `Cluster ${clusterIdx}`,
        type: 'scatter',
        symbolSize: selectedCluster === null || selectedCluster === clusterIdx ? 8 : 4,
        data,
        itemStyle: {
          color,
          opacity: selectedCluster === null || selectedCluster === clusterIdx ? 0.8 : 0.2,
        },
      }
    })

    // Add Centroid points
    const centroidSeries = {
      name: 'Centroids',
      type: 'scatter',
      symbol: 'diamond',
      symbolSize: 14,
      data: clusterSummaries.map((c) => ({
        value: [c.scaledCentroid?.[0] ?? 0, c.scaledCentroid?.[1] ?? 0],
        clusterIndex: c.clusterIndex,
        size: c.size,
      })),
      itemStyle: {
        color: '#f8fafc',
        borderColor: '#0f172a',
        borderWidth: 2,
      },
    }

    return {
      title: {
        text: `K-Means Cluster Projection (K=${k})`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
        subtext: `Projection: First Two Standardized Features (${featX} vs ${featY})`,
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
            clusterIndex: number
            dist?: number
            size?: number
          }
        }) => {
          const d = params.data
          if (!d.obs) {
            return `
              <div style="font-family:monospace;font-size:12px;">
                <div><strong>Centroid of Cluster ${d.clusterIndex}</strong></div>
                <div>Cluster Size: ${(d.size ?? 0).toLocaleString()} obs</div>
              </div>
            `
          }
          return `
            <div style="font-family:monospace;font-size:12px;">
              <div><strong>Cluster Assignment:</strong> Cluster ${d.clusterIndex}</div>
              <div><strong>Row:</strong> #${d.obs.rowIndex}</div>
              ${d.obs.keyId ? `<div><strong>Key:</strong> ${d.obs.keyId}</div>` : ''}
              ${d.obs.lineCode || d.obs.machineCode ? `<div><strong>Scope:</strong> ${d.obs.lineCode || '?'}/${d.obs.machineCode || '?'}</div>` : ''}
              ${d.obs.defectFamily ? `<div><strong>Family:</strong> ${d.obs.defectFamily}</div>` : ''}
              <div><strong>Dist to Centroid:</strong> ${(d.dist ?? 0).toFixed(4)}</div>
              <div style="font-size:10px;color:#38bdf8;margin-top:4px;">(Click point to drill down in preview grid)</div>
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
        name: `${featX} (Std)`,
        nameLocation: 'middle',
        nameGap: 25,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        name: `${featY} (Std)`,
        nameLocation: 'middle',
        nameGap: 35,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [...seriesList, centroidSeries],
    }
  }, [assignments, clusterSummaries, k, featX, featY, selectedCluster, centers, scales])

  const chartEvents = useMemo(
    () => ({
      click: (params: { data?: { value?: [number, number]; clusterIndex?: number; dist?: number; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        const rowIndex = params?.data?.obs?.rowIndex
        if (rowIndex !== undefined) {
          onSelectRowIndex(rowIndex)
        }
        if (params?.data?.value && params?.data?.clusterIndex !== undefined) {
          setSelectedPointPreview({
            xVal: params.data.value[0],
            yVal: params.data.value[1],
            clusterIndex: params.data.clusterIndex,
            dist: params.data.dist,
            rowIndex: params.data.obs?.rowIndex,
            keyId: params.data.obs?.keyId,
            defectFamily: params.data.obs?.defectFamily,
          })
        }
      },
      mouseover: (params: { data?: { value?: [number, number]; clusterIndex?: number; dist?: number; obs?: { rowIndex?: number; keyId?: string; defectFamily?: string } } }) => {
        if (params?.data?.value && params?.data?.clusterIndex !== undefined) {
          setSelectedPointPreview({
            xVal: params.data.value[0],
            yVal: params.data.value[1],
            clusterIndex: params.data.clusterIndex,
            dist: params.data.dist,
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
    <div className="analytics-kmeans-view">
      {/* Evidence Notice */}
      <aside className="analytics-alert-banner info my-2" role="region" aria-label="K-Means Unsupervised Evidence">
        <div className="alert-icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="16" x2="12" y2="12" />
            <line x1="12" y1="8" x2="12.01" y2="8" />
          </svg>
        </div>
        <div className="alert-content">
          <p className="text-xs">
            K-Means discovers mathematical feature clusters via Euclidean distance minimization. Clusters group geometrically similar inspection records; they do <strong>not</strong> correspond to verified defect classes or ground-truth classifications without independent review.
          </p>
        </div>
      </aside>

      {/* Metric Cards */}
      <div className="analytics-summary-cards">
        <div className="analytics-stat-card">
          <span className="stat-label">Clusters (K)</span>
          <span className="stat-value">{k}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Inertia (Within-Cluster SS)</span>
          <span className="stat-value font-mono">{inertia.toFixed(2)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Convergence Status</span>
          <span className={`stat-badge ${converged ? 'badge-success' : 'badge-warning'}`}>
            {converged ? `Converged (${iterations} iters)` : `Limit reached (${iterations} iters)`}
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Starts Run / Best Seed</span>
          <span className="stat-value font-mono">{startsRun} restarts</span>
        </div>
        {silhouetteScore !== null && silhouetteScore !== undefined && (
          <div className="analytics-stat-card">
            <span className="stat-label">Silhouette Coefficient</span>
            <span className="stat-value font-mono">{silhouetteScore.toFixed(3)}</span>
          </div>
        )}
      </div>

      {/* 2D Projection Chart */}
      {scatterOption ? (
        <div className="echarts-container-card my-3">
          <ReactECharts
            option={scatterOption}
            onEvents={chartEvents}
            style={{ height: '360px', width: '100%' }}
          />
        </div>
      ) : (
        <div className="analytics-empty-note">Insufficient assignment data for cluster visualization.</div>
      )}

      {selectedPointPreview && (
        <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
          <div className="preview-header">
            <span className="badge info">K-Means Cluster Feature Point Preview</span>
            <span className="badge primary font-mono">Cluster #{selectedPointPreview.clusterIndex}</span>
            {selectedPointPreview.rowIndex !== undefined && (
              <span className="sample-name font-mono">Row #{selectedPointPreview.rowIndex}</span>
            )}
            {selectedPointPreview.keyId && (
              <span className="text-xs text-slate-400 font-mono">Key: {selectedPointPreview.keyId}</span>
            )}
          </div>
          <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
            <div>
              <span className="text-xs text-slate-400">{featX} (Std): </span>
              <strong className="font-mono text-cyan-400">{selectedPointPreview.xVal.toFixed(4)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">{featY} (Std): </span>
              <strong className="font-mono text-sky-400">{selectedPointPreview.yVal.toFixed(4)}</strong>
            </div>
            {selectedPointPreview.dist !== undefined && (
              <div>
                <span className="text-xs text-slate-400">Dist to Centroid: </span>
                <strong className="font-mono text-emerald-400">{selectedPointPreview.dist.toFixed(4)}</strong>
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

      {/* Centroid & Size Summary Table */}
      <div className="cluster-summary-section mt-4">
        <div className="section-header-row">
          <h4 className="text-sm font-semibold">Cluster Profiles & Centroids</h4>
          {selectedCluster !== null && (
            <button
              type="button"
              className="btn btn-xs btn-outline"
              onClick={() => setSelectedCluster(null)}
            >
              Clear Cluster Filter
            </button>
          )}
        </div>

        <div className="table-responsive">
          <table className="analytics-data-table">
            <thead>
              <tr>
                <th>Cluster</th>
                <th>Observation Count</th>
                <th>Share (%)</th>
                <th>Mean Dist to Centroid</th>
                <th>Centroid Coordinates ({selectedFeatures.join(', ')})</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {clusterSummaries.map((c) => {
                const isSelected = selectedCluster === c.clusterIndex
                const color = CLUSTER_COLORS[c.clusterIndex % CLUSTER_COLORS.length]
                const centroidStr = c.originalCentroid
                  ? c.originalCentroid.map((v) => v.toFixed(3)).join(', ')
                  : 'N/A'

                return (
                  <tr
                    key={c.clusterIndex}
                    className={isSelected ? 'selected-row' : ''}
                    onClick={() => setSelectedCluster(isSelected ? null : c.clusterIndex)}
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
                        Cluster {c.clusterIndex}
                      </span>
                    </td>
                    <td className="font-mono">{c.size.toLocaleString()}</td>
                    <td className="font-mono">{c.percentage.toFixed(1)}%</td>
                    <td className="font-mono">{c.meanDistance.toFixed(4)}</td>
                    <td className="font-mono text-xs">{centroidStr}</td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-xs btn-outline"
                        onClick={(e) => {
                          e.stopPropagation()
                          setSelectedCluster(isSelected ? null : c.clusterIndex)
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
