import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import ReactECharts from '../ReactECharts'
import { productionFeatureApi } from '../../api'
import { formatDate, parseServerTime } from '../../format'
import { CHART, measureLine, refLines, timeAxis, toolbox, tooltipHtml, valueAxis, zoom } from '../../lib/chartTheme'
import type { Notice, ProductionFeatureAnalysis, ProductionFeatureJob } from '../../types'
import { SpcCharts } from './SpcCharts'
import { EwmaChart, CusumChart } from './TimeSeriesCharts'
import { PcaCharts } from './PcaCharts'

type Period = '1d' | '1w' | '1m' | 'custom'

export function ProductionFeatureCharts({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [machineCode, setMachineCode] = useState('All')
  const [lineCode, setLineCode] = useState('All')
  const [period, setPeriod] = useState<Period>('1w')
  const [defectName, setDefectName] = useState('All')
  const [judge, setJudge] = useState('All')
  const [fromUtc, setFromUtc] = useState('')
  const [toUtc, setToUtc] = useState('')
  const [job, setJob] = useState<ProductionFeatureJob | null>(null)
  const [selectedFeature, setSelectedFeature] = useState('')
  const [starting, setStarting] = useState(false)
  const [analysisType, setAnalysisType] = useState<'SPC' | 'EWMA' | 'CUSUM' | 'PCA'>('SPC')
  const [analysis, setAnalysis] = useState<ProductionFeatureAnalysis | null>(null)
  const [analyzing, setAnalyzing] = useState(false)
  const pollRef = useRef<number | null>(null)

  const stopPolling = useCallback(() => {
    if (pollRef.current) {
      window.clearTimeout(pollRef.current)
      pollRef.current = null
    }
  }, [])

  const pollJob = useCallback((jobId: string) => {
    stopPolling()
    const tick = async () => {
      try {
        const next = await productionFeatureApi.getJob(jobId)
        setJob(next)
        setSelectedFeature((current) => current && (next.series ?? []).some((s) => s.featureName === current)
          ? current
          : next.series?.[0]?.featureName ?? '')
        if (next.status === 'Queued' || next.status === 'Running') {
          pollRef.current = window.setTimeout(() => void tick(), 1200)
        }
      } catch {
        pollRef.current = window.setTimeout(() => void tick(), 2500)
      }
    }
    void tick()
  }, [stopPolling])

  useEffect(() => {
    const controller = new AbortController()
    productionFeatureApi.facets(controller.signal)
      .then((facets) => setJob((current) => current ?? facets))
      .catch(() => undefined)
    return () => {
      controller.abort()
      stopPolling()
    }
  }, [stopPolling])

  async function startJob() {
    if (period === 'custom' && (!fromUtc || !toUtc)) {
      onNotice({ tone: 'error', message: 'Custom period requires both From and To.' })
      return
    }
    setStarting(true)
    try {
      if (job?.jobId && (job.status === 'Queued' || job.status === 'Running')) {
        await productionFeatureApi.cancelJob(job.jobId)
      }
      const next = await productionFeatureApi.startJob({
        machineCode: machineCode === 'All' ? undefined : machineCode,
        lineCode: lineCode === 'All' ? undefined : lineCode,
        period,
        fromUtc: period === 'custom' && fromUtc ? new Date(fromUtc).toISOString() : undefined,
        toUtc: period === 'custom' && toUtc ? new Date(toUtc).toISOString() : undefined,
        defectName: defectName === 'All' ? undefined : defectName,
        judge: judge === 'All' ? undefined : judge,
      })
      setJob(next)
      setSelectedFeature(next.series[0]?.featureName ?? '')
      if (next.status === 'Queued' || next.status === 'Running') pollJob(next.jobId)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to start feature loading.' })
    } finally {
      setStarting(false)
    }
  }

  async function runAnalysis(featureName = selectedFeature) {
    if (!job?.jobId || (job.series ?? []).length === 0) return
    setAnalyzing(true)
    try {
      const next = await productionFeatureApi.analyze(
        job.jobId,
        analysisType,
        featureName || job.series?.[0]?.featureName,
        analysisType === 'PCA' ? (job.series ?? []).slice(0, 4).map((s) => s.featureName) : undefined,
      )
      setAnalysis(next)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to analyze loaded features.' })
    } finally {
      setAnalyzing(false)
    }
  }

  async function cancelJob() {
    if (!job?.jobId) return
    try {
      const next = await productionFeatureApi.cancelJob(job.jobId)
      setJob(next)
      stopPolling()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to cancel feature loading.' })
    }
  }

  const running = job?.status === 'Queued' || job?.status === 'Running'
  const activeSeries = (job?.series ?? []).find((s) => s.featureName === selectedFeature) ?? job?.series?.[0]
  const chartOption = useMemo(() => {
    if (!activeSeries) return null
    const points = activeSeries.points
      .map((point) => ({ point, time: parseServerTime(point.inspectedAtUtc) }))
      .filter((item): item is { point: typeof item.point; time: number } => item.time !== null)
      .sort((a, b) => a.time - b.time)
    if (points.length === 0) return null

    const values = points.map((item) => item.point.value)
    const mean = values.reduce((sum, v) => sum + v, 0) / values.length
    const sigma = Math.sqrt(values.reduce((sum, v) => sum + (v - mean) ** 2, 0) / Math.max(values.length - 1, 1))
    const ucl = sigma > 0 ? mean + 3 * sigma : null
    const lcl = sigma > 0 ? mean - 3 * sigma : null
    const outside = points.filter((item) => ucl !== null && lcl !== null && (item.point.value > ucl || item.point.value < lcl))

    return {
      color: [CHART.primary],
      tooltip: {
        trigger: 'axis',
        formatter: (params: Array<{ dataIndex: number }>) => {
          const item = points[params[0]?.dataIndex]
          if (!item) return ''
          return tooltipHtml(activeSeries.featureName, [
            ['Value', item.point.value],
            ['Key', item.point.keyId],
            ['Defect', item.point.defectName || '—'],
            ['Judge', item.point.judge || '—'],
            ['Time', formatDate(item.point.inspectedAtUtc)],
          ])
        },
      },
      toolbox: toolbox(),
      grid: { left: 12, right: 16, top: 28, bottom: 36 },
      xAxis: timeAxis(),
      yAxis: valueAxis(),
      dataZoom: zoom(),
      series: [
        measureLine(activeSeries.featureName, points.map((item) => [item.time, item.point.value]), {
          markLine: refLines([
            { name: 'UCL', value: ucl, kind: 'control' },
            { name: 'Mean', value: mean, kind: 'mean' },
            { name: 'LCL', value: lcl, kind: 'control' },
          ]),
        }),
        {
          name: 'Beyond ±3σ',
          type: 'scatter',
          data: outside.map((item) => [item.time, item.point.value]),
          symbolSize: 7,
          itemStyle: { color: CHART.bad, opacity: 1 },
          tooltip: { show: false },
          z: 5,
        },
      ],
    }
  }, [activeSeries])

  return (
    <section className="panel production-feature-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">PACED FEATURE EXTRACTION</p>
          <h2>Load production features in small batches</h2>
        </div>
        <div className="production-feature-actions">
          <button type="button" disabled={starting} onClick={() => void startJob()}>
            {starting ? 'Starting…' : running ? 'Restart load' : 'Start load'}
          </button>
          {running && (
            <button type="button" className="ghost-button" onClick={() => void cancelJob()}>Stop</button>
          )}
        </div>
      </div>

      <div className="production-feature-filters compact">
        <label>
          Machine
          <select value={machineCode} onChange={(e) => setMachineCode(e.target.value)}>
            <option value="All">All</option>
            {(job?.machines ?? []).map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>
        <label>
          Line
          <select value={lineCode} onChange={(e) => setLineCode(e.target.value)}>
            <option value="All">All</option>
            {(job?.lines ?? []).map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>
        <label>
          Defect
          <select value={defectName} onChange={(e) => setDefectName(e.target.value)}>
            <option value="All">All names</option>
            {(job?.defectNames ?? []).map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>
        <label>
          Judge
          <select value={judge} onChange={(e) => setJudge(e.target.value)}>
            <option value="All">All</option>
            {(job?.judges ?? []).map((item) => <option key={item} value={item}>{item}</option>)}
          </select>
        </label>
        <label>
          Period
          <select value={period} onChange={(e) => setPeriod(e.target.value as Period)}>
            <option value="1d">1d</option>
            <option value="1w">1w</option>
            <option value="1m">1m</option>
            <option value="custom">Custom</option>
          </select>
        </label>
        {period === 'custom' && (
          <>
            <label>
              From
              <input type="datetime-local" value={fromUtc} onChange={(e) => setFromUtc(e.target.value)} />
            </label>
            <label>
              To
              <input type="datetime-local" value={toUtc} onChange={(e) => setToUtc(e.target.value)} />
            </label>
          </>
        )}
      </div>

      <div className="production-feature-progress">
        <div className="production-feature-progress-bar">
          <span style={{ transform: `scaleX(${Math.min(1, Math.max(0, job?.progress ?? 0))})` }} />
        </div>
        <p className="muted">{job?.message || 'Choose a scope, then start a paced SeaweedFS load.'}</p>
        <div className="production-feature-summary">
          <span>{job?.processedDefects.toLocaleString() ?? 0}/{job?.totalDefects.toLocaleString() ?? 0} defects</span>
          <span>{job?.loadedPacks.toLocaleString() ?? 0} packs</span>
          <span>{job?.featureValueCount.toLocaleString() ?? 0} values</span>
          <span>{job?.featuresWithValues.toLocaleString() ?? 0} series</span>
          <span>{job?.status || 'Idle'}</span>
        </div>
      </div>

      {job && (job.series ?? []).length > 0 ? (
        <>
          <div className="production-feature-pills">
            {(job.series ?? []).map((series) => (
              <button
                key={series.featureName}
                type="button"
                className={series.featureName === activeSeries?.featureName ? 'active' : ''}
                onClick={() => {
                  setSelectedFeature(series.featureName)
                  if (job.jobId) void runAnalysis(series.featureName)
                }}
              >
                {series.featureName}
                <small>{series.sampleCount}</small>
              </button>
            ))}
          </div>
          {chartOption && <ReactECharts option={chartOption} style={{ height: 360, width: '100%' }} notMerge />}
          {activeSeries && (
            <p className="muted">
              {activeSeries.featureName}: min {activeSeries.min.toFixed(3)}, mean {activeSeries.mean.toFixed(3)}, max {activeSeries.max.toFixed(3)}
            </p>
          )}

          <div className="production-analysis-bar">
            {(['SPC', 'EWMA', 'CUSUM', 'PCA'] as const).map((type) => (
              <button
                key={type}
                type="button"
                className={analysisType === type ? 'active' : ''}
                onClick={() => {
                  setAnalysisType(type)
                  void runAnalysis(selectedFeature)
                }}
              >
                {type}
              </button>
            ))}
            <button type="button" className="ghost-button compact" disabled={analyzing} onClick={() => void runAnalysis()}>
              {analyzing ? 'Analyzing…' : `Analyze ${selectedFeature || 'selected feature'}`}
            </button>
          </div>

          {analysis?.spc && <SpcCharts result={analysis.spc} isNgOnlyDataset onSelectRowIndex={() => undefined} />}
          {analysis?.ewma && <EwmaChart result={analysis.ewma} onSelectRowIndex={() => undefined} />}
          {analysis?.cusum && <CusumChart result={analysis.cusum} onSelectRowIndex={() => undefined} />}
          {analysis?.pca && <PcaCharts result={analysis.pca} onSelectRowIndex={() => undefined} />}
        </>
      ) : (
        <div className="dashboard-report-empty">
          <strong>Waiting for extracted values</strong>
          <span>The worker reads small product-pack batches, extracts numbers, then the chart updates as each batch arrives.</span>
        </div>
      )}
    </section>
  )
}
