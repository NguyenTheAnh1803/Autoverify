import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { connectionsApi, dataSourcesApi } from '../api'
import { formatDate, formNumber, formString, paginate } from '../format'
import { BatchConnectionsModal } from '../components/BatchConnectionsModal'
import { DataTable, Field, Modal, Pagination, StatusPill } from '../ui'
import type { CreateDatabaseConnectionRequest, DatabaseConnection, DataSource, Notice } from '../types'

function connectionCredentialHint(status: string) {
  if (status === 'Unreadable') return 'Saved password cannot be decrypted. Enter it again before saving.'
  if (status === 'Missing') return 'No password is saved. Enter one before saving.'
  return 'Leave blank to keep the current encrypted password.'
}

export function SourcesTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [sources, setSources] = useState<DataSource[]>([])
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [sourceOpen, setSourceOpen] = useState(false)
  const [connectionOpen, setConnectionOpen] = useState(false)
  const [batchOpen, setBatchOpen] = useState(false)
  const [editing, setEditing] = useState<DatabaseConnection | null>(null)
  const [busy, setBusy] = useState(false)
  const [machineFilter, setMachineFilter] = useState('')
  const [categoryFilter, setCategoryFilter] = useState('')
  const [search, setSearch] = useState('')
  const [engine, setEngine] = useState('')
  const [appliedSources, setAppliedSources] = useState({ machineCode: '', verificationCategory: '' })
  const [appliedConnections, setAppliedConnections] = useState({ search: '', engine: '' })
  const [sourcePage, setSourcePage] = useState(1)
  const pagedSources = paginate(sources, sourcePage)

  const knownMachines = useMemo(() => {
    const set = new Set<string>(['MACHINE-A', 'MACHINE-B', 'MACHINE-C'])
    connections.forEach((c) => { if (c.machineCode) set.add(c.machineCode) })
    return [...set].sort()
  }, [connections])

  const knownLines = useMemo(() => {
    const set = new Set<string>(['LINE-1', 'LINE-2', 'LINE-7', 'ALL'])
    connections.forEach((c) => { if (c.lineCode) set.add(c.lineCode) })
    return [...set].sort()
  }, [connections])

  const loadSources = useCallback(async () => {
    try {
      setSources(await dataSourcesApi.list({
        machineCode: appliedSources.machineCode || undefined,
        verificationCategory: appliedSources.verificationCategory || undefined,
      }))
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load data sources.' })
    }
  }, [appliedSources, onNotice])

  const loadConnections = useCallback(async (nextPage = 1) => {
    try {
      const payload = await connectionsApi.list({
        search: appliedConnections.search || undefined,
        engine: appliedConnections.engine || undefined,
        page: nextPage,
        pageSize: 25,
      })
      setConnections(payload.items)
      setPage(payload.page)
      setTotalPages(payload.totalPages)
      setTotalCount(payload.totalCount)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load connections.' })
    }
  }, [appliedConnections, onNotice])

  useEffect(() => {
    void loadSources()
    void loadConnections(1)
  }, [loadSources, loadConnections])

  async function createSource(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      await dataSourcesApi.create({
        name: formString(event, 'name'),
        type: formString(event, 'type'),
        machineCode: formString(event, 'machineCode'),
        verificationCategory: formString(event, 'verificationCategory'),
        schedulePeriod: formString(event, 'schedulePeriod'),
        environment: formString(event, 'environment'),
        enabled: formString(event, 'enabled') === 'true',
      })
      setSourceOpen(false)
      onNotice({ tone: 'success', message: 'Data source created.' })
      await loadSources()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Create data source failed.' })
    } finally {
      setBusy(false)
    }
  }

  function connectionPayload(event: FormEvent<HTMLFormElement>): CreateDatabaseConnectionRequest {
    return {
      name: formString(event, 'name'),
      engine: formString(event, 'engine'),
      machineCode: formString(event, 'machineCode'),
      lineCode: formString(event, 'lineCode'),
      modelCode: formString(event, 'modelCode') || 'UNASSIGNED',
      host: formString(event, 'host') || null,
      port: formNumber(event, 'port', 3306),
      databaseName: formString(event, 'databaseName') || null,
      username: formString(event, 'username') || null,
      password: formString(event, 'password') || null,
      apiBaseUrl: formString(event, 'imageCrawlerApiUrl') || formString(event, 'apiBaseUrl') || null,
      imageCrawlerApiUrl: formString(event, 'imageCrawlerApiUrl') || formString(event, 'apiBaseUrl') || null,
      imageCrawlerVpMode: formString(event, 'imageCrawlerVpMode') || '0',
      apiKey: formString(event, 'apiKey') || null,
    }
  }

  async function saveConnection(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      const body = connectionPayload(event)
      if (editing) await connectionsApi.update(editing.id, body)
      else await connectionsApi.create(body)
      setConnectionOpen(false)
      setEditing(null)
      onNotice({ tone: 'success', message: editing ? 'Connection updated.' : 'Connection created.' })
      await loadConnections(1)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Save connection failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function testSource(id: string) {
    try {
      const result = await dataSourcesApi.test(id)
      onNotice({
        tone: result.success ? 'success' : 'error',
        message: `${result.sourceName}: ${result.message} (${result.latencyMs} ms)`,
      })
      await loadSources()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Source test failed.' })
    }
  }

  async function testConnection(id: string) {
    try {
      const result = await connectionsApi.test(id)
      onNotice({
        tone: result.success ? 'success' : 'error',
        message: `${result.sourceName}: ${result.message} (${result.latencyMs} ms)`,
      })
      await loadConnections(page)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Connection test failed.' })
    }
  }

  return (
    <div className="tab-stack">
      <div className="section-heading">
        <div>
          <p className="eyebrow">GET /data-sources</p>
          <h2>Verification sources</h2>
        </div>
        <button type="button" onClick={() => setSourceOpen(true)}>
          Add source
        </button>
      </div>

      <form
        className="compact-filter-bar"
        onSubmit={(event) => {
          event.preventDefault()
          setAppliedSources({ machineCode: machineFilter, verificationCategory: categoryFilter })
        }}
      >
        <Field label="Machine code">
          <input value={machineFilter} onChange={(event) => setMachineFilter(event.target.value)} />
        </Field>
        <Field label="Verification category">
          <input value={categoryFilter} onChange={(event) => setCategoryFilter(event.target.value)} />
        </Field>
        <button type="submit" className="ghost-button">
          Filter
        </button>
      </form>

      <DataTable
        columns={['Name', 'Type', 'Machine', 'Category', 'Health', 'Latency', 'Enabled', '']}
        rows={pagedSources.items.map((source) => ({
          id: source.id,
          cells: [
            source.name,
            source.type,
            source.machineCode,
            source.verificationCategory,
            <StatusPill key="h" value={source.health} />,
            `${source.averageLatencyMs} ms`,
            source.enabled ? 'Yes' : 'No',
            <button key="t" type="button" className="table-button" onClick={() => void testSource(source.id)}>
              Test
            </button>,
          ],
        }))}
      />
      <Pagination page={pagedSources.page} totalPages={pagedSources.totalPages} totalCount={pagedSources.totalCount} onPage={setSourcePage} />

      <div className="section-heading">
        <div>
          <p className="eyebrow">GET /database-connections</p>
          <h2>Database connections</h2>
        </div>
        <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
          <button
            type="button"
            className="primary"
            style={{ background: '#2563eb', borderColor: '#3b82f6', fontWeight: 600 }}
            onClick={() => setBatchOpen(true)}
            title="Batch register database connections for 80+ lines using Multi-Path paste with preview"
          >
            🚀 Batch Add Connections (Multi-Path)
          </button>
          <button
            type="button"
            onClick={() => {
              setEditing(null)
              setConnectionOpen(true)
            }}
          >
            + Add Single Connection
          </button>
        </div>
      </div>

      <form
        className="compact-filter-bar"
        onSubmit={(event) => {
          event.preventDefault()
          setAppliedConnections({ search, engine })
        }}
      >
        <Field label="Search" className="filter-search">
          <input value={search} onChange={(event) => setSearch(event.target.value)} />
        </Field>
        <Field label="Engine">
          <input value={engine} onChange={(event) => setEngine(event.target.value)} placeholder="MariaDB" />
        </Field>
        <button type="submit" className="ghost-button">
          Filter
        </button>
      </form>

      <DataTable
        eyebrow="GET /database-connections"
        title="Database Connections (Machine / Line mapping)"
        columns={['Name', 'Model', 'Machine', 'Line', 'Engine', 'Database / Host', 'PC Image API', 'Health', 'Secret', 'Updated', '']}
        rows={connections.map((connection) => ({
          id: connection.id,
          cells: [
            connection.name,
            <span key="model" className="line-pill" style={{ color: '#b45309' }}>{connection.modelCode || 'UNASSIGNED'}</span>,
            <span key="m" className="line-pill">{connection.machineCode}</span>,
            <span key="l" className="line-pill">{connection.lineCode}</span>,
            connection.engine,
            `${connection.databaseName || '—'} (${connection.host}:${connection.port})`,
            <code key="api" style={{ fontSize: '11px', color: '#7c3aed', background: 'rgba(124, 58, 237, 0.08)', padding: '2px 6px', borderRadius: '4px' }}>
              {connection.imageCrawlerApiUrl || connection.apiBaseUrl || '—'}
            </code>,
            <StatusPill key="h" value={connection.health} />,
            <span
              key="secret"
              className={`credential-state ${(connection.credentialStatus || (connection.hasSecret ? 'Protected' : 'Missing')).toLowerCase()}`}
              title={connection.credentialStatus === 'Unreadable'
                ? 'The saved password cannot be decrypted with the current Data Protection key ring.'
                : undefined}
            >
              {connection.credentialStatus || (connection.hasSecret ? 'Protected' : 'Missing')}
            </span>,
            formatDate(connection.updatedAtUtc),
            <div key="a" className="row-actions">
              <button type="button" className="table-button" onClick={() => void testConnection(connection.id)}>
                Test
              </button>
              <button
                type="button"
                className="table-button"
                onClick={() => {
                  setEditing(connection)
                  setConnectionOpen(true)
                }}
              >
                Edit
              </button>
            </div>,
          ],
        }))}
      />
      <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPage={(next) => void loadConnections(next)} />

      <Modal title="Create data source" description="POST /data-sources" open={sourceOpen} onClose={() => setSourceOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void createSource(event)}>
          <div className="query-form-grid">
            <Field label="Name" required>
              <input name="name" required />
            </Field>
            <Field label="Type" required>
              <input name="type" defaultValue="MariaDB" required />
            </Field>
            <Field label="Machine code" required>
              <input name="machineCode" required />
            </Field>
            <Field label="Verification category" required>
              <input name="verificationCategory" defaultValue="Leakage" required />
            </Field>
            <Field label="Schedule period" required>
              <input name="schedulePeriod" defaultValue="Daily" required />
            </Field>
            <Field label="Environment" required>
              <input name="environment" defaultValue="Production" required />
            </Field>
            <Field label="Enabled" required>
              <select name="enabled" defaultValue="true">
                <option value="true">Yes</option>
                <option value="false">No</option>
              </select>
            </Field>
          </div>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Save
            </button>
            <button type="button" className="ghost-button" onClick={() => setSourceOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      <Modal
        title={editing ? 'Edit connection' : 'Create connection'}
        description={editing ? 'PUT /database-connections/{id}' : 'POST /database-connections'}
        open={connectionOpen}
        onClose={() => setConnectionOpen(false)}
        wide
      >
        <form className="modal-form" onSubmit={(event) => void saveConnection(event)}>
          <div className="query-form-grid">
            <Field label="Name" required>
              <input name="name" defaultValue={editing?.name ?? ''} required />
            </Field>
            <Field label="Engine" required>
              <input name="engine" defaultValue={editing?.engine ?? 'MariaDB'} required />
            </Field>
            <Field label="Machine code" required hint="Standard machine reference (e.g. MACHINE-A)">
              <input name="machineCode" list="machine-standards" defaultValue={editing?.machineCode ?? 'MACHINE-A'} required />
              <datalist id="machine-standards">
                {knownMachines.map((m) => (
                  <option key={m} value={m} />
                ))}
              </datalist>
            </Field>
            <Field label="Line code" required hint="Standard line reference (e.g. LINE-1, ALL)">
              <input name="lineCode" list="line-standards" defaultValue={editing?.lineCode ?? 'LINE-1'} required />
              <datalist id="line-standards">
                {knownLines.map((l) => (
                  <option key={l} value={l} />
                ))}
              </datalist>
            </Field>
            <Field label="Model" required hint="Model group for this line (e.g. D111, D222)">
              <input name="modelCode" defaultValue={editing?.modelCode ?? 'UNASSIGNED'} placeholder="D111" required />
            </Field>
            <Field label="Host">
              <input name="host" defaultValue={editing?.host ?? ''} />
            </Field>
            <Field label="Port">
              <input name="port" type="number" defaultValue={editing?.port ?? 3306} />
            </Field>
            <Field label="Database">
              <input name="databaseName" defaultValue={editing?.databaseName ?? ''} />
            </Field>
            <Field label="Username">
              <input name="username" defaultValue={editing?.username ?? ''} autoComplete="username" />
            </Field>
            <Field
              label="Password"
              hint={editing
                ? connectionCredentialHint(editing.credentialStatus || (editing.hasSecret ? 'Protected' : 'Missing'))
                : 'Required for MariaDB authentication. Stored encrypted at rest.'}
            >
              <input
                name="password"
                type="password"
                autoComplete="new-password"
                required={!editing || (editing.credentialStatus || (editing.hasSecret ? 'Protected' : 'Missing')) === 'Missing' || editing.credentialStatus === 'Unreadable'}
                placeholder={(editing?.credentialStatus || (editing?.hasSecret ? 'Protected' : 'Missing')) === 'Protected' ? 'Leave blank to keep saved password' : 'Enter database password'}
              />
            </Field>
            <Field
              label="PC Image Crawler API URL"
              hint="AnywherePix API used to read ZIP/JPG data resolved from image_path"
            >
              <input
                name="imageCrawlerApiUrl"
                defaultValue={editing?.imageCrawlerApiUrl ?? editing?.apiBaseUrl ?? ''}
                placeholder="http://10.121.45.43:336/api/image/get"
              />
            </Field>
            <Field
              label="VP Setting (Camera / Viewpoint)"
              hint="Configure crawler VP mode sent to server (Default: VP = 0 thumbnail)"
            >
              <select
                name="imageCrawlerVpMode"
                defaultValue={editing?.imageCrawlerVpMode ?? 'all'}
                style={{ fontSize: '12px', fontWeight: 600 }}
              >
                <option value="all">⚡ All VPs (Auto-try VP 0 & other VPs)</option>
                <option value="0">VP = 0 (Prefer Thumbnail)</option>
                <option value="auto">Auto VP (Feature G010_VpID / Path)</option>
                <option value="1">VP = 1</option>
                <option value="2">VP = 2</option>
                <option value="3">VP = 3</option>
                <option value="4">VP = 4</option>
              </select>
            </Field>
            <Field label="API key">
              <input name="apiKey" type="password" />
            </Field>
          </div>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Save
            </button>
            <button type="button" className="ghost-button" onClick={() => setConnectionOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      {/* Batch Connections Modal */}
      <BatchConnectionsModal
        open={batchOpen}
        onClose={() => setBatchOpen(false)}
        onNotice={onNotice}
        onCompleted={() => {
          void loadConnections(1)
        }}
      />
    </div>
  )
}
