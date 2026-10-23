import { useEffect, useMemo, useState } from 'react'
import ReactECharts from '../components/ReactECharts'
import { dashboardApi } from '../api'
import { formatDate, formatPercent } from '../format'
import { Button, MetricCard } from '../ui'
import type { DashboardSectionReport, DashboardProductionReport, Notice } from '../types'

type DashboardView = 'overkill' | 'leakage'

function ReportEmptyState({ message }: { message: string }) {
  return (
    <div className="dashboard-report-empty">
      <strong>No production data</strong>
      <span>{message}</span>
    </div>
  )
}

function ProductionReport({ view, report }: { view: DashboardView; report: DashboardSectionReport }) {
  const isOverkill = view === 'overkill'
  const accent = isOverkill ? '#d97706' : '#db2777'
  const title = isOverkill ? 'Overkill' : 'Leakage'

  const trendOption = useMemo(() => ({
    animationDuration: 350,
    color: ['#94a3b8', accent],
    tooltip: {
      trigger: 'axis',
      formatter: (params: Array<{ seriesName: string; value: number; axisValue: string }>) => {
        const point = report.trend.find((item) => item.date.slice(0, 10) === params[0]?.axisValue)
        if (!point) return ''
        return [
          `<strong>${point.date.slice(0, 10)}</strong>`,
          `Production: ${point.productionItems.toLocaleString()}`,
          `Evaluated: ${point.evaluatedItems.toLocaleString()}`,
          `${title}: ${point.anomalyItems.toLocaleString()}`,
          `Rate: ${(point.anomalyRate * 100).toFixed(2)}%`,
        ].join('<br/>')
      },
    },
    grid: { left: 48, right: 48, top: 34, bottom: 42 },
    legend: { top: 0, data: ['Production volume', `${title} rate`] },
    xAxis: {
      type: 'category',
      data: report.trend.map((item) => item.date.slice(0, 10)),
      axisLabel: { color: '#64748b', fontSize: 10, rotate: report.trend.length > 14 ? 35 : 0 },
      axisLine: { lineStyle: { color: '#cbd5e1' } },
    },
    yAxis: [
      {
        type: 'value',
        name: 'Items',
        minInterval: 1,
        axisLabel: { color: '#64748b' },
        splitLine: { lineStyle: { color: '#edf1f7' } },
      },
      {
        type: 'value',
        name: 'Rate',
        min: 0,
        max: (value: { max: number }) => Math.max(0.01, Math.ceil(value.max * 100) / 100),
        axisLabel: { formatter: (value: number) => `${(value * 100).toFixed(1)}%`, color: '#64748b' },
        splitLine: { show: false },
      },
    ],
    series: [
      {
        name: 'Production volume',
        type: 'bar',
        barMaxWidth: 28,
        data: report.trend.map((item) => item.productionItems),
        itemStyle: { borderRadius: [4, 4, 0, 0], color: '#cbd5e1' },
      },
      {
        name: `${title} rate`,
        type: 'line',
        yAxisIndex: 1,
        smooth: true,
        symbolSize: 7,
        data: report.trend.map((item) => item.anomalyRate),
        lineStyle: { width: 3, color: accent },
        itemStyle: { color: accent },
        areaStyle: { color: isOverkill ? 'rgba(217,119,6,.12)' : 'rgba(219,39,119,.12)' },
      },
    ],
  }), [accent, isOverkill, report.trend, title])

  const defectOption = useMemo(() => ({
    animationDuration: 350,
    color: [accent],
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      formatter: (params: Array<{ dataIndex: number }>) => {
        const item = report.defectsByName[params[0]?.dataIndex]
        return item ? `<strong>${item.defectName}</strong><br/>${item.count.toLocaleString()} records<br/>${(item.share * 100).toFixed(1)}%` : ''
      },
    },
    grid: { left: 130, right: 30, top: 10, bottom: 30 },
    xAxis: {
      type: 'value',
      minInterval: 1,
      axisLabel: { color: '#64748b' },
      splitLine: { lineStyle: { color: '#edf1f7' } },
    },
    yAxis: {
      type: 'category',
      inverse: true,
      data: report.defectsByName.map((item) => item.defectName),
      axisLabel: { color: '#334155', width: 112, overflow: 'truncate' },
      axisTick: { show: false },
      axisLine: { show: false },
    },
    series: [{
      type: 'bar',
      barMaxWidth: 22,
      data: report.defectsByName.map((item) => item.count),
      itemStyle: { borderRadius: [0, 4, 4, 0], color: accent },
      label: {
        show: true,
        position: 'right',
        color: '#475569',
        formatter: (params: { dataIndex: number }) => `${(report.defectsByName[params.dataIndex].share * 100).toFixed(1)}%`,
      },
    }],
  }), [accent, report.defectsByName])

  return (
    <div className="dashboard-report-stack">
      <section className="metrics-grid dashboard-production-metrics">
        <MetricCard
          label={isOverkill ? 'Production items' : 'Submitted items'}
          value={report.productionItems.toLocaleString()}
          hint={isOverkill ? 'Rows received from production' : 'Rows submitted to leakage queue'}
        />
        <MetricCard
          label="Evaluated"
          value={report.evaluatedItems.toLocaleString()}
          hint="Completed real verification records"
          tone="info"
        />
        <MetricCard
          label={`${title} findings`}
          value={report.anomalyItems.toLocaleString()}
          hint="Confirmed anomaly records"
          tone={isOverkill ? 'warning' : 'danger'}
        />
        <MetricCard
          label={`${title} rate`}
          value={formatPercent(report.anomalyRate)}
          hint="Findings divided by evaluated records"
          tone={isOverkill ? 'warning' : 'danger'}
        />
        <MetricCard
          label="Defect records"
          value={report.defectRecords.toLocaleString()}
          hint={isOverkill ? 'Production defects in selected period' : 'Defects linked to leakage findings'}
        />
      </section>

      <section className="dashboard-report-grid">
        <article className="panel dashboard-chart-panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">REAL PRODUCTION DATA</p>
              <h2>{title} rate trend</h2>
            </div>
            <span className="dashboard-updated-at">Last data: {formatDate(report.lastUpdatedAtUtc)}</span>
          </div>
          {report.trend.some((item) => item.productionItems > 0 || item.evaluatedItems > 0) ? (
            <ReactECharts option={trendOption} style={{ height: '340px', width: '100%' }} />
          ) : (
            <ReportEmptyState message={`No ${title.toLowerCase()} production records were found in this period.`} />
          )}
        </article>

        <article className="panel dashboard-chart-panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">GROUPED BY DEFECT NAME</p>
              <h2>Defect distribution</h2>
            </div>
            <span className="dashboard-updated-at">Top {report.defectsByName.length || 0}</span>
          </div>
          {report.defectsByName.length > 0 ? (
            <ReactECharts option={defectOption} style={{ height: '340px', width: '100%' }} />
          ) : (
            <ReportEmptyState message="No named defect records are available for this report." />
          )}
        </article>
      </section>
    </div>
  )
}

export function OverviewTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [view, setView] = useState<DashboardView>('overkill')
  const [days, setDays] = useState(14)
  const [report, setReport] = useState<DashboardProductionReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    let active = true
    setLoading(true)
    setLoadError(null)
    dashboardApi
      .productionReport(days)
      .then((nextReport) => {
        if (active) setReport(nextReport)
      })
      .catch((error: unknown) => {
        if (!active) return
        const message = error instanceof Error ? error.message : 'Failed to load production dashboard.'
        setLoadError(message)
        onNotice({ tone: 'error', message })
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [days, onNotice, reloadKey])

  // After a failed load the previous report (if any) is not shown as if it were current.
  const activeReport = loadError ? null : (report?.[view] ?? null)

  return (
    <div className="dashboard-production-workspace">
      <section className="dashboard-report-header">
        <div>
          <p className="eyebrow">PRODUCTION REPORTING</p>
          <h2>Overkill & Leakage Dashboard</h2>
          <p>All values are aggregated from stored production, verification, defect, and queue records. No sample metrics are used.</p>
        </div>
        <label className="dashboard-period-control">
          <span>Report period</span>
          <select value={days} onChange={(event) => setDays(Number(event.target.value))}>
            <option value={7}>Last 7 days</option>
            <option value={14}>Last 14 days</option>
            <option value={30}>Last 30 days</option>
          </select>
        </label>
      </section>

      <div className="dashboard-horizontal-tabs" role="tablist" aria-label="Dashboard report type">
        <button
          type="button"
          role="tab"
          aria-selected={view === 'overkill'}
          className={view === 'overkill' ? 'active overkill' : ''}
          onClick={() => setView('overkill')}
        >
          <span>Overkill</span>
          <small>{report ? `${report.overkill.anomalyItems.toLocaleString()} findings` : 'Production report'}</small>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={view === 'leakage'}
          className={view === 'leakage' ? 'active leakage' : ''}
          onClick={() => setView('leakage')}
        >
          <span>Leakage</span>
          <small>{report ? `${report.leakage.anomalyItems.toLocaleString()} findings` : 'Escape-risk report'}</small>
        </button>
      </div>

      {loading ? (
        <div className="dashboard-report-loading" role="status">Loading production data...</div>
      ) : loadError ? (
        <div className="dashboard-report-empty" role="alert">
          <strong>The production report could not be loaded</strong>
          <span>{loadError}</span>
          <Button variant="secondary" size="sm" onClick={() => setReloadKey((key) => key + 1)}>Try again</Button>
        </div>
      ) : activeReport ? (
        <ProductionReport view={view} report={activeReport} />
      ) : (
        <ReportEmptyState message="No report is available for this period." />
      )}
    </div>
  )
}
