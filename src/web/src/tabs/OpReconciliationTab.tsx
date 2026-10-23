import { memo, useCallback, useEffect, useMemo, useState } from 'react'
import ReactECharts from '../components/ReactECharts'
import { opReconciliationApi } from '../api'
import { useConfirm } from '../confirm'
import { formatDateTime, formatPercent } from '../format'
import { Button, DebouncedInput, Field, Icon, MetricCard, Modal, StatusPill } from '../ui'
import type {
  Notice,
  OpDefectMappingDto,
  ClsfPackageAnalysisResponse,
  OpReconciliationAnalysisResponse,
  OpReconciliationRecordsResponse,
  SaveOpDefectMappingRequest,
} from '../types'

type ReconciliationSubView = 'records' | 'breakdown' | 'deepdive' | 'mapping'

const CONCEPT_SAMPLE_DATA = `FOG ID\tDefect Name\tJudge
FOG ID\tDefect Name\tJudge
A0E7670S19B134214228\tFOP bonding dent\tNG
A0B265KC29B125613228\tPOL damage\tNG
A0AE65D318A125713228\tB/P dent\tNG
A0DH65DB16B125713228\tCOP bonding foreign particle\tNG
A0AJ65DB16B125713228\tPOL dent\tNG
A0D9670P15B134314228\tFOP bonding dent\tNG
A0B7670P15B134314228\tFOP bonding dent\tNG
A0B6670P15B134314228\tFOP bonding dent\tNG
A0E5670P15B134314228\tFOP bonding dent\tNG`

type OpRecord = NonNullable<OpReconciliationRecordsResponse['items']>[number]

function classificationStyle(value: string) {
  switch (value) {
    case 'Real Defect': return { background: '#ede9fe', color: '#6d28d9', border: '1px solid #c4b5fd' }
    case 'Overkill': return { background: '#fef3c7', color: '#b45309', border: '1px solid #fcd34d' }
    case 'Leakage': return { background: '#fee2e2', color: '#b91c1c', border: '1px solid #fca5a5' }
    case 'Consistent OK': return { background: '#d1fae5', color: '#047857', border: '1px solid #6ee7b7' }
    case 'No Detect': return { background: '#e0f2fe', color: '#0369a1', border: '1px solid #7dd3fc' }
    default: return { background: '#e2e8f0', color: '#475569', border: '1px solid #cbd5e1' }
  }
}

/** The ledger rows. Memoised: typing in the search box or changing a filter does not redraw a hundred rows. */
const RecordRows = memo(function RecordRows({ items, offset }: { items?: OpRecord[]; offset: number }) {
  return (
    <>
      {(items ?? []).map((row, index) => (
                        <tr key={row.id}>
                          <td className="text-muted">{offset + index + 1}</td>
                          <td className="font-mono" style={{ fontWeight: 700 }}>{row.keyId}</td>
                          <td>
                            <div>{row.opDefectName || '—'}</div>
                            {row.mappedMachineDefect && (
                              <div className="text-xs text-muted">→ {row.mappedMachineDefect} · {row.mappedDefectFamily || 'Unclassified'}</div>
                            )}
                          </td>
                          <td><StatusPill value={row.opJudge} /></td>
                          <td>{row.productFound ? <StatusPill value={row.productJudge || 'Unknown'} /> : <span className="text-muted">Not Found</span>}</td>
                          <td style={{ minWidth: 160 }}>
                            <div>{row.productNameDefect || ''}</div>
                            {row.productDefectName && row.productDefectName !== row.productNameDefect && (
                              <div className="text-xs text-muted">DefectName: {row.productDefectName}</div>
                            )}
                          </td>
                          <td style={{ minWidth: 260 }}>
                            {row.defects.length > 0 ? (
                              <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                                {row.defects.map((defect) => (
                                  <span
                                    key={defect.id}
                                    className="tag-badge"
                                    title={`Type: ${defect.defectType || '—'} · Table: ${defect.defectTable || '—'} · Severity: ${defect.severity || '—'}`}
                                  >
                                    {defect.defectName}
                                    <span className="text-muted"> · {defect.defectType || defect.defectTable}</span>
                                  </span>
                                ))}
                              </div>
                            ) : null}
                          </td>
                          <td>
                            <span className="tag-badge" style={{ ...classificationStyle(row.classification), fontWeight: 700 }}>
                              {row.classification}
                            </span>
                          </td>
                          <td>
                            <div>{row.productMachineCode || row.machineCode || '—'}</div>
                            <div className="text-xs text-muted">{row.productLineCode || row.lineCode || '—'}</div>
                          </td>
                          <td className="text-xs">{formatDateTime(row.opInspectedAtUtc)}</td>
                          <td className="font-mono text-xs" title={row.batchId}>{row.batchId.slice(0, 8)}</td>
                        </tr>
      ))}
    </>
  )
})

export function OpReconciliationTab({
  onNotice,
  initialMachine = 'All',
  initialLine = 'All',
  mode = 'all',
}: {
  onNotice: (notice: Notice) => void
  initialMachine?: string
  initialLine?: string
  mode?: 'all' | 'records' | 'analysis'
}) {
  const [subView, setSubView] = useState<ReconciliationSubView>(mode === 'analysis' ? 'breakdown' : 'records')
  const [machineCode, setMachineCode] = useState(initialMachine)
  const [lineCode, setLineCode] = useState(initialLine)
  const [appliedMachine, setAppliedMachine] = useState(initialMachine)
  const [appliedLine, setAppliedLine] = useState(initialLine)
  const [targetDefect, setTargetDefect] = useState('All')

  // Analysis Data
  const [analysis, setAnalysis] = useState<OpReconciliationAnalysisResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [records, setRecords] = useState<OpReconciliationRecordsResponse | null>(null)
  const [recordsLoading, setRecordsLoading] = useState(false)
  const [recordsSearch, setRecordsSearch] = useState('')
  const [appliedRecordsSearch, setAppliedRecordsSearch] = useState('')
  const [recordsClassification, setRecordsClassification] = useState('All')
  const [recordsPage, setRecordsPage] = useState(1)
  const [recordsPageSize, setRecordsPageSize] = useState(50)
  const [donutView, setDonutView] = useState<'all' | 'overkill'>('all')

  // Upload Excel state
  const [uploadResultMsg] = useState<string | null>(null)

  // Paste Clipboard state
  const [showPasteModal, setShowPasteModal] = useState(false)
  const [pasteText, setPasteText] = useState('')
  const [pasteMachine, setPasteMachine] = useState(initialMachine !== 'All' ? initialMachine : 'CP_AOI')
  const [pasteDefaultJudge, setPasteDefaultJudge] = useState('NG')
  const [pasting, setPasting] = useState(false)

  // Mapping Dictionary state
  const [mappings, setMappings] = useState<OpDefectMappingDto[]>([])
  const [mappingSearch, setMappingSearch] = useState('')
  const [loadingMappings, setLoadingMappings] = useState(false)
  const [showMappingModal, setShowMappingModal] = useState(false)
  const [showBulkMappingModal, setShowBulkMappingModal] = useState(false)
  const [mappingMachine, setMappingMachine] = useState('CP_AOI')
  const [mappingPackage, setMappingPackage] = useState('CP AOI Default')
  const [bulkMappingText, setBulkMappingText] = useState('')
  const [bulkSaving, setBulkSaving] = useState(false)
  const [clsfAnalyzing, setClsfAnalyzing] = useState(false)
  const [clsfAnalysis, setClsfAnalysis] = useState<ClsfPackageAnalysisResponse | null>(null)
  const [clsfModel, setClsfModel] = useState('')
  const [editingMapping, setEditingMapping] = useState<OpDefectMappingDto | null>(null)
  const [mappingForm, setMappingForm] = useState<SaveOpDefectMappingRequest>({
    machineCode: 'CP_AOI',
    packageName: 'CP AOI Default',
    opDefectName: '',
    machineDefectName: '',
    defectFamily: 'Optical',
    componentGroup: '',
    isConfirmedDefect: true,
    description: '',
  })

  const parsedBulkMappings = useMemo<SaveOpDefectMappingRequest[]>(() => {
    return bulkMappingText
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
      .map((line) => {
        const cols = line.split(/\t|\s{2,}/).map((x) => x.trim()).filter(Boolean)
        if (cols.length < 2) return null
        const [opDefectName, defectFamily = 'Optical', componentGroup = '', machineDefectName = ''] = cols
        return {
          machineCode: mappingMachine,
          packageName: mappingPackage,
          opDefectName,
          defectFamily,
          componentGroup,
          machineDefectName: machineDefectName || componentGroup || defectFamily,
          isConfirmedDefect: true,
          description: `Bulk package: ${mappingPackage}`,
        } as SaveOpDefectMappingRequest
      })
      .filter((x): x is NonNullable<typeof x> => x !== null && Boolean(x.opDefectName && x.machineDefectName))
  }, [bulkMappingText, mappingMachine, mappingPackage])

  // Real-time parser for Pasted OP Clipboard data
  type ParsedPasteRow = {
    keyId: string
    opDefectName: string
    judge: string
    matchedMapping?: OpDefectMappingDto
  }

  const parsedPasteRows = useMemo<ParsedPasteRow[]>(() => {
    if (!pasteText.trim()) return []
    const lines = pasteText.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
    if (lines.length === 0) return []

    const splitLine = (l: string) => {
      if (l.includes('\t')) return l.split('\t').map((x) => x.trim())
      if (l.includes(',')) return l.split(',').map((x) => x.trim())
      if (l.includes(';')) return l.split(';').map((x) => x.trim())
      return l.split(/\s{2,}/).map((x) => x.trim())
    }

    const isHeaderToken = (val?: string) => {
      if (!val) return false
      const u = val.toUpperCase()
      return (
        u === 'FOG ID' ||
        u === 'FOG_ID' ||
        u === 'FOGID' ||
        u === 'KEYID' ||
        u === 'KEY_ID' ||
        u === 'BARCODE' ||
        u === 'SERIAL' ||
        u === 'PANELID' ||
        u === 'PANEL_ID' ||
        u === 'PRODUCT_ID' ||
        u === 'ID' ||
        u === 'STT' ||
        u === 'NO' ||
        u.includes('TÊN LỖI') ||
        u.includes('TEN LOI') ||
        u.includes('불량명') ||
        u.includes('PHÁN ĐỊNH') ||
        u.includes('PHAN DINH') ||
        u.includes('판정') ||
        u.includes('JUDGE')
      )
    }

    let keyCol = 0
    let defectCol = 1
    let judgeCol = 2
    let startIndex = 0

    const line0Parts = splitLine(lines[0])
    const line0IsHeader = line0Parts.some((p) => isHeaderToken(p))
    if (line0IsHeader) {
      line0Parts.forEach((p, idx) => {
        const u = p.toUpperCase()
        if (u.includes('FOG') || u.includes('KEY') || u.includes('BARCODE') || u.includes('SERIAL') || u === 'ID') keyCol = idx
        else if (u.includes('TÊN LỖI') || u.includes('TEN LOI') || u.includes('불량명') || u.includes('DEFECT') || u.includes('LỖI')) defectCol = idx
        else if (u.includes('PHÁN ĐỊNH') || u.includes('PHAN DINH') || u.includes('판정') || u.includes('JUDGE') || u.includes('RESULT') || u.includes('STATUS')) judgeCol = idx
      })
      startIndex = 1

      // Check dual header (Vietnamese on line 0, Korean on line 1)
      if (lines.length > 1) {
        const line1Parts = splitLine(lines[1])
        if (line1Parts.some((p) => isHeaderToken(p))) {
          startIndex = 2
        }
      }
    }

    const rows: ParsedPasteRow[] = []
    for (let i = startIndex; i < lines.length; i++) {
      const parts = splitLine(lines[i])
      if (parts.length === 0) continue
      const keyId = parts[keyCol] || parts[0]
      if (!keyId || isHeaderToken(keyId)) continue
      const numericKey = Number(keyId)
      if (/^\d+$/.test(keyId) && Number.isFinite(numericKey) && numericKey < 10) continue

      const opDefectName = (defectCol >= 0 && parts[defectCol]) || parts[1] || ''
      const judgeRaw = (judgeCol >= 0 && parts[judgeCol]) || parts[2] || pasteDefaultJudge || 'NG'
      const normalizedJudge = ['OK', 'PASS', 'G', 'GOOD', '양호', '정상', 'ĐẠT', 'DAT'].includes(judgeRaw.toUpperCase()) ? 'OK' : 'NG'

      // Match against known mappings
      const cleanOp = opDefectName.replace(/\s+/g, ' ').toLowerCase()
      const matched = mappings.find((m) => {
        const mClean = m.opDefectName.replace(/\s+/g, ' ').toLowerCase()
        return mClean === cleanOp
      })

      rows.push({
        keyId,
        opDefectName,
        judge: normalizedJudge,
        matchedMapping: matched,
      })
    }

    return rows
  }, [pasteText, pasteDefaultJudge, mappings])

  const pasteStats = useMemo(() => {
    const total = parsedPasteRows.length
    const ngCount = parsedPasteRows.filter((r) => r.judge === 'NG').length
    const okCount = parsedPasteRows.filter((r) => r.judge === 'OK').length
    const mappedCount = parsedPasteRows.filter((r) => Boolean(r.matchedMapping)).length
    const uniqueKeys = new Set(parsedPasteRows.map((r) => r.keyId)).size
    return { total, ngCount, okCount, mappedCount, uniqueKeys }
  }, [parsedPasteRows])

  async function handleReadFromClipboard() {
    try {
      if (!navigator.clipboard?.readText) {
        onNotice({ tone: 'info', message: 'Browser does not support automatic clipboard read or permissions are restricted. Please press Ctrl+V into the input box.' })
        return
      }
      const text = await navigator.clipboard.readText()
      if (!text.trim()) {
        onNotice({ tone: 'info', message: 'Clipboard is empty.' })
        return
      }
      setPasteText(text)
      onNotice({ tone: 'success', message: 'Pasted data from Clipboard successfully!' })
    } catch {
      onNotice({ tone: 'info', message: 'Please press Ctrl+V directly into the input area.' })
    }
  }

  async function handleSavePastedRecords() {
    if (parsedPasteRows.length === 0) {
      onNotice({ tone: 'error', message: 'No valid data to save. Please review input.' })
      return
    }

    setPasting(true)
    try {
      const res = await opReconciliationApi.pasteRecords({
        text: pasteText,
        machineCode: pasteMachine || undefined,
        defaultJudge: pasteDefaultJudge,
      })
      onNotice({
        tone: res.rejectedCount > 0 && res.totalRecordsSaved === 0 ? 'error' : 'success',
        message: res.message || `OP review: ${res.createdCount} created, ${res.updatedCount} updated, ${res.rejectedCount} marked No Detect.`,
      })
      setShowPasteModal(false)
      setPasteText('')
      await loadAnalysis()
      await loadRecords()
      await loadMappings()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to save pasted data.' })
    } finally {
      setPasting(false)
    }
  }

  // Load Analysis
  const loadAnalysis = useCallback(async () => {
    setLoading(true)
    try {
      const res = await opReconciliationApi.getAnalysis({
        machineCode: appliedMachine === 'All' ? undefined : appliedMachine,
        lineCode: appliedLine === 'All' ? undefined : appliedLine,
        targetDefect: targetDefect === 'All' ? undefined : targetDefect,
      })
      setAnalysis(res)
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to load OP reconciliation analysis.' })
    } finally {
      setLoading(false)
    }
  }, [appliedMachine, appliedLine, targetDefect, onNotice])

  const loadRecords = useCallback(async () => {
    setRecordsLoading(true)
    try {
      const res = await opReconciliationApi.getRecords({
        search: appliedRecordsSearch || undefined,
        machineCode: appliedMachine === 'All' ? undefined : appliedMachine,
        lineCode: appliedLine === 'All' ? undefined : appliedLine,
        classification: recordsClassification === 'All' ? undefined : recordsClassification,
        page: recordsPage,
        pageSize: recordsPageSize,
      })
      setRecords(res)
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to load unified OP reconciliation records.' })
    } finally {
      setRecordsLoading(false)
    }
  }, [appliedRecordsSearch, appliedMachine, appliedLine, recordsClassification, recordsPage, recordsPageSize, onNotice])

  // Load Mappings
  const loadMappings = useCallback(async () => {
    setLoadingMappings(true)
    try {
      const list = await opReconciliationApi.listMappings(undefined, mappingMachine === 'All' ? undefined : mappingMachine, mappingPackage === 'All' ? undefined : mappingPackage)
      setMappings(list)
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to load OP defect mappings.' })
    } finally {
      setLoadingMappings(false)
    }
  }, [onNotice, mappingMachine, mappingPackage])

  async function handleBulkSaveMappings() {
    if (!mappingMachine.trim() || !mappingPackage.trim() || parsedBulkMappings.length === 0) {
      onNotice({ tone: 'error', message: 'Enter Machine, package name, and paste at least one valid mapping row.' })
      return
    }
    setBulkSaving(true)
    try {
      const saved = await opReconciliationApi.batchSaveMappings(parsedBulkMappings, mappingMachine, mappingPackage)
      onNotice({ tone: 'success', message: `Saved ${saved.length} mappings into package '${mappingPackage}' for machine ${mappingMachine}.` })
      setShowBulkMappingModal(false)
      setBulkMappingText('')
      await loadMappings()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Bulk mapping import failed.' })
    } finally {
      setBulkSaving(false)
    }
  }

  async function handleClsfUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setClsfAnalyzing(true)
    try {
      const result = await opReconciliationApi.analyzeClsf(file, mappingMachine === 'All' ? undefined : mappingMachine, clsfModel || undefined, mappingPackage)
      setClsfAnalysis(result)
      setMappingMachine(result.machineCode)
      setMappingPackage(result.packageName)
      setClsfModel(result.modelCode)
      onNotice({ tone: 'success', message: result.message })
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'CLSF analysis failed.' })
    } finally {
      setClsfAnalyzing(false)
      e.target.value = ''
    }
  }

  useEffect(() => {
    void loadAnalysis()
  }, [loadAnalysis])

  useEffect(() => {
    void loadRecords()
  }, [loadRecords])

  useEffect(() => {
    if (subView === 'mapping') {
      void loadMappings()
    }
  }, [subView, loadMappings])

  // Handle Save Mapping
  async function handleSaveMapping() {
    if (!mappingForm.opDefectName.trim() || !mappingForm.machineDefectName.trim()) {
      onNotice({ tone: 'error', message: 'Both OP defect name and Machine defect name are required.' })
      return
    }

    try {
      if (editingMapping) {
        await opReconciliationApi.updateMapping(editingMapping.id, mappingForm)
        onNotice({ tone: 'success', message: `✓ Updated defect mapping [${mappingForm.opDefectName}] successfully!` })
      } else {
        await opReconciliationApi.createMapping(mappingForm)
        onNotice({ tone: 'success', message: `✓ Created defect mapping [${mappingForm.opDefectName}] successfully!` })
      }
      setShowMappingModal(false)
      setEditingMapping(null)
      await loadMappings()
      await loadAnalysis()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to save mapping.' })
    }
  }

  // Handle Delete Mapping
  const confirm = useConfirm()

  async function handleDeleteMapping(id: string, name: string) {
    const ok = await confirm({
      title: `Delete defect mapping ${name}?`,
      message: 'Reconciliation will no longer use this mapping. This cannot be undone.',
      confirmLabel: 'Delete mapping',
      tone: 'danger',
    })
    if (!ok) return
    try {
      await opReconciliationApi.deleteMapping(id)
      onNotice({ tone: 'success', message: `✓ Deleted defect mapping [${name}].` })
      await loadMappings()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to delete mapping.' })
    }
  }

  const filteredMappings = useMemo(() => {
    if (!mappingSearch.trim()) return mappings
    const s = mappingSearch.toLowerCase()
    return mappings.filter(
      (m) =>
        m.opDefectName.toLowerCase().includes(s) ||
        m.machineDefectName.toLowerCase().includes(s) ||
        m.defectFamily.toLowerCase().includes(s) ||
        m.componentGroup.toLowerCase().includes(s) ||
        m.machineCode.toLowerCase().includes(s) ||
        m.packageName.toLowerCase().includes(s)
    )
  }, [mappings, mappingSearch])

  const reconciliationDonutOption = useMemo(() => {
    const total = records?.totalCount ?? 0
    const realDefect = records?.realDefectCount ?? 0
    const overkill = records?.overkillCount ?? 0
    const leakage = records?.leakageCount ?? 0
    const consistentOk = records?.consistentOkCount ?? 0
    const noDetect = records?.noDetectCount ?? 0
    const notFound = records?.notFoundCount ?? 0
    const machineNg = realDefect + overkill

    if (donutView === 'overkill') {
      return {
        title: {
          text: 'Machine NG: Overkill vs Real Defect',
          subtext: `${machineNg.toLocaleString()} Machine NG evaluated with OP reviews`,
          left: 'center',
          top: 4,
          textStyle: { fontSize: 13, fontWeight: 700, color: '#0f172a' },
          subtextStyle: { fontSize: 11, color: '#64748b' },
        },
        tooltip: {
          trigger: 'item',
          formatter: (p: any) =>
            `<strong>${p.name}</strong><br/>Count: <b>${Number(p.value).toLocaleString()}</b><br/>Share in Machine NG: <b>${p.percent}%</b>`,
        },
        legend: {
          bottom: 2,
          textStyle: { color: '#475569', fontSize: 11, fontWeight: 500 },
          itemWidth: 12,
          itemHeight: 12,
        },
        graphic: machineNg > 0 ? [{
          type: 'text',
          left: 'center',
          top: '44%',
          style: {
            text: `${formatPercent(records?.overkillRate ?? 0)}\nOverkill Rate`,
            textAlign: 'center',
            fill: '#b45309',
            fontSize: 15,
            fontWeight: 800,
            lineHeight: 20,
          },
        }] : [{
          type: 'text',
          left: 'center',
          top: '44%',
          style: {
            text: '0 Machine NG',
            textAlign: 'center',
            fill: '#94a3b8',
            fontSize: 13,
            fontWeight: 600,
          },
        }],
        series: [{
          name: 'Machine NG Ratio',
          type: 'pie',
          radius: ['52%', '76%'],
          center: ['50%', '48%'],
          avoidLabelOverlap: true,
          itemStyle: { borderRadius: 6, borderColor: '#ffffff', borderWidth: 2 },
          label: {
            color: '#334155',
            fontSize: 11,
            fontWeight: 600,
            formatter: '{b}\n{d}%',
          },
          data: machineNg > 0 ? [
            { value: overkill, name: 'Overkill (False Alarm)', itemStyle: { color: '#f59e0b' } },
            { value: realDefect, name: 'Real Defect (Confirmed)', itemStyle: { color: '#8b5cf6' } },
          ] : [{ value: 0, name: 'No Machine NG', itemStyle: { color: '#e2e8f0' } }],
        }],
      }
    }

    const slices = [
      { value: realDefect, name: 'Real Defect', itemStyle: { color: '#8b5cf6' } },
      { value: overkill, name: 'Overkill', itemStyle: { color: '#f59e0b' } },
      { value: leakage, name: 'Leakage', itemStyle: { color: '#ef4444' } },
      { value: consistentOk, name: 'Consistent OK', itemStyle: { color: '#10b981' } },
      { value: noDetect, name: 'No Detect', itemStyle: { color: '#0369a1' } },
      { value: notFound, name: 'Product Not Found', itemStyle: { color: '#64748b' } },
    ].filter((s) => s.value > 0)

    return {
      title: {
        text: 'All Classification Breakdown',
        subtext: `${total.toLocaleString()} total OP reviews across all categories`,
        left: 'center',
        top: 4,
        textStyle: { fontSize: 13, fontWeight: 700, color: '#0f172a' },
        subtextStyle: { fontSize: 11, color: '#64748b' },
      },
      tooltip: {
        trigger: 'item',
        formatter: (p: any) =>
          `<strong>${p.name}</strong><br/>Count: <b>${Number(p.value).toLocaleString()}</b><br/>Ratio: <b>${p.percent}%</b>`,
      },
      legend: {
        bottom: 2,
        textStyle: { color: '#475569', fontSize: 11, fontWeight: 500 },
        itemWidth: 10,
        itemHeight: 10,
      },
      graphic: total > 0 ? [{
        type: 'text',
        left: 'center',
        top: '44%',
        style: {
          text: `${total.toLocaleString()}\nTotal Records`,
          textAlign: 'center',
          fill: '#0f172a',
          fontSize: 14,
          fontWeight: 800,
          lineHeight: 19,
        },
      }] : [{
        type: 'text',
        left: 'center',
        top: '44%',
        style: {
          text: 'No Records',
          textAlign: 'center',
          fill: '#94a3b8',
          fontSize: 13,
          fontWeight: 600,
        },
      }],
      series: [{
        name: 'Reconciliation Distribution',
        type: 'pie',
        radius: ['52%', '76%'],
        center: ['50%', '48%'],
        avoidLabelOverlap: true,
        itemStyle: { borderRadius: 6, borderColor: '#ffffff', borderWidth: 2 },
        label: {
          color: '#334155',
          fontSize: 11,
          fontWeight: 600,
          formatter: '{b}\n{d}%',
        },
        data: slices.length > 0 ? slices : [{ value: 0, name: 'No Data', itemStyle: { color: '#e2e8f0' } }],
      }],
    }
  }, [records, donutView])

  const rateBreakdownItems = useMemo(() => {
    const total = records?.totalCount ?? 0
    const safeTotal = Math.max(total, 1)
    return [
      {
        key: 'Real Defect',
        label: 'Real Defect',
        count: records?.realDefectCount ?? 0,
        rate: (records?.realDefectCount ?? 0) / safeTotal,
        color: '#8b5cf6',
        bg: '#ede9fe',
        border: '#c4b5fd',
        textColor: '#6d28d9',
        hint: 'Machine NG confirmed as real defect by OP',
      },
      {
        key: 'Overkill',
        label: 'Overkill',
        count: records?.overkillCount ?? 0,
        rate: (records?.overkillCount ?? 0) / safeTotal,
        color: '#f59e0b',
        bg: '#fef3c7',
        border: '#fcd34d',
        textColor: '#b45309',
        hint: 'Machine NG confirmed as OK/false alarm by OP',
      },
      {
        key: 'Leakage',
        label: 'Leakage',
        count: records?.leakageCount ?? 0,
        rate: (records?.leakageCount ?? 0) / safeTotal,
        color: '#ef4444',
        bg: '#fee2e2',
        border: '#fca5a5',
        textColor: '#b91c1c',
        hint: 'Machine OK/PASS found to have defect by OP',
      },
      {
        key: 'Consistent OK',
        label: 'Consistent OK',
        count: records?.consistentOkCount ?? 0,
        rate: (records?.consistentOkCount ?? 0) / safeTotal,
        color: '#10b981',
        bg: '#d1fae5',
        border: '#6ee7b7',
        textColor: '#047857',
        hint: 'Both Machine and OP judged as OK/PASS',
      },
      {
        key: 'No Detect',
        label: 'No Detect',
        count: records?.noDetectCount ?? 0,
        rate: (records?.noDetectCount ?? 0) / safeTotal,
        color: '#0369a1',
        bg: '#e0f2fe',
        border: '#7dd3fc',
        textColor: '#0369a1',
        hint: 'Excluded Key ID < 10 or unmarked defect',
      },
      {
        key: 'Not Found',
        label: 'Product Not Found',
        count: records?.notFoundCount ?? 0,
        rate: (records?.notFoundCount ?? 0) / safeTotal,
        color: '#64748b',
        bg: '#e2e8f0',
        border: '#cbd5e1',
        textColor: '#475569',
        hint: 'OP review has no matching Product row in DB',
      },
    ]
  }, [records])

  return (
    <div className="tab-stack">
      {/* Top Banner & File Upload Section */}
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Reconciliation & Overkill Deep-Dive</p>
            <h2>Overkill Classification & Operator Reconciliation</h2>
            <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
              Upload Operator (OP) Excel/CSV review sheets with defect mapping dictionaries to accurately separate <strong>Real Defects</strong> from <strong>Overkill</strong>, then deep-dive into feature distributions to establish Gold Thresholds.
            </p>
          </div>

          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' }}>
            <span className="tag-badge" style={{ background: '#dbeafe', color: '#1d4ed8' }}>
              Input managed at Operations → OP Review Input
            </span>
            <Button type="button" onClick={() => void loadAnalysis()} variant="ghost" loading={loading}>
              {loading ? 'Calculating...' : 'Refresh'}
            </Button>
          </div>
        </div>

        {/* Filter bar */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'center', marginTop: '12px', flexWrap: 'wrap' }}>
          <Field label="Production Machine">
            <input
              type="text"
              value={machineCode}
              onChange={(e) => setMachineCode(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  setAppliedMachine(machineCode)
                  setAppliedLine(lineCode)
                }
              }}
              placeholder="All or MACHINE-A..."
              style={{ width: '160px' }}
            />
          </Field>
          <Field label="Production Line">
            <input
              type="text"
              value={lineCode}
              onChange={(e) => setLineCode(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  setAppliedMachine(machineCode)
                  setAppliedLine(lineCode)
                }
              }}
              placeholder="All or LINE-1..."
              style={{ width: '160px' }}
            />
          </Field>
          <button
            type="button"
            className="ghost-button compact"
            style={{ marginTop: 'auto', marginBottom: '2px' }}
            onClick={() => {
              setAppliedMachine(machineCode)
              setAppliedLine(lineCode)
            }}
            disabled={loading}
          >
            Apply Filters
          </button>
        </div>

        {uploadResultMsg && (
          <div
            style={{
              marginTop: '10px',
              padding: '8px 12px',
              borderRadius: '4px',
              background: 'rgba(16, 185, 129, 0.15)',
              border: '1px solid #10b981',
              color: '#10b981',
              fontSize: '12px',
            }}
          >
            {uploadResultMsg}
          </div>
        )}
      </section>

      {/* KPI Metrics belong to the dedicated OP analysis page. */}
      {mode !== 'records' && <section className="metrics-grid">
        <MetricCard
          label="All OP Records"
          value={records?.totalCount.toLocaleString() ?? 0}
          hint={`${records?.matchedCount.toLocaleString() ?? 0} matched Product`}
        />
        <MetricCard
          label="Product Not Found"
          value={records?.notFoundCount.toLocaleString() ?? 0}
          hint="Excluded from ratio"
          tone="danger"
        />
        <MetricCard
          label="Real Defect"
          value={records?.realDefectCount.toLocaleString() ?? 0}
          hint={`Rate: ${formatPercent(records?.realDefectRate ?? 0)}`}
          tone="info"
        />
        <MetricCard
          label="Overkill (False Alarm)"
          value={records?.overkillCount.toLocaleString() ?? 0}
          hint={`Rate: ${formatPercent(records?.overkillRate ?? 0)}`}
          tone="warning"
        />
        <MetricCard
          label="Defect Escapes (Leakage)"
          value={records?.leakageCount.toLocaleString() ?? 0}
          hint={`${records?.consistentOkCount.toLocaleString() ?? 0} consistent OK`}
          tone={records && records.leakageCount > 0 ? 'danger' : 'success'}
        />
      </section>}

      {/* Sub-View Tabs Navigation */}
      {mode !== 'records' && <div className="module-tabs-bar" style={{ margin: '8px 0 16px 0' }}>
        <div className="module-tabs-list" role="tablist">
          {mode === 'all' && <button
            type="button"
            role="tab"
            className={`module-tab-item ${subView === 'records' ? 'active' : ''}`}
            onClick={() => setSubView('records')}
          >
            <span><Icon name="clipboard" className="icon inline-icon" />All OP + Product Records</span>
            <span className="tag-badge" style={{ background: '#2563eb', color: '#fff', marginLeft: '6px', fontSize: '12px' }}>
              {records?.totalCount ?? 0}
            </span>
          </button>}
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${subView === 'breakdown' ? 'active' : ''}`}
            onClick={() => setSubView('breakdown')}
          >
            <span><Icon name="analysis" className="icon inline-icon" />Defect Breakdown: Real Defect vs Overkill</span>
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${subView === 'deepdive' ? 'active' : ''}`}
            onClick={() => setSubView('deepdive')}
          >
            <span><Icon name="search" className="icon inline-icon" />Overkill Deep-Dive & Gold Thresholds</span>
            {analysis?.deepDive?.overkillCount ? (
              <span className="tag-badge" style={{ background: '#f59e0b', color: '#000', marginLeft: '6px', fontSize: '12px' }}>
                {analysis.deepDive.overkillCount} Overkill
              </span>
            ) : null}
          </button>
          <button
            type="button"
            role="tab"
            className={`module-tab-item ${subView === 'mapping' ? 'active' : ''}`}
            onClick={() => setSubView('mapping')}
          >
            <span><Icon name="book" className="icon inline-icon" />Defect Mapping Dictionary (OP → Machine AOI)</span>
            <span className="tag-badge" style={{ background: '#3b82f6', color: '#fff', marginLeft: '6px', fontSize: '12px' }}>
              {mappings.length || 'Dictionary'}
            </span>
          </button>
        </div>
      </div>}

      {/* VIEW 0: UNIFIED OP RECORDS + PRODUCT RECONCILIATION */}
      {(mode === 'records' || subView === 'records') && (
        <div className="tab-stack">
          {/* 1. Rate KPI Report Cards */}
          <section className="metrics-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 10 }}>
            <MetricCard
              label="Total OP Records"
              value={records?.totalCount.toLocaleString() ?? 0}
              hint="All imported rows"
            />
            <MetricCard
              label="Product Match Rate"
              value={formatPercent(records?.totalCount ? records.matchedCount / records.totalCount : 0)}
              hint={`${records?.matchedCount.toLocaleString() ?? 0} Matched / ${records?.notFoundCount.toLocaleString() ?? 0} Not Found`}
              tone={records?.matchedCount ? 'success' : 'neutral'}
            />
            <MetricCard
              label="Overkill Rate"
              value={formatPercent(records?.overkillRate ?? 0)}
              hint={`${records?.overkillCount.toLocaleString() ?? 0} Overkill / ${((records?.realDefectCount ?? 0) + (records?.overkillCount ?? 0)).toLocaleString()} Compared NG`}
              tone="warning"
            />
            <MetricCard
              label="Real Defect Rate"
              value={formatPercent(records?.realDefectRate ?? 0)}
              hint={`${records?.realDefectCount.toLocaleString() ?? 0} Confirmed NG`}
              tone="info"
            />
            <MetricCard
              label="Defect Escapes (Leakage)"
              value={records?.leakageCount.toLocaleString() ?? 0}
              hint="Machine OK · OP NG"
              tone={records && records.leakageCount > 0 ? 'danger' : 'success'}
            />
            <MetricCard
              label="No Detect"
              value={records?.noDetectCount.toLocaleString() ?? 0}
              hint="Uninspected / Key ID < 10"
              tone="neutral"
            />
          </section>

          {/* 2. Donut Chart & Detailed Rate Report Panel */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Rate Report & Donut Visualization</p>
                <h3>All OP Records Reconciled with Product by FOG ID</h3>
                <p className="text-xs text-muted" style={{ marginTop: 4 }}>
                  Comprehensive breakdown of OP reviews against Machine AOI judgments with interactive donut analytics.
                </p>
              </div>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                <div style={{ display: 'inline-flex', background: '#f1f5f9', padding: '2px', borderRadius: '7px', border: '1px solid #cbd5e1', gap: '2px' }}>
                  <button
                    type="button"
                    style={{
                      padding: '3px 10px',
                      fontSize: '12px',
                      fontWeight: donutView === 'all' ? 700 : 500,
                      height: '26px',
                      borderRadius: '5px',
                      background: donutView === 'all' ? '#ffffff' : 'transparent',
                      color: donutView === 'all' ? '#1d4ed8' : '#475569',
                      boxShadow: donutView === 'all' ? '0 1px 2px rgba(0,0,0,0.08)' : 'none',
                      border: donutView === 'all' ? '1px solid #bfdbfe' : '1px solid transparent',
                      cursor: 'pointer',
                    }}
                    onClick={() => setDonutView('all')}
                  >
                    <Icon name="analysis" className="icon inline-icon" />All Categories
                  </button>
                  <button
                    type="button"
                    style={{
                      padding: '3px 10px',
                      fontSize: '12px',
                      fontWeight: donutView === 'overkill' ? 700 : 500,
                      height: '26px',
                      borderRadius: '5px',
                      background: donutView === 'overkill' ? '#ffffff' : 'transparent',
                      color: donutView === 'overkill' ? '#1d4ed8' : '#475569',
                      boxShadow: donutView === 'overkill' ? '0 1px 2px rgba(0,0,0,0.08)' : 'none',
                      border: donutView === 'overkill' ? '1px solid #bfdbfe' : '1px solid transparent',
                      cursor: 'pointer',
                    }}
                    onClick={() => setDonutView('overkill')}
                  >
                    <Icon name="target" className="icon inline-icon" />Machine NG (Overkill vs Real)
                  </button>
                </div>
                <Button type="button" onClick={() => void loadRecords()} variant="ghost" loading={recordsLoading}>
                  {recordsLoading ? 'Loading...' : 'Refresh'}
                </Button>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'minmax(300px, 420px) minmax(320px, 1fr)', gap: 20, alignItems: 'center', marginTop: 12 }}>
              {/* Donut Chart */}
              <div style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: 8, padding: '12px 8px', minHeight: 290, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <ReactECharts option={reconciliationDonutOption} style={{ height: 280, width: '100%' }} notMerge />
              </div>

              {/* Rate Breakdown Table / Progress List */}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 2 }}>
                  <span style={{ fontSize: '12px', fontWeight: 700, color: '#334155' }}>Classification Rate Breakdown</span>
                  {recordsClassification !== 'All' && (
                    <button
                      type="button"
                      className="ghost-button compact"
                      style={{ fontSize: '12px', color: '#2563eb' }}
                      onClick={() => { setRecordsClassification('All'); setRecordsPage(1); }}
                    >
                      Reset filter ({recordsClassification})
                    </button>
                  )}
                </div>

                {rateBreakdownItems.map((item) => {
                  const isFiltered = recordsClassification === item.key
                  return (
                    <div
                      key={item.key}
                      onClick={() => {
                        setRecordsClassification(recordsClassification === item.key ? 'All' : item.key)
                        setRecordsPage(1)
                      }}
                      style={{
                        padding: '8px 12px',
                        borderRadius: 6,
                        border: `1px solid ${isFiltered ? item.color : '#e2e8f0'}`,
                        background: isFiltered ? item.bg : '#ffffff',
                        cursor: 'pointer',
                        transition: 'all 0.15s ease',
                      }}
                      title={`Click to filter table by ${item.label}`}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                          <span style={{ width: 8, height: 8, borderRadius: '50%', background: item.color, display: 'inline-block' }} />
                          <strong style={{ fontSize: '12px', color: item.textColor }}>{item.label}</strong>
                          <span style={{ fontSize: '12px', color: '#64748b' }}>({item.hint})</span>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <span style={{ fontSize: '12px', fontWeight: 700, color: '#0f172a' }}>{item.count.toLocaleString()}</span>
                          <span
                            className="tag-badge"
                            style={{
                              background: item.bg,
                              color: item.textColor,
                              borderColor: item.border,
                              fontSize: '12px',
                              fontWeight: 700,
                              minWidth: 50,
                              textAlign: 'center',
                            }}
                          >
                            {formatPercent(item.rate)}
                          </span>
                        </div>
                      </div>
                      <div style={{ width: '100%', height: 6, borderRadius: 999, background: '#f1f5f9', overflow: 'hidden' }}>
                        <div
                          style={{
                            height: '100%',
                            width: `${Math.min(100, item.rate * 100)}%`,
                            background: item.color,
                            borderRadius: 999,
                            transition: 'width 0.25s ease',
                          }}
                        />
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          </section>

          {/* 3. Search & Filter Bar */}
          <section className="panel" style={{ padding: '12px 14px' }}>
            <form
              onSubmit={(e) => {
                e.preventDefault()
                setRecordsPage(1)
                setAppliedRecordsSearch(recordsSearch.trim())
              }}
              style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'end' }}
            >
              <Field label="Search FOG ID / Defect / Mapping">
                <DebouncedInput
                  value={recordsSearch}
                  onValueChange={setRecordsSearch}
                  placeholder="FOG ID, OP defect, Product or Defect record..."
                  style={{ minWidth: 280 }}
                />
              </Field>
              <Field label="Classification">
                <select
                  value={recordsClassification}
                  onChange={(e) => {
                    setRecordsClassification(e.target.value)
                    setRecordsPage(1)
                  }}
                >
                  <option value="All">All Classifications</option>
                  <option value="Real Defect">Real Defect</option>
                  <option value="Overkill">Overkill</option>
                  <option value="Leakage">Leakage</option>
                  <option value="Consistent OK">Consistent OK</option>
                  <option value="No Detect">No Detect (N/A / NO NG)</option>
                  <option value="Not Found">Product Not Found</option>
                </select>
              </Field>
              <button type="submit" className="primary">Search</button>
              {(appliedRecordsSearch || recordsClassification !== 'All') && (
                <button
                  type="button"
                  className="ghost-button"
                  onClick={() => {
                    setRecordsSearch('')
                    setAppliedRecordsSearch('')
                    setRecordsClassification('All')
                    setRecordsPage(1)
                  }}
                >
                  Clear Filter
                </button>
              )}
            </form>
          </section>

          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Searchable Reconciliation Ledger</p>
                <h3>OP Review × Product Result</h3>
              </div>
              <Field label="Rows / page">
                <select
                  value={recordsPageSize}
                  onChange={(e) => {
                    setRecordsPageSize(Number(e.target.value))
                    setRecordsPage(1)
                  }}
                >
                  <option value={25}>25</option>
                  <option value={50}>50</option>
                  <option value={100}>100</option>
                  <option value={250}>250</option>
                </select>
              </Field>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table">
                <thead>
                  <tr>
                    <th>#</th>
                    <th>FOG ID / KeyId</th>
                    <th>OP Defect</th>
                    <th>OP Judge</th>
                    <th>Product Judge</th>
                    <th>Product Defect</th>
                    <th>Defect Records</th>
                    <th>Classification</th>
                    <th>Machine / Line</th>
                    <th>OP Time</th>
                    <th>Batch Trace</th>
                  </tr>
                </thead>
                <tbody>
                  <RecordRows items={records?.items} offset={(recordsPage - 1) * recordsPageSize} />
                  {!recordsLoading && (records?.items.length ?? 0) === 0 && (
                    <tr>
                      <td colSpan={11} className="text-center text-muted" style={{ padding: 36 }}>
                        No matching records found. Paste or upload OP review data to begin reconciliation.
                      </td>
                    </tr>
                  )}
                  {recordsLoading && (
                    <tr><td colSpan={11} className="text-center text-muted" style={{ padding: 36 }}>Loading unified reconciliation ledger...</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'center', marginTop: 12, flexWrap: 'wrap' }}>
              <span className="text-xs text-muted">
                Showing {records?.items.length ?? 0} of {records?.totalCount ?? 0} rows · Page {records?.page ?? 1} of {Math.max(records?.totalPages ?? 0, 1)}
              </span>
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  type="button"
                  className="ghost-button compact"
                  disabled={recordsPage <= 1 || recordsLoading}
                  onClick={() => setRecordsPage((p) => Math.max(1, p - 1))}
                >
                  ← Previous
                </button>
                <button
                  type="button"
                  className="ghost-button compact"
                  disabled={recordsLoading || recordsPage >= (records?.totalPages ?? 0)}
                  onClick={() => setRecordsPage((p) => p + 1)}
                >
                  Next →
                </button>
              </div>
            </div>
          </section>
        </div>
      )}

      {/* VIEW 1: DEFECT BREAKDOWN & RECONCILIATION */}
      {mode !== 'records' && subView === 'breakdown' && (
        <div className="tab-stack">
          {/* Recommendations Card */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Reconciliation Insights</p>
                <h3>Overkill Mitigation Recommendations</h3>
              </div>
            </div>
            <ul className="guidance-list">
              {(analysis?.recommendations ?? ['No recommendations available. Upload OP Excel data to reconcile.']).map((r, i) => (
                <li key={i}>
                  <strong><Icon name="info" className="icon inline-icon" /></strong> {r}
                </li>
              ))}
            </ul>
          </section>

          {/* Breakdown Table */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Breakdown by Machine Defect Class</p>
                <h3>Real Defect vs Overkill Ratios Mapped from Operator Evaluations</h3>
              </div>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Machine Defect Name</th>
                    <th>Family</th>
                    <th>Machine NG</th>
                    <th>Real Defect</th>
                    <th>Overkill</th>
                    <th>Overkill Rate (%)</th>
                    <th>Mapped OP Defect Names</th>
                    <th>Assessment & Action</th>
                    <th style={{ textAlign: 'right' }}>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {(analysis?.defectBreakdown ?? []).map((item) => (
                    <tr key={item.machineDefectName}>
                      <td className="font-bold" style={{ color: '#60a5fa' }}>{item.machineDefectName}</td>
                      <td><StatusPill value={item.defectFamily} /></td>
                      <td><strong>{item.totalMachineNg.toLocaleString()}</strong></td>
                      <td style={{ color: '#a855f7', fontWeight: 600 }}>{item.realDefectCount}</td>
                      <td style={{ color: '#f59e0b', fontWeight: 600 }}>{item.overkillCount}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: item.overkillRate >= 0.5 ? 'rgba(239, 68, 68, 0.2)' : 'rgba(245, 158, 11, 0.15)',
                            color: item.overkillRate >= 0.5 ? '#ef4444' : '#f59e0b',
                            fontWeight: 700,
                          }}
                        >
                          {formatPercent(item.overkillRate)}
                        </span>
                      </td>
                      <td className="text-xs font-mono" style={{ maxWidth: 220 }}>
                        {item.associatedOpDefectNames.length > 0 ? item.associatedOpDefectNames.join(', ') : '—'}
                      </td>
                      <td className="text-xs" style={{ maxWidth: 300 }}>{item.overkillSeverityAssessment}</td>
                      <td style={{ textAlign: 'right' }}>
                        <button
                          type="button"
                          className="secondary compact"
                          style={{ fontSize: '12px', whiteSpace: 'nowrap' }}
                          onClick={() => {
                            setTargetDefect(item.machineDefectName)
                            setSubView('deepdive')
                          }}
                        >
                          <Icon name="search" className="icon inline-icon" />Deep-Dive →
                        </button>
                      </td>
                    </tr>
                  ))}
                  {(!analysis?.defectBreakdown || analysis.defectBreakdown.length === 0) && (
                    <tr>
                      <td colSpan={9} className="text-center text-muted" style={{ padding: '36px' }}>
                        No reconciliation records available. Upload an Excel file or click <strong><Icon name="star" className="icon inline-icon" />Seed Sample Data</strong>.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}

      {/* VIEW 2: OVERKILL DEEP DIVE */}
      {mode !== 'records' && subView === 'deepdive' && (
        <div className="tab-stack">
          {/* Target defect selector */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Feature Separation & Threshold Tuning</p>
                <h2>Deep-Dive Feature Analysis: [{analysis?.deepDive?.targetDefectName || 'None Selected'}]</h2>
                <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
                  Compare feature distributions (Area, Width, Height, GrayMin, DL Confidence...) between <strong>Overkill</strong> and <strong>Real Defect</strong> samples to establish Gold Thresholds.
                </p>
              </div>

              <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                <Field label="Target Defect Class">
                  <select
                    value={targetDefect}
                    onChange={(e) => setTargetDefect(e.target.value)}
                    style={{ minWidth: 180 }}
                  >
                    <option value="All">-- Select Defect --</option>
                    {(analysis?.availableDefectTypes ?? []).map((t) => (
                      <option key={t} value={t}>
                        {t}
                      </option>
                    ))}
                  </select>
                </Field>
              </div>
            </div>

            {/* Threshold recommendations */}
            {analysis?.deepDive?.thresholdTuningRecommendations && analysis.deepDive.thresholdTuningRecommendations.length > 0 && (
              <div
                style={{
                  marginTop: '12px',
                  background: 'rgba(59, 130, 246, 0.1)',
                  border: '1px solid rgba(59, 130, 246, 0.3)',
                  borderRadius: '6px',
                  padding: '12px',
                }}
              >
                <strong style={{ color: '#93c5fd', fontSize: '13px' }}><Icon name="target" className="icon inline-icon" />Recommended Gold Thresholds:</strong>
                <ul style={{ margin: '6px 0 0 16px', fontSize: '12px', color: '#cbd5e1' }}>
                  {analysis.deepDive.thresholdTuningRecommendations.map((rec, i) => (
                    <li key={i} style={{ marginBottom: '4px' }}>
                      {rec}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>

          {/* Feature Distribution Table */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Feature Distribution Comparison</p>
                <h3>Measured Feature Statistics: Overkill vs Real Defect</h3>
              </div>
            </div>

            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>Feature Parameter</th>
                    <th style={{ color: '#f59e0b' }}>Overkill Mean</th>
                    <th style={{ color: '#f59e0b' }}>Overkill [Min - Max]</th>
                    <th style={{ color: '#a855f7' }}>Real Defect Mean</th>
                    <th style={{ color: '#a855f7' }}>Real Defect [Min - Max]</th>
                    <th>Separation Assessment</th>
                  </tr>
                </thead>
                <tbody>
                  {(analysis?.deepDive?.featureDistributions ?? []).map((f) => {
                    const isGold = f.separationAssessment.includes('Completely Separated')
                    return (
                      <tr
                        key={f.featureName}
                        style={{
                          background: isGold ? 'rgba(16, 185, 129, 0.08)' : undefined,
                        }}
                      >
                        <td className="font-mono font-bold" style={{ color: isGold ? '#34d399' : '#e2e8f0' }}>
                          {isGold && ''}
                          {f.featureName}
                        </td>
                        <td className="font-mono" style={{ color: '#f59e0b' }}>{f.overkillMean.toFixed(1)}</td>
                        <td className="font-mono text-xs text-muted">[{f.overkillMin} - {f.overkillMax}]</td>
                        <td className="font-mono" style={{ color: '#a855f7' }}>{f.realDefectMean.toFixed(1)}</td>
                        <td className="font-mono text-xs text-muted">[{f.realDefectMin} - {f.realDefectMax}]</td>
                        <td className="text-xs" style={{ maxWidth: 360 }}>
                          <span
                            style={{
                              color: isGold ? '#10b981' : '#cbd5e1',
                              fontWeight: isGold ? 700 : 400,
                            }}
                          >
                            {f.separationAssessment}
                          </span>
                        </td>
                      </tr>
                    )
                  })}
                  {(!analysis?.deepDive?.featureDistributions || analysis.deepDive.featureDistributions.length === 0) && (
                    <tr>
                      <td colSpan={6} className="text-center text-muted" style={{ padding: '24px' }}>
                        No feature distributions available for this defect class.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

          {/* Overkill Samples Gallery */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Verified Overkill Observations</p>
                <h3>Products Flagged NG by Machine but Confirmed Clean / Acceptable by OP</h3>
              </div>
            </div>

            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))',
                gap: '14px',
              }}
            >
              {(analysis?.deepDive?.samples ?? []).map((sample) => (
                <div
                  key={sample.keyId}
                  className="panel"
                  style={{
                    padding: '12px',
                    border: '1px solid rgba(245, 158, 11, 0.3)',
                    background: 'rgba(245, 158, 11, 0.03)',
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                    <strong className="mono-code" style={{ color: '#f59e0b' }}>{sample.keyId}</strong>
                    <span className="tag-badge" style={{ background: 'rgba(245, 158, 11, 0.2)', color: '#f59e0b', fontSize: '12px' }}>
                      OVERKILL
                    </span>
                  </div>

                  <div style={{ fontSize: '12px', color: '#cbd5e1', marginBottom: '4px' }}>
                    Machine NG: <strong style={{ color: '#ef4444' }}>{sample.machineDefectName}</strong>
                  </div>
                  <div style={{ fontSize: '12px', color: '#cbd5e1', marginBottom: '6px' }}>
                    OP Verdict: <strong style={{ color: '#22c55e' }}>{sample.opJudge} ({sample.opDefectName || 'No defect'})</strong>
                  </div>

                  {sample.keyFeatures && Object.keys(sample.keyFeatures).length > 0 && (
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginTop: '6px' }}>
                      {Object.entries(sample.keyFeatures).slice(0, 4).map(([k, v]) => (
                        <span
                          key={k}
                          className="font-mono"
                          style={{
                            background: 'rgba(255,255,255,0.05)',
                            padding: '1px 5px',
                            borderRadius: '3px',
                            fontSize: '12px',
                            color: '#cbd5e1',
                          }}
                        >
                          {k}: <b>{typeof v === 'number' ? v.toFixed(1) : v}</b>
                        </span>
                      ))}
                    </div>
                  )}

                  <p className="text-xs text-muted" style={{ marginTop: '8px', lineHeight: 1.3 }}>
                    {sample.reason}
                  </p>
                </div>
              ))}
              {(!analysis?.deepDive?.samples || analysis.deepDive.samples.length === 0) && (
                <div className="text-muted text-xs" style={{ gridColumn: '1 / -1', padding: '16px' }}>
                  No overkill sample records available.
                </div>
              )}
            </div>
          </section>
        </div>
      )}

      {/* VIEW 3: OP DEFECT MAPPING DICTIONARY */}
      {mode !== 'records' && subView === 'mapping' && (
        <div className="tab-stack">
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">Dictionary Management</p>
                <h2>Defect Mapping Dictionary (OP Defect Name → Machine AOI Defect)</h2>
                <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
                  Translates manual operator entries from Excel review sheets into standard machine defect classes (`DIC_Crack`, `Light_Fail`, `Bridging`...), designating whether the item is a confirmed Real Defect or an Overkill false alarm.
                </p>
              </div>

              <div style={{ display: 'flex', gap: '8px' }}>
                <label className="secondary" style={{ cursor: 'pointer', display: 'inline-flex', alignItems: 'center' }}>
                  {clsfAnalyzing ? 'Analyzing CLSF...' : 'Upload CLSF & Analyze'}
                  <input type="file" accept=".clsf,.json" onChange={(e) => void handleClsfUpload(e)} disabled={clsfAnalyzing} style={{ display: 'none' }} />
                </label>
                <button type="button" className="secondary" onClick={() => setShowBulkMappingModal(true)}>
                  <Icon name="operations" className="icon inline-icon" />Bulk Paste / Machine Package
                </button>
                <button
                  type="button"
                  className="primary"
                  onClick={() => {
                    setEditingMapping(null)
                    setMappingForm({
                      machineCode: mappingMachine,
                      packageName: mappingPackage,
                      opDefectName: '',
                      machineDefectName: '',
                      defectFamily: 'Optical',
                      componentGroup: '',
                      isConfirmedDefect: true,
                      description: '',
                    })
                    setShowMappingModal(true)
                  }}
                >
                  + Add New Mapping
                </button>
              </div>
            </div>

            <div style={{ display: 'flex', gap: '12px', alignItems: 'center', marginTop: '12px' }}>
              <select value={mappingMachine} onChange={(e) => setMappingMachine(e.target.value)} style={{ maxWidth: 180 }}>
                <option value="All">All Machines</option>
                <option value="CP_AOI">CP_AOI</option>
                <option value="MCL">MCL</option>
                <option value="SPI">SPI</option>
                <option value="AOI">AOI</option>
              </select>
              <input value={mappingPackage} onChange={(e) => setMappingPackage(e.target.value)} placeholder="Mapping package name" style={{ maxWidth: 220 }} />
              <DebouncedInput
                type="text"
                placeholder="Search OP defect, machine defect, family..."
                value={mappingSearch}
                onValueChange={setMappingSearch}
                style={{ maxWidth: 360, fontSize: '13px', padding: '6px 12px' }}
              />
              <span className="text-xs text-muted">
                {loadingMappings ? 'Loading mappings...' : `${filteredMappings.length} mappings configured`}
              </span>
            </div>
          </section>

          {clsfAnalysis && (
            <section className="panel">
              <div className="section-heading">
                <div>
                  <p className="eyebrow">CLSF Recipe Occurrence Analysis</p>
                  <h2>{clsfAnalysis.packageName}</h2>
                  <p className="text-xs text-muted">Machine {clsfAnalysis.machineCode} · Model {clsfAnalysis.modelCode} · {clsfAnalysis.activeRuleCount}/{clsfAnalysis.totalRules} active rules producing alarms.</p>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <span className="tag-badge">Total defects: {clsfAnalysis.totalDefectOccurrences}</span>
                  <span className="tag-badge" style={{ background: '#047857', color: '#fff' }}>Active rules: {clsfAnalysis.activeRuleCount}</span>
                </div>
              </div>
              <div className="table-responsive" style={{ maxHeight: 480 }}>
                <table className="analytics-data-table">
                  <thead><tr><th>Priority</th><th>Rule / Error Item</th><th>VP</th><th>Status</th><th>Occurrences</th><th>% Total</th><th>DefectTypeID</th></tr></thead>
                  <tbody>
                    {clsfAnalysis.rules.map((rule) => (
                      <tr key={`${rule.priority}-${rule.ruleName}`} style={{ opacity: rule.isOccurring ? 1 : 0.55 }}>
                        <td>{rule.priority}</td>
                        <td><strong className="font-mono">{rule.ruleName}</strong></td>
                        <td>{rule.vpScope || '—'}</td>
                        <td><span className="tag-badge" style={{ background: rule.isOccurring ? '#dc2626' : '#334155', color: '#fff' }}>{rule.isOccurring ? 'ACTIVE ALARMS' : '0 alarms'}</span></td>
                        <td><strong>{rule.occurrenceCount.toLocaleString()}</strong></td>
                        <td>
                          <div style={{ minWidth: 130 }}>
                            <strong>{rule.occurrencePercent.toFixed(2)}%</strong>
                            <div style={{ height: 5, background: '#1e293b', borderRadius: 4, overflow: 'hidden', marginTop: 3 }}>
                              <div style={{ width: `${Math.min(100, rule.occurrencePercent)}%`, height: '100%', background: rule.isOccurring ? '#ef4444' : '#475569' }} />
                            </div>
                          </div>
                        </td>
                        <td className="font-mono text-xs">{rule.defectTypeId || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          <section className="panel">
            <div className="table-responsive">
              <table className="analytics-data-table" role="table">
                <thead>
                  <tr>
                    <th>OP Defect Name (Input)</th>
                    <th>Machine / Package</th>
                    <th>Machine Defect Target</th>
                    <th>Family</th>
                    <th>Component</th>
                    <th>Classification</th>
                    <th>Description / Notes</th>
                    <th>Updated</th>
                    <th style={{ textAlign: 'right' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredMappings.map((m) => (
                    <tr key={m.id}>
                      <td className="font-bold" style={{ color: '#e2e8f0' }}>{m.opDefectName}</td>
                      <td><strong>{m.machineCode}</strong><div className="text-xs text-muted">{m.packageName}</div></td>
                      <td className="font-mono" style={{ color: '#60a5fa', fontWeight: 600 }}>{m.machineDefectName}</td>
                      <td><StatusPill value={m.defectFamily} /></td>
                      <td>{m.componentGroup || '—'}</td>
                      <td>
                        <span
                          className="tag-badge"
                          style={{
                            background: m.isConfirmedDefect ? 'rgba(168, 85, 247, 0.2)' : 'rgba(245, 158, 11, 0.2)',
                            color: m.isConfirmedDefect ? '#a855f7' : '#f59e0b',
                            fontWeight: 700,
                          }}
                        >
                          {m.isConfirmedDefect ? '✓ REAL DEFECT' : 'OVERKILL (FALSE ALARM)'}
                        </span>
                      </td>
                      <td className="text-xs text-muted" style={{ maxWidth: 280 }}>{m.description || '—'}</td>
                      <td className="text-xs font-mono text-muted">{formatDateTime(m.updatedAtUtc || m.createdAtUtc)}</td>
                      <td style={{ textAlign: 'right' }}>
                        <button
                          type="button"
                          className="ghost-button compact"
                          onClick={() => {
                            setEditingMapping(m)
                            setMappingForm({
                              machineCode: m.machineCode,
                              packageName: m.packageName,
                              opDefectName: m.opDefectName,
                              machineDefectName: m.machineDefectName,
                              defectFamily: m.defectFamily,
                              componentGroup: m.componentGroup,
                              isConfirmedDefect: m.isConfirmedDefect,
                              description: m.description || '',
                            })
                            setShowMappingModal(true)
                          }}
                        >
                          <Icon name="edit" className="icon inline-icon" />Edit
                        </button>
                        <Button
                          variant="danger"
                          size="sm"
                          aria-label={`Delete mapping ${m.opDefectName}`}
                          onClick={() => void handleDeleteMapping(m.id, m.opDefectName)}
                        >
                          Delete
                        </Button>
                      </td>
                    </tr>
                  ))}
                  {filteredMappings.length === 0 && (
                    <tr>
                      <td colSpan={10} className="text-center text-muted" style={{ padding: '24px' }}>
                        No mappings found. Click <strong>+ Add New Mapping</strong> or seed sample data.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}

      {/* MODAL: ADD / EDIT MAPPING */}
      {showMappingModal && (
        <Modal
          title={editingMapping ? `Edit Mapping: ${editingMapping.opDefectName}` : 'Add OP Defect Mapping'}
          description="Map raw operator review descriptions to standard machine defect classes."
          open={showMappingModal}
          onClose={() => setShowMappingModal(false)}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            <div className="query-form-grid">
              <Field label="Machine Package">
                <input value={mappingForm.machineCode ?? ''} onChange={(e) => setMappingForm({ ...mappingForm, machineCode: e.target.value.toUpperCase() })} placeholder="CP_AOI" />
              </Field>
              <Field label="Package Name">
                <input value={mappingForm.packageName ?? ''} onChange={(e) => setMappingForm({ ...mappingForm, packageName: e.target.value })} placeholder="CP AOI Default" />
              </Field>
            </div>
            <Field label="OP Defect Name (Text in Excel) *" required>
              <input
                type="text"
                value={mappingForm.opDefectName}
                onChange={(e) => setMappingForm({ ...mappingForm, opDefectName: e.target.value })}
                placeholder="e.g. Solder bridge, Corner crack, Surface dust..."
              />
            </Field>

            <Field label="Target Machine Defect Name *" required>
              <input
                type="text"
                value={mappingForm.machineDefectName}
                onChange={(e) => setMappingForm({ ...mappingForm, machineDefectName: e.target.value })}
                placeholder="e.g. DIC_Crack, Light_Fail, Bridging, Dust, NONE..."
              />
            </Field>

            <Field label="Defect Family">
              <select
                value={mappingForm.defectFamily}
                onChange={(e) => setMappingForm({ ...mappingForm, defectFamily: e.target.value })}
              >
                <option value="Optical">Optical (AOI Vision)</option>
                <option value="Solder">Solder (SMT Joint)</option>
                <option value="Component">Component</option>
                <option value="ForeignMaterial">Foreign Material / Dust</option>
                <option value="Clean">Clean (No Defect)</option>
              </select>
            </Field>

            <Field label="Component / Station Group">
              <input
                value={mappingForm.componentGroup ?? ''}
                onChange={(e) => setMappingForm({ ...mappingForm, componentGroup: e.target.value })}
                placeholder="COP, MCL, SR COP, SR FOP..."
              />
            </Field>

            <Field label="Operator Judgment Nature">
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', cursor: 'pointer', marginTop: '4px' }}>
                <input
                  type="checkbox"
                  checked={mappingForm.isConfirmedDefect}
                  onChange={(e) => setMappingForm({ ...mappingForm, isConfirmedDefect: e.target.checked })}
                />
                <span style={{ fontSize: '13px', fontWeight: 600 }}>
                  {mappingForm.isConfirmedDefect ? '✓ Confirmed Real Defect' : 'Overkill / False Alarm (Cleanable/Noise)'}
                </span>
              </label>
              <small className="text-muted" style={{ display: 'block', marginTop: '4px' }}>
                Uncheck if operator considers this defect false alarm, cleanable dust, or acceptable within tolerance (Overkill).
              </small>
            </Field>

            <Field label="Description / Notes">
              <input
                type="text"
                value={mappingForm.description ?? ''}
                onChange={(e) => setMappingForm({ ...mappingForm, description: e.target.value })}
                placeholder="Additional notes for this mapping..."
              />
            </Field>

            <div className="actions" style={{ justifyContent: 'flex-end', marginTop: '8px' }}>
              <button type="button" className="ghost-button" onClick={() => setShowMappingModal(false)}>
                Cancel
              </button>
              <button type="button" className="primary" onClick={() => void handleSaveMapping()}>
                Save Mapping
              </button>
            </div>
          </div>
        </Modal>
      )}

      {showBulkMappingModal && (
        <Modal
          title="Bulk Defect Mapping Package"
          description="Paste 4 columns directly from Excel: OP Defect Name | Family | Component | Machine Defect Code. Each package is partitioned by Machine."
          open={showBulkMappingModal}
          onClose={() => setShowBulkMappingModal(false)}
          wide
        >
          <div className="modal-form">
            <div className="query-form-grid">
              <Field label="Machine *" required>
                <input value={mappingMachine} onChange={(e) => setMappingMachine(e.target.value.toUpperCase())} placeholder="CP_AOI" />
              </Field>
              <Field label="Package Name *" required>
                <input value={mappingPackage} onChange={(e) => setMappingPackage(e.target.value)} placeholder="CP AOI D967 Mapping" />
              </Field>
            </div>
            <Field label="Paste Mapping Rows" hint="Copy 4 columns from Excel; tab or multiple spaces are automatically parsed.">
              <textarea
                rows={16}
                value={bulkMappingText}
                onChange={(e) => setBulkMappingText(e.target.value)}
                placeholder={'COP Bonding Particle\tParticle\tCOP\tSR_Bonding_Particle\nCOP DIC Damage\tOptical\tCOP\tDIC_Crack\nMCL Small Bubble\tOptical\tMCL\tMCL_AA_Bubble'}
                style={{ fontFamily: 'monospace', fontSize: '12px', whiteSpace: 'pre' }}
              />
            </Field>
            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
              <span className="tag-badge">Machine: {mappingMachine}</span>
              <span className="tag-badge">Package: {mappingPackage}</span>
              <span className="tag-badge" style={{ background: '#047857', color: '#fff' }}>{parsedBulkMappings.length} rules parsed</span>
            </div>
            {parsedBulkMappings.length > 0 && (
              <div className="table-responsive" style={{ maxHeight: 280 }}>
                <table className="analytics-data-table">
                  <thead><tr><th>OP Defect</th><th>Family</th><th>Component</th><th>Machine Code</th></tr></thead>
                  <tbody>{parsedBulkMappings.slice(0, 50).map((m, i) => (
                    <tr key={`${m.opDefectName}-${i}`}><td>{m.opDefectName}</td><td>{m.defectFamily}</td><td>{m.componentGroup}</td><td className="font-mono">{m.machineDefectName}</td></tr>
                  ))}</tbody>
                </table>
              </div>
            )}
            <div className="actions">
              <button type="button" className="ghost-button" onClick={() => setShowBulkMappingModal(false)}>Cancel</button>
              <Button loading={bulkSaving} disabled={parsedBulkMappings.length === 0} onClick={() => void handleBulkSaveMappings()}>
                {bulkSaving ? 'Saving package...' : `Save ${parsedBulkMappings.length} Mappings`}
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {showPasteModal && (
        <Modal
          title="Paste Operator (OP) Review Data"
          description="Paste directly from Clipboard or Excel format: FOG ID | OP Defect Name | Judgment. Header rows are automatically detected and skipped."
          open={showPasteModal}
          onClose={() => setShowPasteModal(false)}
          wide
        >
          <div className="modal-form">
            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center', marginBottom: '8px' }}>
              <button
                type="button"
                className="secondary"
                onClick={() => void handleReadFromClipboard()}
                style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', fontWeight: 600 }}
              >
                <Icon name="clipboard" className="icon inline-icon" />Read from Clipboard (Auto-paste)
              </button>
              <button
                type="button"
                className="secondary"
                onClick={() => setPasteText(CONCEPT_SAMPLE_DATA)}
                style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}
                title="Load 9 sample rows for FOP, POL, COP, B/P"
              >
                <Icon name="info" className="icon inline-icon" />Load Concept Sample (FOP, POL, COP, B/P)
              </button>
              {pasteText && (
                <button
                  type="button"
                  className="ghost-button"
                  onClick={() => setPasteText('')}
                  style={{ color: '#ef4444' }}
                >
                  <Icon name="trash" className="icon inline-icon" />Clear
                </button>
              )}
            </div>

            <div className="query-form-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))' }}>
              <Field label="Production Machine *" required>
                <input
                  value={pasteMachine}
                  onChange={(e) => setPasteMachine(e.target.value.toUpperCase())}
                  placeholder="CP_AOI"
                />
              </Field>
              <Field label="Default Judgment (if empty)">
                <select
                  value={pasteDefaultJudge}
                  onChange={(e) => setPasteDefaultJudge(e.target.value)}
                >
                  <option value="NG">NG (Real Defect)</option>
                  <option value="OK">OK (Overkill / Clean)</option>
                </select>
              </Field>
            </div>

            <Field
              label="Paste Content (Excel / TSV / CSV)"
              hint="Paste columns FOG ID, Defect Name, Judgment. You can copy table rows directly from Excel."
            >
              <textarea
                rows={10}
                value={pasteText}
                onChange={(e) => setPasteText(e.target.value)}
                placeholder={`FOG ID\tDefect Name\tJudge\nA0E7670S19B134214228\tDent\tNG\nA0B265KC29B125613228\tDamage\tNG\nA0AE65D318A125713228\tCrack\tNG`}
                style={{ fontFamily: 'monospace', fontSize: '12px', whiteSpace: 'pre' }}
              />
            </Field>

            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' }}>
              <span className="tag-badge">Machine: {pasteMachine || 'CP_AOI'}</span>
              <span className="tag-badge" style={{ background: '#dbeafe', color: '#1d4ed8' }}>Line: Auto-resolved from Product by FOG ID</span>
              <span className="tag-badge" style={{ background: '#2563eb', color: '#fff', fontWeight: 600 }}>
                {pasteStats.total} rows parsed ({pasteStats.uniqueKeys} FOG IDs)
              </span>
              <span className="tag-badge" style={{ background: '#ef4444', color: '#fff' }}>
                {pasteStats.ngCount} NG (Defect)
              </span>
              <span className="tag-badge" style={{ background: '#10b981', color: '#fff' }}>
                {pasteStats.okCount} OK (Overkill)
              </span>
              <span className="tag-badge" style={{ background: '#047857', color: '#fff' }}>
                {pasteStats.mappedCount}/{pasteStats.total} Dictionary Mapped
              </span>
            </div>

            {parsedPasteRows.length > 0 && (
              <div className="table-responsive" style={{ maxHeight: 250, overflowY: 'auto' }}>
                <table className="analytics-data-table">
                  <thead>
                    <tr>
                      <th style={{ width: '40px' }}>#</th>
                      <th>FOG ID (Key ID)</th>
                      <th>OP Defect Name</th>
                      <th style={{ width: '90px' }}>Judgment</th>
                      <th>Dictionary Mapping</th>
                    </tr>
                  </thead>
                  <tbody>
                    {parsedPasteRows.map((row, idx) => (
                      <tr key={`${row.keyId}-${idx}`}>
                        <td className="text-muted">{idx + 1}</td>
                        <td className="font-mono" style={{ fontWeight: 600 }}>{row.keyId}</td>
                        <td>{row.opDefectName || <span className="text-muted italic">(Empty)</span>}</td>
                        <td>
                          <span
                            className="tag-badge"
                            style={{
                              background: row.judge === 'NG' ? '#fee2e2' : '#d1fae5',
                              color: row.judge === 'NG' ? '#b91c1c' : '#047857',
                              fontWeight: 700,
                              padding: '2px 8px',
                              borderRadius: '4px',
                            }}
                          >
                            {row.judge}
                          </span>
                        </td>
                        <td>
                          {row.matchedMapping ? (
                            <span
                              className="tag-badge"
                              style={{ background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0' }}
                            >
                              ✓ {row.matchedMapping.machineDefectName} ({row.matchedMapping.defectFamily})
                            </span>
                          ) : (
                            <span
                              className="tag-badge"
                              style={{ background: '#fffbeb', color: '#b45309', border: '1px solid #fde68a' }}
                            >
                              <Icon name="warning" className="icon inline-icon" />Not in dictionary
                            </span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <div className="actions">
              <button
                type="button"
                className="ghost-button"
                onClick={() => setShowPasteModal(false)}
                disabled={pasting}
              >
                Cancel
              </button>
              <Button loading={pasting} disabled={parsedPasteRows.length === 0} onClick={() => void handleSavePastedRecords()}>
                {pasting ? 'Saving & Reconciling...' : `Save & Reconcile OP (${pasteStats.total} rows)`}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  )
}
