import { useCallback, useEffect, useMemo, useState } from 'react'
import ReactECharts from 'echarts-for-react'
import { connectionsApi, productsApi, verificationApi, imageCrawlerApi } from '../api'
import { formatDateTime, formatPercent, paginate } from '../format'
import { OpReconciliationTab } from './OpReconciliationTab'
import { DataTable, DefectFeatureInspector, Field, ImageViewer, JudgeBadge, MetricCard, Modal, Pagination, StatusPill } from '../ui'
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
  const [showLineCounts, setShowLineCounts] = useState(true)
  const [showLineRates, setShowLineRates] = useState(true)

  // Trend & Classifier Breakdown state
  const [trendData, setTrendData] = useState<OverkillTrendResponse | null>(null)
  const [loadingTrend, setLoadingTrend] = useState(false)

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
  const [zoomedItem, setZoomedItem] = useState<AnomalyThumbnailItem | null>(null)
  const [inspectedDefect, setInspectedDefect] = useState<DefectRecord | null>(null)
  const [inspectedVp, setInspectedVp] = useState<string | undefined>(undefined)
  const [loadingDefectInspect, setLoadingDefectInspect] = useState<boolean>(false)

  // Multi-VP Image State for Anomaly Cards & Zoom Modal
  const [cardVpMap, setCardVpMap] = useState<Record<string, string>>({})
  const [cardVpImages, setCardVpImages] = useState<Record<string, Record<string, string>>>({})
  const [cardLoadingVp, setCardLoadingVp] = useState<Record<string, string | null>>({})
  const [zoomVp, setZoomVp] = useState<string>('0')
  const [zoomVpImages, setZoomVpImages] = useState<Record<string, string>>({})
  const [zoomLoading, setZoomLoading] = useState<boolean>(false)

  // Legacy System Analysis state
  const [analysis, setAnalysis] = useState<OverkillAnalysis | null>(null)
  const [candidates, setCandidates] = useState<OverkillCandidate[]>([])
  const [candidatePage, setCandidatePage] = useState(1)
  const [selected, setSelected] = useState<OverkillSystemShare | null>(null)
  const [page, setPage] = useState(1)
  const [busy, setBusy] = useState(false)
  const [autoSelected, setAutoSelected] = useState(false)

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
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load connections.' })
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
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill trend analysis.' })
    } finally {
      setLoadingTrend(false)
    }
  }, [machineCode, lineCode, modelCode, defectFamily, dateFromUtc, dateToUtc, onNotice])

  // Load Anomaly Thumbnails
  const loadThumbnails = useCallback(async () => {
    setLoadingThumbnails(true)
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
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load anomaly thumbnails.' })
    } finally {
      setLoadingThumbnails(false)
    }
  }, [thumbnailCategory, defectFamily, machineCode, galleryLine, galleryModel, galleryCluster, appliedThumbnailSearch, thumbnailPage, thumbnailPageSize, onNotice])

  // Load Legacy Analysis
  const loadAnalysis = useCallback(async () => {
    setBusy(true)
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
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill system analysis.' })
    } finally {
      setBusy(false)
    }
  }, [machineCode, lineCode, minNgShare, onNotice])

  useEffect(() => {
    void loadConnections()
  }, [loadConnections])

  useEffect(() => {
    void loadTrendData()
  }, [loadTrendData])

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

  // Original VP is defined strictly by the source image_path, not classifier features.
  function getItemOriginalVp(item: AnomalyThumbnailItem): string {
    if (item.originalVp != null && item.originalVp !== '') return item.originalVp
    if (item.imageUri) {
      const folderMatch = item.imageUri.match(/[\\/]VP0*(\d+)[\\/]/i)
      if (folderMatch?.[1]) return String(parseInt(folderMatch[1], 10))
      const fileMatch = item.imageUri.match(/VP0*(\d+)[_.-]/i)
      if (fileMatch?.[1]) return String(parseInt(fileMatch[1], 10))
    }
    return '0'
  }

  // Inspector handler for thumbnail items
  async function handleInspectDefect(item: AnomalyThumbnailItem, targetVp?: string) {
    if (!item.keyId) return
    const vp = targetVp || getItemOriginalVp(item)
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
  }

  async function handleSelectCardVp(item: AnomalyThumbnailItem, vp: string, requestOriginal = false) {
    if (!item.defectId) return
    const itemKey = `${item.keyId}_${item.defectId}`
    const cacheKey = requestOriginal ? 'original' : vp
    setCardVpMap((prev) => ({ ...prev, [itemKey]: cacheKey }))

    if (cardVpImages[itemKey]?.[cacheKey]) {
      return
    }

    setCardLoadingVp((prev) => ({ ...prev, [itemKey]: cacheKey }))
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: requestOriginal ? 'original' : vp, forceRefresh: false })
      if (res.success) {
        const requestVp = requestOriginal ? 'original' : vp
        const url = res.imageUrl || imageCrawlerApi.previewUrl(item.defectId, false, requestVp)
        setCardVpImages((prev) => ({
          ...prev,
          [itemKey]: { ...(prev[itemKey] || {}), [cacheKey]: url },
        }))
        onNotice({
          tone: 'success',
          message: `✓ Loaded VP${vp} image for Defect ${item.defectName || item.keyId} (${res.sizeBytes.toLocaleString()} bytes)`,
        })
      } else {
        onNotice({
          tone: 'error',
          message: `✗ Failed to load VP${vp} image: ${res.errorMessage || 'Unknown error'}`,
        })
      }
    } catch (err) {
      onNotice({
        tone: 'error',
        message: err instanceof Error ? err.message : `Failed to load VP${vp} image`,
      })
    } finally {
      setCardLoadingVp((prev) => ({ ...prev, [itemKey]: null }))
    }
  }

  async function handleCrawlThumbnailVp(item: AnomalyThumbnailItem, vp: string, requestOriginal = false) {
    if (!item.defectId) return
    const itemKey = `${item.keyId}_${item.defectId}`
    const cacheKey = requestOriginal ? 'original' : vp
    const requestVp = requestOriginal ? 'original' : vp
    setCardLoadingVp((prev) => ({ ...prev, [itemKey]: cacheKey }))
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: requestOriginal ? 'original' : vp, forceRefresh: true })
      if (res.success) {
        const url = `${imageCrawlerApi.previewUrl(item.defectId, true, requestVp)}&t=${Date.now()}`
        setCardVpImages((prev) => ({
          ...prev,
          [itemKey]: { ...(prev[itemKey] || {}), [cacheKey]: url },
        }))
        setCardVpMap((prev) => ({ ...prev, [itemKey]: cacheKey }))
        onNotice({
          tone: 'success',
          message: `✓ Crawled VP${vp} image for ${item.defectName || item.keyId} (${res.sizeBytes.toLocaleString()} bytes in ${res.latencyMs}ms)!`,
        })
      } else {
        onNotice({
          tone: 'error',
          message: `✗ Failed to crawl VP${vp}: ${res.errorMessage || 'Unknown error'}`,
        })
      }
    } catch (err) {
      onNotice({
        tone: 'error',
        message: err instanceof Error ? err.message : 'Failed to crawl image',
      })
    } finally {
      setCardLoadingVp((prev) => ({ ...prev, [itemKey]: null }))
    }
  }

  function openZoomModal(item: AnomalyThumbnailItem, initialVp = '0') {
    setZoomedItem(item)
    setZoomVp(initialVp)
    const itemKey = `${item.keyId}_${item.defectId || ''}`
    const existing = cardVpImages[itemKey] || {}
    setZoomVpImages(existing)
    if (initialVp !== '0' && !existing[initialVp] && item.defectId) {
      void handleSelectZoomVp(item, initialVp, initialVp === 'original')
    }
  }

  async function handleSelectZoomVp(item: AnomalyThumbnailItem, vp: string, requestOriginal = false) {
    if (!item.defectId) return
    const itemKey = `${item.keyId}_${item.defectId}`
    const cacheKey = requestOriginal ? 'original' : vp
    setZoomVp(cacheKey)

    if (zoomVpImages[cacheKey]) return

    setZoomLoading(true)
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: requestOriginal ? 'original' : vp, forceRefresh: false })
      if (res.success) {
        const requestVp = requestOriginal ? 'original' : vp
        const url = res.imageUrl || imageCrawlerApi.previewUrl(item.defectId, false, requestVp)
        setZoomVpImages((prev) => ({ ...prev, [cacheKey]: url }))
        setCardVpImages((prev) => ({
          ...prev,
          [itemKey]: { ...(prev[itemKey] || {}), [cacheKey]: url },
        }))
      }
    } catch {} finally {
      setZoomLoading(false)
    }
  }

  async function handleForceRefreshZoomVp(item: AnomalyThumbnailItem) {
    if (!item.defectId) return
    const itemKey = `${item.keyId}_${item.defectId}`
    const requestVp = zoomVp === 'original' ? 'original' : zoomVp
    setZoomLoading(true)
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: requestVp, forceRefresh: true })
      if (res.success) {
        const url = `${imageCrawlerApi.previewUrl(item.defectId, true, requestVp)}&t=${Date.now()}`
        setZoomVpImages((prev) => ({ ...prev, [zoomVp]: url }))
        setCardVpImages((prev) => ({
          ...prev,
          [itemKey]: { ...(prev[itemKey] || {}), [zoomVp]: url },
        }))
        onNotice({
          tone: 'success',
          message: `✓ Force crawled VP${zoomVp} image!`,
        })
      } else {
        onNotice({
          tone: 'error',
          message: `✗ Crawl failed: ${res.errorMessage || 'Unknown error'}`,
        })
      }
    } catch (err) {
      onNotice({
        tone: 'error',
        message: err instanceof Error ? err.message : 'Failed to crawl image',
      })
    } finally {
      setZoomLoading(false)
    }
  }

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
        textStyle: { color: '#e2e8f0', fontSize: 13, fontWeight: 600 },
        subtextStyle: { color: '#94a3b8', fontSize: 11 },
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
            tip += `Real Defect: <b style="color:#a855f7">${pt.realNgCount}</b> (${(pt.realNgRate * 100).toFixed(2)}%)<br/>`
            tip += `Overkill (False Alarm): <b style="color:#f59e0b">${pt.overkillCount}</b> (${(pt.overkillRate * 100).toFixed(2)}%)<br/>`
            tip += `Overkill Share in NG: <b style="color:#f59e0b">${(pt.overkillShare * 100).toFixed(1)}%</b><br/>`
            if (pt.leakageCount > 0) {
              tip += `Defect Escape (Leakage): <b style="color:#ef4444">${pt.leakageCount}</b><br/>`
            }
          }
          return tip
        },
      },
      legend: {
        bottom: 0,
        textStyle: { color: '#cbd5e1', fontSize: 11 },
        data: ['Throughput', 'Station NG Rate (%)', 'Real NG Rate (%)', 'Overkill Rate (%)', 'Overkill Share in NG (%)'],
      },
      grid: { left: '3%', right: '4%', bottom: '12%', top: '18%', containLabel: true },
      xAxis: {
        type: 'category',
        data: dates,
        axisLabel: { color: '#94a3b8', fontSize: 10 },
        axisLine: { lineStyle: { color: 'rgba(255,255,255,0.1)' } },
      },
      yAxis: [
        {
          type: 'value',
          name: 'Throughput',
          nameTextStyle: { color: '#94a3b8' },
          axisLabel: { color: '#94a3b8' },
          splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } },
        },
        {
          type: 'value',
          name: 'Rate (%)',
          nameTextStyle: { color: '#94a3b8' },
          axisLabel: { color: '#94a3b8', formatter: '{value}%' },
          splitLine: { show: false },
        },
      ],
      series: [
        {
          name: 'Throughput',
          type: 'bar',
          barMaxWidth: 28,
          itemStyle: { color: 'rgba(148, 163, 184, 0.3)', borderRadius: [4, 4, 0, 0] },
          label: {
            show: true,
            position: 'top',
            formatter: (p: any) => {
              const val = Number(p.value)
              if (!val || val === 0) return ''
              return val >= 1000 ? (val / 1000).toFixed(1) + 'k' : String(val)
            },
            color: '#cbd5e1',
            fontSize: 9,
            fontWeight: 600,
          },
          data: production,
        },
        {
          name: 'Station NG Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#94a3b8' },
          lineStyle: { width: 2, type: 'dashed' },
          data: stationNgRates,
        },
        {
          name: 'Real NG Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#a855f7' },
          lineStyle: { width: 3 },
          label: {
            show: true,
            position: 'top',
            formatter: '{c}%',
            color: '#c084fc',
            fontSize: 9,
            fontWeight: 600,
          },
          data: realNgRates,
        },
        {
          name: 'Overkill Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#f59e0b' },
          lineStyle: { width: 3 },
          label: {
            show: true,
            position: 'bottom',
            formatter: '{c}%',
            color: '#fbbf24',
            fontSize: 9,
            fontWeight: 700,
          },
          data: overkillRates,
        },
        {
          name: 'Overkill Share in NG (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#f97316' },
          lineStyle: { width: 2, type: 'dotted' },
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
        textStyle: { color: '#e2e8f0', fontSize: 13, fontWeight: 600 },
        subtextStyle: { color: '#94a3b8', fontSize: 11 },
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
            tip += `OK Units: <b style="color:#22c55e">${item.okCount.toLocaleString()}</b><br/>`
            tip += `Station NG Alarms: <b style="color:#ef4444">${item.stationNgCount.toLocaleString()}</b><br/>`
            tip += `Station NG Rate: <b style="color:#ef4444">${(item.stationNgRate * 100).toFixed(2)}%</b><br/>`
            tip += `Real Defect Detect: <b style="color:#a855f7">${item.realNgCount}</b> (${(item.realNgRate * 100).toFixed(2)}% of throughput)<br/>`
            tip += `Overkill Detect: <b style="color:#f59e0b">${item.overkillCount}</b> (${(item.overkillRate * 100).toFixed(2)}% of throughput)<br/>`
            if (item.naCount > 0) {
              tip += `Unclassified (N/A): <b>${item.naCount}</b><br/>`
            }
            if (item.leakageCount > 0) {
              tip += `Defect Escape (Leakage): <b style="color:#dc2626">${item.leakageCount}</b><br/>`
            }
            tip += `Assessment: <i>${item.statusAssessment}</i>`
          }
          return tip
        },
      },
      legend: {
        bottom: 0,
        textStyle: { color: '#cbd5e1', fontSize: 11 },
        data: [
          ...(showLineCounts ? ['OK Units', 'Real Defect Detect', 'Overkill Detect', 'Other / Unclassified NG'] : []),
          ...(showLineRates ? ['NG Rate (%)', 'Real Defect Rate (%)', 'Overkill Rate (%)'] : []),
        ],
      },
      grid: { left: '3%', right: '4%', bottom: '12%', top: '18%', containLabel: true },
      xAxis: {
        type: 'category',
        data: lines,
        axisLabel: { color: '#e2e8f0', fontSize: 11, fontWeight: 600 },
        axisLine: { lineStyle: { color: 'rgba(255,255,255,0.1)' } },
      },
      yAxis: [
        {
          type: 'value',
          name: 'Throughput (Units)',
          nameTextStyle: { color: '#94a3b8' },
          axisLabel: { color: '#94a3b8' },
          splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } },
        },
        {
          type: 'value',
          name: 'Rate (%)',
          nameTextStyle: { color: '#94a3b8' },
          axisLabel: { color: '#94a3b8', formatter: '{value}%' },
          splitLine: { show: false },
        },
      ],
      series: [
        {
          name: 'OK Units',
          type: 'bar',
          stack: 'Throughput',
          barMaxWidth: 42,
          itemStyle: { color: '#22c55e', borderRadius: [0, 0, 0, 0] },
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
            fontSize: 10,
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
          itemStyle: { color: '#8b5cf6' },
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
            fontSize: 10,
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
          itemStyle: { color: '#f59e0b' },
          label: {
            show: true,
            position: 'inside',
            formatter: (p: any) => Number(p.value) > 0 ? String(p.value) : '',
            color: '#ffffff',
            fontSize: 10,
            fontWeight: 700,
          },
          data: overkillCounts,
        },
        {
          name: 'Other / Unclassified NG',
          type: 'bar',
          stack: 'Throughput',
          barMaxWidth: 42,
          itemStyle: { color: '#ef4444', borderRadius: [4, 4, 0, 0] },
          label: {
            show: true,
            position: 'inside',
            formatter: (p: any) => Number(p.value) > 0 ? String(p.value) : '',
            color: '#ffffff',
            fontSize: 10,
            fontWeight: 700,
          },
          data: otherNgCounts,
        },
        {
          name: 'NG Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#f87171' },
          lineStyle: { width: 3 },
          label: {
            show: true,
            position: 'top',
            formatter: '{c}%',
            color: '#f87171',
            fontSize: 10,
            fontWeight: 700,
            backgroundColor: 'rgba(15, 23, 42, 0.75)',
            padding: [2, 4],
            borderRadius: 3,
            borderWidth: 1,
            borderColor: 'rgba(248, 113, 113, 0.4)',
          },
          data: ngRates,
        },
        {
          name: 'Real Defect Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#a855f7' },
          lineStyle: { width: 2 },
          label: {
            show: true,
            position: 'bottom',
            formatter: '{c}%',
            color: '#c084fc',
            fontSize: 9,
            fontWeight: 600,
            backgroundColor: 'rgba(15, 23, 42, 0.75)',
            padding: [1, 3],
            borderRadius: 3,
          },
          data: realNgRates,
        },
        {
          name: 'Overkill Rate (%)',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          itemStyle: { color: '#f59e0b' },
          lineStyle: { width: 2, type: 'dashed' },
          label: {
            show: true,
            position: 'top',
            formatter: '{c}%',
            color: '#fbbf24',
            fontSize: 9,
            fontWeight: 700,
            backgroundColor: 'rgba(15, 23, 42, 0.75)',
            padding: [2, 4],
            borderRadius: 3,
            borderWidth: 1,
            borderColor: 'rgba(245, 158, 11, 0.4)',
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
              <span className="tag-badge" style={{ display: 'inline-flex', marginTop: 6, background: '#eff6ff', color: '#1d4ed8' }}>
                ⚡ Auto machine: {detectedMachine}{machineCode === detectedMachine ? ' (Active)' : ''}
              </span>
            )}
          </div>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="secondary"
              onClick={() => setDashboardScopeOpen(true)}
            >
              🎛️ Scope: Model / Line / Defect
            </button>
            {onNavigateTab && (
              <button
                type="button"
                className="secondary"
                style={{ background: 'rgba(59, 130, 246, 0.15)', borderColor: '#3b82f6', color: '#93c5fd', fontWeight: 600 }}
                onClick={() => onNavigateTab('settings', 'classifier-rules')}
                title="Configure and test classifier rules (Import JSON, test script logic)"
              >
                ⚙️ Classifier Rules & Tester
              </button>
            )}
            <button type="button" disabled={loadingTrend} onClick={() => void loadTrendData()}>
              {loadingTrend ? 'Analyzing...' : 'Refresh Analysis'}
            </button>
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
            <div style={{ display: 'flex', gap: '0.4rem', alignItems: 'center' }}>
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
                    {machine} {machine === detectedMachine ? '★ (Auto)' : ''}
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
                  ⚡ Auto
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
            <div style={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
              <div
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  background: '#f1f5f9',
                  padding: '2px',
                  borderRadius: '7px',
                  border: '1px solid #cbd5e1',
                  gap: '2px',
                }}
              >
                {(['all', 'day', 'week', 'month'] as const).map((p) => {
                  const isActive = period === p
                  return (
                    <button
                      key={p}
                      type="button"
                      style={{
                        padding: '3px 9px',
                        fontSize: '11px',
                        fontWeight: isActive ? 700 : 500,
                        height: '24px',
                        lineHeight: '18px',
                        borderRadius: '5px',
                        textTransform: 'capitalize',
                        background: isActive ? '#ffffff' : 'transparent',
                        color: isActive ? '#1d4ed8' : '#475569',
                        boxShadow: isActive ? '0 1px 2px rgba(0,0,0,0.08)' : 'none',
                        border: isActive ? '1px solid #bfdbfe' : '1px solid transparent',
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
                <div style={{ display: 'flex', gap: '3px', alignItems: 'center' }}>
                  <button
                    type="button"
                    className="ghost-button"
                    style={{ padding: '2px 8px', fontSize: '11px', height: '26px' }}
                    onClick={() => setPeriodOffset((prev) => prev - 1)}
                    title="Previous Period"
                  >
                    ◀ Prev
                  </button>
                  <button
                    type="button"
                    className="ghost-button"
                    style={{ padding: '2px 8px', fontSize: '11px', height: '26px' }}
                    onClick={() => setPeriodOffset((prev) => prev + 1)}
                    disabled={periodOffset >= 0}
                    title="Next Period"
                  >
                    Next ▶
                  </button>
                  {periodOffset !== 0 && (
                    <button
                      type="button"
                      className="ghost-button"
                      style={{ padding: '2px 7px', fontSize: '10px', height: '26px', color: '#2563eb', fontWeight: 600 }}
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
          <div style={{ padding: '4px 8px', fontSize: '12px', color: '#2563eb', display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span>📅 <strong>Time Window:</strong> {periodLabel}</span>
          </div>
        )}
      </section>

      {/* KPI Metrics Grid */}
      <section className="metrics-grid" style={{ gridTemplateColumns: 'repeat(5, minmax(150px, 1fr))', gap: 10 }}>
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
              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', alignItems: 'center', marginBottom: 8 }}>
                <div>
                  <strong>OP Review Coverage · Real Defect vs Overkill</strong>
                  <div className="text-xs text-muted">{reviewed.toLocaleString()} / {stationNg.toLocaleString()} Station NG confirmed by OP</div>
                </div>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                  <span className="tag-badge" style={{ background: '#ede9fe', color: '#6d28d9' }}>Real Defect: {opReal}</span>
                  <span className="tag-badge" style={{ background: '#fef3c7', color: '#b45309' }}>Overkill: {opOverkill}</span>
                  <span className="tag-badge" style={{ background: '#e2e8f0', color: '#475569' }}>Pending: {pending}</span>
                </div>
              </div>
              <div style={{ display: 'flex', width: '100%', height: 18, borderRadius: 999, overflow: 'hidden', background: '#e2e8f0' }}>
                {realPct > 0 && <div title={`OP Real Defect ${realPct.toFixed(1)}%`} style={{ width: `${realPct}%`, background: '#8b5cf6', transition: 'width .25s ease' }} />}
                {overkillPct > 0 && <div title={`OP Overkill ${overkillPct.toFixed(1)}%`} style={{ width: `${overkillPct}%`, background: '#f59e0b', transition: 'width .25s ease' }} />}
                {pendingPct > 0 && <div title={`Pending ${pendingPct.toFixed(1)}%`} style={{ width: `${pendingPct}%`, background: '#cbd5e1', transition: 'width .25s ease' }} />}
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, color: '#64748b', marginTop: 5 }}>
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
            <span>⚡ Trend Analysis: Real NG vs Overkill</span>
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'lines' ? 'active' : ''}`}
            onClick={() => setActiveView('lines')}
          >
            <span>📊 Line Comparison</span>
            {trendData?.lineComparison && trendData.lineComparison.length > 0 ? (
              <span className="tag-badge" style={{ background: '#3b82f6', color: '#fff', marginLeft: '6px', fontSize: '10px' }}>
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
            <span>📑 OP Reconciliation Dashboard & Deep-Dive</span>
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${activeView === 'thumbnails' ? 'active' : ''}`}
            onClick={() => setActiveView('thumbnails')}
          >
            <span>🖼️ Image View</span>
            {trendData?.summary.stationNgCount ? (
              <span className="tag-badge" style={{ background: '#ef4444', color: '#fff', marginLeft: '6px', fontSize: '10px' }}>
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
            <span>🛡️ Defect Escape Investigation (Leakage)</span>
            {trendData && trendData.summary.leakageCount > 0 ? (
              <span className="tag-badge" style={{ background: '#ef4444', color: '#fff', marginLeft: '6px', fontSize: '10px' }}>
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
            <span>📋 Defect Systems & Suspects</span>
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
                    <strong>💡 </strong> {item}
                  </li>
                ))}
              </ul>
            </article>

            <article className="panel risk-panel">
              <p className="eyebrow">Overall Separation Breakdown</p>
              <h3>Machine Efficiency vs Classifier Rules</h3>
              <div style={{ marginTop: '12px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <div className="risk-row">
                  <span>Station NG Alarms</span>
                  <strong>{trendData?.summary.stationNgCount ?? 0} units</strong>
                </div>
                <div className="risk-row">
                  <span style={{ color: '#a855f7', fontWeight: 600 }}>Confirmed Real NG</span>
                  <strong style={{ color: '#a855f7' }}>{trendData?.summary.realNgCount ?? 0} units</strong>
                </div>
                <div className="risk-row">
                  <span style={{ color: '#f59e0b', fontWeight: 600 }}>Overkill (False Alarm)</span>
                  <strong style={{ color: '#f59e0b' }}>
                    {trendData?.summary.overkillCount ?? 0} ({formatPercent(trendData?.summary.overkillShare ?? 0)})
                  </strong>
                </div>
                <div className="risk-row">
                  <span style={{ color: '#ef4444', fontWeight: 600 }}>Defect Escapes (Leakage)</span>
                  <strong style={{ color: '#ef4444' }}>{trendData?.summary.leakageCount ?? 0} units</strong>
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
                      <td className="font-bold" style={{ color: '#60a5fa' }}>{fam.defectFamily}</td>
                      <td>{fam.totalDefects.toLocaleString()}</td>
                      <td style={{ color: '#a855f7', fontWeight: 600 }}>{fam.realNgCount.toLocaleString()}</td>
                      <td style={{ color: '#f59e0b', fontWeight: 600 }}>{fam.overkillCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: fam.overkillShare >= 0.5 ? 'rgba(245, 158, 11, 0.2)' : 'rgba(34, 197, 94, 0.15)',
                            color: fam.overkillShare >= 0.5 ? '#f59e0b' : '#22c55e',
                            fontWeight: 600,
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
                      <td style={{ color: '#a855f7' }}>{cls.realNgCount}</td>
                      <td style={{ color: '#f59e0b', fontWeight: 600 }}>{cls.overkillCount}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: cls.overkillShare >= 0.5 ? 'rgba(239, 68, 68, 0.15)' : 'rgba(16, 185, 129, 0.15)',
                            color: cls.overkillShare >= 0.5 ? '#ef4444' : '#10b981',
                            fontWeight: 600,
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
                className="secondary compact"
                onClick={() => setActiveView('lines')}
                style={{ fontSize: '11px' }}
              >
                📊 View Full Line Comparison & Chart →
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
                      <td className="font-bold" style={{ color: '#60a5fa' }}>{lineItem.lineCode}</td>
                      <td className="font-mono text-xs">{lineItem.machineCode}</td>
                      <td><strong>{lineItem.totalInspected.toLocaleString()}</strong></td>
                      <td style={{ color: '#22c55e' }}>{lineItem.okCount.toLocaleString()}</td>
                      <td style={{ color: '#ef4444', fontWeight: 600 }}>{lineItem.stationNgCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: lineItem.stationNgRate >= 0.1 ? 'rgba(239, 68, 68, 0.2)' : 'rgba(34, 197, 94, 0.15)',
                            color: lineItem.stationNgRate >= 0.1 ? '#ef4444' : '#22c55e',
                            fontWeight: 700,
                          }}
                        >
                          {formatPercent(lineItem.stationNgRate)}
                        </span>
                      </td>
                      <td style={{ color: '#a855f7', fontWeight: 700 }}>{lineItem.realNgCount.toLocaleString()}</td>
                      <td>
                        <span className="tag-badge" style={{ background: 'rgba(139, 92, 246, 0.15)', color: '#7c3aed', fontWeight: 700 }}>
                          {formatPercent(lineItem.realNgRate)}
                        </span>
                      </td>
                      <td style={{ color: '#f59e0b', fontWeight: 700 }}>{lineItem.overkillCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: lineItem.overkillRate >= 0.05 ? 'rgba(245, 158, 11, 0.2)' : 'rgba(148, 163, 184, 0.15)',
                            color: lineItem.overkillRate >= 0.05 ? '#f59e0b' : '#cbd5e1',
                            fontWeight: 600,
                          }}
                        >
                          {formatPercent(lineItem.overkillRate)}
                        </span>
                      </td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background:
                              lineItem.riskLevel === 'CRITICAL'
                                ? '#ef4444'
                                : lineItem.riskLevel === 'HIGH'
                                ? 'rgba(239, 68, 68, 0.2)'
                                : lineItem.riskLevel === 'WARNING'
                                ? 'rgba(245, 158, 11, 0.2)'
                                : 'rgba(34, 197, 94, 0.2)',
                            color:
                              lineItem.riskLevel === 'CRITICAL'
                                ? '#fff'
                                : lineItem.riskLevel === 'HIGH'
                                ? '#ef4444'
                                : lineItem.riskLevel === 'WARNING'
                                ? '#f59e0b'
                                : '#22c55e',
                            fontWeight: 700,
                          }}
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
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                gap: 12,
                flexWrap: 'wrap',
                marginBottom: 8,
                paddingBottom: 8,
                borderBottom: '1px solid var(--border)',
              }}
            >
              <div>
                <strong style={{ fontSize: 13 }}>Chart Display Controls</strong>
                <div className="text-xs text-muted">Adjust chart size and show or hide count/rate series.</div>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
                <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, cursor: 'pointer', fontSize: 12, fontWeight: 600 }}>
                  <input
                    type="checkbox"
                    checked={showLineCounts}
                    onChange={(event) => setShowLineCounts(event.target.checked)}
                  />
                  Count / Detect
                </label>
                <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, cursor: 'pointer', fontSize: 12, fontWeight: 600 }}>
                  <input
                    type="checkbox"
                    checked={showLineRates}
                    onChange={(event) => setShowLineRates(event.target.checked)}
                  />
                  Rate (%)
                </label>
                <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: 12, fontWeight: 600 }}>
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
                style={{ height: lineChartHeight, width: '100%' }}
                opts={{ renderer: 'canvas' }}
              />
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
                <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
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
                      <td className="font-bold" style={{ color: '#60a5fa' }}>{l.lineCode}</td>
                      <td className="font-mono text-xs">{l.machineCode}</td>
                      <td><strong>{l.totalInspected.toLocaleString()}</strong></td>
                      <td style={{ color: '#22c55e' }}>{l.okCount.toLocaleString()}</td>
                      <td style={{ color: '#ef4444', fontWeight: 600 }}>{l.stationNgCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: l.stationNgRate >= 0.1 ? 'rgba(239, 68, 68, 0.2)' : 'rgba(34, 197, 94, 0.15)',
                            color: l.stationNgRate >= 0.1 ? '#ef4444' : '#22c55e',
                            fontWeight: 700,
                          }}
                        >
                          {formatPercent(l.stationNgRate)}
                        </span>
                      </td>
                      <td style={{ color: '#a855f7', fontWeight: 700 }}>{l.realNgCount.toLocaleString()}</td>
                      <td>
                        <span className="tag-badge" style={{ background: 'rgba(139, 92, 246, 0.15)', color: '#7c3aed', fontWeight: 700 }}>
                          {formatPercent(l.realNgRate)}
                        </span>
                      </td>
                      <td style={{ color: '#f59e0b', fontWeight: 700 }}>{l.overkillCount.toLocaleString()}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: l.overkillRate >= 0.05 ? 'rgba(245, 158, 11, 0.2)' : 'rgba(148, 163, 184, 0.15)',
                            color: l.overkillRate >= 0.05 ? '#b45309' : '#64748b',
                            fontWeight: 700,
                          }}
                        >
                          {formatPercent(l.overkillRate)}
                        </span>
                      </td>
                      <td className="text-muted font-mono text-xs">{l.naCount}</td>
                      <td className="font-mono text-xs">{formatPercent(l.overkillShare)}</td>
                      <td>
                        {l.leakageCount > 0 ? (
                          <span className="tag-badge" style={{ background: '#ef4444', color: '#fff', fontWeight: 700 }}>
                            {l.leakageCount}
                          </span>
                        ) : (
                          <span className="text-muted">0</span>
                        )}
                      </td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background:
                              l.riskLevel === 'CRITICAL'
                                ? '#ef4444'
                                : l.riskLevel === 'HIGH'
                                ? 'rgba(239, 68, 68, 0.2)'
                                : l.riskLevel === 'WARNING'
                                ? 'rgba(245, 158, 11, 0.2)'
                                : 'rgba(34, 197, 94, 0.2)',
                            color:
                              l.riskLevel === 'CRITICAL'
                                ? '#fff'
                                : l.riskLevel === 'HIGH'
                                ? '#ef4444'
                                : l.riskLevel === 'WARNING'
                                ? '#f59e0b'
                                : '#22c55e',
                            fontWeight: 700,
                          }}
                        >
                          {l.riskLevel}
                        </span>
                      </td>
                      <td className="text-xs" style={{ maxWidth: 300 }}>{l.statusAssessment}</td>
                      <td>
                        <button
                          type="button"
                          className="ghost-button compact"
                          style={{ fontSize: '11px', whiteSpace: 'nowrap' }}
                          onClick={() => {
                            setLineCode(l.lineCode)
                            setActiveView('trend')
                          }}
                          title={`Filter trend analysis for line ${l.lineCode}`}
                        >
                          🔍 Inspect Line
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
                <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
                  Gallery is paginated. The number on this page is not the full Station NG total.
                  {thumbnailTotalCount > 0
                    ? ` Showing ${thumbnailItems.length.toLocaleString()} of ${thumbnailTotalCount.toLocaleString()} matching records (page ${thumbnailPage}/${thumbnailTotalPages}).`
                    : ' Open a category to load matching records.'}
                </p>
              </div>
              <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                <button type="button" className="secondary" onClick={() => setGalleryScopeOpen(true)}>
                  🎯 Scope: Model / Line / Defect
                </button>
                <button
                  type="button"
                  className="secondary"
                  disabled={loadingThumbnails}
                  onClick={() => {
                    setThumbnailPage(1)
                    void loadThumbnails()
                  }}
                >
                  {loadingThumbnails ? 'Loading thumbnails...' : '🔄 Refresh Gallery (Latest)'}
                </button>
              </div>
            </div>

            {/* Filter Pills */}
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', alignItems: 'center', marginTop: '12px' }}>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'All' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'All' ? 'rgba(255,255,255,0.1)' : undefined,
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
                  background: thumbnailCategory === 'StationNG' ? 'rgba(239, 68, 68, 0.2)' : undefined,
                  color: '#ef4444',
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
                  background: thumbnailCategory === 'RealNG' ? 'rgba(168, 85, 247, 0.2)' : undefined,
                  color: '#a855f7',
                  fontWeight: thumbnailCategory === 'RealNG' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('RealNG')
                  setThumbnailPage(1)
                }}
              >
                ✓ Real NG - Confirmed by Script ({categoryBadgeCounts.realNg})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'NA' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'NA' ? 'rgba(245, 158, 11, 0.2)' : undefined,
                  color: '#f59e0b',
                  fontWeight: thumbnailCategory === 'NA' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('NA')
                  setThumbnailPage(1)
                }}
              >
                ❓ Unclassified / N/A ({categoryBadgeCounts.na})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'Leakage' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'Leakage' ? 'rgba(239, 68, 68, 0.2)' : undefined,
                  color: '#ef4444',
                  fontWeight: thumbnailCategory === 'Leakage' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('Leakage')
                  setThumbnailPage(1)
                }}
              >
                🚨 Defect Escapes / Leakage ({categoryBadgeCounts.leakage})
              </button>
              <button
                type="button"
                className={`ghost-button compact ${thumbnailCategory === 'ConsistentOK' ? 'active' : ''}`}
                style={{
                  background: thumbnailCategory === 'ConsistentOK' ? 'rgba(34, 197, 94, 0.2)' : undefined,
                  color: '#22c55e',
                  fontWeight: thumbnailCategory === 'ConsistentOK' ? 600 : 400,
                }}
                onClick={() => {
                  setThumbnailCategory('ConsistentOK')
                  setThumbnailPage(1)
                }}
              >
                ✓ Clean OK Units
              </button>

              <div style={{ marginLeft: 'auto', display: 'flex', gap: '8px' }}>
                <input
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
                  style={{ width: 220, fontSize: '12px', padding: '4px 10px' }}
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
          ) : thumbnailItems.length === 0 ? (
            <div className="dashboard-report-empty">
              <strong>No anomaly thumbnail records found</strong>
              <span>Adjust classification filters or synchronize inspection records from the database.</span>
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '18px' }}>
              {[...new Set(thumbnailItems.map((item) => `${item.modelCode || 'UNASSIGNED'}|${item.lineCode}|${item.defectFamily || item.defectType || 'Unclassified'}`))].map((groupKey) => {
                const [groupModel, groupLine, groupCluster] = groupKey.split('|')
                const groupItems = thumbnailItems.filter((item) => `${item.modelCode || 'UNASSIGNED'}|${item.lineCode}|${item.defectFamily || item.defectType || 'Unclassified'}` === groupKey)
                return (
                  <section key={groupKey}>
                    <div style={{ display: 'flex', gap: '8px', alignItems: 'center', marginBottom: '8px' }}>
                      <strong style={{ color: '#38bdf8' }}>{groupModel}</strong>
                      <span className="line-pill">{groupLine}</span>
                      <StatusPill value={groupCluster} />
                      <span className="text-xs text-muted">{groupItems.length} images</span>
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: '16px' }}>
              {groupItems.map((item, idx) => {
                const isNa = item.anomalyCategory.toLowerCase() === 'na' || item.anomalyCategory === 'N/A'
                const isLeakage = item.anomalyCategory.toLowerCase() === 'leakage'
                const isRealNg = item.anomalyCategory.toLowerCase() === 'realng'
                const isOpReviewed = item.opReviewMark === 'Overkill' || item.opReviewMark === 'Real Defect'

                const badgeBg = isRealNg
                  ? 'rgba(168, 85, 247, 0.2)'
                  : isLeakage
                  ? 'rgba(239, 68, 68, 0.2)'
                  : isNa
                  ? 'rgba(245, 158, 11, 0.2)'
                  : 'rgba(34, 197, 94, 0.2)'
                const badgeColor = isRealNg
                  ? '#a855f7'
                  : isLeakage
                  ? '#ef4444'
                  : isNa
                  ? '#f59e0b'
                  : '#22c55e'
                const badgeLabel = isRealNg
                  ? (item.opReviewMark === 'Real Defect' ? 'REAL DEFECT (OP)' : 'REAL NG (SCRIPT)')
                  : isLeakage
                  ? 'LEAKAGE'
                  : isNa
                  ? 'N/A'
                  : item.opReviewMark === 'Overkill'
                  ? 'OVERKILL (OP)'
                  : 'CLEAN OK'

                const itemKey = `${item.keyId}_${item.defectId || ''}`
                const originalVp = getItemOriginalVp(item)
                const activeVp = cardVpMap[itemKey] || '0'
                const activeImgSrc = cardVpImages[itemKey]?.[activeVp] ||
                  (activeVp === '0'
                    ? (item.thumbnailUrl || (item.defectId ? imageCrawlerApi.previewUrl(item.defectId, false, '0') : ''))
                    : (item.defectId ? imageCrawlerApi.previewUrl(item.defectId, false, activeVp) : ''))
                const isCardLoading = cardLoadingVp[itemKey] !== null && cardLoadingVp[itemKey] !== undefined

                return (
                  <article
                    key={item.defectId ?? `${item.keyId}-${idx}`}
                    className="panel"
                    style={{
                      padding: 0,
                      overflow: 'hidden',
                      border: `1px solid ${isRealNg || isLeakage || isNa ? badgeColor : 'rgba(255,255,255,0.08)'}`,
                      display: 'flex',
                      flexDirection: 'column',
                    }}
                  >
                    {/* Thumbnail Image Container */}
                    <div
                      style={{
                        position: 'relative',
                        height: '160px',
                        background: '#090d16',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        cursor: activeImgSrc ? 'pointer' : 'default',
                      }}
                      onClick={() => activeImgSrc && openZoomModal(item, activeVp)}
                      title={activeImgSrc ? 'Click to enlarge image' : 'No image available'}
                    >
                      {isCardLoading ? (
                        <div style={{ color: '#60a5fa', textAlign: 'center', fontSize: '12px' }}>
                          <div>⌛ Loading image...</div>
                          <div style={{ fontSize: '10px', color: '#94a3b8' }}>VP{cardLoadingVp[itemKey]} from PC</div>
                        </div>
                      ) : activeImgSrc ? (
                        <img
                          src={activeImgSrc}
                          alt={item.defectName ?? 'Defect'}
                          style={{
                            maxWidth: '100%',
                            maxHeight: '100%',
                            objectFit: 'contain',
                          }}
                          onError={(e) => {
                            // Fallback on image load error
                            ;(e.target as HTMLImageElement).style.display = 'none'
                          }}
                        />
                      ) : (
                        <div style={{ color: '#475569', textAlign: 'center', fontSize: '12px' }}>
                          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                            <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                            <circle cx="8.5" cy="8.5" r="1.5" />
                            <polyline points="21 15 16 10 5 21" />
                          </svg>
                          <div style={{ marginTop: '4px' }}>No thumbnail image</div>
                        </div>
                      )}

                      {/* Status Badge Overlay */}
                      <span
                        className="tag-badge"
                        style={{
                          position: 'absolute',
                          top: '8px',
                          left: '8px',
                          background: badgeBg,
                          color: badgeColor,
                          fontWeight: 700,
                          fontSize: '11px',
                          padding: '3px 8px',
                          border: `1px solid ${badgeColor}`,
                          borderRadius: '4px',
                          backdropFilter: 'blur(4px)',
                        }}
                      >
                        {badgeLabel}
                      </span>

                      {isOpReviewed && (
                        <span
                          className="tag-badge"
                          style={{
                            position: 'absolute',
                            top: '38px',
                            left: '8px',
                            background: item.opReviewMark === 'Overkill' ? 'rgba(245, 158, 11, 0.9)' : 'rgba(124, 58, 237, 0.9)',
                            color: '#fff',
                            border: '1px solid rgba(255,255,255,.45)',
                            fontWeight: 700,
                            fontSize: '10px',
                          }}
                        >
                          OP: {item.opReviewMark}
                        </span>
                      )}

                      {/* Real Defect Camera Angle Badge */}
                      <span
                        className="tag-badge"
                        style={{
                          position: 'absolute',
                          top: '8px',
                          right: '8px',
                          background: 'rgba(124, 58, 237, 0.9)',
                          color: '#fff',
                          fontWeight: 700,
                          fontSize: '10px',
                          padding: '2px 7px',
                          borderRadius: '4px',
                          backdropFilter: 'blur(4px)',
                          border: '1px solid rgba(196, 181, 253, 0.6)',
                          boxShadow: '0 1px 3px rgba(0,0,0,0.4)',
                        }}
                        title={`Original VP from image_path: VP${originalVp}`}
                      >
                        Original VP: VP{originalVp}
                      </span>

                      {/* Station Judge vs Rule Verdict Overlay */}
                      <div
                        style={{
                          position: 'absolute',
                          bottom: '6px',
                          right: '6px',
                          display: 'flex',
                          gap: '4px',
                        }}
                      >
                        <span
                          className="tag-badge"
                          style={{
                            background: item.stationJudge === 'NG' ? 'rgba(239, 68, 68, 0.85)' : 'rgba(34, 197, 94, 0.85)',
                            color: '#fff',
                            fontSize: '10px',
                            fontWeight: 700,
                          }}
                        >
                          Station: {item.stationJudge}
                        </span>
                        <span
                          className="tag-badge"
                          style={{
                            background: item.ruleVerdict === 'TrueDefect' || item.ruleVerdict === 'RealNG' ? 'rgba(168, 85, 247, 0.85)' : 'rgba(245, 158, 11, 0.85)',
                            color: '#fff',
                            fontSize: '10px',
                            fontWeight: 700,
                          }}
                        >
                          Rule: {item.ruleVerdict === 'TrueDefect' || item.ruleVerdict === 'RealNG' ? 'NG' : 'OK'}
                        </span>
                      </div>
                    </div>

                    {/* Card Content Body */}
                    <div style={{ padding: '12px', flex: 1, display: 'flex', flexDirection: 'column', gap: '6px' }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                        <div>
                          <strong className="mono-code" style={{ fontSize: '13px', color: '#e2e8f0' }}>
                            {item.keyId}
                          </strong>
                          {item.componentName && (
                            <span className="text-xs text-muted" style={{ marginLeft: '6px' }}>
                              ({item.componentName})
                            </span>
                          )}
                          {item.panelId ? (
                            <div className="text-xs text-muted" style={{ marginTop: '2px' }}>
                              Panel: <span className="mono-code">{item.panelId}</span>
                            </div>
                          ) : null}
                        </div>
                        <span className="text-xs font-mono text-muted">{item.lineCode}</span>
                      </div>

                      <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                        <strong style={{ fontSize: '12px', color: '#cbd5e1' }}>{item.defectName || 'Unnamed defect'}</strong>
                        {item.defectFamily && <StatusPill value={item.defectFamily} />}
                      </div>

                      {(item.productNameDefect || item.productDefectName) && (
                        <div className="text-xs" style={{ color: '#93c5fd' }}>
                          Product defect: <strong>{item.productNameDefect || item.productDefectName}</strong>
                          {item.productDefectName && item.productDefectName !== item.productNameDefect ? ` · ${item.productDefectName}` : ''}
                        </div>
                      )}

                      {/* Decision Reason */}
                      <p
                        className="text-xs"
                        style={{
                          color: '#94a3b8',
                          margin: '4px 0',
                          lineHeight: 1.4,
                          display: '-webkit-box',
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: 'vertical',
                          overflow: 'hidden',
                        }}
                        title={item.decisionReason}
                      >
                        {item.decisionReason}
                      </p>

                      {/* Feature Chips */}
                      {item.keyFeatures && Object.keys(item.keyFeatures).length > 0 && (
                        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginTop: 'auto' }}>
                          {Object.entries(item.keyFeatures)
                            .slice(0, 3)
                            .map(([k, v]) => (
                              <span
                                key={k}
                                className="font-mono"
                                style={{
                                  background: 'rgba(255,255,255,0.05)',
                                  padding: '1px 6px',
                                  borderRadius: '3px',
                                  fontSize: '10px',
                                  color: '#cbd5e1',
                                }}
                              >
                                {k}: <b>{typeof v === 'number' ? v.toFixed(1) : v}</b>
                              </span>
                            ))}
                        </div>
                      )}

                      {/* Camera Angle Selector Bar */}
                      {item.defectId && (
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '4px', flexWrap: 'wrap', padding: '6px 0', borderTop: '1px solid rgba(255,255,255,0.06)' }}>
                          <span style={{ fontSize: '10px', color: '#94a3b8', fontWeight: 600 }}>Camera Angle:</span>
                          <div style={{ display: 'flex', gap: '3px', alignItems: 'center', flexWrap: 'wrap' }}>
                            <button
                              type="button"
                              onClick={() => void handleSelectCardVp(item, '0')}
                              disabled={cardLoadingVp[itemKey] === '0'}
                              style={{
                                padding: '2px 6px',
                                fontSize: '10px',
                                fontWeight: activeVp === '0' ? 700 : 500,
                                borderRadius: '4px',
                                cursor: 'pointer',
                                border: activeVp === '0' ? '1px solid #3b82f6' : '1px solid rgba(255,255,255,0.15)',
                                background: activeVp === '0' ? '#1e3a8a' : 'rgba(255,255,255,0.05)',
                                color: activeVp === '0' ? '#93c5fd' : '#cbd5e1',
                              }}
                              title="VP0 (Default thumbnail)"
                            >
                              VP0
                            </button>

                            <button
                              type="button"
                              onClick={() => void handleSelectCardVp(item, originalVp, true)}
                              disabled={cardLoadingVp[itemKey] === originalVp}
                              style={{
                                padding: '2px 8px',
                                fontSize: '10px',
                                fontWeight: 700,
                                borderRadius: '4px',
                                cursor: 'pointer',
                                border: activeVp === 'original' ? '1.5px solid #a855f7' : '1px solid rgba(168, 85, 247, 0.4)',
                                background: activeVp === 'original' ? '#6b21a8' : 'rgba(168, 85, 247, 0.15)',
                                color: activeVp === 'original' ? '#f3e8ff' : '#d8b4fe',
                                display: 'inline-flex',
                                alignItems: 'center',
                                gap: '3px',
                              }}
                              title={`Load original VP from image_path (VP${originalVp})`}
                            >
                              <span>VP{originalVp} (Path original)</span>
                              {cardLoadingVp[itemKey] === originalVp ? (
                                <span style={{ fontSize: '9px' }}>⌛</span>
                              ) : cardVpImages[itemKey]?.original ? (
                                <span style={{ width: '5px', height: '5px', borderRadius: '50%', background: '#4ade80', display: 'inline-block' }} />
                              ) : null}
                            </button>

                            {['1', '2', '3', '4'].map((vp) => (
                              <button
                                key={vp}
                                type="button"
                                onClick={() => void handleSelectCardVp(item, vp)}
                                disabled={cardLoadingVp[itemKey] === vp}
                                style={{
                                  padding: '2px 5px',
                                  fontSize: '10px',
                                  fontWeight: activeVp === vp ? 700 : 500,
                                  borderRadius: '4px',
                                  cursor: 'pointer',
                                  border: activeVp === vp ? '1px solid #3b82f6' : '1px solid rgba(255,255,255,0.1)',
                                  background: activeVp === vp ? '#1e3a8a' : 'transparent',
                                  color: activeVp === vp ? '#93c5fd' : '#94a3b8',
                                }}
                                title={`Load camera angle VP${vp}`}
                              >
                                VP{vp}
                              </button>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Card Actions */}
                      <div
                        style={{
                          display: 'flex',
                          gap: '6px',
                          marginTop: '6px',
                          paddingTop: '6px',
                          borderTop: '1px solid rgba(255,255,255,0.05)',
                        }}
                      >
                        <button
                          type="button"
                          className="ghost-button compact"
                          style={{ fontSize: '11px', flex: 1 }}
                          onClick={() => openZoomModal(item, activeVp)}
                        >
                          🔍 Zoom
                        </button>
                        {item.defectId && (
                          <button
                            type="button"
                            className="secondary compact"
                            style={{
                              fontSize: '11px',
                              padding: '3px 8px',
                              background: activeVp === 'original' ? 'rgba(168, 85, 247, 0.25)' : 'rgba(168, 85, 247, 0.15)',
                              borderColor: 'rgba(168, 85, 247, 0.4)',
                              color: '#d8b4fe',
                            }}
                            onClick={() => void handleCrawlThumbnailVp(item, activeVp === 'original' ? originalVp : activeVp, activeVp === 'original')}
                            title={activeVp === 'original' ? `Force crawl path original VP${originalVp}` : `Force crawl angle VP${activeVp} from PC API`}
                          >
                            ⚡ Crawl {activeVp === 'original' ? `Path VP${originalVp}` : `VP${activeVp}`}
                          </button>
                        )}
                        <button
                          type="button"
                          className="secondary compact"
                          style={{ fontSize: '11px', flex: 1 }}
                          onClick={() => void handleInspectDefect(item, activeVp)}
                          disabled={loadingDefectInspect}
                        >
                          🔬 Inspect
                        </button>
                      </div>
                    </div>
                  </article>
                )
              })}
                    </div>
                  </section>
                )
              })}
            </div>
          )}

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '12px', alignItems: 'center', justifyContent: 'space-between' }}>
            <span className="text-xs text-muted">
              Image View page size is only the current grid. Station NG total is { (trendData?.summary.stationNgCount ?? 0).toLocaleString() } products.
              Matching gallery records: <strong>{thumbnailTotalCount.toLocaleString()}</strong>
              {thumbnailTotalCount > 0 ? ` · this page ${thumbnailItems.length}` : ''}.
            </span>
            <label className="text-xs text-muted" style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
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
                <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
                  Products judged Pass (OK/G) by inspection stations that actually contain severe defects or violate classifier rule scripts.
                </p>
              </div>
            </div>

            <div className="metrics-grid" style={{ marginTop: '12px' }}>
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
                    <span key="r" className="tag-badge" style={{ background: 'rgba(239,68,68,0.2)', color: '#ef4444', fontWeight: 700 }}>
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
        <OpReconciliationTab
          onNotice={onNotice}
          initialMachine={machineCode}
          initialLine={lineCode}
          mode="records"
        />
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
            rows={pagedCandidates.items.map((item) => ({
              id: item.keyId,
              cells: [
                item.keyId,
                <JudgeBadge key="j" value={item.judge} />,
                item.zone ? (
                  <span key="z" className="tag-badge" style={{ background: 'rgba(16, 185, 129, 0.15)', color: '#10b981', fontWeight: 600 }}>
                    {item.zone}
                  </span>
                ) : '—',
                item.defectName ? (
                  <span key="dn" className="tag-badge" style={{ background: 'rgba(239, 68, 68, 0.15)', color: '#ef4444', fontWeight: 600 }}>
                    {item.defectName}
                  </span>
                ) : item.nameDefect && item.nameDefect !== 'None' ? (
                  <span key="dn-legacy" className="tag-badge" style={{ background: 'rgba(239, 68, 68, 0.15)', color: '#ef4444', fontWeight: 600 }}>
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

      {/* MODAL: Full Resolution Zoom Image */}
      {zoomedItem && (() => {
        const zoomOriginalVp = getItemOriginalVp(zoomedItem)
        const currentZoomImgSrc = zoomVpImages[zoomVp] ||
          (zoomVp === '0'
            ? (zoomedItem.thumbnailUrl || (zoomedItem.defectId ? imageCrawlerApi.previewUrl(zoomedItem.defectId, false, '0') : ''))
            : (zoomedItem.defectId ? imageCrawlerApi.previewUrl(zoomedItem.defectId, false, zoomVp) : ''))

        return (
        <Modal
          title={`Evidence Image Inspection · ${zoomedItem.keyId} · VP${zoomVp}`}
          description={`Station Judgment: ${zoomedItem.stationJudge} · Rule Verdict: ${zoomedItem.ruleVerdict} · Category: ${zoomedItem.anomalyCategory}`}
          open={Boolean(zoomedItem)}
          onClose={() => setZoomedItem(null)}
          wide
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            {/* Camera Angle Selector Bar */}
            {zoomedItem.defectId && (
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  background: 'rgba(255,255,255,0.04)',
                  padding: '8px 12px',
                  borderRadius: '6px',
                  border: '1px solid rgba(255,255,255,0.08)',
                  flexWrap: 'wrap',
                  gap: '8px',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                  <span style={{ fontSize: '11px', fontWeight: 700, color: '#94a3b8' }}>Camera Angle:</span>
                  <button
                    type="button"
                    disabled={zoomLoading && zoomVp === 'original'}
                    onClick={() => void handleSelectZoomVp(zoomedItem, zoomOriginalVp, true)}
                    style={{
                      padding: '4px 12px',
                      fontSize: '11px',
                      fontWeight: 700,
                      borderRadius: '6px',
                      cursor: 'pointer',
                      border: zoomVp === 'original' ? '1.5px solid #a855f7' : '1px solid rgba(168, 85, 247, 0.4)',
                      background: zoomVp === 'original' ? '#7c3aed' : 'rgba(168, 85, 247, 0.15)',
                      color: '#fff',
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '5px',
                    }}
                    title={`Load original VP from image_path (VP${zoomOriginalVp})`}
                  >
                    <span>Load Path Original VP (VP{zoomOriginalVp})</span>
                    {zoomLoading && zoomVp === 'original' ? <span>⌛</span> : null}
                  </button>

                  <span style={{ width: '1px', height: '16px', background: 'rgba(255,255,255,0.15)', margin: '0 2px' }} />

                  {['0', '1', '2', '3', '4'].map((vp) => {
                    const isSelected = zoomVp === vp
                    const isOriginal = vp === zoomOriginalVp
                    return (
                      <button
                        key={vp}
                        type="button"
                        onClick={() => void handleSelectZoomVp(zoomedItem, vp, false)}
                        style={{
                          padding: '4px 9px',
                          fontSize: '11px',
                          fontWeight: isSelected ? 700 : 500,
                          borderRadius: '6px',
                          cursor: 'pointer',
                          border: isSelected ? '1.5px solid #3b82f6' : isOriginal ? '1px dashed #a855f7' : '1px solid rgba(255,255,255,0.12)',
                          background: isSelected ? '#1d4ed8' : isOriginal ? 'rgba(168, 85, 247, 0.1)' : 'rgba(255,255,255,0.03)',
                          color: isSelected ? '#fff' : isOriginal ? '#d8b4fe' : '#cbd5e1',
                        }}
                      >
                        {vp === '0' ? 'VP0 (Default)' : isOriginal ? `VP${vp} (Path Angle)` : `VP${vp}`}
                      </button>
                    )
                  })}
                </div>

                <button
                  type="button"
                  className="secondary compact"
                  style={{ fontSize: '11px', padding: '4px 10px', background: 'rgba(255,255,255,0.08)' }}
                  disabled={zoomLoading}
                  onClick={() => void handleForceRefreshZoomVp(zoomedItem)}
                >
                  {zoomLoading ? 'Loading...' : zoomVp === 'original' ? `⚡ Force Crawl Path Original VP${zoomOriginalVp}` : `⚡ Force Crawl VP${zoomVp}`}
                </button>
              </div>
            )}

            <ImageViewer
              src={currentZoomImgSrc}
              alt={`${zoomedItem.keyId} · ${zoomedItem.defectName || 'Defect Visual'} · VP${zoomVp}`}
              maxHeight="520px"
            />

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px' }}>
              <div>
                <span className="text-xs text-muted">Product:</span>
                <div><strong className="mono-code">{zoomedItem.keyId}</strong></div>
              </div>
              <div>
                <span className="text-xs text-muted">Panel ID:</span>
                <div><strong className="mono-code">{zoomedItem.panelId || '—'}</strong></div>
              </div>
              <div>
                <span className="text-xs text-muted">Category:</span>
                <div>
                  <span className="tag-badge" style={{ background: zoomedItem.anomalyCategory === 'Overkill' ? '#f59e0b' : '#ef4444', color: '#fff' }}>
                    {zoomedItem.anomalyCategory}
                  </span>
                </div>
              </div>
              <div>
                <span className="text-xs text-muted">Defect & Family:</span>
                <div><strong>{zoomedItem.defectName}</strong> ({zoomedItem.defectFamily})</div>
              </div>
              <div>
                <span className="text-xs text-muted">Coordinates:</span>
                <div className="font-mono">{zoomedItem.coordinates || '—'}</div>
              </div>
              <div>
                <span className="text-xs text-muted">Classifier Rule:</span>
                <div className="font-mono text-xs">{zoomedItem.ruleCode} v{zoomedItem.ruleVersion}</div>
              </div>
            </div>

            <div style={{ background: 'rgba(255,255,255,0.03)', padding: '12px', borderRadius: '6px' }}>
              <span className="text-xs text-muted">Decision Explanation from Rule Classifier Engine:</span>
              <p style={{ marginTop: '4px', fontSize: '13px', color: '#cbd5e1' }}>{zoomedItem.decisionReason}</p>
            </div>

            <div className="actions" style={{ justifyContent: 'flex-end' }}>
              <button
                type="button"
                className="secondary"
                onClick={() => {
                  const it = zoomedItem
                  setZoomedItem(null)
                  void handleInspectDefect(it, zoomVp)
                }}
              >
                🔬 Inspect Features & JSON
              </button>
              <button type="button" className="ghost-button" onClick={() => setZoomedItem(null)}>
                Close
              </button>
            </div>
          </div>
        </Modal>
        )
      })()}

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
