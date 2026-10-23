import { useCallback, useEffect, useState } from 'react'
import { verificationApi } from '../api'
import { formatDate } from '../format'
import { DataTable, Field, Pagination, StatusPill, useLoadStatus } from '../ui'
import type { Notice, VerificationHistory } from '../types'

export function HistoryTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [rows, setRows] = useState<VerificationHistory[]>([])
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [category, setCategory] = useState('All')
  const [machineCode, setMachineCode] = useState('')
  const [lineCode, setLineCode] = useState('')
  const [applied, setApplied] = useState({ category: 'All', machineCode: '', lineCode: '' })
  const { status, error: loadError, begin, succeed, fail } = useLoadStatus()

  const load = useCallback(async (nextPage = 1) => {
    try {
      begin()
      const payload = await verificationApi.history({
        category: applied.category === 'All' ? undefined : applied.category,
        machineCode: applied.machineCode || undefined,
        lineCode: applied.lineCode || undefined,
        page: nextPage,
        pageSize: 25,
      })
      setRows(payload.items)
      setPage(payload.page)
      setTotalPages(payload.totalPages)
      setTotalCount(payload.totalCount)
      succeed()
    } catch (error) {
      fail(error)
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load verification history.' })
    }
  }, [applied, onNotice, begin, succeed, fail])

  useEffect(() => {
    void load(1)
  }, [load])

  return (
    <div className="tab-stack">
      <div className="section-heading">
        <div>
          <p className="eyebrow">GET /verification-history</p>
          <h2>Overkill + Leakage history</h2>
        </div>
      </div>
      <form
        className="compact-filter-bar"
        onSubmit={(event) => {
          event.preventDefault()
          setApplied({ category, machineCode, lineCode })
        }}
      >
        <Field label="Category">
          <select value={category} onChange={(event) => setCategory(event.target.value)}>
            <option>All</option>
            <option>Overkill</option>
            <option>Leakage</option>
          </select>
        </Field>
        <Field label="Machine">
          <input value={machineCode} onChange={(event) => setMachineCode(event.target.value)} />
        </Field>
        <Field label="Line">
          <input value={lineCode} onChange={(event) => setLineCode(event.target.value)} />
        </Field>
        <button type="submit" className="ghost-button">
          Filter
        </button>
      </form>
      <DataTable
        columns={['When', 'Category', 'ID', 'Judge', 'Status', 'Machine', 'Line', 'Connection', 'Query', 'Notes']}
        status={status}
        error={loadError}
        onRetry={() => void load(page)}
        rows={rows.map((row) => ({
          id: row.id,
          cells: [
            formatDate(row.createdAtUtc),
            <StatusPill key="c" value={row.category} />,
            row.entityId,
            row.judge,
            <StatusPill key="s" value={row.anomalyStatus} />,
            row.machineCode,
            row.lineCode,
            row.connectionName || '—',
            row.queryName || '—',
            row.eligible ? row.analysisNotes : `Skipped · ${row.analysisNotes}`,
          ],
        }))}
      />
      <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPage={(next) => void load(next)} />
    </div>
  )
}
