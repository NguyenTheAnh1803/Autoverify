import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { aiVerificationApi } from '../api'
import { formatDateTime } from '../format'
import { DataTable, Field, MetricCard, StatusPill } from '../ui'
import type { AiMapping, AiMappingInput, AiVerificationStatus, MasterConnectionStatus, Notice } from '../types'

const EMPTY_FORM: AiMappingInput = {
  machineCode: '',
  lineCode: '',
  modelCode: 'UNASSIGNED',
  stationId: '',
  productId: '',
  enabled: true,
  autoSubmit: false,
}

const MASTER_ID = /^[A-Za-z0-9._-]+$/

function validateIds(form: AiMappingInput): string | null {
  if (!form.machineCode.trim() || !form.lineCode.trim() || !form.modelCode.trim() || !form.stationId.trim() || !form.productId.trim())
    return 'Every field is required.'
  const station = form.stationId.trim()
  const product = form.productId.trim()
  if (!MASTER_ID.test(station) || station.length > 64)
    return 'Station ID must be 1-64 characters: letters, digits, "-", "_" or ".".'
  if (!MASTER_ID.test(product) || product.length > 128)
    return 'Product ID must be 1-128 characters: letters, digits, "-", "_" or ".".'
  return null
}

export function AiVerificationTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [status, setStatus] = useState<AiVerificationStatus | null>(null)
  const [mappings, setMappings] = useState<AiMapping[]>([])
  const [connection, setConnection] = useState<MasterConnectionStatus | null>(null)
  const [testing, setTesting] = useState(false)
  const [form, setForm] = useState<AiMappingInput>(EMPTY_FORM)
  const [editingId, setEditingId] = useState('')
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const load = useCallback(async () => {
    try {
      const [nextStatus, nextMappings] = await Promise.all([aiVerificationApi.status(), aiVerificationApi.mappings()])
      setStatus(nextStatus)
      setMappings(nextMappings)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load AI verification settings.' })
    }
  }, [onNotice])

  useEffect(() => {
    void load()
    const timer = window.setInterval(() => void load(), 15000)
    return () => window.clearInterval(timer)
  }, [load])

  async function testConnection() {
    setTesting(true)
    try {
      setConnection(await aiVerificationApi.testConnection())
    } catch (error) {
      setConnection({ reachable: false, authorized: false, message: error instanceof Error ? error.message : 'Test failed.' })
    } finally {
      setTesting(false)
    }
  }

  async function saveMapping(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const problem = validateIds(form)
    setFormError(problem ?? '')
    if (problem) return
    setSaving(true)
    try {
      await aiVerificationApi.saveMapping({
        ...form,
        machineCode: form.machineCode.trim(),
        lineCode: form.lineCode.trim(),
        modelCode: form.modelCode.trim(),
        stationId: form.stationId.trim(),
        productId: form.productId.trim(),
      })
      onNotice({ tone: 'success', message: 'Station mapping saved.' })
      setForm(EMPTY_FORM)
      setEditingId('')
      await load()
    } catch (error) {
      setFormError(error instanceof Error ? error.message : 'Failed to save mapping.')
    } finally {
      setSaving(false)
    }
  }

  async function removeMapping(mapping: AiMapping) {
    if (!window.confirm(`Delete the mapping for ${mapping.machineCode} / ${mapping.lineCode} / ${mapping.modelCode}?`)) return
    try {
      await aiVerificationApi.deleteMapping(mapping.id)
      if (editingId === mapping.id) {
        setForm(EMPTY_FORM)
        setEditingId('')
      }
      await load()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to delete mapping.' })
    }
  }

  function editMapping(mapping: AiMapping) {
    setEditingId(mapping.id)
    setFormError('')
    setForm({
      machineCode: mapping.machineCode,
      lineCode: mapping.lineCode,
      modelCode: mapping.modelCode,
      stationId: mapping.stationId,
      productId: mapping.productId,
      enabled: mapping.enabled,
      autoSubmit: mapping.autoSubmit,
    })
  }

  const paused = status?.dispatch.pausedUntilUtc && new Date(status.dispatch.pausedUntilUtc) > new Date()
  const setField = <K extends keyof AiMappingInput>(key: K, value: AiMappingInput[K]) => setForm((prev) => ({ ...prev, [key]: value }))

  return (
    <div className="tab-stack">
      <section className="metrics-grid">
        <MetricCard
          label="Master Server"
          value={status ? (status.configured ? 'Configured' : 'Not configured') : '…'}
          tone={status?.configured ? 'success' : 'warning'}
          hint="MasterServer:BaseUrl in appsettings"
        />
        <MetricCard
          label="Verdict polling"
          value={status ? (status.enabled ? `Every ${status.pollIntervalSeconds}s` : 'Off') : '…'}
          tone={status?.enabled ? 'success' : 'warning'}
          hint="AiVerification:Enabled"
        />
        <MetricCard
          label="Auto-submit"
          value={status ? (paused ? 'Paused' : status.autoSubmit ? 'On' : 'Off') : '…'}
          tone={paused ? 'danger' : status?.autoSubmit ? 'success' : 'neutral'}
          hint={status ? `Up to ${status.maxProductsPerTick} products per tick, last ${status.lookbackHours}h` : undefined}
        />
        <MetricCard
          label="Verdicts (24h)"
          value={status ? `${status.last24h.done} done` : '…'}
          tone="info"
          hint={status ? `${status.last24h.submitted} pending · ${status.last24h.unavailable} unavailable · ${status.last24h.rejected} rejected` : undefined}
        />
      </section>

      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">POST /master/test-connection</p>
            <h2>Connection to Master Server</h2>
          </div>
          <button type="button" onClick={() => void testConnection()} disabled={testing}>
            {testing ? 'Testing…' : 'Test connection'}
          </button>
        </div>
        {connection ? (
          <p>
            <StatusPill value={connection.reachable && connection.authorized ? 'Healthy' : 'Unhealthy'} />{' '}
            <span>{connection.message}</span>
          </p>
        ) : (
          <p className="muted">Checks that the Master accepts this host, its API key, and both the verify.read and verify.submit scopes. Nothing is created on the Master.</p>
        )}
        {status?.dispatch.lastRunAtUtc ? (
          <p className="muted">
            Auto-submit last ran {formatDateTime(status.dispatch.lastRunAtUtc)}: picked {status.dispatch.lastPicked}, sent {status.dispatch.lastSubmitted} image(s)
            {status.dispatch.lastSubmitAtUtc ? ` · last sent ${formatDateTime(status.dispatch.lastSubmitAtUtc)}` : ''}.
          </p>
        ) : null}
        {paused ? (
          <p className="notice error">
            Sending is paused until {formatDateTime(status?.dispatch.pausedUntilUtc)}. {status?.dispatch.lastError}
          </p>
        ) : status?.dispatch.lastError ? (
          <p className="muted">Last error: {status.dispatch.lastError}</p>
        ) : null}
      </section>

      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">PUT /ai-verification/mappings</p>
            <h2>{editingId ? 'Edit station mapping' : 'Add station mapping'}</h2>
          </div>
          {editingId ? (
            <button type="button" className="ghost-button" onClick={() => { setForm(EMPTY_FORM); setEditingId(''); setFormError('') }}>
              Cancel edit
            </button>
          ) : null}
        </div>
        <p className="muted">
          Maps a machine / line / model to the Master station and product that hold its AI models. A defect can only be sent when a matching, enabled mapping exists.
        </p>
        <form className="modal-form" onSubmit={(event) => void saveMapping(event)}>
          <div className="query-form-grid">
            <Field label="Machine code" required>
              <input value={form.machineCode} onChange={(e) => setField('machineCode', e.target.value)} disabled={!!editingId} />
            </Field>
            <Field label="Line code" required>
              <input value={form.lineCode} onChange={(e) => setField('lineCode', e.target.value)} disabled={!!editingId} />
            </Field>
            <Field label="Model code" required>
              <input value={form.modelCode} onChange={(e) => setField('modelCode', e.target.value)} disabled={!!editingId} />
            </Field>
            <Field label="Master station ID" required hint="Station with an Edge Predict node, e.g. L01">
              <input value={form.stationId} onChange={(e) => setField('stationId', e.target.value)} />
            </Field>
            <Field label="Master product ID" required hint="Product the models were trained for">
              <input value={form.productId} onChange={(e) => setField('productId', e.target.value)} />
            </Field>
            <Field label="Enabled">
              <select value={String(form.enabled)} onChange={(e) => setField('enabled', e.target.value === 'true')}>
                <option value="true">Yes</option>
                <option value="false">No</option>
              </select>
            </Field>
            <Field label="Auto-submit" hint="Send new products of this line automatically (also needs AiVerification:AutoSubmit)">
              <select value={String(form.autoSubmit)} onChange={(e) => setField('autoSubmit', e.target.value === 'true')}>
                <option value="false">Off</option>
                <option value="true">On</option>
              </select>
            </Field>
          </div>
          {formError ? <p className="notice error" role="alert">{formError}</p> : null}
          <div className="actions">
            <button type="submit" disabled={saving}>{saving ? 'Saving…' : editingId ? 'Update mapping' : 'Save mapping'}</button>
          </div>
        </form>
      </section>

      <DataTable
        eyebrow="GET /ai-verification/mappings"
        title="Station mappings"
        columns={['Machine', 'Line', 'Model', 'Station', 'Product', 'Enabled', 'Auto-submit', 'Updated', '']}
        empty="No mappings yet. Add one above before any image can be sent to the AI."
        rows={mappings.map((m) => ({
          id: m.id,
          selected: m.id === editingId,
          cells: [
            m.machineCode,
            m.lineCode,
            m.modelCode,
            <code key="s">{m.stationId}</code>,
            <code key="p">{m.productId}</code>,
            <span key="e" className={`health-pill ${m.enabled ? 'healthy' : 'unknown'}`}>{m.enabled ? 'Enabled' : 'Disabled'}</span>,
            <span key="a" className={`health-pill ${m.autoSubmit ? 'healthy' : 'unknown'}`}>{m.autoSubmit ? 'On' : 'Off'}</span>,
            formatDateTime(m.updatedAtUtc),
            <div key="act" className="actions">
              <button type="button" className="secondary compact-button" onClick={() => editMapping(m)}>Edit</button>
              <button type="button" className="ghost-button compact" onClick={() => void removeMapping(m)}>Delete</button>
            </div>,
          ],
        }))}
      />
    </div>
  )
}
