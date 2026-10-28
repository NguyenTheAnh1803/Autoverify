import React, { useState, useMemo } from 'react'
import ReactECharts from '../ReactECharts'
import { refLines, toolbox, valueAxis, zoom } from '../../lib/chartTheme'
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
        ? { color: '#c0392b', borderColor: '#c0392b', borderWidth: 2 }
        : { color: '#2f6f9f' },
    }))

    return {
      title: {
        text: `EWMA Drift Control Chart - ${featureName} (λ=${lambda.toFixed(2)}, L=${l.toFixed(1)})`,
        left: 'left',
      },
      tooltip: {
        trigger: 'axis',
        formatter: (params: Array<{ data: { value: number; rowIndex: number; isViolation?: boolean }; seriesName: string; axisValue: string }>) => {
          if (!params || params.length === 0) return ''
          const pt = params[0].data
          const isV = params.some((p) => p.data.isViolation)
          const violationTag = isV
            ? '<div style="color:#c0392b;font-weight:bold;margin-top:4px;">[Violation] EWMA Control Violation!</div>'
            : ''
          const lines = params.map(
            (p) => `<div><strong>${p.seriesName}:</strong> ${typeof p.data.value === 'number' ? p.data.value.toFixed(4) : p.data.value}</div>`
          ).join('')
          return `
            <div style="font-family:var(--font-mono);">
              <div><strong>Sample:</strong> ${params[0].axisValue} (Grid Row: #${pt.rowIndex})</div>
              ${lines}
              ${violationTag}
              <div style="font-size:12px;color:#64748b;margin-top:4px;">(Click point to locate in preview grid)</div>
            </div>
          `
        },
      },
      legend: {
        data: ['Raw Value', 'EWMA Smoothed (Z_i)'],
        right: 40,
        top: 0,
      },
      toolbox: toolbox(),
      grid: { left: 12, right: 16, top: 44, bottom: xData.length > 60 ? 40 : 14 },
      xAxis: { type: 'category', data: xData },
      yAxis: valueAxis(),
      dataZoom: xData.length > 60 ? zoom() : undefined,
      series: [
        {
          name: 'Raw Value',
          type: 'line',
          data: rawData,
          showSymbol: false,
          lineStyle: { color: '#9aa7b8', width: 1 },
        },
        {
          name: 'EWMA Smoothed (Z_i)',
          type: 'line',
          data: ewmaData,
          showSymbol: true,
          symbolSize: (_val: unknown, params: { data: { isViolation?: boolean } }) =>
            params.data.isViolation ? 7 : 3,
          lineStyle: { color: '#2f6f9f', width: 1.25 },
          markLine: refLines([
            { name: 'UCL', value: asymptoticUcl, kind: 'control' },
            { name: 'μ₀', value: target, kind: 'mean' },
            { name: 'LCL', value: asymptoticLcl, kind: 'control' },
          ]),
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
        <div className="analytics-feature-preview-card mt-2">
          <div className="preview-header">
            <span className="badge info">EWMA Feature Point Preview</span>
            <span className="sample-name font-mono">Sample #{selectedPointPreview.sampleIdx}</span>
            <span className="text-xs text-slate-400 font-mono">Row #{selectedPointPreview.rowIndex}</span>
            {selectedPointPreview.isViolation && (
              <span className="badge danger">Control Violation</span>
            )}
          </div>
          <div className="preview-body hstack gap-xl flex-wrap">
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
        ? { color: '#c0392b', borderColor: '#c0392b', borderWidth: 2 }
        : { color: '#2f6f9f' },
    }))
    const cLowData = points.map((p) => ({
      value: -p.cusumLow,
      rowIndex: p.rowIndex,
      isViolation: p.isViolationLow,
      itemStyle: p.isViolationLow
        ? { color: '#c0392b', borderColor: '#c0392b', borderWidth: 2 }
        : { color: '#c8862b' },
    }))

    return {
      title: {
        text: `Tabular CUSUM Shift Chart - ${featureName} (k=${k.toFixed(2)}σ, h=${h.toFixed(1)}σ)`,
        left: 'left',
      },
      tooltip: {
        trigger: 'axis',
        formatter: (params: Array<{ data: { value: number; rowIndex: number; isViolation?: boolean }; seriesName: string; axisValue: string }>) => {
          if (!params || params.length === 0) return ''
          const pt = params[0].data
          const hasV = params.some((p) => p.data.isViolation)
          const violationTag = hasV
            ? '<div style="color:#c0392b;font-weight:bold;margin-top:4px;">[Violation] Decision Interval Exceeded!</div>'
            : ''
          const lines = params.map(
            (p) => `<div><strong>${p.seriesName}:</strong> ${typeof p.data.value === 'number' ? p.data.value.toFixed(4) : p.data.value}</div>`
          ).join('')
          return `
            <div style="font-family:var(--font-mono);">
              <div><strong>Sample:</strong> ${params[0].axisValue} (Grid Row: #${pt.rowIndex})</div>
              ${lines}
              ${violationTag}
              <div style="font-size:12px;color:#64748b;margin-top:4px;">(Click point to locate in preview grid)</div>
            </div>
          `
        },
      },
      legend: {
        data: ['C+ (Upper Drift)', 'C- (Lower Drift)'],
        right: 40,
        top: 0,
      },
      toolbox: toolbox(),
      grid: { left: 12, right: 16, top: 44, bottom: xData.length > 60 ? 40 : 14 },
      xAxis: { type: 'category', data: xData },
      yAxis: valueAxis(),
      dataZoom: xData.length > 60 ? zoom() : undefined,
      series: [
        {
          name: 'C+ (Upper Drift)',
          type: 'line',
          data: cHighData,
          showSymbol: true,
          symbolSize: (_val: unknown, params: { data: { isViolation?: boolean } }) =>
            params.data.isViolation ? 7 : 3,
          lineStyle: { color: '#2f6f9f', width: 1.25 },
          markLine: refLines([
            { name: '+h', value: h, kind: 'control' },
            { name: '0', value: 0, kind: 'mean' },
          ]),
        },
        {
          name: 'C- (Lower Drift)',
          type: 'line',
          data: cLowData,
          showSymbol: true,
          symbolSize: (_val: unknown, params: { data: { isViolation?: boolean } }) =>
            params.data.isViolation ? 7 : 3,
          lineStyle: { color: '#c8862b', width: 1.25 },
          markLine: refLines([{ name: '-h', value: -h, kind: 'control' }]),
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
        <div className="analytics-feature-preview-card mt-2">
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
          <div className="preview-body hstack gap-xl flex-wrap">
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
