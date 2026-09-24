import React, { useState, useMemo } from 'react'
import ReactECharts from 'echarts-for-react'
import type { PcaResult } from '../../types'

export type PcaChartsProps = {
  result: PcaResult
  onSelectRowIndex: (rowIndex: number) => void
}

export const PcaCharts: React.FC<PcaChartsProps> = ({ result, onSelectRowIndex }) => {
  const {
    featureNames,
    totalSamples,
    confidenceLevel,
    hotellingT2Threshold,
    speQThreshold,
    components,
    sampledPoints,
    totalHotellingOutliers,
    totalSpeOutliers,
    warnings,
  } = result

  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    pc1: number
    pc2: number
    rowIndex: number
    hotellingT2: number
    speQ: number
    isHotellingOutlier: boolean
    isSpeOutlier: boolean
  } | null>(null)

  // 1. Scree Plot Option
  const screeOption = useMemo(() => {
    if (!components || components.length === 0) return null

    const xData = components.map((c) => c.name || `PC${c.componentIndex + 1}`)
    const varData = components.map((c) => (c.explainedVarianceRatio * 100).toFixed(2))
    const cumData = components.map((c) => (c.cumulativeVarianceRatio * 100).toFixed(2))

    return {
      title: {
        text: 'PCA Scree Plot (Explained & Cumulative Variance)',
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: Array<{ seriesName: string; value: string; axisValue: string }>) => `
          <div style="font-family:monospace;">
            <div><strong>${params[0].axisValue}</strong></div>
            <div>Explained Var: ${params[0].value}%</div>
            <div>Cumulative Var: ${params[1]?.value}%</div>
          </div>
        `,
      },
      legend: {
        data: ['Explained Variance (%)', 'Cumulative Variance (%)'],
        textStyle: { color: '#94a3b8' },
        right: '20px',
        top: '5px',
      },
      grid: { left: '45px', right: '45px', top: '40px', bottom: '30px' },
      xAxis: {
        type: 'category',
        data: xData,
        axisLine: { lineStyle: { color: '#475569' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: [
        {
          type: 'value',
          name: '% Explained',
          max: 100,
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { lineStyle: { color: '#1e293b' } },
          axisLabel: { color: '#94a3b8', fontSize: 10 },
          nameTextStyle: { color: '#94a3b8', fontSize: 10 },
        },
        {
          type: 'value',
          name: '% Cumulative',
          max: 100,
          axisLine: { lineStyle: { color: '#475569' } },
          splitLine: { show: false },
          axisLabel: { color: '#94a3b8', fontSize: 10 },
          nameTextStyle: { color: '#94a3b8', fontSize: 10 },
        },
      ],
      series: [
        {
          name: 'Explained Variance (%)',
          type: 'bar',
          data: varData,
          itemStyle: { color: '#0284c7' },
        },
        {
          name: 'Cumulative Variance (%)',
          type: 'line',
          yAxisIndex: 1,
          data: cumData,
          itemStyle: { color: '#10b981' },
          lineStyle: { color: '#10b981', width: 2 },
          symbol: 'circle',
          symbolSize: 6,
        },
      ],
    }
  }, [components])

  // 2. Score Plot (PC1 vs PC2)
  const scoreOption = useMemo(() => {
    if (!sampledPoints || sampledPoints.length === 0) return null

    const data = sampledPoints.map((p) => {
      let color = '#38bdf8'
      if (p.isHotellingOutlier && p.isSpeOutlier) {
        color = '#ef4444' // both
      } else if (p.isHotellingOutlier) {
        color = '#f97316' // Hotelling T2
      } else if (p.isSpeOutlier) {
        color = '#a855f7' // SPE Q
      }

      return {
        value: [p.pc1, p.pc2],
        rowIndex: p.rowIndex,
        hotellingT2: p.hotellingT2,
        speQ: p.speQ,
        isHotellingOutlier: p.isHotellingOutlier,
        isSpeOutlier: p.isSpeOutlier,
        itemStyle: { color },
      }
    })

    return {
      title: {
        text: 'PCA Score Plot (PC1 vs PC2) & Multivariate Outliers',
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'item',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: { data: { value: [number, number]; rowIndex: number; hotellingT2: number; speQ: number; isHotellingOutlier: boolean; isSpeOutlier: boolean } }) => {
          const pt = params.data
          const outlierStatus = []
          if (pt.isHotellingOutlier) outlierStatus.push('T² Outlier')
          if (pt.isSpeOutlier) outlierStatus.push('SPE/Q Outlier')
          const badge = outlierStatus.length > 0
            ? `<div style="color:#f87171;font-weight:bold;margin-top:4px;">[Outlier] ${outlierStatus.join(' + ')}</div>`
            : '<div style="color:#10b981;font-size:11px;">Within Normal Boundaries</div>'

          return `
            <div style="font-family:monospace;">
              <div><strong>Grid Row:</strong> #${pt.rowIndex}</div>
              <div><strong>PC1:</strong> ${pt.value[0].toFixed(3)}</div>
              <div><strong>PC2:</strong> ${pt.value[1].toFixed(3)}</div>
              <div><strong>Hotelling T²:</strong> ${pt.hotellingT2.toFixed(2)} (Thresh: ${hotellingT2Threshold.toFixed(2)})</div>
              <div><strong>SPE / Q:</strong> ${pt.speQ.toFixed(2)} (Thresh: ${speQThreshold.toFixed(2)})</div>
              ${badge}
              <div style="font-size:10px;color:#94a3b8;margin-top:4px;">(Click point to locate in preview grid)</div>
            </div>
          `
        },
      },
      grid: { left: '50px', right: '30px', top: '40px', bottom: '40px' },
      xAxis: {
        type: 'value',
        name: 'PC1',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        nameTextStyle: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        name: 'PC2',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        nameTextStyle: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'Samples',
          type: 'scatter',
          data,
          symbolSize: 6,
        },
      ],
    }
  }, [sampledPoints, hotellingT2Threshold, speQThreshold])

  // 3. Feature Loadings Option (PC1 and PC2)
  const loadingsOption = useMemo(() => {
    if (!components || components.length === 0 || !components[0].featureLoadings) return null

    const pc1Loadings = components[0].featureLoadings
    const pc2Loadings = components.length > 1 ? components[1].featureLoadings : {}
    const categories = Object.keys(pc1Loadings)

    return {
      title: {
        text: 'Feature Loadings (Component Contributions)',
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
      },
      legend: {
        data: ['PC1 Loading', 'PC2 Loading'],
        textStyle: { color: '#94a3b8' },
        right: '20px',
        top: '5px',
      },
      grid: { left: '100px', right: '30px', top: '40px', bottom: '30px' },
      xAxis: {
        type: 'value',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'category',
        data: categories,
        axisLine: { lineStyle: { color: '#475569' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'PC1 Loading',
          type: 'bar',
          data: categories.map((k) => pc1Loadings[k]?.toFixed(3) ?? 0),
          itemStyle: { color: '#0284c7' },
        },
        ...(components.length > 1
          ? [
              {
                name: 'PC2 Loading',
                type: 'bar',
                data: categories.map((k) => pc2Loadings[k]?.toFixed(3) ?? 0),
                itemStyle: { color: '#a855f7' },
              },
            ]
          : []),
      ],
    }
  }, [components])

  const chartEvents = useMemo(
    () => ({
      click: (params: { data?: { value?: [number, number]; rowIndex?: number; hotellingT2?: number; speQ?: number; isHotellingOutlier?: boolean; isSpeOutlier?: boolean } }) => {
        if (params?.data?.rowIndex !== undefined) {
          onSelectRowIndex(params.data.rowIndex)
        }
        if (params?.data?.value && params?.data?.rowIndex !== undefined) {
          setSelectedPointPreview({
            pc1: params.data.value[0],
            pc2: params.data.value[1],
            rowIndex: params.data.rowIndex,
            hotellingT2: params.data.hotellingT2 ?? 0,
            speQ: params.data.speQ ?? 0,
            isHotellingOutlier: !!params.data.isHotellingOutlier,
            isSpeOutlier: !!params.data.isSpeOutlier,
          })
        }
      },
      mouseover: (params: { data?: { value?: [number, number]; rowIndex?: number; hotellingT2?: number; speQ?: number; isHotellingOutlier?: boolean; isSpeOutlier?: boolean } }) => {
        if (params?.data?.value && params?.data?.rowIndex !== undefined) {
          setSelectedPointPreview({
            pc1: params.data.value[0],
            pc2: params.data.value[1],
            rowIndex: params.data.rowIndex,
            hotellingT2: params.data.hotellingT2 ?? 0,
            speQ: params.data.speQ ?? 0,
            isHotellingOutlier: !!params.data.isHotellingOutlier,
            isSpeOutlier: !!params.data.isSpeOutlier,
          })
        }
      },
    }),
    [onSelectRowIndex]
  )

  const outliers = useMemo(() => {
    return (sampledPoints || []).filter((p) => p.isHotellingOutlier || p.isSpeOutlier)
  }, [sampledPoints])

  return (
    <div className="analytics-pca-view">
      {warnings && warnings.length > 0 && (
        <div className="analytics-alert-banner warning my-2 text-xs">
          <strong>PCA Notice:</strong> {warnings.join(' | ')}
        </div>
      )}

      <div className="analytics-summary-cards my-3">
        <div className="analytics-stat-card">
          <span className="stat-label">Features / Samples</span>
          <span className="stat-value font-mono">{featureNames.length} / {totalSamples}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Confidence Limit</span>
          <span className="stat-value font-mono">{Math.round(confidenceLevel * 100)}%</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Hotelling T² Outliers</span>
          <span className={`stat-value font-mono ${totalHotellingOutliers > 0 ? 'text-amber-400' : 'text-emerald-400'}`}>
            {totalHotellingOutliers}
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">SPE / Q Outliers</span>
          <span className={`stat-value font-mono ${totalSpeOutliers > 0 ? 'text-purple-400' : 'text-emerald-400'}`}>
            {totalSpeOutliers}
          </span>
        </div>
      </div>

      <div className="grid-2-col">
        {screeOption && (
          <div className="echarts-container-card">
            <ReactECharts
              option={screeOption}
              style={{ height: '300px', width: '100%' }}
            />
          </div>
        )}

        {loadingsOption && (
          <div className="echarts-container-card">
            <ReactECharts
              option={loadingsOption}
              style={{ height: '300px', width: '100%' }}
            />
          </div>
        )}
      </div>

      {scoreOption && (
        <div className="echarts-container-card mt-3">
          <ReactECharts
            option={scoreOption}
            onEvents={chartEvents}
            style={{ height: '360px', width: '100%' }}
          />
        </div>
      )}

      {selectedPointPreview && (
        <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
          <div className="preview-header">
            <span className="badge info">PCA Score Observation Preview</span>
            <span className="sample-name font-mono">Row #{selectedPointPreview.rowIndex}</span>
            {(selectedPointPreview.isHotellingOutlier || selectedPointPreview.isSpeOutlier) ? (
              <span className="badge danger">
                {[
                  selectedPointPreview.isHotellingOutlier && 'T² Outlier',
                  selectedPointPreview.isSpeOutlier && 'SPE/Q Outlier'
                ].filter(Boolean).join(' + ')}
              </span>
            ) : (
              <span className="badge success">Normal Boundary</span>
            )}
          </div>
          <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
            <div>
              <span className="text-xs text-slate-400">PC1: </span>
              <strong className="font-mono text-cyan-400">{selectedPointPreview.pc1.toFixed(3)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">PC2: </span>
              <strong className="font-mono text-purple-400">{selectedPointPreview.pc2.toFixed(3)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">T²: </span>
              <strong className="font-mono text-amber-400">{selectedPointPreview.hotellingT2.toFixed(2)}</strong>
              <span className="text-xs text-slate-500"> / {hotellingT2Threshold.toFixed(1)}</span>
            </div>
            <div>
              <span className="text-xs text-slate-400">SPE/Q: </span>
              <strong className="font-mono text-rose-400">{selectedPointPreview.speQ.toFixed(2)}</strong>
              <span className="text-xs text-slate-500"> / {speQThreshold.toFixed(1)}</span>
            </div>
            <button
              type="button"
              className="btn btn-xs btn-primary ml-auto"
              onClick={() => onSelectRowIndex(selectedPointPreview.rowIndex)}
            >
              Locate in Defect Table
            </button>
          </div>
        </div>
      )}

      {/* Outlier Drill-Down Table */}
      <div className="pca-outliers-section mt-4">
        <div className="section-header">
          <h4>Multivariate Outlier Samples ({outliers.length} flagged)</h4>
        </div>
        {outliers.length === 0 ? (
          <div className="analytics-empty-note success">
            ✓ No multivariate outliers exceeding Hotelling T² or SPE/Q thresholds.
          </div>
        ) : (
          <div className="table-responsive">
            <table className="analytics-data-table" role="table">
              <thead>
                <tr>
                  <th>Row Index</th>
                  <th className="num">PC1</th>
                  <th className="num">PC2</th>
                  <th className="num">Hotelling T²</th>
                  <th className="num">SPE / Q</th>
                  <th>Classification</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {outliers.map((p) => {
                  const isBoth = p.isHotellingOutlier && p.isSpeOutlier
                  return (
                    <tr key={p.rowIndex}>
                      <td className="font-mono font-bold">#{p.rowIndex}</td>
                      <td className="num font-mono">{p.pc1.toFixed(3)}</td>
                      <td className="num font-mono">{p.pc2.toFixed(3)}</td>
                      <td className={`num font-mono ${p.isHotellingOutlier ? 'text-amber-400 font-bold' : ''}`}>
                        {p.hotellingT2.toFixed(2)}
                      </td>
                      <td className={`num font-mono ${p.isSpeOutlier ? 'text-purple-400 font-bold' : ''}`}>
                        {p.speQ.toFixed(2)}
                      </td>
                      <td>
                        <span className={`pill-${isBoth ? 'danger' : p.isHotellingOutlier ? 'warning' : 'info'}`}>
                          {isBoth ? 'T² + SPE Outlier' : p.isHotellingOutlier ? 'T² Extreme' : 'Model Residual (SPE)'}
                        </span>
                      </td>
                      <td>
                        <button
                          type="button"
                          className="btn btn-ghost btn-sm font-mono text-xs"
                          onClick={() => onSelectRowIndex(p.rowIndex)}
                        >
                          Locate Row
                        </button>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
