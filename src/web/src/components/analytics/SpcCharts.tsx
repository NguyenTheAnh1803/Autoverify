import React, { useMemo, useState } from 'react'
import { Icon } from '../../ui'
import ReactECharts from '../ReactECharts'
import type { SpcResult } from '../../types'
import { CHART, measureLine, refLines, spcZones, toolbox, valueAxis, zoom } from '../../lib/chartTheme'

export type SpcChartsProps = {
  result: SpcResult
  isNgOnlyDataset: boolean
  onSelectRowIndex: (rowIndex: number) => void
}

export const SpcCharts: React.FC<SpcChartsProps> = ({
  result,
  isNgOnlyDataset,
  onSelectRowIndex,
}) => {
  const { chartType, featureName, primaryChart, secondaryChart, capability, violations, warnings } = result
  const [expandedRuleId, setExpandedRuleId] = useState<number | null>(null)
  const nelsonGroups = useMemo(() => {
    const groups = new Map<number, { ruleId: number; ruleName: string; description: string; severity: string; indexes: number[] }>()
    for (const violation of violations ?? []) {
      const current = groups.get(violation.ruleId)
      if (current) {
        current.indexes.push(violation.index)
      } else {
        groups.set(violation.ruleId, {
          ruleId: violation.ruleId,
          ruleName: violation.ruleName,
          description: violation.description,
          severity: violation.severity,
          indexes: [violation.index],
        })
      }
    }
    return [...groups.values()].sort((a, b) => a.ruleId - b.ruleId)
  }, [violations])

  // Build Primary Chart ECharts options
  const primaryOption = useMemo(() => {
    if (!primaryChart || !primaryChart.points) return null

    const xData = primaryChart.points.map((p, idx) => p.subgroupId || `#${idx + 1}`)
    const seriesData = primaryChart.points.map((p) => {
      const isViolating = p.violatingRuleIds && p.violatingRuleIds.length > 0
      return {
        value: p.value,
        rowIndex: p.rowIndex,
        violatingRuleIds: p.violatingRuleIds,
        itemStyle: isViolating
          ? { color: '#c0392b', borderColor: '#c0392b', borderWidth: 2 }
          : { color: '#2f6f9f' },
      }
    })

    const ucl = primaryChart.upperControlLimit
    const cl = primaryChart.centerLine
    const lcl = primaryChart.lowerControlLimit

    return {
      title: {
        text: `${primaryChart.name} Control Chart (${chartType}) - ${featureName}`,
        left: 'left',
      },
      tooltip: {
        trigger: 'item',
        formatter: (params: { data: { value: number; rowIndex: number; violatingRuleIds?: number[] }; name: string }) => {
          const pt = params.data
          const ruleList = pt.violatingRuleIds && pt.violatingRuleIds.length > 0
            ? `<div style="color:#c0392b;font-weight:bold;margin-top:4px;">Nelson Rules Violated: ${pt.violatingRuleIds.join(', ')}</div>`
            : ''
          return `
            <div style="font-family:var(--font-mono);">
              <div><strong>Sample:</strong> ${params.name}</div>
              <div><strong>Value:</strong> ${pt.value.toFixed(4)}</div>
              <div><strong>Grid Row Index:</strong> #${pt.rowIndex}</div>
              ${ruleList}
              <div style="font-size:12px;color:#64748b;margin-top:4px;">(Click point to locate in preview grid)</div>
            </div>
          `
        },
      },
      toolbox: toolbox(),
      grid: { left: 12, right: 16, top: 44, bottom: seriesData.length > 60 ? 40 : 14 },
      xAxis: { type: 'category', data: xData },
      yAxis: valueAxis(),
      dataZoom: seriesData.length > 60 ? zoom() : undefined,
      series: [
        measureLine(primaryChart.name, seriesData, {
          showSymbol: true,
          symbolSize: (_value: unknown, params: { data: { violatingRuleIds?: number[] } }) =>
            params.data.violatingRuleIds && params.data.violatingRuleIds.length > 0 ? 8 : 4,
          lineStyle: { color: CHART.primary, width: 1.25 },
          markLine: refLines([
            { name: 'UCL', value: ucl, kind: 'control' },
            { name: 'CL', value: cl, kind: 'mean' },
            { name: 'LCL', value: lcl, kind: 'control' },
          ]),
          markArea: spcZones(cl, ucl, lcl),
        }),
      ],
    }
  }, [primaryChart, chartType, featureName])

  // Build Secondary Chart ECharts options (MR, R, or S)
  const secondaryOption = useMemo(() => {
    if (!secondaryChart || !secondaryChart.points) return null

    const xData = secondaryChart.points.map((p, idx) => p.subgroupId || `#${idx + 1}`)
    const seriesData = secondaryChart.points.map((p) => ({
      value: p.value,
      rowIndex: p.rowIndex,
      itemStyle: { color: CHART.series[4] },
    }))

    const ucl = secondaryChart.upperControlLimit
    const cl = secondaryChart.centerLine
    const lcl = secondaryChart.lowerControlLimit

    return {
      title: {
        text: `${secondaryChart.name} Dispersion Chart - ${featureName}`,
        left: 'left',
      },
      tooltip: {
        trigger: 'item',
        formatter: (params: { data: { value: number; rowIndex: number }; name: string }) => `
          <div style="font-family:var(--font-mono);">
            <div><strong>Sample:</strong> ${params.name}</div>
            <div><strong>Dispersion:</strong> ${params.data.value.toFixed(4)}</div>
            <div><strong>Grid Row Index:</strong> #${params.data.rowIndex}</div>
          </div>
        `,
      },
      toolbox: toolbox(),
      grid: { left: 12, right: 16, top: 44, bottom: seriesData.length > 60 ? 40 : 14 },
      xAxis: { type: 'category', data: xData },
      yAxis: valueAxis(),
      dataZoom: seriesData.length > 60 ? zoom() : undefined,
      series: [
        measureLine(secondaryChart.name, seriesData, {
          showSymbol: true,
          symbolSize: 4,
          lineStyle: { color: CHART.series[4], width: 1.25 },
          markLine: refLines([
            { name: 'UCL', value: ucl, kind: 'control' },
            { name: 'CL', value: cl, kind: 'mean' },
            { name: 'LCL', value: lcl, kind: 'control' },
          ]),
          markArea: spcZones(cl, ucl, lcl),
        }),
      ],
    }
  }, [secondaryChart, featureName])

  const [selectedPointPreview, setSelectedPointPreview] = useState<{
    sampleName: string
    featureName: string
    value: number
    rowIndex: number
    violatingRuleIds?: number[]
  } | null>(null)

  const chartEvents = useMemo(
    () => ({
      click: (params: { name?: string; data?: { value?: number; rowIndex?: number; violatingRuleIds?: number[] } }) => {
        if (params?.data?.rowIndex !== undefined) {
          setSelectedPointPreview({
            sampleName: params.name || 'Sample',
            featureName: featureName,
            value: params.data.value ?? 0,
            rowIndex: params.data.rowIndex,
            violatingRuleIds: params.data.violatingRuleIds,
          })
          onSelectRowIndex(params.data.rowIndex)
        }
      },
      mouseover: (params: { name?: string; data?: { value?: number; rowIndex?: number; violatingRuleIds?: number[] } }) => {
        if (params?.data?.rowIndex !== undefined) {
          setSelectedPointPreview({
            sampleName: params.name || 'Sample',
            featureName: featureName,
            value: params.data.value ?? 0,
            rowIndex: params.data.rowIndex,
            violatingRuleIds: params.data.violatingRuleIds,
          })
        }
      },
    }),
    [onSelectRowIndex, featureName]
  )

  return (
    <div className="analytics-spc-view">
      {/* NG-Only Alert safeguard */}
      {isNgOnlyDataset && (
        <aside
          className="analytics-alert-banner warning my-3"
          role="alert"
          aria-live="polite"
        >
          <div className="alert-icon" aria-hidden="true">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <div className="alert-content">
            <header>
              <h4 className="alert-title">Non-Gaussian / NG Truncation Safeguard</h4>
            </header>
            <p>
              This dataset is filtered strictly to NG defects. SPC control limits and capability metrics (Cp, Cpk) assume a natural random distribution around nominal operating conditions. In truncated defect collections, capability figures are mathematically invalidated.
            </p>
          </div>
        </aside>
      )}

      {/* Warnings from computation */}
      {warnings && warnings.length > 0 && (
        <div className="analytics-alert-banner warning my-2 text-xs">
          <strong>Process Warnings:</strong> {warnings.join(' | ')}
        </div>
      )}

      {/* Primary Chart */}
      {primaryOption ? (
        <div className="echarts-container-card">
          <ReactECharts
            option={primaryOption}
            onEvents={chartEvents}
            style={{ height: '320px', width: '100%' }}
          />
        </div>
      ) : (
        <div className="analytics-empty-note">No Primary SPC Series Available</div>
      )}

      {/* Secondary Chart */}
      {secondaryOption && (
        <div className="echarts-container-card mt-3">
          <ReactECharts
            option={secondaryOption}
            onEvents={chartEvents}
            style={{ height: '260px', width: '100%' }}
          />
        </div>
      )}

      {/* Feature Value Preview Card (Per-Point Preview) */}
      {selectedPointPreview && (
        <div
          className="analytics-feature-preview-card hstack mt-3 justify-between"
          style={{
            padding: '10px 14px',
            background: '#f8fafc',
            border: '1px solid #cbd5e1',
            borderRadius: '8px',
            color: '#1e293b',
          }}
        >
          <div>
            <div className="hstack gap-md">
              <Icon name="analysis" className="icon" />
              <strong className="text-xs" style={{ color: '#2f6f9f' }}>
                Feature Value Preview: <code>{selectedPointPreview.featureName}</code>
              </strong>
              <span className="tag-badge text-xs" style={{ background: '#2f6f9f', color: '#f0f9ff' }}>
                Sample: {selectedPointPreview.sampleName}
              </span>
            </div>
            <div className="mt-1 text-xs gap-xl" style={{ display: 'flex' }}>
              <span>
                Observed Value: <strong className="font-mono text-sky-400" style={{ color: '#2f6f9f' }}>{selectedPointPreview.value.toFixed(4)}</strong>
              </span>
              <span>
                Grid Row Index: <strong>#{selectedPointPreview.rowIndex}</strong>
              </span>
              {selectedPointPreview.violatingRuleIds && selectedPointPreview.violatingRuleIds.length > 0 && (
                <span className="font-semibold" style={{ color: '#c0392b' }}>
                  Nelson Violations: Rules [{selectedPointPreview.violatingRuleIds.join(', ')}]
                </span>
              )}
            </div>
          </div>
          <button
            type="button"
            className="compact-button secondary text-xs"
            style={{ padding: '4px 8px' }}
            onClick={() => onSelectRowIndex(selectedPointPreview.rowIndex)}
          >
            <Icon name="target" className="icon inline-icon" />Locate in Defect Table
          </button>
        </div>
      )}

      {/* Capability Cards */}
      {capability && (
        <div className="spc-capability-section mt-4">
          <div className="section-header">
            <h4>Process Capability & Performance Indices</h4>
            {(!capability.isValid || isNgOnlyDataset) && (
              <span className="badge-warning font-mono text-xs">
                {capability.warningMessage || 'Capability Invalidated'}
              </span>
            )}
          </div>
          <div className="capability-cards-grid">
            <div className="capability-card">
              <span className="cap-label">Cp (Potential Within)</span>
              <span className="cap-value font-mono">
                {capability.cp !== null && capability.cp !== undefined ? capability.cp.toFixed(3) : 'N/A'}
              </span>
            </div>
            <div className={`capability-card ${!isNgOnlyDataset && capability.cpk && capability.cpk >= 1.33 ? 'cap-pass' : ''}`}>
              <span className="cap-label">Cpk (Within Capability)</span>
              <span className="cap-value font-mono">
                {capability.cpk !== null && capability.cpk !== undefined ? capability.cpk.toFixed(3) : 'N/A'}
              </span>
            </div>
            <div className="capability-card">
              <span className="cap-label">Pp (Overall Potential)</span>
              <span className="cap-value font-mono">
                {capability.pp !== null && capability.pp !== undefined ? capability.pp.toFixed(3) : 'N/A'}
              </span>
            </div>
            <div className={`capability-card ${!isNgOnlyDataset && capability.ppk && capability.ppk >= 1.33 ? 'cap-pass' : ''}`}>
              <span className="cap-label">Ppk (Overall Performance)</span>
              <span className="cap-value font-mono">
                {capability.ppk !== null && capability.ppk !== undefined ? capability.ppk.toFixed(3) : 'N/A'}
              </span>
            </div>
            <div className="capability-card">
              <span className="cap-label">Within Sigma (σ_w)</span>
              <span className="cap-value font-mono">{capability.sigmaWithin.toFixed(4)}</span>
            </div>
            <div className="capability-card">
              <span className="cap-label">Overall Sigma (σ_t)</span>
              <span className="cap-value font-mono">{capability.sigmaOverall.toFixed(4)}</span>
            </div>
          </div>
        </div>
      )}

      <div className="nelson-violations-section mt-4">
        <div className="section-header">
          <h4>Nelson rules</h4>
          <span className="muted">{(violations ?? []).length} hits across {nelsonGroups.length} rules</span>
        </div>
        {(violations ?? []).length === 0 ? (
          <div className="analytics-empty-note success">
            In statistical control: no Nelson rule 1-8 hits.
          </div>
        ) : (
          <div className="nelson-rule-list">
            {nelsonGroups.map((group) => {
              const open = expandedRuleId === group.ruleId
              return (
                <article key={group.ruleId} className={`nelson-rule-card ${group.severity.toLowerCase()}`}>
                  <button
                    type="button"
                    className="nelson-rule-summary"
                    onClick={() => setExpandedRuleId(open ? null : group.ruleId)}
                    aria-expanded={open}
                  >
                    <strong>R{group.ruleId}</strong>
                    <span>{group.ruleName.replace(/^Rule \d+:\s*/, '')}</span>
                    <em>{group.indexes.length}</em>
                  </button>
                  {open && (
                    <div className="nelson-rule-detail">
                      <p>{group.description}</p>
                      <div className="nelson-point-pills">
                        {group.indexes.map((index) => (
                          <button
                            key={`${group.ruleId}-${index}`}
                            type="button"
                            onClick={() => {
                              const point = primaryChart?.points[index]
                              if (point) onSelectRowIndex(point.rowIndex)
                            }}
                          >
                            #{index + 1}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </article>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
