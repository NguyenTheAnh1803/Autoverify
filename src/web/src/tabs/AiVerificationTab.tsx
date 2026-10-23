import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { aiVerificationApi } from '../api'
import { useConfirm } from '../confirm'
import { formatDateTime, parseServerTime } from '../format'
import { usePolling } from '../lib/usePolling'
import { Button, DataTable, Field, MetricCard, StatusPill, useLoadStatus } from '../ui'
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

type FieldKey = 'machineCode' | 'lineCode' | 'modelCode' | 'stationId' | 'productId'
type FieldErrors = Partial<Record<FieldKey, string>>

/** One message per field, so each is shown under the box that needs fixing. Empty object = valid. */
function validateIds(form: AiMappingInput): FieldErrors {
  const errors: FieldErrors = {}
  const required: [FieldKey, string][] = [
    ['machineCode', 'Enter the machine code.'],
    ['lineCode', 'Enter the line code.'],
    ['modelCode', 'Enter the model code.'],
    ['stationId', 'Enter the master station ID.'],
    ['productId', 'Enter the master product ID.'],
  ]
  for (const [key, message] of required) if (!form[key].trim()) errors[key] = message
  const station = form.stationId.trim()
  const product = form.productId.trim()
  if (station && (!MASTER_ID.test(station) || station.length > 64))
    errors.stationId = 'Use 1-64 characters: letters, digits, "-", "_" or ".".'
  if (product && (!MASTER_ID.test(product) || product.length > 128))
    errors.productId = 'Use 1-128 characters: letters, digits, "-", "_" or ".".'
  return errors
}

export function AiVerificationTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [status, setStatus] = useState<AiVerificationStatus | null>(null)
  const [mappings, setMappings] = useState<AiMapping[]>([])
  const [connection, setConnection] = useState<MasterConnectionStatus | null>(null)
  const [testing, setTesting] = useState(false)
  const [form, setForm] = useState<AiMappingInput>(EMPTY_FORM)
  const [editingId, setEditingId] = useState('')
  const [formError, setFormError] = useState('')
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [saving, setSaving] = useState(false)

  const list = useLoadStatus()
  const { begin, succeed, fail } = list

  const load = useCallback(async () => {
    begin()
    try {
      const [nextStatus, nextMappings] = await Promise.all([aiVerificationApi.status(), aiVerificationApi.mappings()])
      setStatus(nextStatus)
      setMappings(nextMappings)
      succeed()
    } catch (error) {
      fail(error)
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load AI verification settings.' })
    }
  }, [onNotice])

  useEffect(() => {
    void load()
  }, [load])

  usePolling(load, 15000)

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
    const problems = validateIds(form)
    setFieldErrors(problems)
    setFormError('')
    if (Object.keys(problems).length > 0) {
      // Move to the first box that needs fixing.
      const first = (['machineCode', 'lineCode', 'modelCode', 'stationId', 'productId'] as FieldKey[]).find((key) => problems[key])
      if (first) window.requestAnimationFrame(() => document.querySelector<HTMLElement>(`[data-field="${first}"]`)?.focus())
      return
    }
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

  const confirm = useConfirm()

  async function removeMapping(mapping: AiMapping) {
    const ok = await confirm({
      title: `Delete the mapping for ${mapping.machineCode} / ${mapping.lineCode} / ${mapping.modelCode}?`,
      message: 'Images from this station will no longer be sent to the AI.',
      confirmLabel: 'Delete mapping',
      tone: 'danger',
    })
    if (!ok) return
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
    setFieldErrors({})
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

  const pausedUntil = parseServerTime(status?.dispatch.pausedUntilUtc)
  const paused = pausedUntil !== null && pausedUntil > Date.now()
  const setField = <K extends keyof AiMappingInput>(key: K, value: AiMappingInput[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
    // The message goes away as soon as the box is edited.
    setFieldErrors((prev) => (key in prev ? { ...prev, [key]: undefined } : prev))
  }

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
          <Button type="button" onClick={() => void testConnection()} loading={testing}>
            {testing ? 'Testing…' : 'Test connection'}
          </Button>
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
            <button type="button" className="ghost-button" onClick={() => { setForm(EMPTY_FORM); setEditingId(''); setFormError(''); setFieldErrors({}) }}>
              Cancel edit
            </button>
          ) : null}
        </div>
        <p className="muted">
          Maps a machine / line / model to the Master station and product that hold its AI models. A defect can only be sent when a matching, enabled mapping exists.
        </p>
        <form className="modal-form" onSubmit={(event) => void saveMapping(event)}>
          <div className="query-form-grid">
            <Field label="Machine code" required error={fieldErrors.machineCode}>
              <input data-field="machineCode" value={form.machineCode} onChange={(e) => setField('machineCode', e.target.value)} disabled={!!editingId} />
            </Field>
            <Field label="Line code" required error={fieldErrors.lineCode}>
              <input data-field="lineCode" value={form.lineCode} onChange={(e) => setField('lineCode', e.target.value)} disabled={!!editingId} />
            </Field>
            <Field label="Model code" required error={fieldErrors.modelCode}>
              <input data-field="modelCode" value={form.modelCode} onChange={(e) => setField('modelCode', e.target.value)} disabled={!!editingId} />
            </Field>
            <Field label="Master station ID" required hint="Station with an Edge Predict node, e.g. L01" error={fieldErrors.stationId}>
              <input data-field="stationId" value={form.stationId} onChange={(e) => setField('stationId', e.target.value)} />
            </Field>
            <Field label="Master product ID" required hint="Product the models were trained for" error={fieldErrors.productId}>
              <input data-field="productId" value={form.productId} onChange={(e) => setField('productId', e.target.value)} />
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
            <Button type="submit" loading={saving}>{saving ? 'Saving…' : editingId ? 'Update mapping' : 'Save mapping'}</Button>
          </div>
        </form>
      </section>

      <DataTable
        eyebrow="GET /ai-verification/mappings"
        title="Station mappings"
        columns={['Machine', 'Line', 'Model', 'Station', 'Product', 'Enabled', 'Auto-submit', 'Updated', '']}
        empty="No mappings yet. Add one above before any image can be sent to the AI."
        status={list.status}
        error={list.error}
        onRetry={() => void load()}
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
              <Button variant="danger" size="sm" onClick={() => void removeMapping(m)}>Delete</Button>
            </div>,
          ],
        }))}
      />
    </div>
  )
}
