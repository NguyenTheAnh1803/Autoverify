import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { connectionsApi, importsApi, scanListsApi, verificationApi } from '../api'
import { formatDate, paginate } from '../format'
import { DataTable, Field, Modal, Pagination, StatusPill } from '../ui'
import type { DatabaseConnection, ImportPreview, Notice, ScanList, VerifyBatchResponse } from '../types'

const LEAKAGE_SAMPLE = 'DXDIAN\t301\t10\nANNXKK\t302\t15\nNDNNAJM\t501\t134'

export function ImportTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [lists, setLists] = useState<ScanList[]>([])
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [rawText, setRawText] = useState(LEAKAGE_SAMPLE)
  const [machineCode, setMachineCode] = useState('')
  const [lineCode, setLineCode] = useState('All')
  const [category, setCategory] = useState('Leakage')
  const [busy, setBusy] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [listName, setListName] = useState('')
  const [verifyResult, setVerifyResult] = useState<VerifyBatchResponse | null>(null)
  const [listPage, setListPage] = useState(1)
  const [verifyPage, setVerifyPage] = useState(1)
  const pagedLists = paginate(lists, listPage)
  const pagedVerify = paginate(verifyResult?.items ?? [], verifyPage)

  const machines = useMemo(() => {
    const unique = [...new Set(connections.map((connection) => connection.machineCode).filter(Boolean))]
    return unique.sort((a, b) => a.localeCompare(b))
  }, [connections])

  const linesForMachine = useMemo(() => {
    const lines = connections
      .filter((connection) => !machineCode || connection.machineCode === machineCode)
      .map((connection) => connection.lineCode)
      .filter(Boolean)
    return [...new Set(lines)].sort((a, b) => a.localeCompare(b))
  }, [connections, machineCode])

  const selectedConnections = useMemo(
    () => connections.filter((connection) => connection.machineCode === machineCode),
    [connections, machineCode],
  )

  const loadLists = useCallback(async () => {
    try {
      setLists(await scanListsApi.list())
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load scan lists.' })
    }
  }, [onNotice])

  useEffect(() => {
    void loadLists()
    void connectionsApi
      .list({ page: 1, pageSize: 100 })
      .then((payload) => {
        const items = payload.items ?? []
        setConnections(items)
        setMachineCode((current) => current || items[0]?.machineCode || '')
      })
      .catch((error: unknown) => {
        onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load database connections.' })
      })
  }, [loadLists, onNotice])

  function onMachineChange(nextMachine: string) {
    setMachineCode(nextMachine)
    if (nextMachine === 'AUTO') {
      setLineCode('All')
      return
    }
    const nextLines = connections
      .filter((connection) => connection.machineCode === nextMachine)
      .map((connection) => connection.lineCode)
    setLineCode(nextLines[0] || 'All')
  }

  function handleCategoryChange(nextCategory: 'Leakage' | 'Overkill') {
    setCategory(nextCategory)
    if (nextCategory === 'Overkill') {
      // Overkill: automatically select the required machine if available or AUTO
      const detected = machines[0] || 'AUTO'
      onMachineChange(detected)
      onNotice({
        tone: 'info',
        message: `Overkill mode: Auto-selected machine [${detected}] based on configuration.`,
      })
    }
  }

  async function previewImport() {
    const targetMachine = machineCode || (category === 'Overkill' ? 'AUTO' : '')
    if (!targetMachine) {
      onNotice({ tone: 'error', message: 'Please select a standard machine from database connections before preview.' })
      return
    }
    setBusy(true)
    try {
      const next = await importsApi.preview({
        rawText,
        mappingName: category === 'Leakage' ? 'mappingInt' : 'entityId',
        machineCode: targetMachine,
        lineCode: lineCode === 'All' ? null : lineCode,
        verificationCategory: category,
      })
      setPreview(next)
      onNotice({ tone: 'success', message: `Preview complete: ${next.acceptedCount} accepted, ${next.rejectedCount} rejected.` })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Preview failed.' })
    } finally {
      setBusy(false)
    }
  }

  function selectedConnectionId() {
    return selectedConnections[0]?.id ?? null
  }

  async function runVerify() {
    const targetMachine = machineCode || (category === 'Overkill' ? 'AUTO' : '')
    if (!targetMachine) {
      onNotice({ tone: 'error', message: 'Please select a standard machine from database connections first.' })
      return
    }
    setBusy(true)
    try {
      const body = {
        rawText,
        machineCode: targetMachine,
        lineCode: lineCode === 'All' ? null : lineCode,
        mappingName: category === 'Leakage' ? 'mappingInt' : 'entityId',
        connectionId: selectedConnectionId(),
      }
      const result = category === 'Overkill' ? await verificationApi.overkill(body) : await verificationApi.leakage(body)
      setVerifyResult(result)
      setVerifyPage(1)
      const skipNote = result.skippedItems ? `, skipped ${result.skippedItems} non NG/N/N/A` : ''
      onNotice({
        tone: 'success',
        message: `${result.category} verification completed: ${result.totalItems} rows (${result.eligibleItems} eligible${skipNote}). Machine: ${targetMachine}.`,
      })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Verify failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function createList(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const targetMachine = machineCode || (category === 'Overkill' ? 'AUTO' : '')
    if (!targetMachine) {
      onNotice({ tone: 'error', message: 'Please select a standard machine from database connections first.' })
      return
    }
    setBusy(true)
    try {
      const created = await scanListsApi.create({
        name: listName || `${category} ${targetMachine} ${new Date().toISOString().slice(0, 16)}`,
        mappingName: category === 'Leakage' ? 'mappingInt' : 'entityId',
        rawText,
        machineCode: targetMachine,
        lineCode: lineCode === 'All' ? null : lineCode,
        verificationCategory: category,
      })
      setCreateOpen(false)
      onNotice({ tone: 'success', message: `Created scan list '${created.name}' (${created.acceptedCount} IDs).` })
      await loadLists()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Create scan list failed.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tab-stack">
      <section className="feature-grid">
        <article className="panel form-panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">POST /imports/preview</p>
              <h2>Verify {category}</h2>
            </div>
          </div>
          <div className="category-type-selector">
            {(['Leakage', 'Overkill'] as const).map((value) => (
              <button
                key={value}
                type="button"
                className={`type-toggle-btn ${category === value ? `active ${value.toLowerCase()}` : ''}`}
                onClick={() => handleCategoryChange(value)}
              >
                <span>
                  <strong>{value}</strong>
                  <small>{value === 'Leakage' ? 'Standard Machine/Line selection' : 'Automatic Machine detection'}</small>
                </span>
              </button>
            ))}
          </div>

          <div className="compact-filter-bar">
            <Field
              label={category === 'Overkill' ? 'Machine (Auto / Select)' : 'Standard Machine'}
              required={category === 'Leakage'}
              hint={category === 'Overkill' ? 'Auto-detected from NG records or manual select' : 'Select standard machine from connections'}
            >
              <select value={machineCode} onChange={(event) => onMachineChange(event.target.value)} required={category === 'Leakage'}>
                {category === 'Overkill' && <option value="AUTO">Auto-detect Machine</option>}
                {machines.length === 0 ? <option value="">No connections registered</option> : null}
                {machines.map((machine) => (
                  <option key={machine} value={machine}>
                    {machine}
                  </option>
                ))}
              </select>
            </Field>
            <Field
              label={category === 'Overkill' ? 'Line (Auto / Select)' : 'Standard Line'}
              hint={category === 'Overkill' ? 'Auto-detected with machine' : 'Filtered by selected machine'}
            >
              <select value={lineCode} onChange={(event) => setLineCode(event.target.value)}>
                <option value="All">All lines on machine</option>
                {linesForMachine.map((line) => (
                  <option key={line} value={line}>
                    {line}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Linked Connection">
              <input
                readOnly
                value={
                  category === 'Overkill' && machineCode === 'AUTO'
                    ? 'Auto-bound to matching connection'
                    : selectedConnections.length
                      ? `${selectedConnections.length} connection(s) linked`
                      : 'No linked connection'
                }
              />
            </Field>
          </div>

          {selectedConnections.length > 0 ? (
            <div className="connection-picker">
              {selectedConnections.map((connection) => (
                <div key={connection.id} className="source-row">
                  <div>
                    <strong>{connection.name}</strong>
                    <small>
                      {connection.engine} · {connection.host}:{connection.port}/{connection.databaseName}
                    </small>
                  </div>
                  <StatusPill value={connection.health} />
                  <span className="line-pill">{connection.lineCode}</span>
                  <span className="secret-state">{connection.hasSecret ? 'Secret protected' : 'No secret'}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="muted">Register a database connection first so this combobox can bind a machine.</p>
          )}

          {category === 'Leakage' ? (
            <div className="csv-concept-hint">
              Leakage paste format: <code>ID</code> · <code>Line</code> · <code>Mapping (int)</code>
              <br />
              Example: <code>DXDIAN 301 10</code>
            </div>
          ) : (
            <div className="csv-concept-hint">
              Overkill paste format: <code>ID</code> · <code>Line</code>. Paste the Overkill sheet when ready.
            </div>
          )}

          <Field label="Raw ID list" required>
            <textarea value={rawText} onChange={(event) => setRawText(event.target.value)} />
          </Field>
          <div className="actions">
            <button
              type="button"
              disabled={busy || (!machineCode && category === 'Leakage')}
              onClick={() => void previewImport()}
            >
              Preview import
            </button>
            <button
              type="button"
              className="secondary"
              disabled={busy || (!machineCode && category === 'Leakage')}
              onClick={() => setCreateOpen(true)}
            >
              Create scan list
            </button>
            <button
              type="button"
              disabled={busy || (!machineCode && category === 'Leakage')}
              onClick={() => void runVerify()}
            >
              {category === 'Overkill' ? 'Auto-verify Overkill (NG/N/N/A)' : 'Verify Leakage (Standard)'}
            </button>
          </div>
        </article>

        <article className="panel info-panel">
          <p className="eyebrow">Preview result</p>
          <h2>Validation summary</h2>
          {preview ? (
            <>
              {preview.hasLineWarning ? (
                <div className="line-warning-banner">No specific line selected. Rows without a line will scan the whole machine.</div>
              ) : null}
              <p>
                Machine <strong>{preview.machineCode}</strong>
                {preview.lineCode ? <> · Line <strong>{preview.lineCode}</strong></> : null}
              </p>
              <p>
                <strong>{preview.acceptedCount}</strong> accepted · <strong>{preview.rejectedCount}</strong> rejected ·{' '}
                <strong>{preview.duplicateCount}</strong> duplicates
              </p>
              <code>{preview.checksum}</code>
              <div className="mini-preview-table-wrap">
                <table className="mini-preview-table">
                  <thead>
                    <tr>
                      <th>Row</th>
                      <th>ID</th>
                      <th>Line</th>
                      <th>Mapping</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.acceptedRows.slice(0, 12).map((row) => (
                      <tr key={`ok-${row.rowNumber}`}>
                        <td>{row.rowNumber}</td>
                        <td>{row.entityId}</td>
                        <td>
                          <span className="line-pill">{row.lineCode || 'ALL'}</span>
                        </td>
                        <td>
                          <span className={`mapping-pill ${row.mappingInt != null ? 'int-pill' : ''}`}>
                            {row.mappingInt ?? row.mapping ?? '—'}
                          </span>
                        </td>
                        <td className="row-ok-text">{row.warning || 'Accepted'}</td>
                      </tr>
                    ))}
                    {preview.rejectedRows.slice(0, 8).map((row) => (
                      <tr key={`ng-${row.rowNumber}`}>
                        <td>{row.rowNumber}</td>
                        <td colSpan={3}>{row.rawValue}</td>
                        <td className="row-warning-text">{row.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          ) : (
            <p>Select a machine from database connections, then preview the Leakage list.</p>
          )}
        </article>
      </section>

      {verifyResult ? (
        <section className="panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">POST /verify/{verifyResult.category.toLowerCase()}</p>
              <h2>Latest {verifyResult.category} batch</h2>
            </div>
            <span>
              {verifyResult.eligibleItems} eligible · {verifyResult.skippedItems} skipped · {verifyResult.overkillCount} overkill · {verifyResult.leakageCount} leakage
            </span>
          </div>
          <DataTable
            columns={['ID', 'Judge', 'Eligible', 'Status', 'Machine', 'Line', 'Connection', 'Query', 'Notes']}
            rows={pagedVerify.items.map((item) => ({
              id: item.id,
              cells: [
                item.entityId,
                item.judge,
                item.eligible ? 'Yes' : 'No',
                <StatusPill key="s" value={item.anomalyStatus} />,
                item.machineCode,
                item.lineCode,
                item.connectionName || '—',
                item.queryName || '—',
                item.analysisNotes,
              ],
            }))}
          />
          <Pagination page={pagedVerify.page} totalPages={pagedVerify.totalPages} totalCount={pagedVerify.totalCount} onPage={setVerifyPage} />
        </section>
      ) : null}

      <DataTable
        eyebrow="GET /scan-lists"
        title="Saved scan lists"
        columns={['Name', 'Category', 'Machine', 'Line', 'Accepted', 'Rejected', 'Created']}
        rows={pagedLists.items.map((list) => ({
          id: list.id,
          cells: [
            list.name,
            <StatusPill key="cat" value={list.verificationCategory} />,
            list.machineCode || '—',
            list.lineCode || 'ALL',
            list.acceptedCount,
            list.rejectedCount,
            formatDate(list.createdAtUtc),
          ],
        }))}
      />
      <Pagination page={pagedLists.page} totalPages={pagedLists.totalPages} totalCount={pagedLists.totalCount} onPage={setListPage} />

      <Modal title="Create scan list" description="POST /scan-lists" open={createOpen} onClose={() => setCreateOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void createList(event)}>
          <Field label="List name" required>
            <input name="name" value={listName} onChange={(event) => setListName(event.target.value)} required />
          </Field>
          <p className="muted">
            {category} · machine {machineCode || '—'} · line {lineCode}
          </p>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Save list
            </button>
            <button type="button" className="ghost-button" onClick={() => setCreateOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>
    </div>
  )
}
