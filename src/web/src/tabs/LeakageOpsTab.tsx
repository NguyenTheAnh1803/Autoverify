import { useCallback, useEffect, useMemo, useRef, useState, type ChangeEvent, type DragEvent } from 'react'
import { connectionsApi, leakageQueueApi, storageApi } from '../api'
import { formatDate } from '../format'
import { DefectFeatureInspector, Field, JudgeBadge, Modal, Pagination, StatusPill } from '../ui'
import type {
  DatabaseConnection,
  DefectRecord,
  LeakageBatch,
  LeakageQueueItem,
  LeakageQueueItemDetail,
  LeakageQueueProgress,
  Notice,
} from '../types'

const SAMPLE_PASTE = `CUS-1004\tSMT-02
CUS-1001\tSMT-01
DXDIAN\t301
PROD-9982\tSMT-02
TEST-5501`

type IngestionMode = 'paste' | 'csv'

export function LeakageOpsTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  // Ingestion Mode State
  const [mode, setMode] = useState<IngestionMode>('paste')
  const [pasteText, setPasteText] = useState(SAMPLE_PASTE)
  const [pasteBatchName, setPasteBatchName] = useState('')
  const [pasteDefaultLine, setPasteDefaultLine] = useState('')

  // CSV Drag-and-drop State
  const [csvFile, setCsvFile] = useState<File | null>(null)
  const [csvBatchName, setCsvBatchName] = useState('')
  const [csvDefaultLine, setCsvDefaultLine] = useState('')
  const [csvPreviewRows, setCsvPreviewRows] = useState<Array<{ row: number; id: string; line: string }>>([])
  const [isDragOver, setIsDragOver] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  // System Connections (for line suggestions)
  const [connections, setConnections] = useState<DatabaseConnection[]>([])

  // Queue Data State
  const [batches, setBatches] = useState<LeakageBatch[]>([])
  const [items, setItems] = useState<LeakageQueueItem[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize] = useState(25)
  const [selectedBatchId, setSelectedBatchId] = useState<string>('all')
  const [statusFilter, setStatusFilter] = useState<string>('all')
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())

  // Execution & Progress State
  const [progress, setProgress] = useState<LeakageQueueProgress | null>(null)
  const [isProcessing, setIsProcessing] = useState(false)
  const [busy, setBusy] = useState(false)

  // Inspection Modal State
  const [inspectModalOpen, setInspectModalOpen] = useState(false)
  const [activeItemDetail, setActiveItemDetail] = useState<LeakageQueueItemDetail | null>(null)
  const [activeItemLoading, setActiveItemLoading] = useState(false)
  const [zoomedImageUri, setZoomedImageUri] = useState<string | null>(null)
  const [activeDefectForInspection, setActiveDefectForInspection] = useState<DefectRecord | null>(null)

  // Available unique lines from registered connections
  const availableLines = useMemo(() => {
    const lines = connections.map((c) => c.lineCode).filter(Boolean)
    return [...new Set(lines)].sort((a, b) => a.localeCompare(b))
  }, [connections])

  // Real-time detection of pasted rows
  const parsedPasteStats = useMemo(() => {
    const lines = pasteText
      .split('\n')
      .map((l) => l.trim())
      .filter(Boolean)
    let withLine = 0
    for (const line of lines) {
      const parts = line.split(/[\t, ]+/).filter(Boolean)
      if (parts.length >= 2) withLine++
    }
    return {
      total: lines.length,
      withLine,
      withoutLine: lines.length - withLine,
    }
  }, [pasteText])

  // Load Batches
  const loadBatches = useCallback(async () => {
    try {
      const data = await leakageQueueApi.getBatches(100)
      setBatches(data)
    } catch {
      // Background load error ignored
    }
  }, [])

  // Load Queue Items
  const loadItems = useCallback(async () => {
    try {
      const res = await leakageQueueApi.getItems({
        batchId: selectedBatchId !== 'all' ? selectedBatchId : undefined,
        status: statusFilter !== 'all' ? statusFilter : undefined,
        search: searchQuery.trim() || undefined,
        page,
        pageSize,
      })
      setItems(res.items ?? [])
      setTotalCount(res.totalCount ?? 0)
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to fetch leakage queue items.',
      })
    }
  }, [selectedBatchId, statusFilter, searchQuery, page, pageSize, onNotice])

  // Check progress
  const pollProgress = useCallback(async () => {
    try {
      const prog = await leakageQueueApi.getProgress()
      setProgress(prog)
      if (prog.isRunning) {
        setIsProcessing(true)
      } else if (isProcessing) {
        setIsProcessing(false)
        onNotice({
          tone: 'success',
          message: `Queue execution completed. Found ${prog.leakageCount} escape risks, ${prog.notFoundCount} missing items.`,
        })
        void loadItems()
        void loadBatches()
      }
    } catch {
      // Soft fail
    }
  }, [isProcessing, onNotice, loadItems, loadBatches])

  // Initial Load
  useEffect(() => {
    void loadBatches()
    void loadItems()
    void connectionsApi
      .list({ page: 1, pageSize: 100 })
      .then((payload) => setConnections(payload.items ?? []))
      .catch(() => {})
  }, [loadBatches, loadItems])

  // Polling interval when active
  useEffect(() => {
    const timer = setInterval(() => {
      void pollProgress()
      if (isProcessing) {
        void loadItems()
        void loadBatches()
      }
    }, 2500)
    return () => clearInterval(timer)
  }, [pollProgress, isProcessing, loadItems, loadBatches])

  // Handle Enqueue Paste
  async function handleEnqueuePaste() {
    if (!pasteText.trim()) {
      onNotice({ tone: 'error', message: 'Please provide at least one product ID in the paste box.' })
      return
    }
    setBusy(true)
    try {
      const defaultName = pasteBatchName.trim() || `Paste Lot ${new Date().toISOString().slice(0, 16).replace('T', ' ')}`
      const batch = await leakageQueueApi.enqueuePaste({
        text: pasteText,
        batchName: defaultName,
        defaultLineCode: pasteDefaultLine.trim() || undefined,
      })
      onNotice({
        tone: 'success',
        message: `Enqueued ${batch.totalCount} items into batch "${batch.name}" as Pending.`,
      })
      setPasteText('')
      setPasteBatchName('')
      await loadBatches()
      setSelectedBatchId(batch.id)
      setPage(1)
      await loadItems()
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to enqueue paste list.',
      })
    } finally {
      setBusy(false)
    }
  }

  // Parse CSV File for Preview
  function parseCsvPreview(file: File) {
    const reader = new FileReader()
    reader.onload = (e) => {
      const content = (e.target?.result as string) || ''
      const lines = content.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
      if (lines.length === 0) {
        setCsvPreviewRows([])
        return
      }

      // Check if first row is header
      const firstRow = lines[0].toLowerCase()
      const isHeader = firstRow.includes('id') || firstRow.includes('serial') || firstRow.includes('line')
      const dataRows = isHeader ? lines.slice(1) : lines

      const preview = dataRows.slice(0, 5).map((rowStr, idx) => {
        const cols = rowStr.split(/[,\t;]/).map((c) => c.trim().replace(/^["']|["']$/g, ''))
        return {
          row: idx + 1,
          id: cols[0] || '—',
          line: cols[1] || '—',
        }
      })
      setCsvPreviewRows(preview)
    }
    reader.readAsText(file.slice(0, 8192))
  }

  // Handle File Selection
  function onFileSelected(file: File) {
    setCsvFile(file)
    if (!csvBatchName) {
      const baseName = file.name.replace(/\.[^/.]+$/, '')
      setCsvBatchName(baseName)
    }
    parseCsvPreview(file)
  }

  // Drag and drop handlers
  function handleDragOver(e: DragEvent<HTMLDivElement>) {
    e.preventDefault()
    setIsDragOver(true)
  }
  function handleDragLeave(e: DragEvent<HTMLDivElement>) {
    e.preventDefault()
    setIsDragOver(false)
  }
  function handleDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault()
    setIsDragOver(false)
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      const file = e.dataTransfer.files[0]
      onFileSelected(file)
    }
  }

  // Handle Enqueue CSV
  async function handleEnqueueCsv() {
    if (!csvFile) {
      onNotice({ tone: 'error', message: 'Please select or drop a CSV/TSV file.' })
      return
    }
    setBusy(true)
    try {
      const batch = await leakageQueueApi.uploadCsv(
        csvFile,
        csvBatchName.trim() || undefined,
        csvDefaultLine.trim() || undefined,
      )
      onNotice({
        tone: 'success',
        message: `Successfully uploaded "${csvFile.name}". Saved ${batch.totalCount} items into batch "${batch.name}" as Pending.`,
      })
      setCsvFile(null)
      setCsvBatchName('')
      setCsvPreviewRows([])
      await loadBatches()
      setSelectedBatchId(batch.id)
      setPage(1)
      await loadItems()
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to upload and enqueue CSV.',
      })
    } finally {
      setBusy(false)
    }
  }

  // Execution Triggers
  async function handleProcessAllPending() {
    setBusy(true)
    try {
      await leakageQueueApi.processQueue({
        batchId: selectedBatchId !== 'all' ? selectedBatchId : undefined,
      })
      setIsProcessing(true)
      onNotice({ tone: 'info', message: 'Queue processing started in background...' })
      void pollProgress()
      void loadItems()
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to start queue processing.',
      })
    } finally {
      setBusy(false)
    }
  }

  async function handleProcessSelected() {
    if (selectedIds.size === 0) {
      onNotice({ tone: 'error', message: 'No items selected to process.' })
      return
    }
    setBusy(true)
    try {
      await leakageQueueApi.processQueue({
        itemIds: Array.from(selectedIds),
      })
      setIsProcessing(true)
      setSelectedIds(new Set())
      onNotice({
        tone: 'info',
        message: `Triggered processing for ${selectedIds.size} selected item(s).`,
      })
      void pollProgress()
      void loadItems()
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to process selected items.',
      })
    } finally {
      setBusy(false)
    }
  }

  async function handleRetry(itemIds?: string[]) {
    setBusy(true)
    try {
      const targets = itemIds ?? (selectedIds.size > 0 ? Array.from(selectedIds) : undefined)
      const res = await leakageQueueApi.retryItems({
        batchId: !targets && selectedBatchId !== 'all' ? selectedBatchId : undefined,
        itemIds: targets,
      })
      onNotice({ tone: 'success', message: res.message })
      setSelectedIds(new Set())
      await loadItems()
      await loadBatches()
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Retry failed.',
      })
    } finally {
      setBusy(false)
    }
  }

  async function handleClear(completedOnly = false) {
    const confirmation = window.confirm(
      completedOnly
        ? 'Clear completed records from the queue?'
        : 'Are you sure you want to clear the selected queue items? This action cannot be undone.',
    )
    if (!confirmation) return

    setBusy(true)
    try {
      const targets = selectedIds.size > 0 ? Array.from(selectedIds) : undefined
      const res = await leakageQueueApi.clearQueue({
        batchId: !targets && selectedBatchId !== 'all' ? selectedBatchId : undefined,
        itemIds: targets,
        clearCompletedOnly: completedOnly,
      })
      onNotice({ tone: 'success', message: res.message })
      setSelectedIds(new Set())
      await loadItems()
      await loadBatches()
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to clear queue.',
      })
    } finally {
      setBusy(false)
    }
  }

  // Inspection Action
  async function handleInspect(item: LeakageQueueItem) {
    setActiveItemLoading(true)
    setInspectModalOpen(true)
    try {
      const details = await leakageQueueApi.getItemDetail(item.id)
      setActiveItemDetail(details)
    } catch (error) {
      onNotice({
        tone: 'error',
        message: error instanceof Error ? error.message : 'Failed to load item inspection details.',
      })
      setInspectModalOpen(false)
    } finally {
      setActiveItemLoading(false)
    }
  }

  // Selection Checkbox Helpers
  const isAllSelected = items.length > 0 && items.every((i) => selectedIds.has(i.id))
  function toggleSelectAll() {
    if (isAllSelected) {
      setSelectedIds(new Set())
    } else {
      setSelectedIds(new Set(items.map((i) => i.id)))
    }
  }
  function toggleSelectItem(id: string) {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  // Aggregate Metrics
  const activeBatch = useMemo(
    () => (selectedBatchId !== 'all' ? batches.find((b) => b.id === selectedBatchId) : null),
    [batches, selectedBatchId],
  )

  const aggregateMetrics = useMemo(() => {
    if (activeBatch) {
      return {
        total: activeBatch.totalCount,
        pending: activeBatch.pendingCount,
        processing: activeBatch.processingCount,
        completed: activeBatch.completedCount,
        leakage: activeBatch.leakageCount,
        consistent: activeBatch.consistentCount,
        notFound: activeBatch.notFoundCount,
        failed: activeBatch.failedCount,
      }
    }
    return batches.reduce(
      (acc, b) => ({
        total: acc.total + b.totalCount,
        pending: acc.pending + b.pendingCount,
        processing: acc.processing + b.processingCount,
        completed: acc.completed + b.completedCount,
        leakage: acc.leakage + b.leakageCount,
        consistent: acc.consistent + b.consistentCount,
        notFound: acc.notFound + b.notFoundCount,
        failed: acc.failed + b.failedCount,
      }),
      { total: 0, pending: 0, processing: 0, completed: 0, leakage: 0, consistent: 0, notFound: 0, failed: 0 },
    )
  }, [batches, activeBatch])

  // Progress percentage
  const progressPercent = useMemo(() => {
    if (!progress || progress.totalCount === 0) return 0
    return Math.min(100, Math.round((progress.processedCount / progress.totalCount) * 100))
  }, [progress])

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <div className="tab-stack">
      {/* 1. Ingestion Area */}
      <section className="feature-grid">
        <article className="panel form-panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">MANUAL QUERY INGESTION · ESCAPE RISK VERIFICATION</p>
              <h2>Leakage Manual Verification Pipeline</h2>
            </div>
            <span className="anomaly-pill leakage">🔎 Leakage Ingestion</span>
          </div>

          {/* Mode Switcher */}
          <div className="leakage-mode-tabs">
            <button
              type="button"
              className={`leakage-tab-btn ${mode === 'paste' ? 'active' : ''}`}
              onClick={() => setMode('paste')}
            >
              <span>📋</span> Paste List
            </button>
            <button
              type="button"
              className={`leakage-tab-btn ${mode === 'csv' ? 'active' : ''}`}
              onClick={() => setMode('csv')}
            >
              <span>📁</span> CSV / TSV Upload
            </button>
          </div>

          {/* Mode 1: Paste List */}
          {mode === 'paste' ? (
            <div className="tab-stack">
              <div className="query-form-grid">
                <Field
                  label="Batch Name (Optional)"
                  hint="Friendly reference name for this scan batch"
                >
                  <input
                    type="text"
                    value={pasteBatchName}
                    onChange={(e) => setPasteBatchName(e.target.value)}
                    placeholder="e.g. Customer Return Lot A"
                  />
                </Field>

                <Field
                  label="Default Line Code (Optional)"
                  hint="Applied to rows without an explicit line code"
                >
                  <input
                    type="text"
                    list="available-lines-list"
                    value={pasteDefaultLine}
                    onChange={(e) => setPasteDefaultLine(e.target.value)}
                    placeholder="ALL (or specify e.g. SMT-01)"
                  />
                </Field>

                <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
                  <small className="muted">
                    Detected: <strong>{parsedPasteStats.total}</strong> IDs (
                    <strong>{parsedPasteStats.withLine}</strong> with line,{' '}
                    <strong>{parsedPasteStats.withoutLine}</strong> fallback to default)
                  </small>
                </div>
              </div>

              <Field
                label="Paste Product Serial Numbers"
                required
                hint="Accepts TSV, CSV, or spaces. Format: [Product_ID] [LineCode (optional)] per line"
              >
                <textarea
                  rows={6}
                  value={pasteText}
                  onChange={(e) => setPasteText(e.target.value)}
                  placeholder="CUS-1004&#9;SMT-02&#10;CUS-1001&#9;SMT-01&#10;DXDIAN"
                />
              </Field>

              <div className="actions">
                <button
                  type="button"
                  disabled={busy || !pasteText.trim()}
                  onClick={() => void handleEnqueuePaste()}
                >
                  📥 Save to Database as Pending ({parsedPasteStats.total} items)
                </button>
                <button
                  type="button"
                  className="ghost-button"
                  onClick={() => setPasteText('')}
                >
                  Clear Text
                </button>
              </div>
            </div>
          ) : (
            /* Mode 2: CSV / TSV Upload */
            <div className="tab-stack">
              <div className="query-form-grid">
                <Field
                  label="Batch Name (Optional)"
                  hint="Defaults to the file name"
                >
                  <input
                    type="text"
                    value={csvBatchName}
                    onChange={(e) => setCsvBatchName(e.target.value)}
                    placeholder="e.g. Lot-Audit-2026"
                  />
                </Field>

                <Field
                  label="Default Line Code (Optional)"
                  hint="Fallback line if missing from CSV columns"
                >
                  <input
                    type="text"
                    list="available-lines-list"
                    value={csvDefaultLine}
                    onChange={(e) => setCsvDefaultLine(e.target.value)}
                    placeholder="ALL (or e.g. SMT-01)"
                  />
                </Field>
              </div>

              {/* Drag and drop target */}
              <div
                className={`dropzone-area ${isDragOver ? 'drag-over' : ''}`}
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                onClick={() => fileInputRef.current?.click()}
              >
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".csv,.tsv,.txt"
                  style={{ display: 'none' }}
                  onChange={(e: ChangeEvent<HTMLInputElement>) => {
                    if (e.target.files && e.target.files.length > 0) {
                      onFileSelected(e.target.files[0])
                    }
                  }}
                />
                <span className="dropzone-icon">📂</span>
                {csvFile ? (
                  <div>
                    <strong>{csvFile.name}</strong>
                    <p className="muted" style={{ margin: '4px 0 0' }}>
                      {(csvFile.size / 1024).toFixed(1)} KB · Click or drag to replace
                    </p>
                  </div>
                ) : (
                  <div>
                    <strong>Drag and drop CSV / TSV file here</strong>
                    <p className="muted" style={{ margin: '4px 0 0' }}>
                      or click to browse from your device (.csv, .tsv, .txt)
                    </p>
                  </div>
                )}
              </div>

              {/* Preview parsed rows */}
              {csvPreviewRows.length > 0 ? (
                <div>
                  <small className="muted" style={{ display: 'block', marginBottom: 6 }}>
                    Auto-parsed preview (first {csvPreviewRows.length} rows):
                  </small>
                  <table className="mini-preview-table" style={{ width: '100%' }}>
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>Product ID</th>
                        <th>Line Code</th>
                      </tr>
                    </thead>
                    <tbody>
                      {csvPreviewRows.map((r) => (
                        <tr key={r.row}>
                          <td>{r.row}</td>
                          <td><strong>{r.id}</strong></td>
                          <td><span className="line-pill">{r.line || csvDefaultLine || 'ALL'}</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : null}

              <div className="actions">
                <button
                  type="button"
                  disabled={busy || !csvFile}
                  onClick={() => void handleEnqueueCsv()}
                >
                  🚀 Upload & Enqueue as Pending
                </button>
                {csvFile ? (
                  <button
                    type="button"
                    className="ghost-button"
                    onClick={() => {
                      setCsvFile(null)
                      setCsvPreviewRows([])
                    }}
                  >
                    Remove File
                  </button>
                ) : null}
              </div>
            </div>
          )}

          <datalist id="available-lines-list">
            <option value="ALL" />
            {availableLines.map((line) => (
              <option key={line} value={line} />
            ))}
          </datalist>
        </article>

        {/* Informational Guidelines Panel */}
        <article className="panel info-panel">
          <p className="eyebrow">AUTOMATED VERIFICATION LOGIC</p>
          <h2>How Leakage Detection Operates</h2>
          <div className="csv-concept-hint" style={{ marginTop: 12 }}>
            <strong>1. Immediate Pending Enqueue:</strong> IDs are saved locally into <code>LeakageQueueItems</code> right after ingestion, guaranteeing no loss of query state.
          </div>
          <div className="csv-concept-hint" style={{ marginTop: 8 }}>
            <strong>2. MariaDB Full-Scan Lookup:</strong> The worker searches remote product tables for the product ID, resolving foreign key <code>Fk_ID</code> and station judgment.
          </div>
          <div className="csv-concept-hint" style={{ marginTop: 8 }}>
            <strong>3. Deep Defect Table Query:</strong> Using <code>Fk_ID</code>, queries configured multi-defect tables. If defects exist on a <code>G</code> / <code>OK</code> product, flags <strong>Leakage (Escape Risk)</strong>!
          </div>
          <div className="csv-concept-hint" style={{ marginTop: 8 }}>
            <strong>4. Local Ingestion:</strong> Product and defect rows are persisted into AutoVerify's local database for historical reporting and audit inspection.
          </div>
        </article>
      </section>

      {/* 2. Metrics & Progress Summary */}
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">REAL-TIME QUEUE STATUS</p>
            <h2>Queue Metrics & Execution Progress</h2>
          </div>
          {isProcessing ? (
            <span className="queue-status-pill inprogress">
              ⚡ Worker Active ({progress?.processedCount ?? 0} / {progress?.totalCount ?? 0})
            </span>
          ) : (
            <span className="clean-pill">Idle / Ready</span>
          )}
        </div>

        {/* Real-time counters */}
        <div className="leakage-metrics-bar">
          <div className="leakage-metric-badge">
            <span>Total Items</span>
            <strong>{aggregateMetrics.total}</strong>
          </div>
          <div className="leakage-metric-badge pending">
            <span>Pending</span>
            <strong>{aggregateMetrics.pending}</strong>
          </div>
          <div className="leakage-metric-badge processing">
            <span>Processing</span>
            <strong>{aggregateMetrics.processing}</strong>
          </div>
          <div className="leakage-metric-badge completed">
            <span>Consistent OK</span>
            <strong>{aggregateMetrics.consistent}</strong>
          </div>
          <div className="leakage-metric-badge leakage-suspect">
            <span>⚠️ Leakage Suspects</span>
            <strong>{aggregateMetrics.leakage}</strong>
          </div>
          <div className="leakage-metric-badge not-found">
            <span>Not Found</span>
            <strong>{aggregateMetrics.notFound}</strong>
          </div>
          <div className="leakage-metric-badge failed">
            <span>Failed</span>
            <strong>{aggregateMetrics.failed}</strong>
          </div>
        </div>

        {/* Animated progress bar */}
        {progress && (isProcessing || progress.totalCount > 0) ? (
          <div style={{ marginTop: 16 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, fontWeight: 600 }}>
              <span>Progress: {progress.processedCount} of {progress.totalCount} items verified</span>
              <span>{progressPercent}%</span>
            </div>
            <div className="queue-progress-bar-wrap">
              <div
                className={`queue-progress-bar-fill ${isProcessing ? 'animated' : ''}`}
                style={{ width: `${progressPercent}%` }}
              />
            </div>
          </div>
        ) : null}
      </section>

      {/* 3. Queue Management & Action Controls */}
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">WORKFLOW CONTROLS</p>
            <h2>Queue Management</h2>
          </div>
          <div className="actions" style={{ margin: 0 }}>
            <button
              type="button"
              disabled={busy || isProcessing || aggregateMetrics.pending === 0}
              onClick={() => void handleProcessAllPending()}
            >
              ▶ Process All Pending ({aggregateMetrics.pending})
            </button>
            <button
              type="button"
              className="secondary"
              disabled={busy || isProcessing || selectedIds.size === 0}
              onClick={() => void handleProcessSelected()}
            >
              ▶ Process Selected ({selectedIds.size})
            </button>
            <button
              type="button"
              className="ghost-button"
              disabled={busy}
              onClick={() => void loadItems()}
            >
              🔄 Refresh
            </button>
            <button
              type="button"
              className="ghost-button"
              disabled={busy || isProcessing}
              onClick={() => void handleRetry()}
            >
              🔁 Retry Failed / Not Found
            </button>
            <button
              type="button"
              className="ghost-button"
              disabled={busy || isProcessing}
              onClick={() => void handleClear(true)}
            >
              🧹 Clear Completed
            </button>
            <button
              type="button"
              className="ghost-button"
              disabled={busy || isProcessing}
              onClick={() => void handleClear(false)}
            >
              🗑️ Clear Queue
            </button>
          </div>
        </div>

        <div className="compact-filter-bar" style={{ marginTop: 14 }}>
          {/* Batch Selector */}
          <Field label="Filter by Ingestion Batch">
            <select
              value={selectedBatchId}
              onChange={(e) => {
                setSelectedBatchId(e.target.value)
                setPage(1)
              }}
            >
              <option value="all">All Batches ({batches.length})</option>
              {batches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name} ({b.totalCount} items · {b.leakageCount} leakages)
                </option>
              ))}
            </select>
          </Field>

          {/* Search Input */}
          <Field label="Search by Product ID or FK ID">
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => {
                setSearchQuery(e.target.value)
                setPage(1)
              }}
              placeholder="Filter serials or FK IDs..."
            />
          </Field>

          {/* Status Filter Pills */}
          <div>
            <label className="field">
              <span>Filter Status</span>
              <div className="filter-chips" style={{ marginTop: 4 }}>
                {[
                  { id: 'all', label: 'All' },
                  { id: 'Pending', label: 'Pending' },
                  { id: 'InProgress', label: 'Processing' },
                  { id: 'Completed', label: 'Completed' },
                  { id: 'Leakage', label: '⚠️ Leakage' },
                  { id: 'NotFound', label: 'Not Found' },
                  { id: 'Failed', label: 'Failed' },
                ].map((f) => (
                  <button
                    key={f.id}
                    type="button"
                    className={`filter-chip ${statusFilter === f.id ? 'active' : ''}`}
                    onClick={() => {
                      setStatusFilter(f.id)
                      setPage(1)
                    }}
                  >
                    {f.label}
                  </button>
                ))}
              </div>
            </label>
          </div>
        </div>
      </section>

      {/* 4. Compact Queue Records */}
      <section className="panel operations-record-panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">DATABASE QUEUE ITEMS</p>
            <h2>Queue Records ({totalCount} Total)</h2>
            <p className="field-hint">Core status is shown here. Use Inspect for full product and defect evidence.</p>
          </div>
          {items.length > 0 ? (
            <button type="button" className="secondary compact-button" onClick={toggleSelectAll}>
              {isAllSelected ? 'Deselect Page' : 'Select Page'}
            </button>
          ) : null}
        </div>

        <div className="operations-record-list">
          {items.length === 0 ? (
            <div className="operations-empty-row">No queue records match the current filters.</div>
          ) : items.map((item, idx) => {
          const isSelected = selectedIds.has(item.id)
          const rowNumber = (page - 1) * pageSize + idx + 1

          let statusBadge = <span className="queue-status-pill pending">Pending</span>
          if (item.status === 'InProgress') {
            statusBadge = <span className="queue-status-pill inprogress">⚡ Processing...</span>
          } else if (item.status === 'Completed') {
            statusBadge = <span className="queue-status-pill completed">Completed</span>
          } else if (item.status === 'NotFound') {
            statusBadge = <span className="queue-status-pill notfound">Not Found</span>
          } else if (item.status === 'Failed') {
            statusBadge = (
              <span className="queue-status-pill failed" title={item.errorMessage || 'Execution Error'}>
                Failed
              </span>
            )
          }

          let verdictElement = <span className="clean-pill">—</span>
          if (item.isLeakageSuspect) {
            verdictElement = (
              <span className="queue-status-pill leakage">
                ⚠️ Leakage ({item.anomalyScore.toFixed(2)})
              </span>
            )
          } else if (item.status === 'Completed') {
            verdictElement = <span className="queue-status-pill consistentok">Consistent OK</span>
          } else if (item.status === 'NotFound') {
            verdictElement = <span className="queue-status-pill notfound">Not In DB</span>
          }

          return (
            <article key={item.id} className={`operations-record ${isSelected ? 'selected' : ''}`}>
              <div className="operations-record-select">
                <input
                  type="checkbox"
                  checked={isSelected}
                  onChange={() => toggleSelectItem(item.id)}
                  aria-label={`Select item ${item.productId}`}
                />
                <span>#{rowNumber}</span>
              </div>

              <div className="operations-record-identity">
                <strong>{item.productId}</strong>
                <span>{item.lineCode || 'ALL'} · {item.tableScanned || 'No source table'}</span>
              </div>

              <div className="operations-record-status">
                {statusBadge}
                {verdictElement}
              </div>

              <div className="operations-record-values">
                <div><span>Resolved FK</span><strong className="mono-code">{item.resolvedFkId || '—'}</strong></div>
                <div><span>Station judge</span><strong>{item.stationJudge ? <JudgeBadge value={item.stationJudge} /> : '—'}</strong></div>
                <div><span>Defects</span><strong className={item.defectCount > 0 ? 'danger-value' : ''}>{item.defectCount}</strong></div>
                <div><span>Anomaly score</span><strong>{item.anomalyScore > 0 ? item.anomalyScore.toFixed(2) : '—'}</strong></div>
              </div>

              <div className="operations-record-actions">
                <button
                  type="button"
                  className="secondary compact-button"
                  onClick={() => void handleInspect(item)}
                >
                  Inspect
                </button>
                {item.status === 'Failed' || item.status === 'NotFound' ? (
                    <button
                      type="button"
                      className="ghost-button compact-button"
                      onClick={() => void handleRetry([item.id])}
                    >
                      Retry
                  </button>
                ) : null}
              </div>
            </article>
          )
        })}
        </div>
      </section>

      <Pagination
        page={page}
        totalPages={totalPages}
        totalCount={totalCount}
        onPage={(p) => setPage(p)}
      />

      {/* 5. Defect Detail Modal / Drawer */}
      <Modal
        title={activeItemDetail ? `Product Detail: ${activeItemDetail.queueItem.productId}` : 'Product Details'}
        description="Comprehensive MariaDB inspection records and retrieved defect evidence"
        open={inspectModalOpen}
        onClose={() => {
          setInspectModalOpen(false)
          setActiveItemDetail(null)
          setZoomedImageUri(null)
        }}
        wide
      >
        {activeItemLoading || !activeItemDetail ? (
          <p className="empty-state">Loading product and defect details...</p>
        ) : (
          <div className="tab-stack">
            {/* Anomaly Assessment Banner */}
            {activeItemDetail.queueItem.isLeakageSuspect ? (
              <div
                style={{
                  background: '#ffe4e6',
                  border: '2px solid #f43f5e',
                  borderRadius: 10,
                  padding: 14,
                  color: '#881337',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                  <span style={{ fontSize: 20 }}>⚠️</span>
                  <strong style={{ fontSize: 15 }}>CRITICAL ESCAPE RISK (LEAKAGE DETECTED)</strong>
                </div>
                <p style={{ margin: 0, fontSize: 13 }}>
                  This product was recorded as <strong>{activeItemDetail.queueItem.stationJudge || 'PASS/OK'}</strong> at the vision inspection station, but remote defect tables contain <strong>{activeItemDetail.defects.length} defect row(s)</strong>.
                  Anomaly Score: <strong>{activeItemDetail.queueItem.anomalyScore.toFixed(2)}</strong>.
                </p>
                {activeItemDetail.queueItem.errorMessage ? (
                  <p style={{ margin: '6px 0 0', fontSize: 12, color: '#9f1239' }}>
                    Note: {activeItemDetail.queueItem.errorMessage}
                  </p>
                ) : null}
              </div>
            ) : activeItemDetail.queueItem.status === 'Completed' ? (
              <div
                style={{
                  background: '#f0fdf4',
                  border: '1px solid #86efac',
                  borderRadius: 10,
                  padding: 12,
                  color: '#166534',
                }}
              >
                <strong>Consistent Clean Unit:</strong> Zero defect records found matching foreign key <code>{activeItemDetail.queueItem.resolvedFkId}</code>. Unit is confirmed OK.
              </div>
            ) : null}

            {/* Product Metadata Grid */}
            <div className="modal-detail-grid">
              <div className="modal-detail-field">
                <span>Product Serial ID</span>
                <strong>{activeItemDetail.queueItem.productId}</strong>
              </div>
              <div className="modal-detail-field">
                <span>Inspection Line</span>
                <strong>{activeItemDetail.queueItem.lineCode || 'ALL'}</strong>
              </div>
              <div className="modal-detail-field">
                <span>Station Judge</span>
                <div>
                  {activeItemDetail.queueItem.stationJudge ? (
                    <JudgeBadge value={activeItemDetail.queueItem.stationJudge} />
                  ) : (
                    '—'
                  )}
                </div>
              </div>
              <div className="modal-detail-field">
                <span>Resolved Fk_ID</span>
                <code>{activeItemDetail.queueItem.resolvedFkId || '—'}</code>
              </div>
              <div className="modal-detail-field">
                <span>Source Table / Connection</span>
                <strong>{activeItemDetail.queueItem.tableScanned || '—'}</strong>
              </div>
              <div className="modal-detail-field">
                <span>Processed Timestamp</span>
                <strong>{formatDate(activeItemDetail.queueItem.processedAtUtc || activeItemDetail.queueItem.createdAtUtc)}</strong>
              </div>
            </div>

            {/* Retrieved Defect Records Table */}
            <div>
              <div className="section-heading" style={{ marginBottom: 8, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <div>
                  <h3 style={{ margin: 0, fontSize: 15 }}>
                    Retrieved Defect Records ({activeItemDetail.defects.length})
                  </h3>
                  <small className="muted">Extracted from target MariaDB defect tables using relational key</small>
                </div>
                {activeItemDetail.queueItem.productId && (
                  <a
                    href={storageApi.productDefectPackUrl(activeItemDetail.queueItem.productId)}
                    target="_blank"
                    rel="noreferrer"
                    style={{
                      fontSize: '12px',
                      color: '#7c3aed',
                      fontWeight: 600,
                      textDecoration: 'underline',
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '4px',
                    }}
                  >
                    📦 Open Full Product Defect Pack (.json.gz)
                  </a>
                )}
              </div>

              {activeItemDetail.defects.length === 0 ? (
                <p className="empty-state">No defect records found for this unit.</p>
              ) : (
                <div className="operations-defect-list">
                  {activeItemDetail.defects.map((def: DefectRecord, idx: number) => {
                        let parsedCoordStr: string | null = null
                        if (def.coordinateX != null && def.coordinateY != null) {
                          parsedCoordStr = `(${def.coordinateX.toFixed(1)}, ${def.coordinateY.toFixed(1)})`
                        } else if (def.featureJson) {
                          try {
                            const p = JSON.parse(def.featureJson)
                            if (p && typeof p === 'object') {
                              const x = p.G001_RealX ?? p.G005_PxlX
                              const y = p.G002_RealY ?? p.G006_PxlY
                              if (x != null && y != null) {
                                parsedCoordStr = `(${String(x)}, ${String(y)})`
                              }
                            }
                          } catch {}
                        }

                    return (
                      <article key={def.id || idx} className="operations-defect-row">
                        <div className="operations-defect-index">#{idx + 1}</div>
                        <div className="operations-defect-identity">
                          <strong>{def.defectName || 'Unnamed defect'}</strong>
                          <span>{def.defectType || 'Unspecified type'}</span>
                          {def.reason ? <small>{def.reason}</small> : null}
                        </div>
                        <div className="operations-defect-status">
                          <JudgeBadge value={def.judge} />
                          {def.severity ? <StatusPill value={def.severity} /> : null}
                        </div>
                        <div className="operations-record-values defect-values">
                          <div><span>Table</span><strong className="mono-code">{def.defectTable || '—'}</strong></div>
                          <div><span>Spec</span><strong>{def.specName ? `${def.specName}${def.specJudge ? ` (${def.specJudge})` : ''}` : '—'}</strong></div>
                          <div><span>Equipment</span><strong>{def.eqpName || '—'}</strong></div>
                          <div><span>Operator / Priority</span><strong>{def.emNo || '—'}{def.priority != null ? ` / P${def.priority}` : ''}</strong></div>
                          <div><span>Coordinates</span><strong className="mono-code">{parsedCoordStr || '—'}</strong></div>
                        </div>
                        <div className="operations-record-actions">
                          <button type="button" className="secondary compact-button" onClick={() => setActiveDefectForInspection(def)}>
                            Features & JSON
                          </button>
                          {def.imageUri ? (
                            <button type="button" className="ghost-button compact-button" onClick={() => setZoomedImageUri(def.imageUri || null)}>
                              View Image
                            </button>
                          ) : null}
                        </div>
                      </article>
                    )
                  })}
                </div>
              )}
            </div>

            {/* Image zoom popup */}
            {zoomedImageUri ? (
              <div
                style={{
                  background: '#0f172a',
                  padding: 16,
                  borderRadius: 12,
                  textAlign: 'center',
                  marginTop: 12,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
                  <span style={{ color: '#cbd5e1', fontSize: 12 }}>Defect Image Zoom</span>
                  <button
                    type="button"
                    className="ghost-button"
                    style={{ color: '#fff', padding: '2px 8px' }}
                    onClick={() => setZoomedImageUri(null)}
                  >
                    ✕ Close Preview
                  </button>
                </div>
                <img
                  src={zoomedImageUri}
                  alt="Defect visual evidence"
                  style={{ maxWidth: '100%', maxHeight: 400, borderRadius: 8, objectFit: 'contain' }}
                />
              </div>
            ) : null}

            <div className="actions" style={{ justifyContent: 'flex-end', marginTop: 16 }}>
              <button
                type="button"
                className="secondary"
                onClick={() => {
                  setInspectModalOpen(false)
                  setActiveItemDetail(null)
                  setZoomedImageUri(null)
                }}
              >
                Close
              </button>
            </div>
          </div>
        )}
      </Modal>

      <Modal
        title={`Defect Feature Analysis · ${activeDefectForInspection?.defectName || 'Defect'}`}
        description="Parsed coordinates, inspection metrics, and raw feature JSON viewer"
        open={Boolean(activeDefectForInspection)}
        onClose={() => setActiveDefectForInspection(null)}
        wide
      >
        {activeDefectForInspection ? <DefectFeatureInspector defect={activeDefectForInspection} /> : null}
      </Modal>
    </div>
  )
}
