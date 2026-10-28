import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import {
  connectionsApi,
  deleteOverkillPipelineConfig,
  getOverkillPipelineConfigs,
  getOverkillPipelineExecutions,
  queryProfilesApi,
  runOverkillPipelineConfig,
  toggleOverkillPipelineConfig,
} from '../api'
import { formatDate, formNumber, formString, paginate } from '../format'
import { BatchPipelinesModal } from '../components/BatchPipelinesModal'
import { Button, DataTable, Field, Icon, Modal, Pagination, StatusPill, useLoadStatus } from '../ui'
import { useConfirm } from '../confirm'
import type {
  CreateQueryProfileRequest,
  DatabaseConnection,
  Notice,
  OverkillPipelineConfig,
  OverkillPipelineExecution,
  QueryProfile,
  QueryTestResponse,
  RenderedQuery,
} from '../types'

// The pipeline dialog is ~1,700 lines and only needed when a pipeline is created or edited.
const PipelineFormModal = lazy(() => import('../components/queries/PipelineFormModal').then((m) => ({ default: m.PipelineFormModal })))

function schemaKeys(json: string) {
  try {
    return Object.keys(JSON.parse(json) as Record<string, unknown>)
  } catch {
    return []
  }
}

type SubView = 'profiles' | 'pipelines'


export function QueriesTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [subView, setSubView] = useState<SubView>('pipelines')

  // Standard Profiles state
  const [profiles, setProfiles] = useState<QueryProfile[]>([])
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [selectedConnectionId, setSelectedConnectionId] = useState('')
  const [profileOpen, setProfileOpen] = useState(false)
  const [editing, setEditing] = useState<QueryProfile | null>(null)
  const [runOpen, setRunOpen] = useState<QueryProfile | null>(null)
  const [rendered, setRendered] = useState<RenderedQuery | null>(null)
  const [tested, setTested] = useState<QueryTestResponse | null>(null)
  const [busy, setBusy] = useState(false)
  const [draftConnectionId, setDraftConnectionId] = useState('')
  const [profilePage, setProfilePage] = useState(1)
  const [filterMachine, setFilterMachine] = useState('All')
  const [filterLine, setFilterLine] = useState('All')
  const [filterCategory, setFilterCategory] = useState('All')

  // Overkill Automated Pipelines state
  const [pipelines, setPipelines] = useState<OverkillPipelineConfig[]>([])
  // The pipeline dialog: null = closed, { pipeline: null } = new pipeline, otherwise the pipeline being edited.
  const [pipelineEditor, setPipelineEditor] = useState<{ pipeline: OverkillPipelineConfig | null } | null>(null)
  const [pipelinePage, setPipelinePage] = useState(1)
  const [executions, setExecutions] = useState<OverkillPipelineExecution[]>([])
  const [executionFilterConfigId, setExecutionFilterConfigId] = useState<string>('All')
  const [executionPage, setExecutionPage] = useState(1)
  const [runningPipelineId, setRunningPipelineId] = useState<string | null>(null)
  const [batchProvisionModalOpen, setBatchProvisionModalOpen] = useState(false)


  const machines = useMemo(() => {
    const list = new Set<string>()
    connections.forEach((c) => {
      if (c.machineCode) list.add(c.machineCode)
    })
    profiles.forEach((p) => {
      if (p.machineCode) list.add(p.machineCode)
    })
    return [...list].sort()
  }, [connections, profiles])

  const lines = useMemo(() => {
    const list = new Set<string>()
    connections
      .filter((c) => filterMachine === 'All' || c.machineCode === filterMachine)
      .forEach((c) => {
        if (c.lineCode) list.add(c.lineCode)
      })
    profiles
      .filter((p) => filterMachine === 'All' || p.machineCode === filterMachine)
      .forEach((p) => {
        if (p.lineCode) list.add(p.lineCode)
      })
    return [...list].sort()
  }, [connections, profiles, filterMachine])

  const compatibleConnections = useMemo(() => {
    return connections.filter((c) => {
      if (filterMachine !== 'All' && c.machineCode !== filterMachine) return false
      if (filterLine !== 'All' && c.lineCode !== filterLine) return false
      return true
    })
  }, [connections, filterMachine, filterLine])

  const selectedConnection = useMemo(
    () => connections.find((connection) => connection.id === selectedConnectionId) ?? null,
    [connections, selectedConnectionId],
  )
  const draftConnection = useMemo(
    () => connections.find((connection) => connection.id === draftConnectionId) ?? selectedConnection,
    [connections, draftConnectionId, selectedConnection],
  )
  const runKeys = useMemo(() => (runOpen ? schemaKeys(runOpen.parameterSchemaJson) : []), [runOpen])

  const filteredProfiles = useMemo(() => {
    return profiles.filter((profile) => {
      if (filterCategory !== 'All' && profile.verificationCategory !== filterCategory) return false
      if (filterMachine !== 'All' && profile.machineCode !== filterMachine) return false
      if (filterLine !== 'All' && profile.lineCode !== filterLine) return false
      if (selectedConnectionId && profile.dataSourceId !== selectedConnectionId) return false
      return true
    })
  }, [profiles, filterCategory, filterMachine, filterLine, selectedConnectionId])

  const pagedProfiles = paginate(filteredProfiles, profilePage)
  const pagedPipelines = paginate(pipelines, pipelinePage, 8)

  const filteredExecutions = useMemo(() => {
    if (executionFilterConfigId === 'All') return executions
    return executions.filter((e) => e.configId === executionFilterConfigId)
  }, [executions, executionFilterConfigId])

  const pagedExecutions = paginate(filteredExecutions, executionPage, 8)

  const profilesLoad = useLoadStatus()
  const pipelinesLoad = useLoadStatus()
  const executionsLoad = useLoadStatus()
  const { begin: beginProfiles, succeed: succeedProfiles, fail: failProfiles } = profilesLoad
  const { begin: beginPipelines, succeed: succeedPipelines, fail: failPipelines } = pipelinesLoad
  const { begin: beginExecutions, succeed: succeedExecutions, fail: failExecutions } = executionsLoad

  const loadProfilesAndConnections = useCallback(async () => {
    beginProfiles()
    try {
      const [nextProfiles, connectionPage] = await Promise.all([
        queryProfilesApi.list(),
        connectionsApi.list({ engine: 'MariaDB', page: 1, pageSize: 100 }),
      ])
      setProfiles(nextProfiles)
      setConnections(connectionPage.items)
      setSelectedConnectionId((current) => current || connectionPage.items[0]?.id || '')
      succeedProfiles()
    } catch (error) {
      failProfiles(error)
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load MariaDB queries.' })
    }
  }, [onNotice, beginProfiles, succeedProfiles, failProfiles])

  const loadPipelines = useCallback(async () => {
    beginPipelines()
    try {
      const data = await getOverkillPipelineConfigs()
      setPipelines(data)
      succeedPipelines()
    } catch (error) {
      failPipelines(error)
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill pipelines.' })
    }
  }, [onNotice, beginPipelines, succeedPipelines, failPipelines])

  const loadExecutions = useCallback(async () => {
    beginExecutions()
    try {
      const data = await getOverkillPipelineExecutions()
      setExecutions(data)
      succeedExecutions()
    } catch (error) {
      failExecutions(error)
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load pipeline executions.' })
    }
  }, [onNotice, beginExecutions, succeedExecutions, failExecutions])

  useEffect(() => {
    void loadProfilesAndConnections()
    void loadPipelines()
    void loadExecutions()
  }, [loadProfilesAndConnections, loadPipelines, loadExecutions])

  // Profile Form Handling
  function profilePayload(event: FormEvent<HTMLFormElement>): CreateQueryProfileRequest {
    const connectionId = formString(event, 'dataSourceId') || selectedConnectionId
    const connection = connections.find((item) => item.id === connectionId)
    return {
      name: formString(event, 'name'),
      dataSourceId: connectionId || null,
      machineCode: connection?.machineCode ?? formString(event, 'machineCode'),
      lineCode: connection?.lineCode ?? formString(event, 'lineCode'),
      concept: formString(event, 'concept') || 'PRODUCT-STATUS',
      sourceSystem: 'MARIADB',
      sqlTemplate: formString(event, 'sqlTemplate'),
      parameterSchemaJson: formString(event, 'parameterSchemaJson') || '{"keyId":"string","lineCode":"string"}',
      timeoutSeconds: formNumber(event, 'timeoutSeconds', 30),
      maxConcurrency: formNumber(event, 'maxConcurrency', 2),
      notes: formString(event, 'notes') || null,
      verificationCategory: formString(event, 'verificationCategory') || 'Overkill',
    }
  }

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      const body = profilePayload(event)
      if (!body.dataSourceId) throw new Error('Select a MariaDB connection first.')
      if (editing) await queryProfilesApi.update(editing.id, body)
      else await queryProfilesApi.create(body)
      setProfileOpen(false)
      setEditing(null)
      onNotice({
        tone: 'success',
        message: editing ? 'Query updated and bound to connection.' : 'Query saved to MariaDB connection.',
      })
      await loadProfilesAndConnections()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Save query failed.' })
    } finally {
      setBusy(false)
    }
  }

  function collectParameters(event: FormEvent<HTMLFormElement>) {
    const data = new FormData(event.currentTarget)
    const parameters: Record<string, string> = {}
    for (const key of runKeys) parameters[key] = String(data.get(key) ?? '')
    return parameters
  }

  async function renderProfile(event: FormEvent<HTMLFormElement>) {
    if (!runOpen) return
    setBusy(true)
    try {
      setRendered(await queryProfilesApi.render(runOpen.id, collectParameters(event)))
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Render failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function testProfile(event: FormEvent<HTMLFormElement>) {
    if (!runOpen) return
    setBusy(true)
    try {
      const result = await queryProfilesApi.test(runOpen.id, collectParameters(event))
      setTested(result)
      onNotice({ tone: result.success ? 'success' : 'error', message: result.message })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Test failed.' })
    } finally {
      setBusy(false)
    }
  }

  // Pipeline Handlers
  const openCreatePipeline = () => setPipelineEditor({ pipeline: null })
  const openEditPipeline = (p: OverkillPipelineConfig) => setPipelineEditor({ pipeline: p })

  // Two quick clicks would flip the pipeline back; ignore clicks while its toggle is in flight.
  const [togglingPipelineId, setTogglingPipelineId] = useState<string | null>(null)

  async function handleTogglePipeline(id: string) {
    if (togglingPipelineId) return
    setTogglingPipelineId(id)
    try {
      const updated = await toggleOverkillPipelineConfig(id)
      onNotice({
        tone: 'info',
        message: `Pipeline '${updated.name}' is now ${updated.enabled ? 'Enabled' : 'Disabled'}.`,
      })
      await loadPipelines()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to toggle pipeline.' })
    } finally {
      setTogglingPipelineId(null)
    }
  }

  async function handleRunPipeline(id: string) {
    setRunningPipelineId(id)
    try {
      const result = await runOverkillPipelineConfig(id)
      onNotice({
        tone: result.status === 'Success' ? 'success' : 'error',
        message: `${result.message} (Scanned: ${result.productsScanned}, Defects: ${result.defectsExtracted}, Overkill Suspects: ${result.overkillCandidatesCount})`,
      })
      await Promise.all([loadPipelines(), loadExecutions()])
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Execution failed.' })
    } finally {
      setRunningPipelineId(null)
    }
  }

  const confirm = useConfirm()

  async function handleDeletePipeline(id: string, name: string) {
    const ok = await confirm({
      title: `Delete pipeline ${name}?`,
      message: 'The pipeline stops running and its configuration is removed. This cannot be undone.',
      confirmLabel: 'Delete pipeline',
      tone: 'danger',
    })
    if (!ok) return
    try {
      await deleteOverkillPipelineConfig(id)
      onNotice({ tone: 'info', message: `Pipeline '${name}' deleted.` })
      await Promise.all([loadPipelines(), loadExecutions()])
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to delete pipeline.' })
    }
  }

  return (
    <div className="tab-stack">
      {/* View Switcher Bar */}
      <div className="hstack flex-wrap gap-md" style={{ marginBottom: '4px' }}>
        <button
          type="button"
          className={`filter-chip ${subView === 'pipelines' ? 'active' : ''}`}
          onClick={() => setSubView('pipelines')}
        >
          <Icon name="operations" className="icon inline-icon" />Overkill Automated Pipelines ({pipelines.length})
        </button>
        <button
          type="button"
          className={`filter-chip ${subView === 'profiles' ? 'active' : ''}`}
          onClick={() => setSubView('profiles')}
        >
          Standard SQL Query Profiles ({profiles.length})
        </button>
      </div>

      {subView === 'pipelines' ? (
        <>
          {/* Overkill Pipelines Section */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">GET /api/v1/overkill-pipeline/configs</p>
                <h2>Overkill Automated Pipelines (MariaDB)</h2>
              </div>
              <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap' }}>
                <button
                  type="button"
                  className="primary font-semibold"
                  style={{ background: 'var(--blue-600)', borderColor: 'var(--status-info-border)' }}
                  onClick={() => setBatchProvisionModalOpen(true)}
                  title="Batch configure Overkill pipelines and multi-defect tables for lines"
                >
                  <Icon name="play" className="icon inline-icon" />Batch Setup Defect Pipelines
                </button>
                <button type="button" onClick={openCreatePipeline}>
                  + New Overkill Pipeline
                </button>
              </div>
            </div>
            <p className="muted" style={{ marginBottom: '14px' }}>
              Automated pipelines periodically query MariaDB product tables for NG/N judges, extract corresponding defect records
              from multiple linked inspection tables, and identify Overkill suspects.
            </p>

            <DataTable
              eyebrow="SCHEDULED PIPELINES"
              title="Configured Overkill Pipelines"
              columns={['Pipeline Name', 'Connection', 'Machine / Line', 'Schedule Interval', 'Status', 'Last Run / Scan', 'Actions']}
              status={pipelinesLoad.status}
              error={pipelinesLoad.error}
              onRetry={() => void loadPipelines()}
              rows={pagedPipelines.items.map((p) => {
                const intervalHours = (p.scheduleIntervalMinutes / 60).toFixed(1).replace('.0', '')
                const isRunning = runningPipelineId === p.id || p.lastRunStatus === 'Running'

                return {
                  id: p.id,
                  cells: [
                    <div key="name">
                      <strong>{p.name}</strong>
                      <div className="field-hint">
                        Table: <code>{p.productTableName}</code> · Zone: <code>{p.zoneColumnName || 'zone'}</code> · Defect: <code>{p.defectNameColumnName || 'defect_name'}</code> · Defect tables: {p.defectTables?.length ?? 0}
                      </div>
                      <div className="field-hint" style={{ marginTop: '2px' }}>
                        PC Image API: <code className="text-real">{p.imageCrawlerApiUrl || 'Default PC API'}</code> {p.autoCrawlImages ? '· Auto-crawl' : ''}
                      </div>
                    </div>,
                    <div className="vstack" key="conn" style={{ gap: '2px' }}>
                      <span>{p.connectionName}</span>
                      {p.offloadDefectsToSeaweedFs && (
                        <span className="tag-badge text-info text-xs" style={{ background: 'var(--status-info-bg)', borderColor: 'var(--status-info-border)', width: 'fit-content' }}>
                          <Icon name="leakage" className="icon inline-icon" />SeaweedFS
                        </span>
                      )}
                    </div>,
                    <span key="ml" className="line-pill">
                      {p.machineCode}/{p.lineCode}
                    </span>,
                    <span key="sched" className="tag-badge">
                      Every {p.scheduleIntervalMinutes}m ({intervalHours}h)
                    </span>,
                    <div key="status" className="status-badges">
                      <StatusPill value={p.enabled ? (isRunning ? 'Running' : p.lastRunStatus || 'Idle') : 'Disabled'} />
                    </div>,
                    <div key="run">
                      <div>{p.lastRunAtUtc ? formatDate(p.lastRunAtUtc) : 'Never'}</div>
                      <small className="field-hint">
                        {p.lastRunProductsCount} prods · {p.lastRunDefectsCount} defects
                      </small>
                    </div>,
                    <div key="actions" className="row-actions">
                      <button
                        type="button"
                        className="table-button"
                        disabled={isRunning}
                        onClick={() => void handleRunPipeline(p.id)}
                        title="Run this pipeline immediately"
                      >
                        {isRunning ? 'Running...' : 'Run Now'}
                      </button>
                      <button
                        type="button"
                        className="table-button"
                        onClick={() => void handleTogglePipeline(p.id)}
                        disabled={togglingPipelineId === p.id}
                        aria-busy={togglingPipelineId === p.id || undefined}
                      >
                        {p.enabled ? 'Pause' : 'Enable'}
                      </button>
                      <button
                        type="button"
                        className="table-button"
                        onClick={() => openEditPipeline(p)}
                      >
                        Edit
                      </button>
                      <Button
                        variant="danger"
                        size="sm"
                        aria-label={`Delete pipeline ${p.name}`}
                        onClick={() => void handleDeletePipeline(p.id, p.name)}
                      >
                        Delete
                      </Button>
                    </div>,
                  ],
                }
              })}
            />
            <Pagination
              page={pagedPipelines.page}
              totalPages={pagedPipelines.totalPages}
              totalCount={pagedPipelines.totalCount}
              onPage={setPipelinePage}
            />
          </section>

          {/* Execution History Section */}
          <section className="panel" style={{ marginTop: '14px' }}>
            <div className="section-heading">
              <div>
                <p className="eyebrow">GET /api/v1/overkill-pipeline/executions</p>
                <h2>Pipeline Execution History</h2>
              </div>
              <div className="hstack gap-md">
                <select
                  value={executionFilterConfigId}
                  onChange={(e) => {
                    setExecutionFilterConfigId(e.target.value)
                    setExecutionPage(1)
                  }}
                  style={{ width: 'auto', minWidth: '200px' }}
                  aria-label="Filter executions by pipeline"
                >
                  <option value="All">All Pipelines</option>
                  {pipelines.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
                <button
                  type="button"
                  className="secondary"
                  onClick={() => void loadExecutions()}
                >
                  Refresh Logs
                </button>
              </div>
            </div>

            <DataTable
              eyebrow="AUDIT TRAIL"
              title="Recent Automated & Manual Ingestions"
              columns={['Pipeline', 'Trigger', 'Started', 'Duration', 'Status', 'Products', 'Defects', 'Overkill Suspects', 'Message']}
              status={executionsLoad.status}
              error={executionsLoad.error}
              onRetry={() => void loadExecutions()}
              rows={pagedExecutions.items.map((exec) => ({
                id: exec.id,
                cells: [
                  <strong key="p">{exec.pipelineName}</strong>,
                  <span key="trig" className="clean-pill">{exec.triggerType}</span>,
                  <span key="st">{formatDate(exec.startedAtUtc)}</span>,
                  <span key="lat">{exec.latencyMs}ms</span>,
                  <StatusPill key="stat" value={exec.status} />,
                  <strong key="prod">{exec.productsScanned}</strong>,
                  <span key="def">{exec.defectsExtracted}</span>,
                  <span key="ok" className={exec.overkillCandidatesCount > 0 ? 'warning-stat-badge' : 'clean-pill'}>
                    {exec.overkillCandidatesCount}
                  </span>,
                  <small key="msg" className="field-hint" style={{ maxWidth: '240px', overflow: 'hidden', textOverflow: 'ellipsis', display: 'block' }}>
                    {exec.errorMessage || 'Completed successfully'}
                  </small>,
                ],
              }))}
            />
            <Pagination
              page={pagedExecutions.page}
              totalPages={pagedExecutions.totalPages}
              totalCount={pagedExecutions.totalCount}
              onPage={setExecutionPage}
            />
          </section>

          {pipelineEditor && (
            <Suspense fallback={null}>
              <PipelineFormModal
                pipeline={pipelineEditor.pipeline}
                connections={connections}
                onClose={() => setPipelineEditor(null)}
                onSaved={() => {
                  setPipelineEditor(null)
                  void loadPipelines()
                }}
                onNotice={onNotice}
              />
            </Suspense>
          )}
        </>
      ) : (
        <>
          {/* Standard SQL Query Profiles */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">GET /database-connections</p>
                <h2>MariaDB connection</h2>
              </div>
              <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap' }}>
                <button
                  type="button"
                  className="primary font-semibold"
                  style={{ background: 'var(--blue-600)', borderColor: 'var(--status-info-border)' }}
                  onClick={() => setBatchProvisionModalOpen(true)}
                  title="Batch configure Overkill pipelines and multi-defect tables for lines"
                >
                  <Icon name="play" className="icon inline-icon" />Batch Setup Defect Pipelines
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setEditing(null)
                    setDraftConnectionId(selectedConnectionId)
                    setProfileOpen(true)
                  }}
                  disabled={!selectedConnectionId}
                >
                  New SQL query
                </button>
              </div>
            </div>
            <div className="compact-filter-bar">
              <Field label="Machine filter" hint="Standard machine reference">
                <select
                  value={filterMachine}
                  onChange={(event) => {
                    setFilterMachine(event.target.value)
                    setFilterLine('All')
                    setProfilePage(1)
                  }}
                >
                  <option value="All">All machines</option>
                  {machines.map((m) => (
                    <option key={m} value={m}>
                      {m}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Line filter" hint="Filtered by machine">
                <select
                  value={filterLine}
                  onChange={(event) => {
                    setFilterLine(event.target.value)
                    setProfilePage(1)
                  }}
                >
                  <option value="All">All lines</option>
                  {lines.map((l) => (
                    <option key={l} value={l}>
                      {l}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Category" hint="Leakage or Overkill verification">
                <select
                  value={filterCategory}
                  onChange={(event) => {
                    setFilterCategory(event.target.value)
                    setProfilePage(1)
                  }}
                >
                  <option value="All">All categories</option>
                  <option value="Leakage">Leakage</option>
                  <option value="Overkill">Overkill</option>
                </select>
              </Field>
              <Field label="MariaDB connection" hint="Bound connection">
                <select
                  value={selectedConnectionId}
                  onChange={(event) => {
                    setSelectedConnectionId(event.target.value)
                    setProfilePage(1)
                  }}
                >
                  <option value="">All connections</option>
                  {compatibleConnections.map((connection) => (
                    <option key={connection.id} value={connection.id}>
                      {connection.name} · {connection.machineCode}/{connection.lineCode}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            {selectedConnection ? (
              <p className="muted">
                {selectedConnection.engine} {selectedConnection.host}:{selectedConnection.port}/{selectedConnection.databaseName} ·{' '}
                <StatusPill value={selectedConnection.health} />
              </p>
            ) : (
              <p className="muted">Create a MariaDB connection on the Sources tab first. Queries inherit its machine and line.</p>
            )}
          </section>

          <DataTable
            eyebrow="GET /query-profiles"
            title="Saved query content"
            columns={['Name', 'Category', 'Machine', 'Line', 'Connection', 'Updated', '']}
            status={profilesLoad.status}
            error={profilesLoad.error}
            onRetry={() => void loadProfilesAndConnections()}
            rows={pagedProfiles.items.map((profile) => ({
              id: profile.id,
              cells: [
                profile.name,
                <StatusPill key="c" value={profile.verificationCategory} />,
                profile.machineCode,
                profile.lineCode,
                profile.connectionName || 'Unbound',
                formatDate(profile.updatedAtUtc),
                <div key="a" className="row-actions">
                  <button
                    type="button"
                    className="table-button"
                    onClick={() => {
                      setEditing(profile)
                      setDraftConnectionId(profile.dataSourceId ?? selectedConnectionId)
                      setProfileOpen(true)
                    }}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className="table-button"
                    onClick={() => {
                      setRunOpen(profile)
                      setRendered(null)
                      setTested(null)
                    }}
                  >
                    Render / test
                  </button>
                </div>,
              ],
            }))}
          />
          <Pagination
            page={pagedProfiles.page}
            totalPages={pagedProfiles.totalPages}
            totalCount={pagedProfiles.totalCount}
            onPage={setProfilePage}
          />

          <Modal
            title={editing ? 'Edit MariaDB query' : 'Save MariaDB query'}
            description={editing ? 'PUT /query-profiles/{id}' : 'POST /query-profiles'}
            open={profileOpen}
            onClose={() => setProfileOpen(false)}
            wide
          >
            <form className="modal-form" onSubmit={(event) => void saveProfile(event)}>
              <div className="query-form-grid">
                <Field label="Query name" required>
                  <input name="name" defaultValue={editing?.name ?? ''} required />
                </Field>
                <Field label="MariaDB connection" required hint="Machine/Line auto-assigned">
                  <select
                    name="dataSourceId"
                    value={draftConnectionId || selectedConnectionId}
                    onChange={(event) => setDraftConnectionId(event.target.value)}
                    required
                  >
                    {connections.map((connection) => (
                      <option key={connection.id} value={connection.id}>
                        {connection.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Verification category" required>
                  <select name="verificationCategory" defaultValue={editing?.verificationCategory ?? 'Overkill'}>
                    <option>Overkill</option>
                    <option>Leakage</option>
                  </select>
                </Field>
                <Field label="Concept" required>
                  <input name="concept" defaultValue={editing?.concept ?? 'PRODUCT-STATUS'} required />
                </Field>
                <Field label="Machine">
                  <input readOnly value={draftConnection?.machineCode ?? '—'} />
                </Field>
                <Field label="Line">
                  <input readOnly value={draftConnection?.lineCode ?? '—'} />
                </Field>
                <Field label="Timeout (s)">
                  <input name="timeoutSeconds" type="number" defaultValue={editing?.timeoutSeconds ?? 30} />
                </Field>
                <Field label="Max concurrency">
                  <input name="maxConcurrency" type="number" defaultValue={editing?.maxConcurrency ?? 2} />
                </Field>
              </div>
              <Field label="SQL template" required hint="SELECT/WITH only. Use {{keyId}} {{lineCode}} placeholders.">
                <textarea
                  name="sqlTemplate"
                  className="code-textarea"
                  defaultValue={
                    editing?.sqlTemplate ??
                    'SELECT key_id, judge, line_code FROM products WHERE key_id = {{keyId}} AND line_code = {{lineCode}} LIMIT 1'
                  }
                  required
                />
              </Field>
              <Field label="Parameter schema JSON" required>
                <textarea
                  name="parameterSchemaJson"
                  className="code-textarea"
                  defaultValue={editing?.parameterSchemaJson ?? '{"keyId":"string","lineCode":"string"}'}
                  required
                />
              </Field>
              <Field label="Notes">
                <textarea name="notes" defaultValue={editing?.notes ?? ''} />
              </Field>
              <div className="actions">
                <button type="submit" disabled={busy}>
                  Save query
                </button>
                <button type="button" className="ghost-button" onClick={() => setProfileOpen(false)}>
                  Cancel
                </button>
              </div>
            </form>
          </Modal>

          <Modal
            title={runOpen ? `Render / test · ${runOpen.name}` : 'Render / test'}
            description="POST /query-profiles/{id}/render · /test"
            open={Boolean(runOpen)}
            onClose={() => setRunOpen(null)}
            wide
          >
            {runOpen ? (
              <form
                className="modal-form"
                onSubmit={(event) => {
                  event.preventDefault()
                  const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null
                  if (submitter?.value === 'test') void testProfile(event)
                  else void renderProfile(event)
                }}
              >
                <p className="muted">
                  Connection {runOpen.connectionName || 'unbound'} · {runOpen.machineCode}/{runOpen.lineCode}
                </p>
                <div className="query-form-grid">
                  {runKeys.length === 0 ? <p className="muted">No parameters in schema.</p> : null}
                  {runKeys.map((key) => (
                    <Field key={key} label={key} required>
                      <input name={key} defaultValue={key === 'lineCode' ? runOpen.lineCode : ''} required />
                    </Field>
                  ))}
                </div>
                <div className="actions">
                  <button type="submit" value="render" disabled={busy}>
                    Render SQL
                  </button>
                  <button type="submit" value="test" className="secondary" disabled={busy}>
                    Test on MariaDB
                  </button>
                </div>
                {rendered ? (
                  <div className="rendered-query">
                    <strong>Rendered SQL</strong>
                    {rendered.missingParameters.length ? <p>Missing: {rendered.missingParameters.join(', ')}</p> : null}
                    {rendered.warnings.length ? <p>Warnings: {rendered.warnings.join(', ')}</p> : null}
                    <pre>{rendered.renderedSql}</pre>
                  </div>
                ) : null}
                {tested ? (
                  <div className="rendered-query">
                    <strong>
                      {tested.success ? 'Success' : 'Failed'} · {tested.rowCount} rows · {tested.latencyMs} ms
                    </strong>
                    <pre>{JSON.stringify(tested.rows.slice(0, 8), null, 2)}</pre>
                  </div>
                ) : null}
              </form>
            ) : null}
          </Modal>
        </>
      )}

      {/* Batch Pipelines Modal - Rendered outside subView ternary so it opens in both Pipelines and Profiles */}
      <BatchPipelinesModal
        open={batchProvisionModalOpen}
        onClose={() => setBatchProvisionModalOpen(false)}
        onNotice={onNotice}
        onCompleted={() => {
          void loadPipelines()
          void loadExecutions()
        }}
        connections={connections}
      />
    </div>
  )
}
