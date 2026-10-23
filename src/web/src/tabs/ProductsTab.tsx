import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { aiVerificationApi, connectionsApi, defectsApi, productsApi, storageApi } from '../api'
import { AiDefectVerdict, AiStatusBadge } from '../components/AiStatusBadge'
import { formatDateTime, formNumber, formString, paginate } from '../format'
import { usePolling } from '../lib/usePolling'
import { DataTable, DebouncedInput, DefectFeatureInspector, extractOriginalDefectVp, Field, Icon, JudgeBadge, Modal, Pagination, StatusPill, useLoadStatus } from '../ui'
import type { AiProductResult, AiProductSummary, AiSubmitResult, DatabaseConnection, DefectRecord, MultiTableScanResponse, Notice, ProductFilterOptions, ProductHandoff, ProductRecord } from '../types'

const PRODUCT_COLUMNS = ['Key ID', 'Judge', 'AI', 'OP Marking', 'OP Defect / Note', 'Zone', 'Defect Name', 'Panel', 'Panel judge', 'Machine', 'Line', 'DateTime Inspection', 'Defect Pack']

export function ProductsTab({
  onNotice,
  onAnalyzeDefects,
}: {
  onNotice: (notice: Notice) => void
  onAnalyzeDefects?: (handoff: ProductHandoff) => void
}) {
  const [products, setProducts] = useState<ProductRecord[]>([])
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [search, setSearch] = useState('')
  const [judge, setJudge] = useState('')
  const [zoneFilter, setZoneFilter] = useState('')
  const [defectFilter, setDefectFilter] = useState('')
  const [machineFilter, setMachineFilter] = useState('')
  const [lineFilter, setLineFilter] = useState('')
  const [filterOptions, setFilterOptions] = useState<ProductFilterOptions>({ judges: [], zones: [], defectNames: [], machineCodes: [], lineCodes: [] })
  const [applied, setApplied] = useState({ search: '', judge: '', zoneFilter: '', defectFilter: '', machineFilter: '', lineFilter: '' })
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [scanResult, setScanResult] = useState<MultiTableScanResponse | null>(null)
  const [defects, setDefects] = useState<DefectRecord[]>([])
  const [selectedKey, setSelectedKey] = useState('')
  const [productOpen, setProductOpen] = useState(false)
  const [defectOpen, setDefectOpen] = useState(false)
  const [scanOpen, setScanOpen] = useState(false)
  const [inspectOpen, setInspectOpen] = useState(false)
  const [jsonOpen, setJsonOpen] = useState<DefectRecord | null>(null)
  const [jsonOpenVp, setJsonOpenVp] = useState<string>('0')
  const [busy, setBusy] = useState(false)
  const [pane, setPane] = useState<'catalog' | 'scan'>('catalog')
  const [scanPage, setScanPage] = useState(1)
  const [defectPage, setDefectPage] = useState(1)
  const [aiStatuses, setAiStatuses] = useState<Record<string, AiProductSummary>>({})
  const [aiDetail, setAiDetail] = useState<AiProductResult | null>(null)
  const [aiBusy, setAiBusy] = useState('')
  const pagedScan = paginate(scanResult?.items ?? [], scanPage)
  const pagedDefects = paginate(defects, defectPage)

  const catalogLoad = useLoadStatus()
  const { begin: beginCatalog, succeed: succeedCatalog, fail: failCatalog } = catalogLoad

  const load = useCallback(async (nextPage = 1) => {
    beginCatalog()
    try {
      const [payload, connectionPage, options] = await Promise.all([
        productsApi.list({ search: applied.search, judge: applied.judge, machineCode: applied.machineFilter, lineCode: applied.lineFilter, zone: applied.zoneFilter, defectName: applied.defectFilter, page: nextPage, pageSize: 25 }),
        connectionsApi.list({ page: 1, pageSize: 100 }),
        productsApi.filterOptions({ machineCode: machineFilter || undefined, lineCode: lineFilter || undefined }),
      ])
      setProducts(payload.items)
      setPage(payload.page)
      setTotalPages(payload.totalPages)
      setTotalCount(payload.totalCount)
      setConnections(connectionPage.items)
      setFilterOptions(options)
      succeedCatalog()
    } catch (error) {
      failCatalog(error)
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load products.' })
    }
  }, [applied, machineFilter, lineFilter, onNotice, beginCatalog, succeedCatalog, failCatalog])

  useEffect(() => {
    void load(1)
  }, [load])

  // The AI column is optional: if the lookup fails the catalog still works and the column stays empty.
  const loadAiStatuses = useCallback(async (keyIds: string[]) => {
    if (keyIds.length === 0) {
      setAiStatuses({})
      return
    }
    try {
      const list = await aiVerificationApi.productStatuses(keyIds)
      setAiStatuses(Object.fromEntries(list.map((item) => [item.keyId, item])))
    } catch {
      /* keep the previous values */
    }
  }, [])

  useEffect(() => {
    void loadAiStatuses(products.map((product) => product.keyId))
  }, [products, loadAiStatuses])

  const hasPendingAi = Object.values(aiStatuses).some((item) => item.aiStatus === 'Pending')
  usePolling(() => loadAiStatuses(products.map((product) => product.keyId)), 15000, hasPendingAi)

  function describeAiSubmit(result: AiSubmitResult): string {
    if (result.message) return result.message
    if (result.noMapping > 0) return 'No station mapping for this machine / line / model. Add one in Configuration & Data → AI Verification.'
    if (result.noImage > 0) return 'The defect image could not be retrieved, so nothing was sent.'
    if (result.alreadyJudged > 0) return 'Already sent to the AI. Use Resend to send it again.'
    return 'Nothing to send: this product has no defect images for the AI to judge.'
  }

  async function sendToAi(keyId: string, resubmit: boolean) {
    setAiBusy(keyId)
    try {
      const result = await aiVerificationApi.submit([keyId], resubmit)
      if (result.submitted > 0) {
        onNotice({ tone: 'success', message: `Sent ${result.submitted} defect image(s) of ${keyId} to the AI.` })
      } else {
        onNotice({ tone: result.transient ? 'error' : 'info', message: describeAiSubmit(result) })
      }
      await loadAiStatuses(products.map((product) => product.keyId))
      if (selectedKey === keyId) setAiDetail(await aiVerificationApi.product(keyId).catch(() => null))
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to send to the AI.' })
    } finally {
      setAiBusy('')
    }
  }

  function aiActions(keyId: string) {
    const status = aiStatuses[keyId]?.aiStatus
    if (status === 'Pending') return null
    const resend = status !== undefined
    if (resend && status !== 'AiUnavailable') return null
    return (
      <button
        type="button"
        className="secondary compact-button"
        disabled={aiBusy === keyId}
        onClick={() => void sendToAi(keyId, resend)}
        title={resend ? 'Send the defect images to the AI again' : 'Send the defect images to the AI for an OK/NG verdict'}
      >
        {aiBusy === keyId ? 'Sending…' : resend ? 'Resend' : 'Send to AI'}
      </button>
    )
  }

  async function openDefects(keyId: string) {
    setSelectedKey(keyId)
    setInspectOpen(true)
    setAiDetail(null)
    void aiVerificationApi.product(keyId).then(setAiDetail).catch(() => setAiDetail(null))
    try {
      setDefects(await productsApi.defects(keyId))
      setDefectPage(1)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load defects.' })
    }
  }

  async function createProduct(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      await productsApi.create({
        keyId: formString(event, 'keyId'),
        judge: formString(event, 'judge'),
        panelId: formString(event, 'panelId'),
        panelJudge: formString(event, 'panelJudge'),
        nameDefect: formString(event, 'nameDefect'),
        zone: formString(event, 'zone') || null,
        defectName: formString(event, 'defectName') || null,
        machineCode: formString(event, 'machineCode'),
        lineCode: formString(event, 'lineCode'),
      })
      setProductOpen(false)
      onNotice({ tone: 'success', message: 'Product record created.' })
      await load(1)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Create product failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function createDefect(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      const created = await defectsApi.create({
        keyId: formString(event, 'keyId'),
        defectTable: formString(event, 'defectTable'),
        defectName: formString(event, 'defectName'),
        defectType: formString(event, 'defectType'),
        componentName: formString(event, 'componentName') || null,
        coordinateX: formString(event, 'coordinateX') ? formNumber(event, 'coordinateX') : null,
        coordinateY: formString(event, 'coordinateY') ? formNumber(event, 'coordinateY') : null,
        severity: formString(event, 'severity'),
        judge: formString(event, 'judge'),
        confidenceScore: formNumber(event, 'confidenceScore', 0),
        imageUri: formString(event, 'imageUri') || null,
        featureJson: formString(event, 'featureJson') || null,
        customFeatureJson: formString(event, 'customFeatureJson') || null,
      })
      setDefectOpen(false)
      onNotice({ tone: 'success', message: `Defect created for ${created.keyId}.` })
      if (selectedKey === created.keyId) await openDefects(created.keyId)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Create defect failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function runScan(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      const keyIds = formString(event, 'keyIds')
        .split(/[\n,]/)
        .map((value) => value.trim())
        .filter(Boolean)
      const result = await productsApi.scan({
        connectionId: formString(event, 'connectionId') || null,
        keyIds: keyIds.length ? keyIds : null,
        panelId: formString(event, 'panelId') || null,
        machineCode: formString(event, 'machineCode') || null,
        lineCode: formString(event, 'lineCode') || null,
        includeDefects: true,
      })
      setScanResult(result)
      setScanPage(1)
      setScanOpen(false)
      setPane('scan')
      onNotice({
        tone: 'success',
        message: `Scanned ${result.scannedProductsCount} products · ${result.overkillCount} overkill · ${result.leakageCount} leakage.`,
      })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Multi-table scan failed.' })
    } finally {
      setBusy(false)
    }
  }

  // Rebuilt only when the data or the AI state changes, so typing in the filter box does not redraw every row.
  /* eslint-disable react-hooks/exhaustive-deps */
  const productRows = useMemo(
    () =>
      products.map((product) => ({
                  id: product.id,
                  cells: [
                    <button key="id" type="button" className="link-button" onClick={() => void openDefects(product.keyId)}>
                      {product.keyId}
                    </button>,
                    <JudgeBadge key="j" value={product.judge} />,
                    <div key="ai" style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                      <AiStatusBadge summary={aiStatuses[product.keyId]} />
                      {aiActions(product.keyId)}
                    </div>,
                    product.opReviewMark ? (
                      <span
                        key="op"
                        className="tag-badge"
                        style={product.opReviewMark === 'Overkill'
                          ? { background: '#fef3c7', color: '#b45309', borderColor: '#fcd34d', fontWeight: 700 }
                          : { background: '#ede9fe', color: '#6d28d9', borderColor: '#c4b5fd', fontWeight: 700 }}
                      >
                        {product.opReviewMark}
                      </span>
                    ) : (
                      <span key="op" />
                    ),
                    product.opReviewDefectName || product.opReviewNote ? (
                      <div key="op-detail" title={product.opReviewNote || undefined}>
                        <div>{product.opReviewDefectName || ''}</div>
                        {product.opReviewMappedDefect && <div className="text-xs text-muted">→ {product.opReviewMappedDefect}</div>}
                      </div>
                    ) : <span key="op-detail" />,
                    product.zone ? (
                      <span key="z" className="tag-badge" style={{ background: '#f0fdf4', color: '#15803d', borderColor: '#bbf7d0' }}>
                        {product.zone}
                      </span>
                    ) : (
                      <span key="z" className="muted">—</span>
                    ),
                    product.defectName ? (
                      <span key="d" className="tag-badge" style={{ background: '#fef2f2', color: '#b91c1c', borderColor: '#fecaca' }}>
                        {product.defectName}
                      </span>
                    ) : product.nameDefect && product.nameDefect !== 'None' ? (
                      <span key="d">{product.nameDefect}</span>
                    ) : (
                      <span key="d" className="muted">—</span>
                    ),
                    product.panelId,
                    product.panelJudge,
                    product.machineCode,
                    product.lineCode,
                    formatDateTime(product.inspectedAtUtc),
                    product.defectPackUrl ? (
                      <div key="pack" style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                        <a
                          href={storageApi.productDefectPackUrl(product.keyId)}
                          target="_blank"
                          rel="noreferrer"
                          className="tag-badge"
                          style={{ background: '#f5f3ff', color: '#6d28d9', borderColor: '#ddd6fe', fontWeight: 600, textDecoration: 'none' }}
                          title="Download/View unified defect pack JSON"
                        >
                          <Icon name="package" className="icon inline-icon" />Defect Pack
                        </a>
                        {onAnalyzeDefects && (
                          <button
                            type="button"
                            className="compact-button secondary"
                            onClick={() => onAnalyzeDefects({ keyId: product.keyId, sourceUri: product.defectPackUrl ?? undefined })}
                            title="Decompress & parse defect records in Defect & Feature Analysis"
                            style={{ fontSize: '12px', padding: '2px 8px' }}
                          >
                            Analyze Defects
                          </button>
                        )}
                      </div>
                    ) : (
                      <div key="pack" style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                        <span className="muted" style={{ fontSize: '12px' }}>—</span>
                        {onAnalyzeDefects && (
                          <button
                            type="button"
                            className="compact-button secondary"
                            onClick={() => onAnalyzeDefects({ keyId: product.keyId })}
                            title="Analyze defects by Key ID in Defect & Feature Analysis"
                            style={{ fontSize: '12px', padding: '1px 6px' }}
                          >
                            Analyze Defects
                          </button>
                        )}
                      </div>
                    ),
                  ],
                })),
    [products, aiStatuses, aiBusy, selectedKey, onAnalyzeDefects],
  )
  /* eslint-enable react-hooks/exhaustive-deps */

  return (
    <div className="tab-stack">
      <div className="workspace-pane-tabs" role="tablist" aria-label="Products sections">
        <button type="button" role="tab" aria-selected={pane === 'catalog'} className={pane === 'catalog' ? 'active' : ''} onClick={() => setPane('catalog')}>
          Product catalog
          <small>{totalCount.toLocaleString()} records</small>
        </button>
        <button type="button" role="tab" aria-selected={pane === 'scan'} className={pane === 'scan' ? 'active' : ''} onClick={() => setPane('scan')}>
          Scan result
          <small>{scanResult ? `${scanResult.scannedProductsCount} products` : 'None yet'}</small>
        </button>
      </div>

      {pane === 'catalog' && (
      <>
      <div className="section-heading">
        <div>
          <p className="eyebrow">GET /products</p>
          <h2>Product records</h2>
        </div>
        <div className="actions">
          <button type="button" className="secondary" onClick={() => setScanOpen(true)}>
            Multi-table scan
          </button>
          <button type="button" className="secondary" onClick={() => setDefectOpen(true)}>
            Add defect
          </button>
          <button type="button" onClick={() => setProductOpen(true)}>
            Add product
          </button>
        </div>
      </div>

      <form
        className="compact-filter-bar"
        onSubmit={(event) => {
          event.preventDefault()
          setApplied({ search, judge, zoneFilter, defectFilter, machineFilter, lineFilter })
        }}
      >
        <Field label="Search" className="filter-search">
          <DebouncedInput
            value={search}
            onValueChange={setSearch}
            placeholder="Key ID, Zone, Defect..."
          />
        </Field>
        <Field label="Judge">
          <select value={judge} onChange={(event) => setJudge(event.target.value)}>
            <option value="">All judges</option>
            {filterOptions.judges.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </Field>
        <Field label="Zone">
          <select value={zoneFilter} onChange={(event) => setZoneFilter(event.target.value)}>
            <option value="">All zones</option>
            {filterOptions.zones.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </Field>
        <Field label="Defect Name">
          <select value={defectFilter} onChange={(event) => setDefectFilter(event.target.value)}>
            <option value="">All defects</option>
            {filterOptions.defectNames.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </Field>
        <Field label="Machine">
          <select value={machineFilter} onChange={(event) => { setMachineFilter(event.target.value); setLineFilter('') }}>
            <option value="">All machines</option>
            {filterOptions.machineCodes.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </Field>
        <Field label="Line">
          <select value={lineFilter} onChange={(event) => setLineFilter(event.target.value)}>
            <option value="">All lines</option>
            {filterOptions.lineCodes.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </Field>
        <button type="submit" className="ghost-button">
          Apply
        </button>
        {(applied.search || applied.judge || applied.zoneFilter || applied.defectFilter || applied.machineFilter || applied.lineFilter) && (
          <button
            type="button"
            className="ghost-button"
            onClick={() => {
              setSearch('')
              setJudge('')
              setZoneFilter('')
              setDefectFilter('')
              setMachineFilter('')
              setLineFilter('')
              setApplied({ search: '', judge: '', zoneFilter: '', defectFilter: '', machineFilter: '', lineFilter: '' })
            }}
          >
            Reset
          </button>
        )}
      </form>

      <DataTable
        columns={PRODUCT_COLUMNS}
        rows={productRows}
        status={catalogLoad.status}
        error={catalogLoad.error}
        onRetry={() => void load(page)}
      />
      <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPage={(next) => void load(next)} />
      </>
      )}

      {pane === 'scan' && scanResult && (
        <section className="panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">POST /products/scan</p>
              <h2>Latest multi-table scan</h2>
            </div>
            <span>
              {scanResult.scannedProductsCount} products · {scanResult.totalDefectsFound} defects · {scanResult.executionLatencyMs} ms
            </span>
          </div>
          <DataTable
            columns={['Key ID', 'Judge', 'OP Marking', 'OP Defect', 'Machine', 'Line', 'Zone', 'Defect Name', 'Anomaly', 'Score', 'Defects', 'Notes']}
            rows={pagedScan.items.map((item) => ({
              id: item.product.id,
              cells: [
                item.product.keyId,
                <JudgeBadge key="j" value={item.product.judge} />,
                item.product.opReviewMark || '',
                item.product.opReviewDefectName || '',
                item.product.machineCode,
                item.product.lineCode,
                item.product.zone ? (
                  <span key="z" className="tag-badge">
                    {item.product.zone}
                  </span>
                ) : (
                  '—'
                ),
                item.product.defectName || item.product.nameDefect || '—',
                <StatusPill key="a" value={item.anomalyStatus} />,
                item.anomalyScore.toFixed(2),
                item.totalDefects,
                item.analysisNotes,
              ],
            }))}
          />
          <Pagination page={pagedScan.page} totalPages={pagedScan.totalPages} totalCount={pagedScan.totalCount} onPage={setScanPage} />
        </section>
      )}

      {pane === 'scan' && !scanResult && (
        <div className="dashboard-report-empty">
          <strong>No scan result yet</strong>
          <span>Run Multi-table scan from the catalog tab. The result will appear here instead of stretching the product list.</span>
        </div>
      )}

      <Modal title="Create product" description="POST /products" open={productOpen} onClose={() => setProductOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void createProduct(event)}>
          <div className="query-form-grid">
            <Field label="Key ID" required>
              <input name="keyId" required />
            </Field>
            <Field label="Judge" required>
              <input name="judge" defaultValue="OK" required />
            </Field>
            <Field label="Panel ID" required>
              <input name="panelId" required />
            </Field>
            <Field label="Panel judge" required>
              <input name="panelJudge" defaultValue="OK" required />
            </Field>
            <Field label="Defect Name">
              <input name="defectName" placeholder="e.g. Scratch, Particle, NG-01" />
            </Field>
            <Field label="Zone">
              <input name="zone" placeholder="e.g. Zone A, Station 1" />
            </Field>
            <Field label="Name defect" required>
              <input name="nameDefect" defaultValue="None" required />
            </Field>
            <Field label="Machine code" required>
              <input name="machineCode" required />
            </Field>
            <Field label="Line code" required>
              <input name="lineCode" required />
            </Field>
          </div>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Save
            </button>
            <button type="button" className="ghost-button" onClick={() => setProductOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      <Modal title="Create defect" description="POST /defects" open={defectOpen} onClose={() => setDefectOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void createDefect(event)}>
          <div className="query-form-grid">
            <Field label="Key ID" required>
              <input name="keyId" defaultValue={selectedKey} required />
            </Field>
            <Field label="Defect table" required>
              <input name="defectTable" required />
            </Field>
            <Field label="Defect name" required>
              <input name="defectName" required />
            </Field>
            <Field label="Defect type" required>
              <input name="defectType" required />
            </Field>
            <Field label="Component">
              <input name="componentName" />
            </Field>
            <Field label="Severity" required>
              <input name="severity" defaultValue="Minor" required />
            </Field>
            <Field label="Judge" required>
              <input name="judge" defaultValue="NG" required />
            </Field>
            <Field label="Confidence" required>
              <input name="confidenceScore" type="number" step="0.01" defaultValue="0.9" required />
            </Field>
            <Field label="X">
              <input name="coordinateX" type="number" step="0.01" />
            </Field>
            <Field label="Y">
              <input name="coordinateY" type="number" step="0.01" />
            </Field>
            <Field label="Image URI">
              <input name="imageUri" />
            </Field>
          </div>
          <Field label="Feature JSON">
            <textarea name="featureJson" className="code-textarea" />
          </Field>
          <Field label="Custom Feature JSON">
            <textarea name="customFeatureJson" className="code-textarea" />
          </Field>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Save
            </button>
            <button type="button" className="ghost-button" onClick={() => setDefectOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      <Modal title="Multi-table product scan" description="POST /products/scan" open={scanOpen} onClose={() => setScanOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void runScan(event)}>
          <Field label="Database connection">
            <select name="connectionId" defaultValue="">
              <option value="">None</option>
              {connections.map((connection) => (
                <option key={connection.id} value={connection.id}>
                  {connection.name}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Key IDs">
            <textarea name="keyIds" placeholder="One per line" />
          </Field>
          <div className="query-form-grid">
            <Field label="Panel ID">
              <input name="panelId" />
            </Field>
            <Field label="Machine code">
              <input name="machineCode" />
            </Field>
            <Field label="Line code">
              <input name="lineCode" />
            </Field>
          </div>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Run scan
            </button>
            <button type="button" className="ghost-button" onClick={() => setScanOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      <Modal title={`Defects · ${selectedKey}`} description="GET /products/{keyId}/defects" open={inspectOpen} onClose={() => setInspectOpen(false)} wide>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
          <span style={{ fontSize: '12px', color: '#64748b' }}>
            List of individual defects for product <strong>{selectedKey}</strong>
            <span style={{ marginLeft: '8px' }}><AiStatusBadge summary={aiStatuses[selectedKey]} /></span>
            <span style={{ marginLeft: '6px' }}>{aiActions(selectedKey)}</span>
          </span>
          {selectedKey && (
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <a
                href={storageApi.productDefectPackUrl(selectedKey)}
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
                <Icon name="package" className="icon inline-icon" />Open Full Product Defect Pack (.json.gz)
              </a>
              {onAnalyzeDefects && (
                <button
                  type="button"
                  className="compact-button secondary"
                  onClick={() => {
                    setInspectOpen(false)
                    onAnalyzeDefects({
                      keyId: selectedKey,
                      sourceUri: storageApi.productDefectPackUrl(selectedKey),
                    })
                  }}
                  style={{ fontSize: '12px', padding: '2px 8px' }}
                  title="Decompress & parse defect records in Defect & Feature Analysis"
                >
                  Analyze Defects
                </button>
              )}
            </div>
          )}
        </div>
        <DataTable
          columns={['Table', 'Defect Name', 'Judge / Reason', 'AI verdict', 'Spec / Equipment', 'Priority', 'VP Angles (Click)', 'Features & Analysis']}
          rows={pagedDefects.items.map((defect) => ({
            id: defect.id,
            cells: [
              <code key="tbl">{defect.defectTable}</code>,
              <div key="name" style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <strong style={{ color: '#0f172a' }}>{defect.defectName}</strong>
                <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap', alignItems: 'center' }}>
                  {defect.defectType && <span className="tag-badge">{defect.defectType}</span>}
                  {defect.modelCode && defect.modelCode !== 'UNASSIGNED' && (
                    <span className="tag-badge" style={{ background: '#f0fdf4', color: '#166534', borderColor: '#bbf7d0', fontSize: '12px' }}>
                      {defect.modelCode}
                    </span>
                  )}
                </div>
              </div>,
              <div key="judge" style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <div style={{ display: 'flex', gap: '4px', alignItems: 'center' }}>
                  <JudgeBadge value={defect.judge} />
                  {defect.severity && (
                    <span className={`severity-tag ${defect.severity.toLowerCase()}`}>{defect.severity}</span>
                  )}
                </div>
                {defect.reason && <span style={{ fontSize: '12px', color: '#dc2626' }}>{defect.reason}</span>}
              </div>,
              <AiDefectVerdict key="ai" result={aiDetail?.defects.find((item) => item.defectId.toLowerCase() === defect.id.toLowerCase())} />,
              <div key="spec" style={{ fontSize: '12px', display: 'flex', flexDirection: 'column', gap: '2px' }}>
                {defect.specName ? (
                  <span>
                    Spec: <strong>{defect.specName}</strong> {defect.specJudge ? `(${defect.specJudge})` : ''}
                  </span>
                ) : null}
                {defect.eqpName ? (
                  <span style={{ color: '#64748b' }}>
                    Eqp: {defect.eqpName} {defect.emNo ? `· ${defect.emNo}` : ''}
                  </span>
                ) : null}
                {!defect.specName && !defect.eqpName && <span className="muted">—</span>}
              </div>,
              defect.priority != null ? (
                <span key="p" className="clean-pill" style={{ fontWeight: 600 }}>{defect.priority}</span>
              ) : (
                '—'
              ),
              <div key="vp" style={{ display: 'flex', alignItems: 'center', gap: '3px', flexWrap: 'wrap' }}>
                <button
                  type="button"
                  className="secondary compact-button"
                  style={{ fontSize: '12px', padding: '2px 6px', fontWeight: 700, background: '#eff6ff', borderColor: '#93c5fd', color: '#1d4ed8' }}
                  title="Open default VP0 thumbnail"
                  onClick={() => {
                    setJsonOpenVp('0')
                    setJsonOpen(defect)
                  }}
                >
                  VP0 (Default)
                </button>
                <button
                  type="button"
                  className="compact-button"
                  style={{ fontSize: '12px', padding: '2px 6px', fontWeight: 700, background: '#f5f3ff', borderColor: '#c4b5fd', color: '#6d28d9' }}
                  title={`Click to open original VP from image_path (VP${extractOriginalDefectVp(defect)})`}
                  onClick={() => {
                    setJsonOpenVp(extractOriginalDefectVp(defect))
                    setJsonOpen(defect)
                  }}
                >
                  Path Original (VP{extractOriginalDefectVp(defect)})
                </button>
                {['1', '2', '3', '4'].filter((v) => v !== extractOriginalDefectVp(defect)).map((vp) => (
                  <button
                    key={vp}
                    type="button"
                    className="compact-button"
                    style={{ fontSize: '12px', padding: '2px 5px', background: '#f8fafc', borderColor: '#cbd5e1', color: '#475569' }}
                    title={`Click to open angle VP${vp}`}
                    onClick={() => {
                      setJsonOpenVp(vp)
                      setJsonOpen(defect)
                    }}
                  >
                    VP{vp}
                  </button>
                ))}
              </div>,
              <button
                key="json"
                type="button"
                className="secondary compact-button"
                onClick={() => {
                  setJsonOpenVp('0')
                  setJsonOpen(defect)
                }}
              >
                <Icon name="search" className="icon inline-icon" />Inspect Features & JSON
              </button>,
            ],
          }))}
        />
        <Pagination page={pagedDefects.page} totalPages={pagedDefects.totalPages} totalCount={pagedDefects.totalCount} onPage={setDefectPage} />
      </Modal>

      <Modal
        title={`Defect Feature Analysis · ${jsonOpen?.defectName || 'Defect'}`}
        description="Parsed coordinates, inspection metrics, and raw feature JSON viewer"
        open={Boolean(jsonOpen)}
        onClose={() => setJsonOpen(null)}
        wide
      >
        {jsonOpen ? <DefectFeatureInspector defect={jsonOpen} initialVp={jsonOpenVp} /> : null}
      </Modal>
    </div>
  )
}
