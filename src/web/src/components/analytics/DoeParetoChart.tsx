import React, { useMemo } from 'react'
import ReactECharts from '../ReactECharts'
import type { DoeResult } from '../../types'

export type DoeParetoChartProps = {
  result: DoeResult
  onSelectRowIndex: (rowIndex: number) => void
}

export const DoeParetoChart: React.FC<DoeParetoChartProps> = ({ result, onSelectRowIndex }) => {
  const {
    responseName,
    factorNames,
    totalRuns,
    isUnreplicatedSaturated,
    significanceThreshold,
    summary,
    effectsPareto,
    residualDiagnostics,
    warnings,
  } = result

  // 1. Pareto Chart Option
  const paretoOption = useMemo(() => {
    if (!effectsPareto || effectsPareto.length === 0) return null

    // Reverse for horizontal display (highest on top)
    const sorted = [...effectsPareto].reverse()
    const categories = sorted.map((e) => e.term)
    const data = sorted.map((e) => ({
      value: e.standardizedEffectMagnitude,
      isSig: e.isStatisticallySignificant,
      itemStyle: {
        color: e.isStatisticallySignificant ? '#f43f5e' : '#0369a1',
      },
    }))

    return {
      title: {
        text: `Pareto Chart of Standardized Effects (Response: ${responseName})`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: Array<{ name: string; value: number }>) => {
          const item = effectsPareto.find((e) => e.term === params[0].name)
          if (!item) return ''
          const sigTag = item.isStatisticallySignificant
            ? '<span style="color:#f87171;font-weight:bold;">★ Statistically Significant</span>'
            : '<span style="color:#94a3b8;">Not Significant</span>'
          return `
            <div style="font-family:monospace;">
              <div><strong>Term:</strong> ${item.term} (${item.termType})</div>
              <div><strong>|t-value|:</strong> ${item.standardizedEffectMagnitude.toFixed(3)}</div>
              <div><strong>Coefficient:</strong> ${item.coefficient.toFixed(4)}</div>
              <div><strong>p-value:</strong> ${item.pValue.toFixed(4)}</div>
              <div>${sigTag}</div>
            </div>
          `
        },
      },
      grid: { left: '120px', right: '80px', top: '40px', bottom: '30px' },
      xAxis: {
        type: 'value',
        name: 'Standardized Effect (|t|)',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        nameTextStyle: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'category',
        data: categories,
        axisLine: { lineStyle: { color: '#475569' } },
        axisLabel: { color: '#cbd5e1', fontSize: 11 },
      },
      series: [
        {
          name: 'Standardized Effect',
          type: 'bar',
          data,
          markLine: {
            silent: false,
            symbol: 'none',
            data: [
              {
                xAxis: significanceThreshold,
                name: 'Significance Threshold',
                lineStyle: { color: '#f43f5e', type: 'dashed', width: 2 },
                label: {
                  formatter: `Threshold: ${significanceThreshold.toFixed(2)}`,
                  position: 'end',
                  color: '#f43f5e',
                  fontSize: 10,
                },
              },
            ],
          },
        },
      ],
    }
  }, [effectsPareto, responseName, significanceThreshold])

  // 2. Residual Diagnostics Option (Residuals vs Fitted)
  const residualOption = useMemo(() => {
    if (!residualDiagnostics || residualDiagnostics.length === 0) return null

    const data = residualDiagnostics.map((r) => ({
      value: [r.fitted, r.residual],
      rowIndex: r.rowIndex,
      actual: r.actual,
      fitted: r.fitted,
      residual: r.residual,
      itemStyle: { color: '#38bdf8' },
    }))

    return {
      title: {
        text: 'Residual Diagnostics (Residuals vs Fitted Values)',
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'item',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: { data: { value: [number, number]; rowIndex: number; actual: number; fitted: number; residual: number } }) => `
          <div style="font-family:monospace;">
            <div><strong>Grid Row:</strong> #${params.data.rowIndex}</div>
            <div><strong>Actual Y:</strong> ${params.data.actual.toFixed(4)}</div>
            <div><strong>Fitted Y:</strong> ${params.data.fitted.toFixed(4)}</div>
            <div><strong>Residual (e):</strong> ${params.data.residual.toFixed(4)}</div>
            <div style="font-size:12px;color:#94a3b8;margin-top:4px;">(Click point to locate in preview grid)</div>
          </div>
        `,
      },
      grid: { left: '50px', right: '30px', top: '40px', bottom: '40px' },
      xAxis: {
        type: 'value',
        name: 'Fitted Value',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        nameTextStyle: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        name: 'Residual',
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        nameTextStyle: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'Residuals',
          type: 'scatter',
          data,
          symbolSize: 6,
          markLine: {
            silent: true,
            symbol: 'none',
            data: [
              {
                yAxis: 0,
                lineStyle: { color: '#64748b', type: 'solid' },
              },
            ],
          },
        },
      ],
    }
  }, [residualDiagnostics])

  const chartEvents = useMemo(
    () => ({
      click: (params: { data?: { rowIndex?: number } }) => {
        if (params?.data?.rowIndex !== undefined) {
          onSelectRowIndex(params.data.rowIndex)
        }
      },
    }),
    [onSelectRowIndex]
  )

  return (
    <div className="analytics-doe-view">
      {isUnreplicatedSaturated && (
        <div className="analytics-alert-banner warning my-2 text-xs">
          <strong>Lenth PSE Safeguard:</strong> Unreplicated saturated design detected (no degrees of freedom for pure error). Effect significance evaluated using Lenth Pseudo Standard Error (PSE).
        </div>
      )}

      {warnings && warnings.length > 0 && (
        <div className="analytics-alert-banner warning my-2 text-xs">
          <strong>Model Warnings:</strong> {warnings.join(' | ')}
        </div>
      )}

      <div className="analytics-summary-cards my-3">
        <div className="analytics-stat-card">
          <span className="stat-label">Response Variable (Runs)</span>
          <span className="stat-value font-mono text-base">{responseName} (N={totalRuns})</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Model R² / Adj R²</span>
          <span className="stat-value font-mono">
            {(summary.r2 * 100).toFixed(1)}% / {(summary.adjustedR2 * 100).toFixed(1)}%
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">RMSE (Std Error)</span>
          <span className="stat-value font-mono">{summary.rootMeanSquareError.toFixed(4)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Model F-Stat (p-value)</span>
          <span className="stat-value font-mono">
            {summary.fStatistic.toFixed(2)} (p = {summary.pValue.toFixed(4)})
          </span>
        </div>
      </div>

      <div className="grid-2-col">
        {paretoOption && (
          <div className="echarts-container-card">
            <ReactECharts
              option={paretoOption}
              style={{ height: '340px', width: '100%' }}
            />
          </div>
        )}

        {residualOption && (
          <div className="echarts-container-card">
            <ReactECharts
              option={residualOption}
              onEvents={chartEvents}
              style={{ height: '340px', width: '100%' }}
            />
          </div>
        )}
      </div>

      {/* Model Coefficients & Significance Table */}
      <div className="doe-effects-section mt-4">
        <div className="section-header">
          <h4>Estimated Model Coefficients & Standardized Effects ({factorNames.join(', ')})</h4>
        </div>
        <div className="table-responsive">
          <table className="analytics-data-table" role="table">
            <thead>
              <tr>
                <th>Term</th>
                <th>Type</th>
                <th className="num">Coefficient</th>
                <th className="num">Std Error</th>
                <th className="num">T-Statistic</th>
                <th className="num">P-Value</th>
                <th>Significance</th>
              </tr>
            </thead>
            <tbody>
              {effectsPareto.map((ef) => (
                <tr key={ef.term}>
                  <td className="font-mono font-bold">{ef.term}</td>
                  <td>{ef.termType}</td>
                  <td className="num font-mono">{ef.coefficient.toFixed(4)}</td>
                  <td className="num font-mono">{ef.standardError.toFixed(4)}</td>
                  <td className="num font-mono">{ef.tStatistic.toFixed(3)}</td>
                  <td className={`num font-mono ${ef.isStatisticallySignificant ? 'text-rose-400 font-bold' : ''}`}>
                    {ef.pValue.toFixed(4)}
                  </td>
                  <td>
                    <span className={`pill-${ef.isStatisticallySignificant ? 'danger' : 'secondary'}`}>
                      {ef.isStatisticallySignificant ? 'Significant' : 'Not Significant'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
