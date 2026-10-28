import { lazy, Suspense, useCallback, useDeferredValue, useEffect, useMemo, useState } from 'react'
import ReactECharts from '../components/ReactECharts'
import { connectionsApi, productsApi, verificationApi } from '../api'
import { formatDateTime, formatPercent, paginate } from '../format'
import { TabSkeleton } from '../components/TabSkeleton'
import { Button, ChartSkeleton, DataTable, DefectFeatureInspector, Field, Icon, JudgeBadge, LoadFailure, MetricCard, Modal, Pagination, StatusPill } from '../ui'
import { ThumbnailCard } from '../components/overkill/ThumbnailCard'
import { ThumbnailZoomModal } from '../components/overkill/ThumbnailZoomModal'
import { originalVpOf } from '../components/overkill/vpImageStore'
const OpReconciliationTab = lazy(() => import('./OpReconciliationTab').then((m) => ({ default: m.OpReconciliationTab })))

import { STATUS, statusChip, statusSolid } from '../lib/status'
import type {
  AnomalyThumbnailItem,
  DatabaseConnection,
  DefectRecord,
  Notice,
  OverkillAnalysis,
  OverkillCandidate,
  OverkillSystemShare,
  OverkillTrendResponse,
  TabId,
} from '../types'

/** Line risk level → verdict colour: only CRITICAL is solid, so it is the one that catches the eye on a long table. */
function riskBadgeStyle(level: string) {
  if (level === 'CRITICAL') return statusSolid('ng')
  if (level === 'HIGH') return statusChip('ng')
  if (level === 'WARNING') return statusChip('overkill')
  return statusChip('ok')
}

type OverkillActiveView = 'trend' | 'lines' | 'reconciliation' | 'thumbnails' | 'leakage' | 'systems'

export function OverkillTab({
  onNotice,
  onNavigateTab,
}: {
  onNotice: (notice: Notice) => void
  onNavigateTab?: (section: 'dashboard' | 'operations' | 'products' | 'settings', tab: TabId) => void
}) {
  const [activeView, setActiveView] = useState<OverkillActiveView>('trend')

  // Common filters
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [machineCode, setMachineCode] = useState('All')
  const [lineCode, setLineCode] = useState('All')
  const [modelCode, setModelCode] = useState('All')
  const [defectFamily, setDefectFamily] = useState('All')
  const [minNgShare, setMinNgShare] = useState('0.3')
  const [systemFilter, setSystemFilter] = useState('All')

  // Time Period & Window Filter state (All / Day / Week / Month with backward step)
  type TimePeriod = 'all' | 'day' | 'week' | 'month'
  const [period, setPeriod] = useState<TimePeriod>('all')
  const [periodOffset, setPeriodOffset] = useState<number>(0)
  const [lineChartHeight, setLineChartHeight] = useState(420)
  // While the slider is dragged the label follows at once; the chart resizes when the drag catches up.
  const chartHeight = useDeferredValue(lineChartHeight)
  const [showLineCounts, setShowLineCounts] = useState(true)
  const [showLineRates, setShowLineRates] = useState(true)

  // Trend & Classifier Breakdown state
  const [trendData, setTrendData] = useState<OverkillTrendResponse | null>(null)
  const [loadingTrend, setLoadingTrend] = useState(false)
  const [trendError, setTrendError] = useState<string | null>(null)
  const [analysisError, setAnalysisError] = useState<string | null>(null)
  const [thumbnailError, setThumbnailError] = useState<string | null>(null)

  // Anomaly Thumbnails state
  const [thumbnailItems, setThumbnailItems] = useState<AnomalyThumbnailItem[]>([])
  const [thumbnailCategory, setThumbnailCategory] = useState<string>('All')
  const [galleryScopeOpen, setGalleryScopeOpen] = useState(false)
  const [galleryModel, setGalleryModel] = useState('All')
  const [galleryLine, setGalleryLine] = useState('All')
  const [galleryCluster, setGalleryCluster] = useState('All')
  const [dashboardScopeOpen, setDashboardScopeOpen] = useState(false)
  const [thumbnailSearch, setThumbnailSearch] = useState<string>('')
  const [appliedThumbnailSearch, setAppliedThumbnailSearch] = useState<string>('')
  const [thumbnailPage, setThumbnailPage] = useState<number>(1)
  const [thumbnailPageSize, setThumbnailPageSize] = useState<number>(24)
  const [thumbnailTotalCount, setThumbnailTotalCount] = useState<number>(0)
  const [thumbnailTotalPages, setThumbnailTotalPages] = useState<number>(1)
  const [loadingThumbnails, setLoadingThumbnails] = useState<boolean>(false)

  // Zoom Image & Feature Inspector modals
  // Zoom dialog: the item and the angle it opens on; null = closed.
  const [zoomRequest, setZoomRequest] = useState<{ item: AnomalyThumbnailItem; vp: string } | null>(null)
  const [inspectedDefect, setInspectedDefect] = useState<DefectRecord | null>(null)
  const [inspectedVp, setInspectedVp] = useState<string | undefined>(undefined)
  const [loadingDefectInspect, setLoadingDefectInspect] = useState<boolean>(false)

  // Multi-VP Image State for Anomaly Cards & Zoom Modal

  // Legacy System Analysis state
  const [analysis, setAnalysis] = useState<OverkillAnalysis | null>(null)
  const [candidates, setCandidates] = useState<OverkillCandidate[]>([])
  const [candidatePage, setCandidatePage] = useState(1)
  const [selected, setSelected] = useState<OverkillSystemShare | null>(null)
  const [page, setPage] = useState(1)
  const [busy, setBusy] = useState(false)
  const [autoSelected, setAutoSelected] = useState(false)
  const [connectionsLoaded, setConnectionsLoaded] = useState(false)

  const machines = useMemo(() => [...new Set(connections.map((item) => item.machineCode))].sort(), [connections])
  const models = useMemo(() => [...new Set(connections.map((item) => item.modelCode).filter(Boolean))].sort(), [connections])

  const detectedMachine = useMemo(() => {
    if (candidates.length > 0) {
      const counts: Record<string, number> = {}
      for (const item of candidates) {
        if (item.machineCode) counts[item.machineCode] = (counts[item.machineCode] || 0) + 1
      }
      const sorted = Object.entries(counts).sort((a, b) => b[1] - a[1])
      if (sorted[0]?.[0]) return sorted[0][0]
    }
    return machines[0] ?? ''
  }, [candidates, machines])

  useEffect(() => {
    if (!autoSelected && detectedMachine && machineCode === 'All') {
      setMachineCode(detectedMachine)
      setAutoSelected(true)
    }
  }, [autoSelected, detectedMachine, machineCode])

  const lines = useMemo(
    () =>
      [...new Set(connections.filter((item) => machineCode === 'All' || item.machineCode === machineCode).map((item) => item.lineCode))].sort(),
    [connections, machineCode],
  )

  const availableFamilies = useMemo(() => {
    const list = trendData?.familyBreakdown?.map((f) => f.defectFamily).filter(Boolean) || []
    return ['All', ...new Set(list)]
  }, [trendData])

  const galleryLines = useMemo(
    () => [...new Set(connections.filter((c) => galleryModel === 'All' || c.modelCode === galleryModel).map((c) => c.lineCode))].sort(),
    [connections, galleryModel],
  )

  const galleryClusters = useMemo(() => {
    const values = thumbnailItems.flatMap((item) => [item.defectFamily, item.defectType, item.defectName]).filter(Boolean) as string[]
    return ['All', ...new Set(values)].sort()
  }, [thumbnailItems])

  const loadConnections = useCallback(async () => {
    try {
      const payload = await connectionsApi.list({ page: 1, pageSize: 100 })
      setConnections(payload.items)
      const firstMachine = [...new Set(payload.items.map((item) => item.machineCode).filter(Boolean))].sort()[0]
      if (firstMachine) {
        setMachineCode((prev) => (prev === 'All' ? firstMachine : prev))
        setAutoSelected(true)
      }
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load connections.' })
    } finally {
      setConnectionsLoaded(true)
    }
  }, [onNotice])

  // Compute date range based on period and offset
  const { dateFromUtc, dateToUtc, periodLabel } = useMemo(() => {
    if (period === 'all') {
      return { dateFromUtc: undefined, dateToUtc: undefined, periodLabel: 'All Time' }
    }

    const now = new Date()
    const from = new Date(now)
    const to = new Date(now)

    if (period === 'day') {
      from.setDate(now.getDate() + periodOffset)
      from.setHours(0, 0, 0, 0)
      to.setDate(now.getDate() + periodOffset)
      to.setHours(23, 59, 59, 999)

      const label = periodOffset === 0
        ? `Today (${from.toLocaleDateString()})`
        : periodOffset === -1
        ? `Yesterday (${from.toLocaleDateString()})`
        : `${Math.abs(periodOffset)} days ago (${from.toLocaleDateString()})`

      return { dateFromUtc: from.toISOString(), dateToUtc: to.toISOString(), periodLabel: label }
    }

    if (period === 'week') {
      const dayOfWeek = now.getDay()
      const diffToMonday = (dayOfWeek + 6) % 7
      from.setDate(now.getDate() - diffToMonday + periodOffset * 7)
      from.setHours(0, 0, 0, 0)
      to.setDate(from.getDate() + 6)
      to.setHours(23, 59, 59, 999)

      const label = periodOffset === 0
        ? `This Week (${from.toLocaleDateString()} - ${to.toLocaleDateString()})`
        : periodOffset === -1
        ? `Last Week (${from.toLocaleDateString()} - ${to.toLocaleDateString()})`
        : `${Math.abs(periodOffset)} weeks ago (${from.toLocaleDateString()} - ${to.toLocaleDateString()})`

      return { dateFromUtc: from.toISOString(), dateToUtc: to.toISOString(), periodLabel: label }
    }

    if (period === 'month') {
      from.setDate(1)
      from.setMonth(now.getMonth() + periodOffset)
      from.setHours(0, 0, 0, 0)
      to.setMonth(from.getMonth() + 1, 0)
      to.setHours(23, 59, 59, 999)

      const label = periodOffset === 0
        ? `This Month (${from.toLocaleDateString(undefined, { month: 'short', year: 'numeric' })})`
        : periodOffset === -1
        ? `Last Month (${from.toLocaleDateString(undefined, { month: 'short', year: 'numeric' })})`
        : `${Math.abs(periodOffset)} months ago (${from.toLocaleDateString(undefined, { month: 'short', year: 'numeric' })})`

      return { dateFromUtc: from.toISOString(), dateToUtc: to.toISOString(), periodLabel: label }
    }

    return { dateFromUtc: undefined, dateToUtc: undefined, periodLabel: '' }
  }, [period, periodOffset])

  // Load Overkill & Real NG Trend Data
  const loadTrendData = useCallback(async () => {
    setLoadingTrend(true)
    setTrendError(null)
    try {
      const data = await verificationApi.overkillTrend({
        machineCode: machineCode === 'All' ? undefined : machineCode,
        lineCode: lineCode === 'All' ? undefined : lineCode,
        modelCode: modelCode === 'All' ? undefined : modelCode,
        defectFamily: defectFamily === 'All' ? undefined : defectFamily,
        fromUtc: dateFromUtc,
        toUtc: dateToUtc,
      })
      setTrendData(data)
    } catch (error) {
      setTrendError(error instanceof Error ? error.message : 'Failed to load Overkill trend analysis.')
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill trend analysis.' })
    } finally {
      setLoadingTrend(false)
    }
  }, [machineCode, lineCode, modelCode, defectFamily, dateFromUtc, dateToUtc, onNotice])

  // Load Anomaly Thumbnails
  const loadThumbnails = useCallback(async () => {
    setLoadingThumbnails(true)
    setThumbnailError(null)
    try {
        const res = await verificationApi.anomalyThumbnails({
        category: thumbnailCategory,
        defectFamily: defectFamily === 'All' ? undefined : defectFamily,
        machineCode: machineCode === 'All' ? undefined : machineCode,
        lineCode: galleryLine === 'All' ? undefined : galleryLine,
        modelCode: galleryModel === 'All' ? undefined : galleryModel,
        defectCluster: galleryCluster === 'All' ? undefined : galleryCluster,
        search: appliedThumbnailSearch.trim() || undefined,
        page: thumbnailPage,
        pageSize: thumbnailPageSize,
      })
      setThumbnailItems(res.items ?? [])
      setThumbnailTotalCount(res.totalCount ?? 0)
      setThumbnailTotalPages(res.totalPages ?? 1)
    } catch (error) {
      setThumbnailError(error instanceof Error ? error.message : 'Failed to load anomaly thumbnails.')
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load anomaly thumbnails.' })
    } finally {
      setLoadingThumbnails(false)
    }
  }, [thumbnailCategory, defectFamily, machineCode, galleryLine, galleryModel, galleryCluster, appliedThumbnailSearch, thumbnailPage, thumbnailPageSize, onNotice])

  // Load Legacy Analysis
  const loadAnalysis = useCallback(async () => {
    setBusy(true)
    setAnalysisError(null)
    try {
      const [nextAnalysis, nextCandidates] = await Promise.all([
        verificationApi.overkillAnalysis({
          machineCode: machineCode === 'All' ? undefined : machineCode,
          lineCode: lineCode === 'All' ? undefined : lineCode,
          minNgShare: Number(minNgShare) || 0,
        }),
        productsApi.overkill(),
      ])
      setAnalysis(nextAnalysis)
      setCandidates(nextCandidates)
      setSelected(nextAnalysis.systems[0] ?? null)
      setPage(1)
    } catch (error) {
      setAnalysisError(error instanceof Error ? error.message : 'Failed to load Overkill system analysis.')
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill system analysis.' })
    } finally {
      setBusy(false)
    }
  }, [machineCode, lineCode, minNgShare, onNotice])

  useEffect(() => {
    void loadConnections()
  }, [loadConnections])

  useEffect(() => {
    if (!connectionsLoaded) return
    void loadTrendData()
  }, [connectionsLoaded, loadTrendData])

  useEffect(() => {
    if (activeView === 'thumbnails') {
      void loadThumbnails()
    }
  }, [activeView, loadThumbnails])

  useEffect(() => {
    if (activeView === 'systems') {
      void loadAnalysis()
    }
  }, [activeView, loadAnalysis])

  // Inspector handler for thumbnail items
  const handleInspectDefect = useCallback(async (item: AnomalyThumbnailItem, targetVp?: string) => {
    if (!item.keyId) return
    const vp = targetVp || originalVpOf(item)
    setInspectedVp(vp)
    setLoadingDefectInspect(true)
    try {
      const defs = await productsApi.defects(item.keyId)
      const matched = defs.find((d) => d.id === item.defectId) ?? defs[0]
      if (matched) {
        setInspectedDefect(matched)
      } else {
        // Construct fallback defect record from item
        setInspectedDefect({
          id: item.defectId ?? item.productId,
          keyId: item.keyId,
          defectTable: 'AOI_DEFECTS',
          defectName: item.defectName ?? 'Defect',
          defectType: item.defectType ?? item.defectFamily ?? 'Optical',
          componentName: item.componentName ?? null,
          coordinateX: null,
          coordinateY: null,
          severity: item.severity ?? 'Major',
          judge: item.stationJudge,
          confidenceScore: item.confidenceScore ?? 1.0,
          imageUri: item.imageUri ?? item.thumbnailUrl ?? null,
          imageUrl: item.thumbnailUrl ?? item.imageUri ?? null,
          feature1StorageUri: null,
          feature2StorageUri: null,
          createdAtUtc: item.inspectedAtUtc,
          featureJson: item.keyFeatures ? JSON.stringify(item.keyFeatures, null, 2) : null,
          customFeatureJson: null,
          reason: item.decisionReason,
        })
      }
    } catch {
      onNotice({ tone: 'error', message: 'Failed to fetch defect features for inspection.' })
    } finally {
      setLoadingDefectInspect(false)
    }
  }, [onNotice])

  // Stable identities so the memoised gallery cards do not re-render when unrelated tab state changes.
  const openZoom = useCallback((item: AnomalyThumbnailItem, vp: string) => setZoomRequest({ item, vp }), [])
  const inspectThumbnail = useCallback((item: AnomalyThumbnailItem, vp: string) => void handleInspectDefect(item, vp), [handleInspectDefect])

  // Dual-Axis Trend Chart Option
  const trendChartOption = useMemo(() => {
    if (!trendData?.trendPoints || trendData.trendPoints.length === 0) return null

    const dates = trendData.trendPoints.map((p) => p.dateLabel)
    const production = trendData.trendPoints.map((p) => p.totalInspected)
    const stationNgRates = trendData.trendPoints.map((p) => +(p.stationNgRate * 100).toFixed(2))
    const realNgRates = trendData.trendPoints.map((p) => +(p.realNgRate * 100).toFixed(2))
    const overkillRates = trendData.trendPoints.map((p) => +(p.overkillRate * 100).toFixed(2))
    const overkillShares = trendData.trendPoints.map((p) => +(p.overkillShare * 100).toFixed(1))

    return {
      title: {
        text: 'Trend Decomposition: Station NG vs Real NG vs Overkill',
        subtext: `Classifier Rule analysis for Machine: ${trendData.machineCode}, Line: ${trendData.lineCode}`,
        left: 'center',
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'cross' },
        formatter: (rawParams: unknown) => {
          const params = rawParams as Array<{ seriesName: string; value: number; marker: string; name?: string; axisValue?: string }>
          const p0 = params[0]
          const axisVal = p0?.axisValue ?? p0?.name ?? ''
          const pt = trendData.trendPoints.find((p) => p.dateLabel === axisVal)
          let tip = `<strong>${axisVal}</strong><br/>`
          if (pt) {
            tip += `Inspection Throughput: <b>${pt.totalInspected.toLocaleString()}</b><br/>`
            tip += `Station NG Alarms: <b>${pt.stationNgCount}</b> (${(pt.stationNgRate * 100).toFixed(2)}%)<br/>`
            tip += `Real Defect: <b style="color:var(--status-real-text)">${pt.realNgCount}</b> (${(pt.realNgRate * 100).toFixed(2)}%)<br/>`
            tip += `Overkill (False Alarm): <b style="color:var(--status-overkill-text)">${pt.overkillCount}</b> (${(pt.overkillRate * 100).toFixed(2)}%)<br/>`
            tip += `Overkill Share in NG: <b style="color:var(--status-overkill-text)">${(pt.overkillShare * 100).toFixed(1)}%</b><br/>`
            if (pt.leakageCount > 0) {
              tip += `Defect Escape (Leakage): <b style="color:var(--status-leakage-text)">${pt.leakageCount}</b><br/>`
            }
          }
          return tip
        },
      },
      legend: {
        bottom: 0,
        data: ['Throughput', 'Station NG Rate (%)', 'Real NG Rate (%)', 'Overkill Rate (%)', 'Overkill Share in NG (%)'],
      },
      grid: { left: '3%', right: '4%', bottom: '12%', top: '18%', containLabel: true },
      xAxis: {
        type: 'category',
        data: dates,
      },
      yAxis: [
        {
          type: 'value',
          name: 'Throughput',
        },
        {
          type: 'value',
          name: 'Rate (%)',
          axisLabel: { formatter: '{value}%' },
          splitLine: { show: false },
        },
      ],
      series: [
        {
          name: 'Throughput',
          type: 'bar',
          barMaxWidth: 28,
          itemStyle: { color: '#cbd5e1' },
          label: {
            show: true,
            position: 'top',
            formatter: (p: any) => {
              const val = Number(p.value)
              if (!val || val === 0) return ''
              return val >= 1000 ? (val / 1000).toFixed(1) + 'k' : String(val)
            },
            color: '#475569',
            fontSize: 11,
            fontWeight: 600,
          },
          data: production,
        },
        {
          name: 'Station NG Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.na.fill },
          lineStyle: { width: 1.5, type: 'dashed' },
          data: stationNgRates,
        },
        {
          name: 'Real NG Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.real.fill },
          lineStyle: { width: 1.5 },
          label: {
            show: true,
            position: 'top',
            formatter: '{c}%',
            color: STATUS.real.fill,
            fontSize: 11,
            fontWeight: 600,
          },
          data: realNgRates,
        },
        {
          name: 'Overkill Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.overkill.fill },
          lineStyle: { width: 1.5 },
          label: {
            show: true,
            position: 'bottom',
            formatter: '{c}%',
            color: STATUS.overkill.text,
            fontSize: 11,
            fontWeight: 700,
          },
          data: overkillRates,
        },
        {
          name: 'Overkill Share in NG (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.overkill.text },
          lineStyle: { width: 1.5, type: 'dotted' },
          data: overkillShares,
        },
      ],
      backgroundColor: 'transparent',
    }
  }, [trendData])

  // Cross-Line Comparison Chart Option
  const lineComparisonChartOption = useMemo(() => {
    if (!trendData?.lineComparison || trendData.lineComparison.length === 0) return null

    const lines = trendData.lineComparison.map((l) => l.lineCode)
    const okCounts = trendData.lineComparison.map((l) => l.okCount)
    const realDefectCounts = trendData.lineComparison.map((l) => l.realNgCount)
    const overkillCounts = trendData.lineComparison.map((l) => l.overkillCount)
    const otherNgCounts = trendData.lineComparison.map((l) =>
      Math.max(0, l.stationNgCount - l.realNgCount - l.overkillCount),
    )
    const ngRates = trendData.lineComparison.map((l) => +(l.stationNgRate * 100).toFixed(2))
    const overkillRates = trendData.lineComparison.map((l) => +(l.overkillRate * 100).toFixed(2))
    const realNgRates = trendData.lineComparison.map((l) => +(l.realNgRate * 100).toFixed(2))

    return {
      title: {
        text: `Cross-Line Comparison: Real Defect & Overkill Detection (${periodLabel})`,
        subtext: 'Detected counts and rates across production lines',
        left: 'center',
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (rawParams: unknown) => {
          const params = rawParams as Array<{ seriesName: string; value: number; marker: string; axisValue?: string; name?: string }>
          const p0 = params[0]
          const lineName = p0?.axisValue ?? p0?.name ?? ''
          const item = trendData.lineComparison.find((l) => l.lineCode === lineName)
          let tip = `<strong>Line: ${lineName}</strong> (${item?.machineCode || ''})<br/>`
          if (item) {
            tip += `Throughput: <b>${item.totalInspected.toLocaleString()}</b> units<br/>`
            tip += `OK Units: <b style="color:var(--status-ok-text)">${item.okCount.toLocaleString()}</b><br/>`
            tip += `Station NG Alarms: <b style="color:var(--status-ng-text)">${item.stationNgCount.toLocaleString()}</b><br/>`
            tip += `Station NG Rate: <b style="color:var(--status-ng-text)">${(item.stationNgRate * 100).toFixed(2)}%</b><br/>`
            tip += `Real Defect Detect: <b style="color:var(--status-real-text)">${item.realNgCount}</b> (${(item.realNgRate * 100).toFixed(2)}% of throughput)<br/>`
            tip += `Overkill Detect: <b style="color:var(--status-overkill-text)">${item.overkillCount}</b> (${(item.overkillRate * 100).toFixed(2)}% of throughput)<br/>`
            if (item.naCount > 0) {
              tip += `Unclassified (N/A): <b>${item.naCount}</b><br/>`
            }
            if (item.leakageCount > 0) {
              tip += `Defect Escape (Leakage): <b style="color:var(--status-leakage-text)">${item.leakageCount}</b><br/>`
            }
            tip += `Assessment: <i>${item.statusAssessment}</i>`
          }
          return tip
        },
      },
      legend: {
        bottom: 0,
        data: [
          ...(showLineCounts ? ['OK Units', 'Real Defect Detect', 'Overkill Detect', 'Other / Unclassified NG'] : []),
          ...(showLineRates ? ['NG Rate (%)', 'Real Defect Rate (%)', 'Overkill Rate (%)'] : []),
        ],
      },
      grid: { left: '3%', right: '4%', bottom: '12%', top: '18%', containLabel: true },
      xAxis: {
        type: 'category',
        data: lines,
      },
      yAxis: [
        {
          type: 'value',
          name: 'Throughput (Units)',
        },
        {
          type: 'value',
          name: 'Rate (%)',
          axisLabel: { formatter: '{value}%' },
          splitLine: { show: false },
        },
      ],
      series: [
        {
          name: 'OK Units',
          type: 'bar',
          stack: 'Throughput',
          barMaxWidth: 42,
          itemStyle: { color: STATUS.ok.fill },
          label: {
            show: true,
            position: 'inside',
            formatter: (p: any) => {
              const val = Number(p.value)
              if (!val || val === 0) return ''
              const item = trendData.lineComparison[p.dataIndex]
              if (!item || item.totalInspected === 0) return val.toLocaleString()
              const pct = ((val / item.totalInspected) * 100).toFixed(1)
              return `${pct}%\n(${val >= 1000 ? (val / 1000).toFixed(1) + 'k' : val})`
            },
            color: '#ffffff',
            fontSize: 11,
            fontWeight: 700,
            lineHeight: 12,
          },
          data: okCounts,
        },
        {
          name: 'Real Defect Detect',
          type: 'bar',
          stack: 'Throughput',
          barMaxWidth: 42,
          itemStyle: { color: STATUS.real.fill },
          label: {
            show: true,
            position: 'inside',
            formatter: (p: any) => {
              const val = Number(p.value)
              if (!val || val === 0) return ''
              const item = trendData.lineComparison[p.dataIndex]
              if (!item || item.totalInspected === 0) return val.toLocaleString()
              const pct = ((val / item.totalInspected) * 100).toFixed(1)
              return `${pct}%\n(${val})`
            },
            color: '#ffffff',
            fontSize: 11,
            fontWeight: 700,
            lineHeight: 12,
          },
          data: realDefectCounts,
        },
        {
          name: 'Overkill Detect',
          type: 'bar',
          stack: 'Throughput',
          barMaxWidth: 42,
          itemStyle: { color: STATUS.overkill.fill },
          label: {
            show: true,
            position: 'inside',
            formatter: (p: any) => Number(p.value) > 0 ? String(p.value) : '',
            color: '#ffffff',
            fontSize: 11,
            fontWeight: 700,
          },
          data: overkillCounts,
        },
        {
          name: 'Other / Unclassified NG',
          type: 'bar',
          stack: 'Throughput',
          barMaxWidth: 42,
          itemStyle: { color: STATUS.ng.fill },
          label: {
            show: true,
            position: 'inside',
            formatter: (p: any) => Number(p.value) > 0 ? String(p.value) : '',
            color: '#ffffff',
            fontSize: 11,
            fontWeight: 700,
          },
          data: otherNgCounts,
        },
        {
          name: 'NG Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.ng.fill },
          lineStyle: { width: 1.5 },
          label: {
            show: true,
            position: 'top',
            formatter: '{c}%',
            color: STATUS.ng.fill,
            fontSize: 11,
            fontWeight: 700,
            backgroundColor: 'rgba(255, 255, 255, 0.9)',
            padding: [2, 4],
            borderRadius: 3,
            borderWidth: 1,
            borderColor: 'rgba(192, 57, 43, 0.35)',
          },
          data: ngRates,
        },
        {
          name: 'Real Defect Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.real.fill },
          lineStyle: { width: 1.5 },
          label: {
            show: true,
            position: 'bottom',
            formatter: '{c}%',
            color: STATUS.real.fill,
            fontSize: 11,
            fontWeight: 600,
            backgroundColor: 'rgba(255, 255, 255, 0.9)',
            padding: [1, 3],
            borderRadius: 3,
          },
          data: realNgRates,
        },
        {
          name: 'Overkill Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: false,
          itemStyle: { color: STATUS.overkill.fill },
          lineStyle: { width: 1.5, type: 'dashed' },
          label: {
            show: true,
            position: 'top',
            formatter: '{c}%',
            color: STATUS.overkill.text,
            fontSize: 11,
            fontWeight: 700,
            backgroundColor: 'rgba(255, 255, 255, 0.9)',
            padding: [2, 4],
            borderRadius: 3,
            borderWidth: 1,
            borderColor: 'rgba(200, 134, 43, 0.45)',
          },
          data: overkillRates,
        },
      ].filter((series) => series.type === 'bar' ? showLineCounts : showLineRates),
      backgroundColor: 'transparent',
    }
  }, [trendData, periodLabel, showLineCounts, showLineRates])

  const topNgLine = useMemo(() => {
    if (!trendData?.lineComparison || trendData.lineComparison.length === 0) return null
    return [...trendData.lineComparison].sort((a, b) => b.stationNgRate - a.stationNgRate)[0]
  }, [trendData])

  const topOverkillLine = useMemo(() => {
    if (!trendData?.lineComparison || trendData.lineComparison.length === 0) return null
    return [...trendData.lineComparison].sort((a, b) => b.overkillRate - a.overkillRate)[0]
  }, [trendData])

  const totalThroughputAllLines = useMemo(() => {
    if (!trendData?.lineComparison) return 0
    return trendData.lineComparison.reduce((sum, l) => sum + l.totalInspected, 0)
  }, [trendData])

  // Category counts for thumbnail tabs
  const categoryBadgeCounts = useMemo(() => {
    return {
      all: trendData?.summary ? trendData.summary.totalInspected : 0,
      overkill: trendData?.summary ? trendData.summary.overkillCount : 0,
      na: trendData?.summary ? trendData.summary.naCount : 0,
      leakage: trendData?.summary ? trendData.summary.leakageCount : 0,
      realNg: trendData?.summary ? trendData.summary.realNgCount : 0,
      consistentOk: trendData?.summary ? trendData.summary.consistentOkCount : 0,
    }
  }, [trendData])

  const filteredSystems = useMemo(() => {
    const systems = analysis?.systems ?? []
    if (systemFilter === 'All') return systems
    return systems.filter((system) => system.systemName.toLowerCase().includes(systemFilter.toLowerCase()))
  }, [analysis, systemFilter])

  const paged = paginate(filteredSystems, page, 10)
  const pagedCandidates = paginate(candidates, candidatePage)

  return (
    <div className="tab-stack">
      {/* Top Filter Bar */}
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Classifier-Driven Anomaly Verification</p>
            <h2>Anomaly Verification: Real NG vs Overkill & Leakage</h2>
            {detectedMachine && (
              <span className="tag-badge" style={{ display: 'inline-flex', marginTop: 6, background: 'var(--status-info-bg)', color: 'var(--blue-700)' }}>
                <Icon name="operations" className="icon inline-icon" />Auto machine: {detectedMachine}{machineCode === detectedMachine ? ' (Active)' : ''}
              </span>
            )}
          </div>
          <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="secondary"
              onClick={() => setDashboardScopeOpen(true)}
            >
              <Icon name="settings" className="icon inline-icon" />Scope: Model / Line / Defect
            </button>
            {onNavigateTab && (
              <button
                type="button"
                className="secondary text-info font-semibold"
                style={{ background: 'var(--status-info-bg)', borderColor: 'var(--status-info-border)' }}
                onClick={() => onNavigateTab('settings', 'classifier-rules')}
                title="Configure and test classifier rules (Import JSON, test script logic)"
              >
                <Icon name="settings" className="icon inline-icon" />Classifier Rules & Tester
              </button>
            )}
            <Button type="button" onClick={() => void loadTrendData()} loading={loadingTrend}>
              {loadingTrend ? 'Analyzing...' : 'Refresh Analysis'}
            </Button>
          </div>
        </div>

        <form
          className="compact-filter-bar"
          onSubmit={(event) => {
            event.preventDefault()
            void loadTrendData()
          }}
        >
          <Field label="Machine">
            <div className="hstack" style={{ gap: '0.4rem' }}>
              <select
                value={machineCode}
                onChange={(event) => {
                  setMachineCode(event.target.value)
                  setLineCode('All')
                }}
              >
                <option value="All">All machines</option>
                {machines.map((machine) => (
                  <option key={machine} value={machine}>
                    {machine} {machine === detectedMachine ? '(auto-detected)' : ''}
                  </option>
                ))}
              </select>
              {detectedMachine && machineCode !== detectedMachine && (
                <button
                  type="button"
                  className="ghost-button"
                  style={{ whiteSpace: 'nowrap', fontSize: '0.8rem', padding: '0.35rem 0.6rem' }}
                  onClick={() => {
                    setMachineCode(detectedMachine)
                    setLineCode('All')
                  }}
                  title="Auto-select required machine"
                >
                  <Icon name="operations" className="icon inline-icon" />Auto
                </button>
              )}
            </div>

          </Field>

          <Field label="Line">
            <select value={lineCode} onChange={(event) => setLineCode(event.target.value)}>
              <option value="All">All lines</option>
              {lines.map((line) => (
                <option key={line} value={line}>
                  {line}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Model">
            <select value={modelCode} onChange={(event) => setModelCode(event.target.value)}>
              <option value="All">All models</option>
              {models.map((model) => (
                <option key={model || 'UNASSIGNED'} value={model || 'UNASSIGNED'}>{model || 'UNASSIGNED'}</option>
              ))}
            </select>
          </Field>

          <Field label="Defect Family">
            <select value={defectFamily} onChange={(event) => setDefectFamily(event.target.value)}>
              {availableFamilies.map((fam) => (
                <option key={fam} value={fam}>
                  {fam}
                </option>
              ))}
            </select>
          </Field>

          {/* Time Window (Day / Week / Month) & Step Navigator */}
          <Field label="Time Window">
            <div className="hstack gap-sm flex-wrap">
              <div
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  background: 'var(--slate-100)',
                  padding: '2px',
                  borderRadius: '7px',
                  border: '1px solid var(--slate-300)',
                  gap: '2px',
                }}
              >
                {(['all', 'day', 'week', 'month'] as const).map((p) => {
                  const isActive = period === p
                  return (
                    <button
                      className="text-xs"
                      key={p}
                      type="button"
                      style={{
                        padding: '3px 9px',
                        fontWeight: isActive ? 700 : 500,
                        height: '24px',
                        lineHeight: '18px',
                        borderRadius: '5px',
                        textTransform: 'capitalize',
                        background: isActive ? '#ffffff' : 'transparent',
                        color: isActive ? 'var(--blue-700)' : 'var(--slate-600)',
                        boxShadow: isActive ? '0 1px 2px rgba(0,0,0,0.08)' : 'none',
                        border: isActive ? '1px solid var(--status-info-border)' : '1px solid transparent',
                        cursor: 'pointer',
                        transition: 'all 0.15s ease',
                      }}
                      onClick={() => {
                        setPeriod(p)
                        setPeriodOffset(0)
                      }}
                    >
                      {p === 'all' ? 'All' : p === 'day' ? 'Day' : p === 'week' ? 'Week' : 'Month'}
                    </button>
                  )
                })}
              </div>

              {period !== 'all' && (
                <div className="hstack" style={{ gap: '3px' }}>
                  <button
                    type="button"
                    className="ghost-button text-xs"
                    style={{ padding: '2px 8px', height: '26px' }}
                    onClick={() => setPeriodOffset((prev) => prev - 1)}
                    title="Previous Period"
                  >
                    ◀ Prev
                  </button>
                  <button
                    type="button"
                    className="ghost-button text-xs"
                    style={{ padding: '2px 8px', height: '26px' }}
                    onClick={() => setPeriodOffset((prev) => prev + 1)}
                    disabled={periodOffset >= 0}
                    title="Next Period"
                  >
                    Next ▶
                  </button>
                  {periodOffset !== 0 && (
                    <button
                      type="button"
                      className="ghost-button text-xs font-semibold"
                      style={{ padding: '2px 7px', height: '26px', color: 'var(--blue-600)' }}
                      onClick={() => setPeriodOffset(0)}
                      title="Reset to current period"
                    >
                      Current
                    </button>
                  )}
                </div>
              )}
            </div>
          </Field>

          <button type="submit" className="primary" disabled={loadingTrend} style={{ minWidth: 90 }}>
            {loadingTrend ? 'Loading...' : 'Apply'}
          </button>
        </form>
        {period !== 'all' && (
          <div className="hstack text-xs gap-sm" style={{ padding: '4px 8px', color: 'var(--blue-600)' }}>
            <span><Icon name="clock" className="icon inline-icon" /><strong>Time Window:</strong> {periodLabel}</span>
          </div>
        )}
      </section>

      {/* KPI Metrics Grid */}
      <section className="metrics-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(min(150px, 100%), 1fr))', gap: 10 }}>
        <MetricCard
          label="Total Throughput"
          value={trendData?.summary.totalInspected.toLocaleString() ?? 0}
          hint="Product inspection records"
        />
        <MetricCard
          label="Station NG Alarms"
          value={trendData?.summary.stationNgCount ?? 0}
          hint={`Rate: ${formatPercent(trendData?.summary.stationNgRate ?? 0)}`}
          tone="danger"
        />
        <MetricCard
          label="Real NG (Confirmed)"
          value={trendData?.summary.realNgCount ?? 0}
          hint={`Rate: ${formatPercent(trendData?.summary.realNgRate ?? 0)}`}
          tone="info"
        />
        <MetricCard
          label="Unclassified (N/A)"
          value={trendData?.summary.naCount ?? 0}
          hint="Pending overkill algorithm/script"
          tone="warning"
        />
        <MetricCard
          label="Defect Escapes (Leakage)"
          value={trendData?.summary.leakageCount ?? 0}
          hint={`Rate: ${formatPercent(trendData?.summary.leakageRate ?? 0)}`}
          tone={trendData && trendData.summary.leakageCount > 0 ? 'danger' : 'success'}
        />
      </section>

      <section className="panel" style={{ padding: '12px 14px' }}>
        {(() => {
          const stationNg = trendData?.summary.stationNgCount ?? 0
          const opReal = trendData?.summary.opRealDefectCount ?? 0
          const opOverkill = trendData?.summary.opOverkillCount ?? 0
          const reviewed = trendData?.summary.opReviewedCount ?? 0
          const pending = Math.max(0, stationNg - reviewed)
          const denominator = Math.max(stationNg, 1)
          const realPct = (opReal / denominator) * 100
          const overkillPct = (opOverkill / denominator) * 100
          const pendingPct = Math.max(0, 100 - realPct - overkillPct)
          return (
            <>
              <div className="hstack justify-between gap-lg flex-wrap" style={{ marginBottom: 8 }}>
                <div>
                  <strong>OP Review Coverage · Real Defect vs Overkill</strong>
                  <div className="text-xs text-muted">{reviewed.toLocaleString()} / {stationNg.toLocaleString()} Station NG confirmed by OP</div>
                </div>
                <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap' }}>
                  <span className="tag-badge text-real" style={{ background: 'var(--status-real-bg)' }}>Real Defect: {opReal}</span>
                  <span className="tag-badge text-overkill" style={{ background: 'var(--status-overkill-bg)' }}>Overkill: {opOverkill}</span>
                  <span className="tag-badge text-soft" style={{ background: 'var(--slate-200)' }}>Pending: {pending}</span>
                </div>
              </div>
              <div className="w-full" style={{ display: 'flex', height: 18, borderRadius: 999, overflow: 'hidden', background: 'var(--slate-200)' }}>
                {realPct > 0 && <div title={`OP Real Defect ${realPct.toFixed(1)}%`} style={{ width: `${realPct}%`, background: 'var(--status-real)' }} />}
                {overkillPct > 0 && <div title={`OP Overkill ${overkillPct.toFixed(1)}%`} style={{ width: `${overkillPct}%`, background: 'var(--status-overkill)' }} />}
                {pendingPct > 0 && <div title={`Pending ${pendingPct.toFixed(1)}%`} style={{ width: `${pendingPct}%`, background: 'var(--slate-300)' }} />}
              </div>
              <div className="text-xs text-muted" style={{ display: 'flex', justifyContent: 'space-between', marginTop: 5 }}>
                <span>Reviewed coverage: {stationNg > 0 ? ((reviewed / stationNg) * 100).toFixed(1) : '0.0'}%</span>
                <span>Real {realPct.toFixed(1)}% · Overkill {overkillPct.toFixed(1)}% · Pending {pendingPct.toFixed(1)}%</span>
              </div>
            </>
          )
        })()}
      </section>

      {/* Sub-View Navigation Tabs */}
      <div className="module-tabs-bar" style={{ margin: '8px 0 16px 0' }}>
        <div className="module-tabs-list" role="tablist">
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'trend' ? 'active' : ''}`}
            onClick={() => setActiveView('trend')}
          >
            <span><Icon name="operations" className="icon inline-icon" />Trend Analysis: Real NG vs Overkill</span>
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'lines' ? 'active' : ''}`}
            onClick={() => setActiveView('lines')}
          >
            <span><Icon name="analysis" className="icon inline-icon" />Line Comparison</span>
            {trendData?.lineComparison && trendData.lineComparison.length > 0 ? (
              <span className="tag-badge text-xs" style={{ background: 'var(--status-info)', color: '#fff', marginLeft: '6px' }}>
                {trendData.lineComparison.length} lines
              </span>
            ) : null}
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'reconciliation' ? 'active' : ''}`}
            onClick={() => setActiveView('reconciliation')}
          >
            <span><Icon name="clipboard" className="icon inline-icon" />OP Reconciliation Dashboard & Deep-Dive</span>
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'thumbnails' ? 'active' : ''}`}
            onClick={() => setActiveView('thumbnails')}
          >
            <span><Icon name="image" className="icon inline-icon" />Image View</span>
            {trendData?.summary.stationNgCount ? (
              <span className="tag-badge text-xs" style={{ background: 'var(--status-ng)', color: '#fff', marginLeft: '6px' }}>
                {trendData.summary.stationNgCount.toLocaleString()} NG
              </span>
            ) : null}
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'leakage' ? 'active' : ''}`}
            onClick={() => setActiveView('leakage')}
          >
            <span><Icon name="overkill" className="icon inline-icon" />Defect Escape Investigation (Leakage)</span>
            {trendData && trendData.summary.leakageCount > 0 ? (
              <span className="tag-badge text-xs" style={{ background: 'var(--status-ng)', color: '#fff', marginLeft: '6px' }}>
                {trendData.summary.leakageCount}
              </span>
            ) : null}
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'systems' ? 'active' : ''}`}
            onClick={() => setActiveView('systems')}
          >
            <span><Icon name="clipboard" className="icon inline-icon" />Defect Systems & Suspects</span>
          </button>
        </div>
      </div>

      {/* VIEW 1: TREND & CLASSIFIER BREAKDOWN */}
      {activeView === 'trend' && (
        <div className="tab-stack">
          {/* Dual-Axis Trend Chart */}
          <article className="panel">
            {trendChartOption ? (
              <ReactECharts
                option={trendChartOption}
                style={{ height: 380, width: '100%' }}
                opts={{ renderer: 'canvas' }}
              />
            ) : trendError ? (
              <LoadFailure title="The trend analysis could not be loaded" error={trendError} onRetry={() => void loadTrendData()} />
            ) : loadingTrend && !trendData ? (
              <ChartSkeleton height={380} />
            ) : (
              <div className="dashboard-report-empty">
                <strong>No trend data available</strong>
                <span>Import or sync defect records from database to render trend analysis.</span>
              </div>
            )}
          </article>

          {/* Guidance & Recommendations */}
          <section className="dashboard-grid">
            <article className="panel">
              <div className="section-heading">
                <div>
                  <p className="eyebrow">Recipe / Rule Tuning Recommendations</p>
                  <h3>Analytical Insights & Guidance</h3>
                </div>
              </div>
              <ul className="guidance-list">
                {(trendData?.recommendations ?? ['No recommendations yet. Run synchronization to evaluate.']).map((item, idx) => (
                  <li key={idx}>
                    <strong><Icon name="info" className="icon inline-icon" /></strong> {item}
                  </li>
                ))}
              </ul>
            </article>

            <article className="panel risk-panel">
              <p className="eyebrow">Overall Separation Breakdown</p>
              <h3>Machine Efficiency vs Classifier Rules</h3>
              <div className="vstack mt-3 gap-md">
                <div className="risk-row">
                  <span>Station NG Alarms</span>
                  <strong>{trendData?.summary.stationNgCount ?? 0} units</strong>
                </div>
                <div className="risk-row">
                  <span className="text-real font-semibold">Confirmed Real NG</span>
                  <strong className="text-real">{trendData?.summary.realNgCount ?? 0} units</strong>
                </div>
                <div className="risk-row">
                  <span className="text-overkill font-semibold">Overkill (False Alarm)</span>
                  <strong className="text-overkill">
                    {trendData?.summary.overkillCount ?? 0} ({formatPercent(trendData?.summary.overkillShare ?? 0)})
                  </strong>
                </div>
                <div className="risk-row">
                  <span className="text-leakage font-semibold">Defect Escapes (Leakage)</span>
                  <strong className="text-leakage">{trendData?.summary.leakageCount ?? 0} units</strong>
                </div>
              </div>
            </article>
          </section>

          {/* Breakdown by Defect Family */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Breakdown by Defect Family</p>
                <h3>Real NG vs Overkill Ratios across Defect Families</h3>
              </div>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Defect Family</th>
                    <th>Total Defects</th>
                    <th>Real NG</th>
                    <th>Overkill</th>
                    <th>Overkill Rate (%)</th>
                    <th>Active Classifier Rule</th>
                    <th>Dominant Defects</th>
                    <th>Mean Confidence</th>
                  </tr>
                </thead>
                <tbody>
                  {(trendData?.familyBreakdown ?? []).map((fam) => (
                    <tr key={fam.defectFamily}>
                      <td className="font-bold text-info">{fam.defectFamily}</td>
                      <td>{fam.totalDefects.toLocaleString()}</td>
                      <td className="text-real font-semibold">{fam.realNgCount.toLocaleString()}</td>
                      <td className="text-overkill font-semibold">{fam.overkillCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge font-semibold"
                          style={{
                            background: fam.overkillShare >= 0.5 ? 'var(--status-overkill-bg)' : 'var(--status-ok-bg)',
                            color: fam.overkillShare >= 0.5 ? 'var(--status-overkill-text)' : 'var(--status-ok-text)',
                          }}
                        >
                          {formatPercent(fam.overkillShare)}
                        </span>
                      </td>
                      <td className="font-mono text-xs">{fam.activeRuleCode || 'DEFAULT_HEURISTIC'}</td>
                      <td className="text-xs">{fam.topDefectNames.join(', ') || '—'}</td>
                      <td className="font-mono text-xs">{fam.meanConfidence ? fam.meanConfidence.toFixed(2) : '—'}</td>
                    </tr>
                  ))}
                  {(!trendData?.familyBreakdown || trendData.familyBreakdown.length === 0) && (
                    <tr>
                      <td colSpan={8} className="text-center text-muted" style={{ padding: '24px' }}>
                        No defect family breakdown data available.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

          {/* Breakdown by Defect Class / Name */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Breakdown by Defect Class / Name</p>
                <h3>Defect Details & Threshold Guidance by Class</h3>
              </div>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Defect Name</th>
                    <th>Family</th>
                    <th>Total Defects</th>
                    <th>Real NG</th>
                    <th>Overkill</th>
                    <th>Overkill Rate</th>
                    <th>Rule Code</th>
                    <th>Evaluation Guidance</th>
                  </tr>
                </thead>
                <tbody>
                  {(trendData?.classBreakdown ?? []).map((cls) => (
                    <tr key={cls.defectName}>
                      <td className="font-bold">{cls.defectName}</td>
                      <td><StatusPill value={cls.defectFamily} /></td>
                      <td>{cls.totalDefects.toLocaleString()}</td>
                      <td className="text-real">{cls.realNgCount}</td>
                      <td className="text-overkill font-semibold">{cls.overkillCount}</td>
                      <td>
                        <span
                          className="tag-badge font-semibold"
                          style={{
                            background: cls.overkillShare >= 0.5 ? 'var(--status-ng-bg)' : 'var(--status-ok-bg)',
                            color: cls.overkillShare >= 0.5 ? 'var(--status-ng-text)' : 'var(--status-ok-text)',
                          }}
                        >
                          {formatPercent(cls.overkillShare)}
                        </span>
                      </td>
                      <td className="font-mono text-xs">{cls.ruleCode || 'DEFAULT'}</td>
                      <td className="text-xs" style={{ maxWidth: 360 }}>{cls.evaluationGuidance}</td>
                    </tr>
                  ))}
                  {(!trendData?.classBreakdown || trendData.classBreakdown.length === 0) && (
                    <tr>
                      <td colSpan={8} className="text-center text-muted" style={{ padding: '24px' }}>
                        No defect class breakdown data available.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

          {/* Section: Cross-Line Throughput, NG Rate & Overkill Comparison */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Cross-Line Comparison</p>
                <h3>Throughput, NG Rate & Overkill Benchmarks across Production Lines</h3>
              </div>
              <button
                type="button"
                className="secondary compact text-xs"
                onClick={() => setActiveView('lines')}
              >
                <Icon name="analysis" className="icon inline-icon" />View Full Line Comparison & Chart →
              </button>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Line Code</th>
                    <th>Machine</th>
                    <th>Throughput (OK + NG)</th>
                    <th>OK Units</th>
                    <th>Station NG</th>
                    <th>Station NG Rate</th>
                    <th>Real Defect Detect</th>
                    <th>Real Defect Rate</th>
                    <th>Overkill Detect</th>
                    <th>Overkill Rate</th>
                    <th>Risk Level</th>
                  </tr>
                </thead>
                <tbody>
                  {(trendData?.lineComparison ?? []).map((lineItem) => (
                    <tr key={lineItem.lineCode}>
                      <td className="font-bold text-info">{lineItem.lineCode}</td>
                      <td className="font-mono text-xs">{lineItem.machineCode}</td>
                      <td><strong>{lineItem.totalInspected.toLocaleString()}</strong></td>
                      <td className="text-ok">{lineItem.okCount.toLocaleString()}</td>
                      <td className="text-ng font-semibold">{lineItem.stationNgCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge font-bold"
                          style={{
                            background: lineItem.stationNgRate >= 0.1 ? 'var(--status-ng-bg)' : 'var(--status-ok-bg)',
                            color: lineItem.stationNgRate >= 0.1 ? 'var(--status-ng-text)' : 'var(--status-ok-text)',
                          }}
                        >
                          {formatPercent(lineItem.stationNgRate)}
                        </span>
                      </td>
                      <td className="text-real font-bold">{lineItem.realNgCount.toLocaleString()}</td>
                      <td>
                        <span className="tag-badge text-real font-bold" style={{ background: 'var(--status-real-bg)' }}>
                          {formatPercent(lineItem.realNgRate)}
                        </span>
                      </td>
                      <td className="text-overkill font-bold">{lineItem.overkillCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge font-semibold"
                          style={{
                            background: lineItem.overkillRate >= 0.05 ? 'var(--status-overkill-bg)' : 'var(--status-na-bg)',
                            color: lineItem.overkillRate >= 0.05 ? 'var(--status-overkill-text)' : 'var(--slate-600)',
                          }}
                        >
                          {formatPercent(lineItem.overkillRate)}
                        </span>
                      </td>
                      <td>
                        <span
                          className="tag-badge font-bold"
                          style={{ ...riskBadgeStyle(lineItem.riskLevel) }}
                        >
                          {lineItem.riskLevel}
                        </span>
                      </td>
                    </tr>
                  ))}
                  {(!trendData?.lineComparison || trendData.lineComparison.length === 0) && (
                    <tr>
                      <td colSpan={11} className="text-center text-muted" style={{ padding: '24px' }}>
                        No line comparison data available. Check filter criteria or run scan pipeline with OK product collection enabled.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}

      {/* VIEW: LINE COMPARISON DEEP DIVE */}
      {activeView === 'lines' && (
        <div className="tab-stack">
          {/* Header KPIs for Lines */}
          <section className="metrics-grid">
            <MetricCard
              label="Lines Monitored"
              value={trendData?.lineComparison?.length ?? 0}
              hint="Active production lines"
            />
            <MetricCard
              label="Total Throughput"
              value={totalThroughputAllLines.toLocaleString()}
              hint="Combined OK & NG products"
              tone="info"
            />
            <MetricCard
              label="Highest NG Rate Line"
              value={topNgLine ? `${topNgLine.lineCode}` : '—'}
              hint={topNgLine ? `NG Rate: ${formatPercent(topNgLine.stationNgRate)} (${topNgLine.stationNgCount}/${topNgLine.totalInspected})` : '—'}
              tone="danger"
            />
            <MetricCard
              label="Highest Overkill Line"
              value={topOverkillLine ? `${topOverkillLine.lineCode}` : '—'}
              hint={topOverkillLine ? `Overkill/Throughput: ${formatPercent(topOverkillLine.overkillRate)}` : '—'}
              tone="warning"
            />
          </section>

          {/* Cross-Line Chart */}
          <article className="panel">
            <div
              className="hstack justify-between gap-lg flex-wrap"
              style={{
                marginBottom: 8,
                paddingBottom: 8,
                borderBottom: '1px solid var(--border)',
              }}
            >
              <div>
                <strong className="text-sm">Chart Display Controls</strong>
                <div className="text-xs text-muted">Adjust chart size and show or hide count/rate series.</div>
              </div>
              <div className="hstack gap-lg flex-wrap">
                <label className="gap-sm text-xs font-semibold" style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer' }}>
                  <input
                    type="checkbox"
                    checked={showLineCounts}
                    onChange={(event) => setShowLineCounts(event.target.checked)}
                  />
                  Count / Detect
                </label>
                <label className="gap-sm text-xs font-semibold" style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer' }}>
                  <input
                    type="checkbox"
                    checked={showLineRates}
                    onChange={(event) => setShowLineRates(event.target.checked)}
                  />
                  Rate (%)
                </label>
                <label className="gap-md text-xs font-semibold" style={{ display: 'inline-flex', alignItems: 'center' }}>
                  Chart Size
                  <input
                    type="range"
                    min={300}
                    max={720}
                    step={20}
                    value={lineChartHeight}
                    onChange={(event) => setLineChartHeight(Number(event.target.value))}
                    style={{ width: 130 }}
                  />
                  <span className="tag-badge" style={{ minWidth: 58, textAlign: 'center' }}>{lineChartHeight}px</span>
                </label>
                <button
                  type="button"
                  className="ghost-button compact"
                  onClick={() => {
                    setLineChartHeight(420)
                    setShowLineCounts(true)
                    setShowLineRates(true)
                  }}
                >
                  Reset
                </button>
              </div>
            </div>
            {lineComparisonChartOption ? (
              <ReactECharts
                option={lineComparisonChartOption}
                style={{ height: chartHeight, width: '100%' }}
                opts={{ renderer: 'canvas' }}
              />
            ) : trendError ? (
              <LoadFailure title="The line comparison could not be loaded" error={trendError} onRetry={() => void loadTrendData()} />
            ) : loadingTrend && !trendData ? (
              <ChartSkeleton height={380} />
            ) : (
              <div className="dashboard-report-empty">
                <strong>No line comparison data available</strong>
                <span>Inspection records including OK products are required to benchmark lines.</span>
              </div>
            )}
          </article>

          {/* Full Line Comparison Table */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Production Line Detailed Comparison</p>
                <h3>Throughput, Station NG Alarms, Real NG vs Overkill Benchmark ({periodLabel})</h3>
                <p className="text-xs text-muted mt-1">
                  Metrics aggregated from all units running through (OK + NG IDs) during inspection window: <strong>{periodLabel}</strong>.
                </p>
              </div>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Line Code</th>
                    <th>Machine</th>
                    <th>Throughput (OK + NG)</th>
                    <th>OK Units</th>
                    <th>Station NG</th>
                    <th>Station NG Rate (%)</th>
                    <th>Real Defect Detect</th>
                    <th>Real Defect Rate (%)</th>
                    <th>Overkill Detect</th>
                    <th>Overkill Rate (%)</th>
                    <th>Unclassified (N/A)</th>
                    <th>Overkill / NG (%)</th>
                    <th>Defect Escapes</th>
                    <th>Risk Level</th>
                    <th>Assessment & Guidance</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {(trendData?.lineComparison ?? []).map((l) => (
                    <tr key={l.lineCode}>
                      <td className="font-bold text-info">{l.lineCode}</td>
                      <td className="font-mono text-xs">{l.machineCode}</td>
                      <td><strong>{l.totalInspected.toLocaleString()}</strong></td>
                      <td className="text-ok">{l.okCount.toLocaleString()}</td>
                      <td className="text-ng font-semibold">{l.stationNgCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge font-bold"
                          style={{
                            background: l.stationNgRate >= 0.1 ? 'var(--status-ng-bg)' : 'var(--status-ok-bg)',
                            color: l.stationNgRate >= 0.1 ? 'var(--status-ng-text)' : 'var(--status-ok-text)',
                          }}
                        >
                          {formatPercent(l.stationNgRate)}
                        </span>
                      </td>
                      <td className="text-real font-bold">{l.realNgCount.toLocaleString()}</td>
                      <td>
                        <span className="tag-badge text-real font-bold" style={{ background: 'var(--status-real-bg)' }}>
                          {formatPercent(l.realNgRate)}
                        </span>
                      </td>
                      <td className="text-overkill font-bold">{l.overkillCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge font-bold"
                          style={{
                            background: l.overkillRate >= 0.05 ? 'var(--status-overkill-bg)' : 'var(--status-na-bg)',
                            color: l.overkillRate >= 0.05 ? 'var(--status-overkill-text)' : 'var(--slate-500)',
                          }}
                        >
                          {formatPercent(l.overkillRate)}
                        </span>
                      </td>
                      <td className="text-muted font-mono text-xs">{l.naCount}</td>
                      <td className="font-mono text-xs">{formatPercent(l.overkillShare)}</td>
                      <td>
                        {l.leakageCount > 0 ? (
                          <span className="tag-badge font-bold" style={{ background: 'var(--status-ng)', color: '#fff' }}>
                            {l.leakageCount}
                          </span>
                        ) : (
                          <span className="text-muted">0</span>
                        )}
                      </td>
                      <td>
                        <span
                          className="tag-badge font-bold"
                          style={{ ...riskBadgeStyle(l.riskLevel) }}
                        >
                          {l.riskLevel}
                        </span>
                      </td>
                      <td className="text-xs" style={{ maxWidth: 300 }}>{l.statusAssessment}</td>
                      <td>
                        <button
                          type="button"
                          className="ghost-button compact text-xs"
                          style={{ whiteSpace: 'nowrap' }}
                          onClick={() => {
                            setLineCode(l.lineCode)
                            setActiveView('trend')
                          }}
                          title={`Filter trend analysis for line ${l.lineCode}`}
                        >
                          <Icon name="search" className="icon inline-icon" />Inspect Line
                        </button>
                      </td>
                    </tr>
                  ))}
                  {(!trendData?.lineComparison || trendData.lineComparison.length === 0) && (
                    <tr>
                      <td colSpan={16} className="text-center text-muted" style={{ padding: '36px' }}>
                        No line comparison data available. Run pipeline scan or import data with OK product tracking enabled.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}

      {/* VIEW 2: VISUAL ANOMALY THUMBNAIL GALLERY */}
      {activeView === 'thumbnails' && (
        <div className="tab-stack">
          {/* Gallery Header & Controls */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Visual Evidence Verification</p>
                <h2>Image View</h2>
                <p className="text-xs text-muted mt-1">
                  Gallery is paginated. The number on this page is not the full Station NG total.
                  {thumbnailTotalCount > 0
                    ? ` Showing ${thumbnailItems.length.toLocaleString()} of ${thumbnailTotalCount.toLocaleString()} matching records (page ${thumbnailPage}/${thumbnailTotalPages}).`
                    : ' Open a category to load matching records.'}
                </p>
              </div>
              <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap' }}>
                <button type="button" className="secondary" onClick={() => setGalleryScopeOpen(true)}>
                  <Icon name="target" className="icon inline-icon" />Scope: Model / Line / Defect
                </button>
                <Button type="button" onClick={() => {
                    setThumbnailPage(1)
                    void loadThumbnails()
                  }} variant="secondary" loading={loadingThumbnails}>
                  {loadingThumbnails ? 'Loading thumbnails...' : 'Refresh Gallery (Latest)'}
                </Button>
              </div>
            </div>

            {/* Filter Pills */}
            <div className="hstack flex-wrap gap-md mt-3">
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'All' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'All' ? 'var(--slate-100)' : undefined,
                  fontWeight: thumbnailCategory === 'All' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('All')
                  setThumbnailPage(1)
                }}
              >
                All products ({categoryBadgeCounts.all.toLocaleString()})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'StationNG' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'StationNG' ? 'var(--status-ng-bg)' : undefined,
                  color: 'var(--status-ng-text)',
                  fontWeight: thumbnailCategory === 'StationNG' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('StationNG')
                  setThumbnailPage(1)
                }}
              >
                Station NG Alarms ({(trendData?.summary.stationNgCount ?? 0).toLocaleString()})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'RealNG' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'RealNG' ? 'var(--status-real-bg)' : undefined,
                  color: 'var(--status-real-text)',
                  fontWeight: thumbnailCategory === 'RealNG' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('RealNG')
                  setThumbnailPage(1)
                }}
              >
                Real NG - Confirmed by Script ({categoryBadgeCounts.realNg})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'NA' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'NA' ? 'var(--status-overkill-bg)' : undefined,
                  color: 'var(--status-overkill-text)',
                  fontWeight: thumbnailCategory === 'NA' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('NA')
                  setThumbnailPage(1)
                }}
              >
                <Icon name="help" className="icon inline-icon" />Unclassified / N/A ({categoryBadgeCounts.na})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'Leakage' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'Leakage' ? 'var(--status-leakage-bg)' : undefined,
                  color: 'var(--status-ng-text)',
                  fontWeight: thumbnailCategory === 'Leakage' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('Leakage')
                  setThumbnailPage(1)
                }}
              >
                <Icon name="warning" className="icon inline-icon" />Defect Escapes / Leakage ({categoryBadgeCounts.leakage})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'ConsistentOK' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'ConsistentOK' ? 'var(--status-ok-bg)' : undefined,
                  color: 'var(--status-ok-text)',
                  fontWeight: thumbnailCategory === 'ConsistentOK' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('ConsistentOK')
                  setThumbnailPage(1)
                }}
              >
                Clean OK Units
              </button>

              <div className="gap-md" style={{ marginLeft: 'auto', display: 'flex' }}>
                <input
                  className="text-xs"
                  type="text"
                  placeholder="Search KeyId, defect, component..."
                  value={thumbnailSearch}
                  onChange={(e) => setThumbnailSearch(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      setAppliedThumbnailSearch(thumbnailSearch.trim())
                      setThumbnailPage(1)
                    }
                  }}
                  style={{ width: 220, padding: '4px 10px' }}
                />
                <button
                  type="button"
                  className="ghost-button compact"
                  onClick={() => {
                    setAppliedThumbnailSearch(thumbnailSearch.trim())
                    setThumbnailPage(1)
                  }}
                >
                  Search
                </button>
              </div>
            </div>
          </section>

          {/* Thumbnail Cards Grid */}
          {loadingThumbnails ? (
            <div className="dashboard-report-empty">
              <div className="spinner" />
              <span>Fetching images and evaluating classifier rules...</span>
            </div>
          ) : thumbnailError && thumbnailItems.length === 0 ? (
            <LoadFailure title="The thumbnails could not be loaded" error={thumbnailError} onRetry={() => void loadThumbnails()} />
          ) : thumbnailItems.length === 0 ? (
            <div className="dashboard-report-empty">
              <strong>No anomaly thumbnail records found</strong>
              <span>Adjust classification filters or synchronize inspection records from the database.</span>
            </div>
          ) : (
            <div className="vstack" style={{ gap: '18px' }}>
              {[...new Set(thumbnailItems.map((item) => `${item.modelCode || 'UNASSIGNED'}|${item.lineCode}|${item.defectFamily || item.defectType || 'Unclassified'}`))].map((groupKey) => {
                const [groupModel, groupLine, groupCluster] = groupKey.split('|')
                const groupItems = thumbnailItems.filter((item) => `${item.modelCode || 'UNASSIGNED'}|${item.lineCode}|${item.defectFamily || item.defectType || 'Unclassified'}` === groupKey)
                return (
                  <section key={groupKey}>
                    <div className="hstack gap-md" style={{ marginBottom: '8px' }}>
                      <strong className="text-info">{groupModel}</strong>
                      <span className="line-pill">{groupLine}</span>
                      <StatusPill value={groupCluster} />
                      <span className="text-xs text-muted">{groupItems.length} images</span>
                    </div>
                    <div className="gap-xl" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(280px, 100%), 1fr))' }}>
              {groupItems.map((item, idx) => (
                <ThumbnailCard
                  key={item.defectId ?? `${item.keyId}-${idx}`}
                  item={item}
                  inspectBusy={loadingDefectInspect}
                  onZoom={openZoom}
                  onInspect={inspectThumbnail}
                  onNotice={onNotice}
                />
              ))}
                    </div>
                  </section>
                )
              })}
            </div>
          )}

          <div className="hstack flex-wrap gap-lg justify-between">
            <span className="text-xs text-muted">
              Image View page size is only the current grid. Station NG total is { (trendData?.summary.stationNgCount ?? 0).toLocaleString() } products.
              Matching gallery records: <strong>{thumbnailTotalCount.toLocaleString()}</strong>
              {thumbnailTotalCount > 0 ? ` · this page ${thumbnailItems.length}` : ''}.
            </span>
            <label className="text-xs text-muted hstack gap-sm">
              Per page
              <select
                value={thumbnailPageSize}
                onChange={(e) => {
                  setThumbnailPageSize(Number(e.target.value))
                  setThumbnailPage(1)
                }}
              >
                <option value={24}>24</option>
                <option value={48}>48</option>
                <option value={96}>96</option>
              </select>
            </label>
          </div>
          <Pagination
            page={thumbnailPage}
            totalPages={thumbnailTotalPages}
            totalCount={thumbnailTotalCount}
            onPage={setThumbnailPage}
          />
        </div>
      )}

      {/* VIEW 3: LEAKAGE ESCAPE ANALYSIS */}
      {activeView === 'leakage' && (
        <div className="tab-stack">
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Defect Escape Investigation</p>
                <h2>Defect Escape (Leakage) Investigation</h2>
                <p className="text-xs text-muted mt-1">
                  Products judged Pass (OK/G) by inspection stations that actually contain severe defects or violate classifier rule scripts.
                </p>
              </div>
            </div>

            <div className="metrics-grid mt-3">
              <MetricCard
                label="Total Defect Escapes"
                value={trendData?.summary.leakageCount ?? 0}
                tone={trendData && trendData.summary.leakageCount > 0 ? 'danger' : 'success'}
              />
              <MetricCard
                label="Defect Escape Rate"
                value={formatPercent(trendData?.summary.leakageRate ?? 0)}
                tone={trendData && trendData.summary.leakageCount > 0 ? 'danger' : 'success'}
              />
              <MetricCard
                label="Escape Risk Level"
                value={trendData && trendData.summary.leakageCount > 0 ? 'HIGH RISK' : 'NORMAL'}
                tone={trendData && trendData.summary.leakageCount > 0 ? 'danger' : 'success'}
              />
            </div>
          </section>

          {/* Table of Leakage items */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Defect Escape Audit List</p>
                <h3>Escaped Products Requiring Immediate Investigation</h3>
              </div>
            </div>

            <DataTable
              eyebrow="GET /verification/anomaly-thumbnails?category=Leakage"
              title="Leakage Records"
              columns={['Key ID', 'Station Judgment', 'Rule Verdict', 'Product Defect', 'Defect Record', 'Family', 'Severity', 'Inspected Time', 'Reason']}
              rows={thumbnailItems
                .filter((item) => item.anomalyCategory === 'Leakage')
                .map((item) => ({
                  id: item.keyId,
                  cells: [
                    <strong key="k" className="mono-code">{item.keyId}</strong>,
                    <JudgeBadge key="j" value={item.stationJudge} />,
                    <span key="r" className="tag-badge text-ng font-bold" style={{ background: 'var(--status-ng-bg)' }}>
                      TRUE DEFECT ({item.ruleCode || 'DEFAULT'})
                    </span>,
                    item.productNameDefect || item.productDefectName || '',
                    item.defectName || '—',
                    <StatusPill key="f" value={item.defectFamily ?? 'General'} />,
                    <StatusPill key="s" value={item.severity ?? 'Major'} />,
                    formatDateTime(item.inspectedAtUtc),
                    item.decisionReason,
                  ],
                }))}
            />
          </section>
        </div>
      )}

      {/* VIEW: OP EXCEL RECONCILIATION & OVERKILL DEEP-DIVE */}
      {activeView === 'reconciliation' && (
        <Suspense fallback={<TabSkeleton />}>
          <OpReconciliationTab
            onNotice={onNotice}
            initialMachine={machineCode}
            initialLine={lineCode}
            mode="records"
          />
        </Suspense>
      )}

      {/* VIEW 4: LEGACY SYSTEMS & SUSPECT CANDIDATES */}
      {activeView === 'systems' && (
        <div className="tab-stack">
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Defect Systems</p>
                <h2>NG Share by Defect Table & Tuning Guidance</h2>
              </div>
            </div>

            <form
              className="compact-filter-bar"
              onSubmit={(event) => {
                event.preventDefault()
                void loadAnalysis()
              }}
            >
              <Field label="Min NG share" hint="Filter tables with high NG ratio">
                <select value={minNgShare} onChange={(event) => setMinNgShare(event.target.value)}>
                  <option value="0">0%</option>
                  <option value="0.3">30%</option>
                  <option value="0.5">50%</option>
                  <option value="0.7">70%</option>
                </select>
              </Field>
              <Field label="Defect system">
                <select value={systemFilter} onChange={(event) => setSystemFilter(event.target.value)}>
                  <option>All</option>
                  <option>AOI</option>
                  <option>AI</option>
                  <option>SPI</option>
                </select>
              </Field>
              <button type="submit" className="ghost-button" disabled={busy}>
                Apply
              </button>
            </form>
          </section>

          <DataTable
            eyebrow="Defect systems"
            title="NG share by defect system"
            columns={['System', 'Type', 'Defects', 'NG defects', 'NG share', 'Suspects', 'Guidance']}
            status={analysisError ? 'error' : busy && !analysis ? 'loading' : 'ready'}
            error={analysisError ?? undefined}
            onRetry={() => void loadAnalysis()}
            selectedRow={selected?.systemName}
            onRowClick={(id) => setSelected(filteredSystems.find((item) => item.systemName === id) ?? null)}
            rows={paged.items.map((system) => ({
              id: system.systemName,
              selected: selected?.systemName === system.systemName,
              cells: [
                system.systemName,
                system.defectType,
                system.totalDefects,
                system.ngDefects,
                formatPercent(system.ngShare),
                system.overkillSuspects,
                system.guidance,
              ],
            }))}
          />
          <Pagination page={paged.page} totalPages={paged.totalPages} totalCount={paged.totalCount} onPage={setPage} />

          <DataTable
            eyebrow="GET /products/overkill-candidates"
            title="Overkill Candidate List (Station NG with clean defect tables)"
            columns={['Key ID', 'Judge', 'Zone', 'Defect Name', 'Panel judge', 'Machine', 'Line', 'Inspection Time']}
            status={analysisError ? 'error' : busy && !analysis ? 'loading' : 'ready'}
            error={analysisError ?? undefined}
            onRetry={() => void loadAnalysis()}
            rows={pagedCandidates.items.map((item) => ({
              id: item.keyId,
              cells: [
                item.keyId,
                <JudgeBadge key="j" value={item.judge} />,
                item.zone ? (
                  <span key="z" className="tag-badge text-ok font-semibold" style={{ background: 'var(--status-ok-bg)' }}>
                    {item.zone}
                  </span>
                ) : '—',
                item.defectName ? (
                  <span key="dn" className="tag-badge text-ng font-semibold" style={{ background: 'var(--status-ng-bg)' }}>
                    {item.defectName}
                  </span>
                ) : item.nameDefect && item.nameDefect !== 'None' ? (
                  <span key="dn-legacy" className="tag-badge text-ng font-semibold" style={{ background: 'var(--status-ng-bg)' }}>
                    {item.nameDefect}
                  </span>
                ) : 'None',
                item.panelJudge,
                item.machineCode,
                item.lineCode,
                formatDateTime(item.inspectedAtUtc),
              ],
            }))}
          />
          <Pagination
            page={pagedCandidates.page}
            totalPages={pagedCandidates.totalPages}
            totalCount={pagedCandidates.totalCount}
            onPage={setCandidatePage}
          />
        </div>
      )}

      <Modal
        title="Dashboard Scope Filter"
        description="Update KPIs, trend charts, and Line Comparison by Model, Line, and Defect Type."
        open={dashboardScopeOpen}
        onClose={() => setDashboardScopeOpen(false)}
      >
        <div className="modal-form">
          <Field label="Model">
            <select value={modelCode} onChange={(e) => setModelCode(e.target.value)}>
              <option value="All">All models</option>
              {models.map((model) => <option key={model || 'UNASSIGNED'} value={model || 'UNASSIGNED'}>{model || 'UNASSIGNED'}</option>)}
            </select>
          </Field>
          <Field label="Line">
            <select value={lineCode} onChange={(e) => setLineCode(e.target.value)}>
              <option value="All">All lines</option>
              {lines.map((line) => <option key={line} value={line}>{line}</option>)}
            </select>
          </Field>
          <Field label="Defect Type">
            <select value={defectFamily} onChange={(e) => setDefectFamily(e.target.value)}>
              {availableFamilies.map((family) => <option key={family} value={family}>{family}</option>)}
            </select>
          </Field>
          <div className="actions">
            <button type="button" className="ghost-button" onClick={() => { setModelCode('All'); setLineCode('All'); setDefectFamily('All') }}>Reset</button>
            <button type="button" className="primary" onClick={() => { setDashboardScopeOpen(false); setTimeout(() => void loadTrendData(), 0) }}>Apply Scope</button>
          </div>
        </div>
      </Modal>

      <Modal
        title="Gallery Scope Filter"
        description="Group images by Model, Line, and Defect Cluster before display."
        open={galleryScopeOpen}
        onClose={() => setGalleryScopeOpen(false)}
      >
        <div className="modal-form">
          <Field label="Model">
            <select value={galleryModel} onChange={(e) => { setGalleryModel(e.target.value); setGalleryLine('All') }}>
              <option value="All">All models</option>
              {models.map((model) => <option key={model || 'UNASSIGNED'} value={model || 'UNASSIGNED'}>{model || 'UNASSIGNED'}</option>)}
            </select>
          </Field>
          <Field label="Line">
            <select value={galleryLine} onChange={(e) => setGalleryLine(e.target.value)}>
              <option value="All">All lines</option>
              {galleryLines.map((line) => <option key={line} value={line}>{line}</option>)}
            </select>
          </Field>
          <Field label="Defect Cluster / Type">
            <select value={galleryCluster} onChange={(e) => setGalleryCluster(e.target.value)}>
              {galleryClusters.map((cluster) => <option key={cluster} value={cluster}>{cluster}</option>)}
            </select>
          </Field>
          <div className="actions">
            <button type="button" className="ghost-button" onClick={() => { setGalleryModel('All'); setGalleryLine('All'); setGalleryCluster('All') }}>Reset</button>
            <button type="button" className="primary" onClick={() => { setThumbnailPage(1); setGalleryScopeOpen(false); setTimeout(() => void loadThumbnails(), 0) }}>Apply Scope</button>
          </div>
        </div>
      </Modal>

      {zoomRequest && (
        <ThumbnailZoomModal
          item={zoomRequest.item}
          initialVp={zoomRequest.vp}
          onClose={() => setZoomRequest(null)}
          onInspect={(item, vp) => {
            setZoomRequest(null)
            inspectThumbnail(item, vp)
          }}
          onNotice={onNotice}
        />
      )}

      {/* MODAL: Defect Feature Inspector */}
      {inspectedDefect && (
        <Modal
          title={`Defect Feature Analysis · ${inspectedDefect.defectName || 'Defect'}`}
          description={`Key ID: ${inspectedDefect.keyId} · Judgment: ${inspectedDefect.judge} · Severity: ${inspectedDefect.severity}`}
          open={Boolean(inspectedDefect)}
          onClose={() => setInspectedDefect(null)}
          wide
        >
          <DefectFeatureInspector defect={inspectedDefect} initialVp={inspectedVp} />
        </Modal>
      )}
    </div>
  )
}
