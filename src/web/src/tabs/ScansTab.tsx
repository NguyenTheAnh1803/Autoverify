import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { dataSourcesApi, scanExecutionsApi, scanListsApi } from '../api'
import { formatDate, formString } from '../format'
import { DataTable, Field, Modal, Pagination, StatusPill } from '../ui'
import type { DataSource, Notice, ScanExecution, ScanList, VerificationResult } from '../types'

export function ScansTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [lists, setLists] = useState<ScanList[]>([])
  const [sources, setSources] = useState<DataSource[]>([])
  const [executions, setExecutions] = useState<ScanExecution[]>([])
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [runOpen, setRunOpen] = useState(false)
  const [resultsOpen, setResultsOpen] = useState(false)
  const [selected, setSelected] = useState<ScanExecution | null>(null)
  const [results, setResults] = useState<VerificationResult[]>([])
  const [resultPage, setResultPage] = useState(1)
  const [resultTotalPages, setResultTotalPages] = useState(1)
  const [resultTotal, setResultTotal] = useState(0)
  const [busy, setBusy] = useState(false)

  const load = useCallback(async (nextPage = 1) => {
    try {
      const [nextLists, nextSources, nextExecutions] = await Promise.all([
        scanListsApi.list(),
        dataSourcesApi.list(),
        scanExecutionsApi.list(nextPage, 25),
      ])
      setLists(nextLists)
      setSources(nextSources)
      setExecutions(nextExecutions.items)
      setPage(nextExecutions.page)
      setTotalPages(nextExecutions.totalPages)
      setTotalCount(nextExecutions.totalCount)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load scans.' })
    }
  }, [onNotice])

  useEffect(() => {
    void load(1)
  }, [load])

  async function openResults(execution: ScanExecution, nextPage = 1) {
    setSelected(execution)
    setResultsOpen(true)
    try {
      const payload = await scanExecutionsApi.results(execution.id, nextPage, 25)
      setResults(payload.items)
      setResultPage(payload.page)
      setResultTotalPages(payload.totalPages)
      setResultTotal(payload.totalCount)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load results.' })
    }
  }

  async function startScan(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const scanListId = formString(event, 'scanListId')
    const name = formString(event, 'name')
    const selectedIds = [...form.querySelectorAll<HTMLInputElement>('input[name="sourceIds"]:checked')].map((input) => input.value)
    if (!scanListId) {
      onNotice({ tone: 'error', message: 'Select a scan list first.' })
      return
    }
    setBusy(true)
    try {
      const created = await scanExecutionsApi.create({ scanListId, name, sourceIds: selectedIds })
      setRunOpen(false)
      onNotice({ tone: 'success', message: `Scan '${created.name}' completed with status ${created.status}.` })
      await load(1)
      await openResults(created)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Scan start failed.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tab-stack">
      <div className="section-heading">
        <div>
          <p className="eyebrow">GET /scan-executions</p>
          <h2>Verification executions</h2>
        </div>
        <button type="button" onClick={() => setRunOpen(true)}>
          Run scan
        </button>
      </div>

      <DataTable
        columns={['Name', 'List', 'Status', 'Processed', 'Failed', 'Anomalies', 'Started']}
        rows={executions.map((execution) => ({
          id: execution.id,
          cells: [
            execution.name,
            execution.scanListName,
            <StatusPill key="st" value={execution.status} />,
            execution.totalItems,
            execution.failedItems,
            execution.anomalyItems,
            <button key="open" type="button" className="table-button" onClick={() => void openResults(execution)}>
              {formatDate(execution.startedAtUtc)}
            </button>,
          ],
        }))}
      />
      <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPage={(next) => void load(next)} />

      <Modal title="Start verification scan" description="POST /scan-executions" open={runOpen} onClose={() => setRunOpen(false)}>
        <form className="modal-form" onSubmit={(event) => void startScan(event)}>
          <Field label="Scan list" required>
            <select name="scanListId" defaultValue={lists[0]?.id ?? ''} required>
              {lists.map((list) => (
                <option key={list.id} value={list.id}>
                  {list.name} ({list.acceptedCount} IDs)
                </option>
              ))}
            </select>
          </Field>
          <Field label="Scan name" required>
            <input name="name" defaultValue="Manual verification scan" required />
          </Field>
          <fieldset className="checkbox-set">
            <legend>Data sources</legend>
            {sources.map((source) => (
              <label key={source.id} className="check-row">
                <input type="checkbox" name="sourceIds" value={source.id} defaultChecked={source.enabled} />
                {source.name} · {source.type}
              </label>
            ))}
          </fieldset>
          <div className="actions">
            <button type="submit" disabled={busy}>
              Start scan
            </button>
            <button type="button" className="ghost-button" onClick={() => setRunOpen(false)}>
              Cancel
            </button>
          </div>
        </form>
      </Modal>

      <Modal
        title={selected ? `Results · ${selected.name}` : 'Results'}
        description="GET /scan-executions/{id}/results"
        open={resultsOpen}
        onClose={() => setResultsOpen(false)}
        wide
      >
        <DataTable
          columns={['Entity', 'Source', 'Status', 'Severity', 'Rule', 'Score', 'Message']}
          rows={results.map((result) => ({
            id: result.id,
            cells: [
              result.entityId,
              result.sourceName,
              <StatusPill key="st" value={result.status} />,
              result.severity,
              result.ruleCode,
              result.anomalyScore.toFixed(2),
              result.message,
            ],
          }))}
        />
        <Pagination
          page={resultPage}
          totalPages={resultTotalPages}
          totalCount={resultTotal}
          onPage={(next) => selected && void openResults(selected, next)}
        />
      </Modal>
    </div>
  )
}
