import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { connectionsApi, overkillPipelineApi, productsApi, scanListsApi, storageApi, verificationApi } from '../api'
import { formatDate, formatDateTime, paginate } from '../format'
import { DefectFeatureInspector, Field, JudgeBadge, Modal, Pagination, StatusPill } from '../ui'
import { BatchPipelinesModal } from '../components/BatchPipelinesModal'
import type {
  DatabaseConnection,
  DefectRecord,
  Notice,
  OverkillCandidate,
  OverkillPipelineConfig,
  OverkillPipelineExecution,
  ScanList,
  VerifyBatchResponse,
} from '../types'

export function OverkillOpsTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [lists, setLists] = useState<ScanList[]>([])
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [candidates, setCandidates] = useState<OverkillCandidate[]>([])
  const [pipelines, setPipelines] = useState<OverkillPipelineConfig[]>([])
  const [recentExecutions, setRecentExecutions] = useState<OverkillPipelineExecution[]>([])
  const [runningPipelineId, setRunningPipelineId] = useState<string | null>(null)
  const [machineCode, setMachineCode] = useState('AUTO')
  const [lineCode, setLineCode] = useState('All')
  const [busy, setBusy] = useState(false)
  const [verifyResult, setVerifyResult] = useState<VerifyBatchResponse | null>(null)
  const [candidatePage, setCandidatePage] = useState(1)
  const [verifyPage, setVerifyPage] = useState(1)
  const [listPage, setListPage] = useState(1)
  const [createOpen, setCreateOpen] = useState(false)
  const [listName, setListName] = useState('')
  const [inspectKey, setInspectKey] = useState<string | null>(null)
  const [candidateDefects, setCandidateDefects] = useState<DefectRecord[]>([])
  const [inspectLoading, setInspectLoading] = useState(false)
  const [selectedDefectForInspection, setSelectedDefectForInspection] = useState<DefectRecord | null>(null)
  const [batchProvisionOpen, setBatchProvisionOpen] = useState(false)

  async function openCandidateDefects(keyId: string) {
    setInspectKey(keyId)
    setInspectLoading(true)
    try {
      const defs = await productsApi.defects(keyId)
      setCandidateDefects(defs)
    } catch (err) {
      onNotice({
        tone: 'error',
        message: err instanceof Error ? err.message : 'Failed to fetch candidate defects',
      })
      setCandidateDefects([])
    } finally {
      setInspectLoading(false)
    }
  }

  const overkillLists = useMemo(
    () => lists.filter((l) => l.verificationCategory.toLowerCase() === 'overkill'),
    [lists],
  )
  const pagedLists = paginate(overkillLists, listPage, 6)
  const pagedCandidates = paginate(candidates, candidatePage, 8)
  const pagedVerify = paginate(verifyResult?.items ?? [], verifyPage, 8)

  const machines = useMemo(() => {
    const unique = [...new Set(connections.map((c) => c.machineCode).filter(Boolean))]
    return unique.sort((a, b) => a.localeCompare(b))
  }, [connections])

  const linesForMachine = useMemo(() => {
    const lines = connections
      .filter((c) => machineCode === 'AUTO' || !machineCode || c.machineCode === machineCode)
      .map((c) => c.lineCode)
      .filter(Boolean)
    return [...new Set(lines)].sort((a, b) => a.localeCompare(b))
  }, [connections, machineCode])

  const loadLists = useCallback(async () => {
    try {
      setLists(await scanListsApi.list())
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load scan lists.' })
    }
  }, [onNotice])

  const loadCandidates = useCallback(async () => {
    try {
      const data = await productsApi.overkill({
        machineCode: machineCode === 'AUTO' || machineCode === 'All' ? undefined : machineCode,
        lineCode: lineCode === 'All' ? undefined : lineCode,
      })
      setCandidates(data)
      setCandidatePage(1)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill candidates.' })
    }
  }, [machineCode, lineCode, onNotice])

  const loadPipelines = useCallback(async () => {
    try {
      const data = await overkillPipelineApi.getConfigs()
      setPipelines(data)
    } catch {
      // soft fail on background fetch
    }
  }, [])

  const loadRecentExecutions = useCallback(async () => {
    try {
      const res = await overkillPipelineApi.getExecutions()
      setRecentExecutions(res)
    } catch {
      // soft fail on background fetch
    }
  }, [])

  useEffect(() => {
    void loadLists()
    void loadCandidates()
    void loadPipelines()
    void loadRecentExecutions()
    void connectionsApi
      .list({ page: 1, pageSize: 100 })
      .then((payload) => {
        setConnections(payload.items ?? [])
      })
      .catch((error: unknown) => {
        onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load database connections.' })
      })
  }, [loadLists, loadCandidates, loadPipelines, loadRecentExecutions, onNotice])

  async function handleRunPipelineNow(id: string, name: string) {
    setRunningPipelineId(id)
    try {
      const res = await overkillPipelineApi.runConfig(id)
      onNotice({
        tone: res.status === 'Failed' ? 'error' : 'success',
        message: `Pipeline '${name}' completed: Processed ${res.productsScanned} products, ${res.defectsExtracted} defects (${res.status}).`,
      })
      await loadRecentExecutions()
      await loadCandidates()
    } catch (err) {
      onNotice({
        tone: 'error',
        message: err instanceof Error ? err.message : `Failed to run pipeline '${name}'.`,
      })
    } finally {
      setRunningPipelineId(null)
    }
  }

  function onMachineChange(nextMachine: string) {
    setMachineCode(nextMachine)
    if (nextMachine === 'AUTO') {
      setLineCode('All')
      return
    }
    const nextLines = connections
      .filter((c) => c.machineCode === nextMachine)
      .map((c) => c.lineCode)
    setLineCode(nextLines[0] || 'All')
  }

  // Automated Overkill Verification: queries products with Station Judge N / NG / NA directly from database without pasting IDs
  async function runAutoOverkillVerify() {
    setBusy(true)
    try {
      const body = {
        machineCode: machineCode === 'AUTO' ? undefined : machineCode,
        lineCode: lineCode === 'All' ? undefined : lineCode,
        mappingName: 'entityId',
      }
      const result = await verificationApi.overkill(body)
      setVerifyResult(result)
      setVerifyPage(1)
      const skipNote = result.skippedItems ? `, skipped ${result.skippedItems} non-NG items` : ''
      onNotice({
        tone: 'success',
        message: `Automated Overkill completed: ${result.totalItems} records (${result.eligibleItems} eligible [N/NG/NA]${skipNote}, ${result.overkillCount} False Alarms, ${result.consistentNgCount} Confirmed NG).`,
      })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Overkill verification failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function createScanListFromCandidates(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!listName.trim()) return
    const idsToInclude = candidates.map((c) => c.keyId)
    if (idsToInclude.length === 0) {
      onNotice({ tone: 'error', message: 'No Overkill candidates available to create a scan list.' })
      return
    }
    setBusy(true)
    try {
      const rawText = idsToInclude.join('\n')
      await scanListsApi.create({
        name: listName.trim(),
        verificationCategory: 'Overkill',
        machineCode: machineCode === 'AUTO' ? 'AUTO' : machineCode,
        lineCode: lineCode === 'All' ? null : lineCode,
        rawText,
        mappingName: 'entityId',
      })
      setListName('')
      setCreateOpen(false)
      await loadLists()
      onNotice({ tone: 'success', message: `Overkill scan list created with ${idsToInclude.length} suspect items.` })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to create scan list.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tab-stack">
      {/* MariaDB Automated Pipeline Ingestion Widget */}
      <section className="panel" style={{ borderLeft: '4px solid var(--primary-accent, #3b82f6)' }}>
        <div className="panel-header-inline">
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <span className="eyebrow-accent" style={{ margin: 0 }}>Automated MariaDB Ingestion</span>
              <span className="tag-badge" style={{ background: '#e0f2fe', color: '#0284c7' }}>MariaDB Engine</span>
            </div>
            <h3 style={{ margin: '4px 0 0' }}>Overkill Query Pipelines ({pipelines.length})</h3>
            <p className="field-hint">
              Scheduled background pipeline extracting product & defect tables from MariaDB into local inspection tables.
            </p>
          </div>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="primary compact-button"
              style={{ background: '#2563eb', borderColor: '#3b82f6', fontWeight: 600 }}
              onClick={() => setBatchProvisionOpen(true)}
              title="Batch configure Overkill pipelines for multiple lines and defect tables"
            >
              🚀 Batch Setup Defect Pipelines
            </button>
            <button type="button" className="ghost-button compact" onClick={() => { void loadPipelines(); void loadRecentExecutions(); }}>
              ↻ Refresh Pipelines
            </button>
          </div>
        </div>

        {pipelines.length === 0 ? (
          <div style={{ padding: '16px', background: 'var(--surface-muted, #f8fafc)', borderRadius: '6px', textAlign: 'center', marginTop: '10px' }}>
            <p style={{ margin: 0, color: 'var(--text-muted, #64748b)' }}>
              No Overkill MariaDB Pipelines configured yet. You can create automated multi-table pipelines in the <strong>Query Profiles</strong> tab.
            </p>
          </div>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: '12px', marginTop: '12px' }}>
            {pipelines.map((p) => {
              const lastExec = recentExecutions.find((e) => e.configId === p.id)
              const isRunning = runningPipelineId === p.id || lastExec?.status === 'Running'
              return (
                <div key={p.id} className="pipeline-run-card" style={{ borderColor: isRunning ? '#3b82f6' : undefined }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                    <div>
                      <strong style={{ fontSize: '1rem', color: 'var(--text-main, #0f172a)' }}>{p.name}</strong>
                      <div style={{ fontSize: '0.8rem', color: 'var(--text-muted, #64748b)', marginTop: '2px' }}>
                        Machine: <strong>{p.machineCode}</strong> &bull; Line: <strong>{p.lineCode}</strong>
                      </div>
                    </div>
                    <span className="tag-badge" style={{
                      backgroundColor: p.enabled ? '#ecfdf5' : '#f1f5f9',
                      color: p.enabled ? '#059669' : '#64748b',
                    }}>
                      {p.enabled ? `Every ${p.scheduleIntervalMinutes}m` : 'Paused'}
                    </span>
                  </div>

                  <div style={{ fontSize: '0.82rem', marginTop: '8px', color: 'var(--text-muted, #64748b)' }}>
                    <span>Connection: <strong>{p.connectionName}</strong></span>
                    <br />
                    <span>Product Table: <code>{p.productTableName}</code> &bull; Defect Tables: <strong>{p.defectTables.length}</strong></span>
                    <br />
                    <span>PC Image API: <code style={{ color: '#9333ea' }}>{p.imageCrawlerApiUrl || 'Default PC API'}</code></span>
                  </div>

                  <div style={{
                    marginTop: '10px',
                    padding: '8px 10px',
                    background: 'var(--surface-muted, #f8fafc)',
                    borderRadius: '4px',
                    fontSize: '0.8rem',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center'
                  }}>
                    <div>
                      <div style={{ color: 'var(--text-muted, #64748b)' }}>Last Ingestion:</div>
                      <strong>
                        {lastExec ? formatDate(lastExec.startedAtUtc) : 'Never executed'}
                      </strong>
                    </div>
                    {lastExec ? (
                      <div style={{ textAlign: 'right' }}>
                        <StatusPill value={lastExec.status} />
                        <div style={{ fontSize: '0.75rem', marginTop: '2px', color: 'var(--text-muted, #64748b)' }}>
                          +{lastExec.productsScanned} items &bull; +{lastExec.defectsExtracted} defects
                        </div>
                      </div>
                    ) : null}
                  </div>

                  <div style={{ marginTop: '12px', display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                    <button
                      type="button"
                      className="primary-button compact"
                      disabled={isRunning}
                      onClick={() => void handleRunPipelineNow(p.id, p.name)}
                    >
                      {isRunning ? '⏳ Running Pipeline...' : '⚡ Run Pipeline Now'}
                    </button>
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </section>

      {/* Automated Overkill Operations Control Panel */}
      <section className="feature-grid">
        <article className="panel form-panel">
          <div className="panel-header-stacked">
            <span className="eyebrow-accent">Automated Operations (No Manual Paste)</span>
            <h3>Overkill Automated Verification</h3>
            <p className="field-hint">
              Overkill is automatically queried and monitored. The engine inspects station records where <strong>Judge is N / NG / NA</strong>.
              If 0 defect records exist, it flags an Overkill false alarm.
            </p>
          </div>

          <div className="compact-filter-bar">
            <Field
              label="Machine Target"
              hint="Select a specific machine or let Auto-Detection resolve active stations"
            >
              <select value={machineCode} onChange={(e) => onMachineChange(e.target.value)}>
                <option value="AUTO">AUTO (Detect active NG stations)</option>
                {machines.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            </Field>

            <Field label="Line Target" hint="Target production line filter">
              <select value={lineCode} onChange={(e) => setLineCode(e.target.value)}>
                <option value="All">All Lines</option>
                {linesForMachine.map((l) => (
                  <option key={l} value={l}>
                    {l}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          <div className="form-actions" style={{ marginTop: '1.25rem' }}>
            <button
              type="button"
              className="primary-button"
              disabled={busy}
              onClick={() => void runAutoOverkillVerify()}
            >
              {busy ? 'Running Auto Scan...' : '⚡ Run Overkill Auto-Verification'}
            </button>
            <button
              type="button"
              className="ghost-button"
              disabled={busy || candidates.length === 0}
              onClick={() => {
                setListName(`Overkill_Auto_${new Date().toISOString().slice(0, 10)}`)
                setCreateOpen(true)
              }}
            >
              Save Candidates to Scan List
            </button>
          </div>
        </article>

        <article className="panel info-panel">
          <div className="panel-header-stacked">
            <span className="eyebrow-accent">Rule Engine Specification</span>
            <h3>Overkill Rule Criteria</h3>
          </div>
          <div className="audit-flow">
            <div className="audit-step">
              <div className="audit-badge">01</div>
              <div>
                <strong>Station Judge Criteria</strong>
                <p>Only station records with <code>Judge = 'N' | 'NG' | 'NA' | 'N/A'</code> are evaluated. Good/Pass panels are bypassed.</p>
              </div>
            </div>
            <div className="audit-step">
              <div className="audit-badge">02</div>
              <div>
                <strong>Defect Count Check</strong>
                <p>Associated defect records are inspected. If <code>TotalDefects == 0</code>, product is flagged as <strong>Overkill (False Alarm)</strong>.</p>
              </div>
            </div>
            <div className="audit-step">
              <div className="audit-badge">03</div>
              <div>
                <strong>Consistent NG</strong>
                <p>If defect records exist with confirmed NG defect types, flagged as <strong>Consistent NG</strong>.</p>
              </div>
            </div>
          </div>
        </article>
      </section>

      {/* Verification Output If Run */}
      {verifyResult ? (
        <section className="panel">
          <div className="panel-header-inline">
            <div>
              <h3>Automated Verification Output</h3>
              <p className="field-hint">
                Evaluated {verifyResult.eligibleItems} eligible items: {verifyResult.overkillCount} Overkill false alarms, {verifyResult.consistentNgCount} confirmed NG.
              </p>
            </div>
            <button type="button" className="ghost-button compact" onClick={() => setVerifyResult(null)}>
              Dismiss
            </button>
          </div>

          <div className="operations-record-list compact">
            {pagedVerify.items.map((item) => (
              <article key={item.id} className="operations-record overkill-record">
                <div className="operations-record-identity">
                  <strong>{item.entityId}</strong>
                  <span>{item.machineCode || '—'} · {item.lineCode || 'ALL'}</span>
                </div>
                <div className="operations-record-status">
                  <JudgeBadge value={item.judge} />
                  <StatusPill value={item.anomalyStatus} />
                </div>
                <div className="operations-record-values verify-values">
                  <div><span>Eligible</span><strong>{item.eligible ? 'Yes' : 'No'}</strong></div>
                  <div><span>Connection</span><strong>{item.connectionName || '—'}</strong></div>
                  <div className="wide-value"><span>Analysis</span><strong>{item.analysisNotes || '—'}</strong></div>
                </div>
              </article>
            ))}
          </div>
          <Pagination page={pagedVerify.page} totalPages={pagedVerify.totalPages} totalCount={pagedVerify.totalCount} onPage={setVerifyPage} />
        </section>
      ) : null}

      {/* Live Overkill Candidates Automatically Detected from Stations */}
      <section className="panel">
        <div className="panel-header-inline">
          <div>
            <h3>Live Overkill Suspect Candidates ({candidates.length})</h3>
            <p className="field-hint">
              Products judged N / NG / NA queried automatically from production stations pending Overkill inspection.
            </p>
          </div>
          <button type="button" className="ghost-button compact" onClick={() => void loadCandidates()}>
            ↻ Refresh Candidates
          </button>
        </div>

        <div className="operations-record-list compact">
          {pagedCandidates.items.map((candidate) => {
            const defectName = candidate.defectName || (candidate.nameDefect !== 'None' ? candidate.nameDefect : null)
            return (
              <article key={candidate.keyId} className="operations-record overkill-record">
                <div className="operations-record-identity">
                  <strong>{candidate.keyId}</strong>
                  <span>Panel: {candidate.panelId || '—'}</span>
                </div>
                <div className="operations-record-status">
                  <JudgeBadge value={candidate.judge} />
                  {candidate.zone ? <span className="tag-badge">{candidate.zone}</span> : null}
                </div>
                <div className="operations-record-values candidate-values">
                  <div><span>Machine / Line</span><strong>{candidate.machineCode || '—'} · {candidate.lineCode || 'ALL'}</strong></div>
                  <div><span>Defect name</span><strong className={defectName ? 'danger-value' : ''}>{defectName || 'None'}</strong></div>
                  <div><span>Inspection time</span><strong>{formatDateTime(candidate.inspectedAtUtc)}</strong></div>
                </div>
                <div className="operations-record-actions">
                  <button
                    type="button"
                    className="secondary compact-button"
                    onClick={() => void openCandidateDefects(candidate.keyId)}
                  >
                    Inspect Defects
                  </button>
                </div>
              </article>
            )
          })}
        </div>
        <Pagination page={pagedCandidates.page} totalPages={pagedCandidates.totalPages} totalCount={pagedCandidates.totalCount} onPage={setCandidatePage} />
      </section>

      {/* Saved Overkill Scan Lists */}
      <section className="panel">
        <div className="panel-header-inline">
          <div>
            <h3>Overkill Scan Lists ({overkillLists.length})</h3>
            <p className="field-hint">Dedicated Overkill batches saved for automated verification and inspection runs.</p>
          </div>
        </div>

        <div className="operations-record-list compact">
          {pagedLists.items.map((list) => (
            <article key={list.id} className="operations-record scan-list-record">
              <div className="operations-record-identity">
                <strong>{list.name}</strong>
                <span>Created {formatDate(list.createdAtUtc)}</span>
              </div>
              <div className="operations-record-status">
                <StatusPill value={list.verificationCategory} />
              </div>
              <div className="operations-record-values scan-list-values">
                <div><span>Machine / Line</span><strong>{list.machineCode || '—'} · {list.lineCode || 'ALL'}</strong></div>
                <div><span>Accepted</span><strong>{list.acceptedCount}</strong></div>
                <div><span>Rejected</span><strong className={list.rejectedCount > 0 ? 'danger-value' : ''}>{list.rejectedCount}</strong></div>
              </div>
            </article>
          ))}
        </div>
        <Pagination page={pagedLists.page} totalPages={pagedLists.totalPages} totalCount={pagedLists.totalCount} onPage={setListPage} />
      </section>

      {/* Modal to Save Candidates as Scan List */}
      <Modal title="Save Overkill Scan List" description="Create a dedicated Overkill scan list from live candidates" open={createOpen} onClose={() => setCreateOpen(false)}>
        <form onSubmit={(e) => void createScanListFromCandidates(e)} className="form-stack">
          <Field label="Scan List Name" hint="Friendly name for this Overkill batch">
            <input
              type="text"
              value={listName}
              onChange={(e) => setListName(e.target.value)}
              placeholder="e.g. Overkill_Auto_SMT"
              required
            />
          </Field>
          <div className="form-summary-box">
            <small>
              This will create a scan list containing <strong>{candidates.length}</strong> suspect N/NG/NA products for machine <strong>{machineCode}</strong>.
            </small>
          </div>
          <div className="modal-actions">
            <button type="button" className="ghost-button" onClick={() => setCreateOpen(false)}>
              Cancel
            </button>
            <button type="submit" className="primary-button" disabled={busy}>
              {busy ? 'Saving...' : 'Save Scan List'}
            </button>
          </div>
        </form>
      </Modal>

      {/* Candidate Defect Inspection Modal */}
      <Modal
        title={`Suspect Defect Records · ${inspectKey || ''}`}
        description="Associated multi-table defect rows, equipment attribution, and feature coordinates"
        open={Boolean(inspectKey)}
        onClose={() => {
          setInspectKey(null)
          setCandidateDefects([])
        }}
        wide
      >
        {inspectLoading ? (
          <p className="field-hint">Querying defect tables...</p>
        ) : candidateDefects.length === 0 ? (
          <p className="empty-state">No defect records found for this unit.</p>
        ) : (
          <div className="tab-stack">
            {inspectKey && (
              <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '8px' }}>
                <a
                  href={storageApi.productDefectPackUrl(inspectKey)}
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
              </div>
            )}
            <div className="operations-defect-list">
              {candidateDefects.map((def: DefectRecord, idx: number) => {
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
                      <button type="button" className="secondary compact-button" onClick={() => setSelectedDefectForInspection(def)}>
                        Features & JSON
                      </button>
                    </div>
                  </article>
                )
              })}
            </div>
          </div>
        )}
      </Modal>

      {/* Nested Detailed Defect Feature Inspector Modal */}
      <Modal
        title={`Defect Feature Analysis · ${selectedDefectForInspection?.defectName || 'Defect'}`}
        description="Parsed coordinates, inspection metrics, and raw feature JSON viewer"
        open={Boolean(selectedDefectForInspection)}
        onClose={() => setSelectedDefectForInspection(null)}
        wide
      >
        {selectedDefectForInspection ? <DefectFeatureInspector defect={selectedDefectForInspection} /> : null}
      </Modal>

      {/* Batch Pipelines Modal */}
      <BatchPipelinesModal
        open={batchProvisionOpen}
        onClose={() => setBatchProvisionOpen(false)}
        onNotice={onNotice}
        onCompleted={() => {
          void loadPipelines()
          void loadRecentExecutions()
        }}
        connections={connections}
      />
    </div>
  )
}
