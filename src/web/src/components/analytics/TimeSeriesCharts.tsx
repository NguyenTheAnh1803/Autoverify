import React, { useState, useMemo } from 'react'
import ReactECharts from '../ReactECharts'
import type { EwmaResult, CusumResult } from '../../types'

export type EwmaChartProps = {
  result: EwmaResult
  onSelectRowIndex: (rowIndex: number) => void
}

export const EwmaChart: React.FC<EwmaChartProps> = ({ result, onSelectRowIndex }) => {
  const {
    featureName,
    lambda,
    l,
    target,
    sigma,
    asymptoticUcl,
    asymptoticLcl,
    violationCount,
    points,
  } = result

  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    sampleIdx: number
    rawValue: number
    ewmaValue: number
    rowIndex: number
    isViolation: boolean
  } | null>(null)

  const option = useMemo(() => {
    if (!points || points.length === 0) return null

    const xData = points.map((_, i) => `#${i + 1}`)
    const rawData = points.map((p) => ({
      value: p.rawValue,
      rowIndex: p.rowIndex,
    }))
    const ewmaData = points.map((p) => ({
      value: p.ewmaValue,
      rowIndex: p.rowIndex,
      isViolation: p.isViolation,
      itemStyle: p.isViolation
        ? { color: '#ef4444', borderColor: '#f87171', borderWidth: 2 }
        : { color: '#06b6d4' },
    }))

    return {
      title: {
        text: `EWMA Drift Control Chart - ${featureName} (λ=${lambda.toFixed(2)}, L=${l.toFixed(1)})`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: Array<{ data: { value: number; rowIndex: number; isViolation?: boolean }; seriesName: string; axisValue: string }>) => {
          if (!params || params.length === 0) return ''
          const pt = params[0].data
          const isV = params.some((p) => p.data.isViolation)
          const violationTag = isV
            ? '<div style="color:#f87171;font-weight:bold;margin-top:4px;">[Violation] EWMA Control Violation!</div>'
            : ''
          const lines = params.map(
            (p) => `<div><strong>${p.seriesName}:</strong> ${typeof p.data.value === 'number' ? p.data.value.toFixed(4) : p.data.value}</div>`
          ).join('')
          return `
            <div style="font-family:monospace;">
              <div><strong>Sample:</strong> ${params[0].axisValue} (Grid Row: #${pt.rowIndex})</div>
              ${lines}
              ${violationTag}
              <div style="font-size:12px;color:#94a3b8;margin-top:4px;">(Click point to locate in preview grid)</div>
            </div>
          `
        },
      },
      legend: {
        data: ['Raw Value', 'EWMA Smoothed (Z_i)'],
        textStyle: { color: '#94a3b8' },
        right: '100px',
        top: '5px',
      },
      grid: { left: '50px', right: '90px', top: '40px', bottom: '30px' },
      xAxis: {
        type: 'category',
        data: xData,
        axisLine: { lineStyle: { color: '#475569' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        scale: true,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'Raw Value',
          type: 'line',
          data: rawData,
          showSymbol: false,
          lineStyle: { color: '#64748b', width: 1, type: 'dotted' },
        },
        {
          name: 'EWMA Smoothed (Z_i)',
          type: 'line',
          data: ewmaData,
          showSymbol: true,
          symbolSize: (_val: unknown, params: { data: { isViolation?: boolean } }) =>
            params.data.isViolation ? 7 : 3,
          lineStyle: { color: '#06b6d4', width: 2 },
          markLine: {
            silent: false,
            symbol: 'none',
            data: [
              {
                yAxis: asymptoticUcl,
                name: 'UCL',
                lineStyle: { color: '#f43f5e', type: 'dashed' },
                label: { formatter: `UCL: ${asymptoticUcl.toFixed(3)}`, position: 'end', color: '#f43f5e', fontSize: 10 },
              },
              {
                yAxis: target,
                name: 'Target (μ₀)',
                lineStyle: { color: '#10b981', type: 'solid' },
                label: { formatter: `μ₀: ${target.toFixed(3)}`, position: 'end', color: '#10b981', fontSize: 10 },
              },
              {
                yAxis: asymptoticLcl,
                name: 'LCL',
                lineStyle: { color: '#f43f5e', type: 'dashed' },
                label: { formatter: `LCL: ${asymptoticLcl.toFixed(3)}`, position: 'end', color: '#f43f5e', fontSize: 10 },
              },
            ],
          },
        },
      ],
    }
  }, [points, featureName, lambda, l, target, asymptoticUcl, asymptoticLcl])

  const chartEvents = useMemo(
    () => ({
      click: (params: { dataIndex?: number; data?: { rowIndex?: number } }) => {
        if (params?.data?.rowIndex !== undefined) {
          onSelectRowIndex(params.data.rowIndex)
        }
        if (params?.dataIndex !== undefined && points[params.dataIndex]) {
          const pt = points[params.dataIndex]
          setSelectedPointPreview({
            sampleIdx: params.dataIndex + 1,
            rawValue: pt.rawValue,
            ewmaValue: pt.ewmaValue,
            rowIndex: pt.rowIndex,
            isViolation: pt.isViolation,
          })
        }
      },
      mouseover: (params: { dataIndex?: number }) => {
        if (params?.dataIndex !== undefined && points[params.dataIndex]) {
          const pt = points[params.dataIndex]
          setSelectedPointPreview({
            sampleIdx: params.dataIndex + 1,
            rawValue: pt.rawValue,
            ewmaValue: pt.ewmaValue,
            rowIndex: pt.rowIndex,
            isViolation: pt.isViolation,
          })
        }
      },
    }),
    [onSelectRowIndex, points]
  )

  return (
    <div className="analytics-ewma-view">
      <div className="analytics-summary-cards my-3">
        <div className="analytics-stat-card">
          <span className="stat-label">Process Mean (μ₀)</span>
          <span className="stat-value font-mono">{target.toFixed(4)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Estimated Sigma (σ₀)</span>
          <span className="stat-value font-mono">{sigma.toFixed(4)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">EWMA Violations</span>
          <span className={`stat-value font-mono ${violationCount > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>
            {violationCount}
          </span>
        </div>
      </div>

      {option && (
        <div className="echarts-container-card">
          <ReactECharts
            option={option}
            onEvents={chartEvents}
            style={{ height: '360px', width: '100%' }}
          />
        </div>
      )}

      {selectedPointPreview && (
        <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
          <div className="preview-header">
            <span className="badge info">EWMA Feature Point Preview</span>
            <span className="sample-name font-mono">Sample #{selectedPointPreview.sampleIdx}</span>
            <span className="text-xs text-slate-400 font-mono">Row #{selectedPointPreview.rowIndex}</span>
            {selectedPointPreview.isViolation && (
              <span className="badge danger">Control Violation</span>
            )}
          </div>
          <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
            <div>
              <span className="text-xs text-slate-400">Raw {featureName}: </span>
              <strong className="font-mono text-cyan-400">{selectedPointPreview.rawValue.toFixed(4)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">EWMA (Z_i): </span>
              <strong className="font-mono text-emerald-400">{selectedPointPreview.ewmaValue.toFixed(4)}</strong>
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
    </div>
  )
}

export type CusumChartProps = {
  result: CusumResult
  onSelectRowIndex: (rowIndex: number) => void
}

export const CusumChart: React.FC<CusumChartProps> = ({ result, onSelectRowIndex }) => {
  const {
    featureName,
    target,
    sigma,
    k,
    h,
    violationHighCount,
    violationLowCount,
    points,
  } = result

  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    sampleIdx: number
    rawValue: number
    cusumHigh: number
    cusumLow: number
    rowIndex: number
    isViolationHigh: boolean
    isViolationLow: boolean
  } | null>(null)

  const option = useMemo(() => {
    if (!points || points.length === 0) return null

    const xData = points.map((_, i) => `#${i + 1}`)
    const cHighData = points.map((p) => ({
      value: p.cusumHigh,
      rowIndex: p.rowIndex,
      isViolation: p.isViolationHigh,
      itemStyle: p.isViolationHigh
        ? { color: '#ef4444', borderColor: '#f87171', borderWidth: 2 }
        : { color: '#38bdf8' },
    }))
    const cLowData = points.map((p) => ({
      value: -p.cusumLow,
      rowIndex: p.rowIndex,
      isViolation: p.isViolationLow,
      itemStyle: p.isViolationLow
        ? { color: '#ef4444', borderColor: '#f87171', borderWidth: 2 }
        : { color: '#f59e0b' },
    }))

    return {
      title: {
        text: `Tabular CUSUM Shift Chart - ${featureName} (k=${k.toFixed(2)}σ, h=${h.toFixed(1)}σ)`,
        left: 'left',
        textStyle: { color: '#cbd5e1', fontSize: 13, fontWeight: 'normal' },
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1e293b',
        borderColor: '#475569',
        textStyle: { color: '#f1f5f9', fontSize: 12 },
        formatter: (params: Array<{ data: { value: number; rowIndex: number; isViolation?: boolean }; seriesName: string; axisValue: string }>) => {
          if (!params || params.length === 0) return ''
          const pt = params[0].data
          const hasV = params.some((p) => p.data.isViolation)
          const violationTag = hasV
            ? '<div style="color:#f87171;font-weight:bold;margin-top:4px;">[Violation] Decision Interval Exceeded!</div>'
            : ''
          const lines = params.map(
            (p) => `<div><strong>${p.seriesName}:</strong> ${typeof p.data.value === 'number' ? p.data.value.toFixed(4) : p.data.value}</div>`
          ).join('')
          return `
            <div style="font-family:monospace;">
              <div><strong>Sample:</strong> ${params[0].axisValue} (Grid Row: #${pt.rowIndex})</div>
              ${lines}
              ${violationTag}
              <div style="font-size:12px;color:#94a3b8;margin-top:4px;">(Click point to locate in preview grid)</div>
            </div>
          `
        },
      },
      legend: {
        data: ['C+ (Upper Drift)', 'C- (Lower Drift)'],
        textStyle: { color: '#94a3b8' },
        right: '100px',
        top: '5px',
      },
      grid: { left: '50px', right: '90px', top: '40px', bottom: '30px' },
      xAxis: {
        type: 'category',
        data: xData,
        axisLine: { lineStyle: { color: '#475569' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      yAxis: {
        type: 'value',
        scale: true,
        axisLine: { lineStyle: { color: '#475569' } },
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 },
      },
      series: [
        {
          name: 'C+ (Upper Drift)',
          type: 'line',
          data: cHighData,
          showSymbol: true,
          symbolSize: (_val: unknown, params: { data: { isViolation?: boolean } }) =>
            params.data.isViolation ? 7 : 3,
          lineStyle: { color: '#38bdf8', width: 2 },
          markLine: {
            silent: false,
            symbol: 'none',
            data: [
              {
                yAxis: h,
                name: 'Upper Decision (+h)',
                lineStyle: { color: '#f43f5e', type: 'dashed' },
                label: { formatter: `+h: ${h.toFixed(2)}`, position: 'end', color: '#f43f5e', fontSize: 10 },
              },
              {
                yAxis: 0,
                name: 'Center Zero',
                lineStyle: { color: '#475569', type: 'solid' },
              },
            ],
          },
        },
        {
          name: 'C- (Lower Drift)',
          type: 'line',
          data: cLowData,
          showSymbol: true,
          symbolSize: (_val: unknown, params: { data: { isViolation?: boolean } }) =>
            params.data.isViolation ? 7 : 3,
          lineStyle: { color: '#f59e0b', width: 2 },
          markLine: {
            silent: false,
            symbol: 'none',
            data: [
              {
                yAxis: -h,
                name: 'Lower Decision (-h)',
                lineStyle: { color: '#f43f5e', type: 'dashed' },
                label: { formatter: `-h: -${h.toFixed(2)}`, position: 'end', color: '#f43f5e', fontSize: 10 },
              },
            ],
          },
        },
      ],
    }
  }, [points, featureName, k, h])

  const chartEvents = useMemo(
    () => ({
      click: (params: { dataIndex?: number; data?: { rowIndex?: number } }) => {
        if (params?.data?.rowIndex !== undefined) {
          onSelectRowIndex(params.data.rowIndex)
        }
        if (params?.dataIndex !== undefined && points[params.dataIndex]) {
          const pt = points[params.dataIndex]
          setSelectedPointPreview({
            sampleIdx: params.dataIndex + 1,
            rawValue: pt.rawValue,
            cusumHigh: pt.cusumHigh,
            cusumLow: pt.cusumLow,
            rowIndex: pt.rowIndex,
            isViolationHigh: pt.isViolationHigh,
            isViolationLow: pt.isViolationLow,
          })
        }
      },
      mouseover: (params: { dataIndex?: number }) => {
        if (params?.dataIndex !== undefined && points[params.dataIndex]) {
          const pt = points[params.dataIndex]
          setSelectedPointPreview({
            sampleIdx: params.dataIndex + 1,
            rawValue: pt.rawValue,
            cusumHigh: pt.cusumHigh,
            cusumLow: pt.cusumLow,
            rowIndex: pt.rowIndex,
            isViolationHigh: pt.isViolationHigh,
            isViolationLow: pt.isViolationLow,
          })
        }
      },
    }),
    [onSelectRowIndex, points]
  )

  return (
    <div className="analytics-cusum-view">
      <div className="analytics-summary-cards my-3">
        <div className="analytics-stat-card">
          <span className="stat-label">Target Nominal (μ₀)</span>
          <span className="stat-value font-mono">{target.toFixed(4)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Estimated Sigma (σ₀)</span>
          <span className="stat-value font-mono">{sigma.toFixed(4)}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">High Violations (C+)</span>
          <span className={`stat-value font-mono ${violationHighCount > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>
            {violationHighCount}
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Low Violations (C-)</span>
          <span className={`stat-value font-mono ${violationLowCount > 0 ? 'text-amber-400' : 'text-emerald-400'}`}>
            {violationLowCount}
          </span>
        </div>
      </div>

      {option && (
        <div className="echarts-container-card">
          <ReactECharts
            option={option}
            onEvents={chartEvents}
            style={{ height: '360px', width: '100%' }}
          />
        </div>
      )}

      {selectedPointPreview && (
        <div className="analytics-feature-preview-card" style={{ marginTop: '8px' }}>
          <div className="preview-header">
            <span className="badge info">CUSUM Feature Point Preview</span>
            <span className="sample-name font-mono">Sample #{selectedPointPreview.sampleIdx}</span>
            <span className="text-xs text-slate-400 font-mono">Row #{selectedPointPreview.rowIndex}</span>
            {(selectedPointPreview.isViolationHigh || selectedPointPreview.isViolationLow) && (
              <span className="badge danger">
                {selectedPointPreview.isViolationHigh ? 'High Violation (C+)' : 'Low Violation (C-)'}
              </span>
            )}
          </div>
          <div className="preview-body" style={{ display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap' }}>
            <div>
              <span className="text-xs text-slate-400">Raw {featureName}: </span>
              <strong className="font-mono text-cyan-400">{selectedPointPreview.rawValue.toFixed(4)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">C+ (Upper): </span>
              <strong className="font-mono text-sky-400">{selectedPointPreview.cusumHigh.toFixed(4)}</strong>
            </div>
            <div>
              <span className="text-xs text-slate-400">C- (Lower): </span>
              <strong className="font-mono text-amber-400">{selectedPointPreview.cusumLow.toFixed(4)}</strong>
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
    </div>
  )
}
