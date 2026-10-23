import { useCallback, useEffect, useState } from 'react'
import { systemApi } from '../api'
import { formatDate, paginate } from '../format'
import { DataTable, Pagination, StatusPill, useLoadStatus } from '../ui'
import type { AuditEvent, Notice, SystemHealth } from '../types'

export function AuditTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [events, setEvents] = useState<AuditEvent[]>([])
  const [health, setHealth] = useState<SystemHealth | null>(null)
  const [page, setPage] = useState(1)
  const paged = paginate(events, page)
  const { status, error, begin, succeed, fail } = useLoadStatus()

  const load = useCallback(async () => {
    begin()
    try {
      const [nextEvents, nextHealth] = await Promise.all([systemApi.audit(), systemApi.health()])
      setEvents(nextEvents)
      setHealth(nextHealth)
      succeed()
    } catch (loadError) {
      fail(loadError)
      onNotice({ tone: 'error', message: loadError instanceof Error ? loadError.message : 'Failed to load audit events.' })
    }
  }, [begin, succeed, fail, onNotice])

  useEffect(() => {
    void load()
  }, [load])

  return (
    <div className="tab-stack">
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">GET /system/health</p>
            <h2>Service health</h2>
          </div>
          {health ? <StatusPill value={health.status} /> : null}
        </div>
        <p className="muted">
          {health?.service} {health?.version} · storage {health?.storageMode} · checked {formatDate(health?.checkedAtUtc)}
        </p>
      </section>
      <DataTable
        eyebrow="GET /audit-events"
        title="Full audit log"
        columns={['When', 'Actor', 'Action', 'Target type', 'Target ID', 'Message']}
        status={status}
        error={error}
        onRetry={() => void load()}
        rows={paged.items.map((event) => ({
          id: event.id,
          cells: [formatDate(event.occurredAtUtc), event.actor, event.action, event.targetType, event.targetId, event.message],
        }))}
      />
      <Pagination page={paged.page} totalPages={paged.totalPages} totalCount={paged.totalCount} onPage={setPage} />
    </div>
  )
}
