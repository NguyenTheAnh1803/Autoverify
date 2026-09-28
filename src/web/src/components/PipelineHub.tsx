import { useEffect, useMemo, useState } from 'react'
import { formatDateTime } from '../format'
import type { OverkillPipelineConfig, OverkillPipelineExecution } from '../types'

type HealthBucket = 'failed' | 'attention' | 'running' | 'healthy' | 'paused'
type BucketFilter = 'all' | HealthBucket
type ViewMode = 'cards' | 'table'
type SortMode = 'priority' | 'name' | 'line'

type HubPrefs = {
  machine: string
  line: string
  model: string
  sort: SortMode
  view: ViewMode
  collapsed: boolean
}

type PipelineRow = {
  pipeline: OverkillPipelineConfig
  bucket: HealthBucket
  statusLabel: string | null
  lastRunAt: string | null
  itemsCount: number | null
  defectsCount: number | null
  runLocked: boolean
}

const PREFS_KEY = 'autoverify.pipelineHub.v1'
const ALL = 'All'
const LINE_CHIP_LIMIT = 24
const ACTIVE_REFRESH_MS = 15_000
const IDLE_REFRESH_MS = 60_000
const MIN_STALL_MINUTES = 30

const DEFAULT_PREFS: HubPrefs = { machine: ALL, line: ALL, model: ALL, sort: 'priority', view: 'cards', collapsed: false }

const BUCKET_ORDER: Record<HealthBucket, number> = { failed: 0, attention: 1, running: 2, healthy: 3, paused: 4 }

const KPI_ITEMS: { key: HealthBucket; label: string }[] = [
  { key: 'failed', label: 'Failed' },
  { key: 'attention', label: 'Attention' },
  { key: 'running', label: 'Running' },
  { key: 'healthy', label: 'Healthy' },
  { key: 'paused', label: 'Paused' },
]

const naturalCompare = (a: string, b: string) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' })

function readPrefs(): HubPrefs {
  try {
    const raw = localStorage.getItem(PREFS_KEY)
    if (!raw) return DEFAULT_PREFS
    const parsed = JSON.parse(raw) as Partial<HubPrefs>
    return {
      machine: typeof parsed.machine === 'string' ? parsed.machine : ALL,
      line: typeof parsed.line === 'string' ? parsed.line : ALL,
      model: typeof parsed.model === 'string' ? parsed.model : ALL,
      sort: parsed.sort === 'name' || parsed.sort === 'line' ? parsed.sort : 'priority',
      view: parsed.view === 'table' ? 'table' : 'cards',
      collapsed: parsed.collapsed === true,
    }
  } catch {
    return DEFAULT_PREFS
  }
}

function writePrefs(prefs: HubPrefs) {
  try {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs))
  } catch {
    // Preferences are a convenience; the hub works without storage.
  }
}

function toTime(value: string | null | undefined) {
  if (!value) return null
  const time = new Date(value).getTime()
  return Number.isNaN(time) ? null : time
}

function formatAgo(value: string | number | null, now: number, withSeconds = false) {
  const time = typeof value === 'number' ? value : toTime(value)
  if (time === null) return null
  const diff = Math.max(0, now - time)
  if (diff < 60_000) return withSeconds ? `${Math.floor(diff / 1000)}s ago` : 'just now'
  const minutes = Math.floor(diff / 60_000)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}

function formatShortTime(value: string | null, now: number) {
  const time = toTime(value)
  if (time === null) return null
  const date = new Date(time)
  const sameDay = new Date(now).toDateString() === date.toDateString()
  const clock = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  return sameDay ? clock : `${date.toLocaleDateString([], { month: 'short', day: 'numeric' })} ${clock}`
}

function buildRow(
  pipeline: OverkillPipelineConfig,
  lastExec: OverkillPipelineExecution | undefined,
  locallyRunning: boolean,
  now: number,
): PipelineRow {
  const lastRunAt = lastExec?.startedAtUtc ?? pipeline.lastRunAtUtc
  const rawStatus = lastExec?.status ?? (pipeline.lastRunAtUtc ? pipeline.lastRunStatus : null)
  const itemsCount = lastExec ? lastExec.productsScanned : pipeline.lastRunAtUtc ? pipeline.lastRunProductsCount : null
  const defectsCount = lastExec ? lastExec.defectsExtracted : pipeline.lastRunAtUtc ? pipeline.lastRunDefectsCount : null
  const intervalMs = Math.max(1, pipeline.scheduleIntervalMinutes) * 60_000
  const lastRunTime = toTime(lastRunAt)
  const reportedRunning = rawStatus === 'Running'
  const stalled =
    reportedRunning &&
    !locallyRunning &&
    lastRunTime !== null &&
    now - lastRunTime > Math.max(intervalMs, MIN_STALL_MINUTES * 60_000)
  const overdueReference = lastRunTime ?? toTime(pipeline.createdAtUtc)
  const overdue = pipeline.enabled && overdueReference !== null && now - overdueReference > intervalMs * 2

  let bucket: HealthBucket
  let statusLabel = rawStatus
  if (locallyRunning) {
    bucket = 'running'
    statusLabel = 'Running'
  } else if (stalled) {
    bucket = 'attention'
    statusLabel = 'Stalled'
  } else if (reportedRunning) {
    bucket = 'running'
  } else if (!pipeline.enabled) {
    bucket = 'paused'
  } else if (rawStatus === 'Failed') {
    bucket = 'failed'
  } else if (rawStatus === 'PartialError') {
    bucket = 'attention'
    statusLabel = 'Partial error'
  } else if (overdue) {
    bucket = 'attention'
    statusLabel = 'Overdue'
  } else {
    bucket = 'healthy'
  }

  return {
    pipeline,
    bucket,
    statusLabel,
    lastRunAt,
    itemsCount,
    defectsCount,
    runLocked: locallyRunning || reportedRunning,
  }
}

function StatusBadge({ bucket, label }: { bucket: HealthBucket; label: string }) {
  return (
    <span className={`pipeline-status is-${bucket}`}>
      <span className="pipeline-status-dot" aria-hidden="true" />
      {label}
    </span>
  )
}

function ScheduleBadge({ pipeline }: { pipeline: OverkillPipelineConfig }) {
  return (
    <span className={`pipeline-schedule${pipeline.enabled ? '' : ' is-paused'}`}>
      {pipeline.enabled ? `Every ${pipeline.scheduleIntervalMinutes}m` : 'Paused'}
    </span>
  )
}

export function PipelineHub({
  pipelines,
  executions,
  runningIds,
  updatedAt,
  onRun,
  onRefresh,
  onBatchSetup,
}: {
  pipelines: OverkillPipelineConfig[]
  executions: OverkillPipelineExecution[]
  runningIds: ReadonlySet<string>
  updatedAt: number | null
  onRun: (id: string, name: string) => void
  onRefresh: () => Promise<void>
  onBatchSetup: () => void
}) {
  const [prefs, setPrefs] = useState<HubPrefs>(readPrefs)
  const [bucketFilter, setBucketFilter] = useState<BucketFilter>('all')
  const [search, setSearch] = useState('')
  const [now, setNow] = useState(() => Date.now())
  const [refreshing, setRefreshing] = useState(false)

  const updatePrefs = (patch: Partial<HubPrefs>) => {
    setPrefs((current) => {
      const next = { ...current, ...patch }
      writePrefs(next)
      return next
    })
  }

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 5_000)
    return () => window.clearInterval(timer)
  }, [])

  const rows = useMemo(() => {
    const latestByConfig = new Map<string, OverkillPipelineExecution>()
    for (const execution of executions) {
      const current = latestByConfig.get(execution.configId)
      if (!current || (toTime(execution.startedAtUtc) ?? 0) > (toTime(current.startedAtUtc) ?? 0)) {
        latestByConfig.set(execution.configId, execution)
      }
    }
    return pipelines.map((pipeline) =>
      buildRow(pipeline, latestByConfig.get(pipeline.id), runningIds.has(pipeline.id), now),
    )
  }, [pipelines, executions, runningIds, now])

  const anyRunning = rows.some((row) => row.bucket === 'running')

  useEffect(() => {
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') void onRefresh()
    }, anyRunning ? ACTIVE_REFRESH_MS : IDLE_REFRESH_MS)
    return () => window.clearInterval(timer)
  }, [anyRunning, onRefresh])

  const machineOptions = useMemo(() => {
    const counts = new Map<string, number>()
    for (const row of rows) {
      const key = row.pipeline.machineCode || 'Unassigned'
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
    return [...counts.entries()].sort(([a], [b]) => naturalCompare(a, b))
  }, [rows])
  const machine = machineOptions.some(([code]) => code === prefs.machine) ? prefs.machine : ALL
  const machineRows = useMemo(
    () => (machine === ALL ? rows : rows.filter((row) => (row.pipeline.machineCode || 'Unassigned') === machine)),
    [rows, machine],
  )

  const lineOptions = useMemo(() => {
    const counts = new Map<string, number>()
    for (const row of machineRows) {
      const key = row.pipeline.lineCode || 'Unassigned'
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
    return [...counts.entries()].sort(([a], [b]) => naturalCompare(a, b))
  }, [machineRows])
  const line = lineOptions.some(([code]) => code === prefs.line) ? prefs.line : ALL
  const lineRows = useMemo(
    () => (line === ALL ? machineRows : machineRows.filter((row) => (row.pipeline.lineCode || 'Unassigned') === line)),
    [machineRows, line],
  )

  const modelOptions = useMemo(
    () => [...new Set(lineRows.map((row) => row.pipeline.modelCode).filter((code): code is string => Boolean(code)))].sort(naturalCompare),
    [lineRows],
  )
  const model = modelOptions.includes(prefs.model) ? prefs.model : ALL

  const scopedRows = useMemo(() => {
    const term = search.trim().toLowerCase()
    return lineRows.filter((row) => {
      const p = row.pipeline
      if (model !== ALL && p.modelCode !== model) return false
      if (!term) return true
      return [p.name, p.connectionName, p.machineCode, p.lineCode, p.modelCode ?? '', p.productTableName]
        .some((value) => value?.toLowerCase().includes(term))
    })
  }, [lineRows, model, search])

  const bucketCounts = useMemo(() => {
    const counts: Record<HealthBucket, number> = { failed: 0, attention: 0, running: 0, healthy: 0, paused: 0 }
    for (const row of scopedRows) counts[row.bucket]++
    return counts
  }, [scopedRows])

  const visibleRows = useMemo(() => {
    const filtered = bucketFilter === 'all' ? scopedRows : scopedRows.filter((row) => row.bucket === bucketFilter)
    return [...filtered].sort((a, b) => {
      if (prefs.sort === 'priority') {
        const byBucket = BUCKET_ORDER[a.bucket] - BUCKET_ORDER[b.bucket]
        if (byBucket !== 0) return byBucket
      } else if (prefs.sort === 'line') {
        const byLine = naturalCompare(a.pipeline.lineCode ?? '', b.pipeline.lineCode ?? '')
        if (byLine !== 0) return byLine
      }
      return naturalCompare(a.pipeline.name ?? '', b.pipeline.name ?? '')
    })
  }, [scopedRows, bucketFilter, prefs.sort])

  const filtersActive = machine !== ALL || line !== ALL || model !== ALL || bucketFilter !== 'all' || search.trim() !== ''

  function clearFilters() {
    updatePrefs({ machine: ALL, line: ALL, model: ALL })
    setBucketFilter('all')
    setSearch('')
  }

  async function handleRefresh() {
    setRefreshing(true)
    try {
      await onRefresh()
    } finally {
      setRefreshing(false)
    }
  }

  const updatedLabel = updatedAt ? `Updated ${formatAgo(updatedAt, now, true)}` : 'Not loaded yet'

  function renderRunButton(row: PipelineRow) {
    return (
      <button
        type="button"
        className="pipeline-run-button"
        disabled={row.runLocked}
        onClick={() => onRun(row.pipeline.id, row.pipeline.name)}
        title={
          row.statusLabel === 'Stalled'
            ? 'Last run still reports Running but has exceeded its schedule interval (possibly stalled)'
            : row.runLocked
              ? 'Pipeline is currently running'
              : `Run ${row.pipeline.name} now`
        }
      >
        {row.runLocked ? 'Running…' : 'Run now'}
      </button>
    )
  }

  function renderLastRun(row: PipelineRow) {
    if (!row.lastRunAt) return <span className="pipeline-muted">Never executed</span>
    return (
      <span title={formatDateTime(row.lastRunAt)}>
        {formatShortTime(row.lastRunAt, now)} <span className="pipeline-muted">· {formatAgo(row.lastRunAt, now)}</span>
      </span>
    )
  }

  function renderResult(row: PipelineRow) {
    if (row.itemsCount === null) return <span className="pipeline-muted">—</span>
    return (
      <span>
        +{row.itemsCount.toLocaleString()} items · +{(row.defectsCount ?? 0).toLocaleString()} defects
      </span>
    )
  }

  function sortHeader(label: string, mode: SortMode) {
    const active = prefs.sort === mode
    return (
      <button
        type="button"
        className={`pipeline-sort-button${active ? ' active' : ''}`}
        onClick={() => updatePrefs({ sort: mode })}
        aria-pressed={active}
      >
        {label}
        {active ? ' ▾' : ''}
      </button>
    )
  }

  return (
    <section className="panel pipeline-hub" aria-label="Overkill query pipelines">
      <div className="pipeline-hub-header">
        <div className="pipeline-hub-title">
          <div className="pipeline-hub-eyebrow">
            <span className="eyebrow-accent">Automated MariaDB Ingestion</span>
            <span className="tag-badge" style={{ background: '#e0f2fe', color: '#0284c7' }}>MariaDB Engine</span>
          </div>
          <h3>
            Overkill Query Pipelines ({pipelines.length})
            {filtersActive && pipelines.length > 0 ? (
              <span className="pipeline-hub-showing">Showing {visibleRows.length} of {pipelines.length}</span>
            ) : null}
          </h3>
          <p className="field-hint">
            Scheduled background pipeline extracting product & defect tables from MariaDB into local inspection tables.
          </p>
        </div>
        <div className="pipeline-hub-actions">
          <button
            type="button"
            className="primary compact-button pipeline-hub-batch"
            onClick={onBatchSetup}
            title="Batch configure Overkill pipelines for multiple lines and defect tables"
          >
            🚀 Batch Setup Defect Pipelines
          </button>
          <button type="button" className="ghost-button compact" disabled={refreshing} onClick={() => void handleRefresh()}>
            ↻ Refresh Pipelines
          </button>
          {pipelines.length > 0 ? (
            <>
              <div className="pipeline-view-toggle" role="group" aria-label="View mode">
                <button type="button" aria-pressed={prefs.view === 'cards'} className={prefs.view === 'cards' ? 'active' : ''} onClick={() => updatePrefs({ view: 'cards' })}>
                  Cards
                </button>
                <button type="button" aria-pressed={prefs.view === 'table'} className={prefs.view === 'table' ? 'active' : ''} onClick={() => updatePrefs({ view: 'table' })}>
                  Table
                </button>
              </div>
              <button
                type="button"
                className="ghost-button compact"
                aria-expanded={!prefs.collapsed}
                onClick={() => updatePrefs({ collapsed: !prefs.collapsed })}
              >
                {prefs.collapsed ? 'Expand ▾' : 'Collapse ▴'}
              </button>
            </>
          ) : null}
        </div>
      </div>

      {pipelines.length === 0 ? (
        <div className="pipeline-hub-empty">
          <p>
            No Overkill MariaDB Pipelines configured yet. You can create automated multi-table pipelines in the <strong>Query Profiles</strong> tab.
          </p>
        </div>
      ) : (
        <>
          <div className="pipeline-kpi-bar">
            <button
              type="button"
              className={`pipeline-kpi is-total${bucketFilter === 'all' ? ' active' : ''}`}
              aria-pressed={bucketFilter === 'all'}
              onClick={() => setBucketFilter('all')}
            >
              <strong>{scopedRows.length}</strong>
              <span>Total</span>
            </button>
            {KPI_ITEMS.map((item) => {
              const count = bucketCounts[item.key]
              const active = bucketFilter === item.key
              return (
                <button
                  key={item.key}
                  type="button"
                  className={`pipeline-kpi is-${item.key}${count > 0 ? ' has-items' : ''}${active ? ' active' : ''}`}
                  aria-pressed={active}
                  onClick={() => setBucketFilter(active ? 'all' : item.key)}
                >
                  <strong>
                    <span className="pipeline-status-dot" aria-hidden="true" />
                    {count}
                  </strong>
                  <span>{item.label}</span>
                </button>
              )
            })}
            <span className="pipeline-hub-updated" title={updatedAt ? formatDateTime(new Date(updatedAt).toISOString()) : undefined}>
              {updatedLabel} · auto-refresh {anyRunning ? '15s' : '60s'}
            </span>
          </div>

          {prefs.collapsed ? null : (
            <>
              <div className="pipeline-hub-filters">
                <div className="pipeline-filter-row">
                  <span className="pipeline-filter-label">Machine</span>
                  <div className="filter-chips">
                    <button type="button" className={`filter-chip${machine === ALL ? ' active' : ''}`} aria-pressed={machine === ALL} onClick={() => updatePrefs({ machine: ALL, line: ALL, model: ALL })}>
                      All {rows.length}
                    </button>
                    {machineOptions.map(([code, count]) => (
                      <button
                        key={code}
                        type="button"
                        className={`filter-chip${machine === code ? ' active' : ''}`}
                        aria-pressed={machine === code}
                        onClick={() => updatePrefs({ machine: code, line: ALL, model: ALL })}
                      >
                        {code} {count}
                      </button>
                    ))}
                  </div>
                  <div className="pipeline-filter-tools">
                    {modelOptions.length > 0 ? (
                      <select aria-label="Filter by model" value={model} onChange={(event) => updatePrefs({ model: event.target.value })}>
                        <option value={ALL}>All models</option>
                        {modelOptions.map((code) => (
                          <option key={code} value={code}>
                            Model {code}
                          </option>
                        ))}
                      </select>
                    ) : null}
                    <input
                      type="search"
                      aria-label="Search pipelines"
                      placeholder="Search name, connection, table…"
                      value={search}
                      onChange={(event) => setSearch(event.target.value)}
                    />
                    <select aria-label="Sort pipelines" value={prefs.sort} onChange={(event) => updatePrefs({ sort: event.target.value as SortMode })}>
                      <option value="priority">Sort: Needs attention first</option>
                      <option value="name">Sort: Name</option>
                      <option value="line">Sort: Line</option>
                    </select>
                    {filtersActive ? (
                      <button type="button" className="ghost-button compact" onClick={clearFilters}>
                        Clear filters
                      </button>
                    ) : null}
                  </div>
                </div>

                <div className="pipeline-filter-row">
                  <span className="pipeline-filter-label">Line</span>
                  {lineOptions.length > LINE_CHIP_LIMIT ? (
                    <select aria-label="Filter by line" value={line} onChange={(event) => updatePrefs({ line: event.target.value, model: ALL })}>
                      <option value={ALL}>All lines ({machineRows.length})</option>
                      {lineOptions.map(([code, count]) => (
                        <option key={code} value={code}>
                          Line {code} ({count})
                        </option>
                      ))}
                    </select>
                  ) : (
                    <div className="filter-chips">
                      <button type="button" className={`filter-chip${line === ALL ? ' active' : ''}`} aria-pressed={line === ALL} onClick={() => updatePrefs({ line: ALL, model: ALL })}>
                        All
                      </button>
                      {lineOptions.map(([code, count]) => (
                        <button
                          key={code}
                          type="button"
                          className={`filter-chip${line === code ? ' active' : ''}`}
                          aria-pressed={line === code}
                          onClick={() => updatePrefs({ line: code, model: ALL })}
                          title={`${count} pipeline(s)`}
                        >
                          {code}
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              {visibleRows.length === 0 ? (
                <div className="pipeline-hub-empty">
                  <p>No pipelines match the current filters.</p>
                  <button type="button" className="ghost-button compact" onClick={clearFilters}>
                    Clear filters
                  </button>
                </div>
              ) : prefs.view === 'cards' ? (
                <div className="pipeline-hub-grid">
                  {visibleRows.map((row) => {
                    const p = row.pipeline
                    const imageApi = p.imageCrawlerApiUrl || 'Default PC API'
                    return (
                      <article key={p.id} className={`pipeline-card is-${row.bucket}`} aria-label={`${p.name}, ${row.statusLabel ?? 'never executed'}`}>
                        <strong className="pipeline-card-name" title={p.name}>{p.name}</strong>
                        <div className="pipeline-card-tags">
                          <span>{p.machineCode}</span>
                          <span>Line {p.lineCode}</span>
                          {p.modelCode ? <span>{p.modelCode}</span> : null}
                          <ScheduleBadge pipeline={p} />
                        </div>
                        <dl className="pipeline-card-meta">
                          <dt>Connection</dt>
                          <dd title={p.connectionName}>{p.connectionName}</dd>
                          <dt>Tables</dt>
                          <dd>
                            <code>{p.productTableName}</code> · {p.defectTables.length} defect
                          </dd>
                          <dt>Image API</dt>
                          <dd title={imageApi}>
                            <code className="pipeline-image-api">{imageApi}</code>
                          </dd>
                        </dl>
                        <div className="pipeline-card-foot">
                          <div className="pipeline-card-foot-row">
                            <span className="pipeline-foot-label">Last ingestion</span>
                            {row.statusLabel ? <StatusBadge bucket={row.bucket} label={row.statusLabel} /> : null}
                          </div>
                          <div className="pipeline-card-foot-row">
                            {renderLastRun(row)}
                            {renderRunButton(row)}
                          </div>
                          <div className="pipeline-card-foot-row pipeline-muted">{renderResult(row)}</div>
                        </div>
                      </article>
                    )
                  })}
                </div>
              ) : (
                <div className="table-wrap pipeline-hub-table">
                  <table>
                    <thead>
                      <tr>
                        <th>{sortHeader('Status', 'priority')}</th>
                        <th>{sortHeader('Pipeline', 'name')}</th>
                        <th>Machine</th>
                        <th>{sortHeader('Line', 'line')}</th>
                        <th>Model</th>
                        <th>Connection</th>
                        <th>Tables</th>
                        <th>Image API</th>
                        <th>Schedule</th>
                        <th>Last ingestion</th>
                        <th>Result</th>
                        <th className="pipeline-col-action" aria-label="Actions" />
                      </tr>
                    </thead>
                    <tbody>
                      {visibleRows.map((row) => {
                        const p = row.pipeline
                        const imageApi = p.imageCrawlerApiUrl || 'Default PC API'
                        return (
                          <tr key={p.id} className={`pipeline-row is-${row.bucket}`}>
                            <td>{row.statusLabel ? <StatusBadge bucket={row.bucket} label={row.statusLabel} /> : <span className="pipeline-muted">—</span>}</td>
                            <td className="pipeline-row-name" title={p.name}>{p.name}</td>
                            <td>{p.machineCode}</td>
                            <td>{p.lineCode}</td>
                            <td>{p.modelCode || '—'}</td>
                            <td>{p.connectionName}</td>
                            <td>
                              <code>{p.productTableName}</code> · {p.defectTables.length} defect
                            </td>
                            <td className="pipeline-row-api" title={imageApi}>
                              <code className="pipeline-image-api">{imageApi}</code>
                            </td>
                            <td><ScheduleBadge pipeline={p} /></td>
                            <td>{renderLastRun(row)}</td>
                            <td className="pipeline-muted">{renderResult(row)}</td>
                            <td className="pipeline-col-action">{renderRunButton(row)}</td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </>
      )}
    </section>
  )
}
