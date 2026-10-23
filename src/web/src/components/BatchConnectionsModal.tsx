import React, { useMemo, useState } from 'react'
import { connectionsApi } from '../api'
import { Field, Icon, Modal } from '../ui'
import type {
  BatchProvisionLineItem,
  BatchProvisionRequest,
  BatchProvisionResponse,
  Notice,
} from '../types'

export type BatchConnectionsModalProps = {
  open: boolean
  onClose: () => void
  onNotice: (notice: Notice) => void
  onCompleted: () => void
}

function normalizeLineToken(token: string): string {
  const clean = token.trim().replace(/[\s_]+/g, '-')
  const match = clean.match(/^LINE-(\d+)$/i)
  if (match) {
    return `LINE-${match[1].padStart(2, '0')}`
  }
  const numOnly = clean.match(/^\d+$/)
  if (numOnly) {
    return `LINE-${clean.padStart(2, '0')}`
  }
  return clean.toUpperCase()
}

function extractLine(leaf: string, segs: string[]): string {
  const m = leaf.match(/\b(LINE[_\-\s]?\d+|L\d+)\b/i)
  if (m) return normalizeLineToken(m[0])
  for (let i = segs.length - 1; i >= 0; i--) {
    const sm = segs[i].match(/\b(LINE[_\-\s]?\d+|L\d+)\b/i)
    if (sm) return normalizeLineToken(sm[0])
  }
  return normalizeLineToken(leaf)
}

function extractMachine(leaf: string, segs: string[], fallbackLine: string): string {
  const m = leaf.match(/\b(AOI[_\-\s]?\d+|M[_\-\s]?\d+)\b/i)
  if (m) return m[0].toUpperCase().replace(/[\s_]+/g, '-')
  for (let i = segs.length - 1; i >= 0; i--) {
    const sm = segs[i].match(/\b(AOI[_\-\s]?\d+|M[_\-\s]?\d+)\b/i)
    if (sm) return sm[0].toUpperCase().replace(/[\s_]+/g, '-')
  }
  return `AOI-${fallbackLine}`
}

function parseClientPath(raw: string, defaultHost = '127.0.0.1', defaultDb = 'ProductDB'): {
  raw: string
  lineCode: string
  machineCode: string
  modelCode: string
  host: string
  databaseName: string
} {
  const trimmed = raw.trim()
  if (trimmed.includes(',')) {
    const parts = trimmed.split(',').map((s) => s.trim())
    const l = parts[0] ? normalizeLineToken(parts[0]) : 'LINE-01'
    const m = parts[1] || `AOI-${l}`
    const looksLikeHost = (value: string) => /^\d{1,3}(\.\d{1,3}){3}$/.test(value) || value.includes('.') || value.includes(':') || value.toLowerCase() === 'localhost'
    const hasModel = Boolean(parts[2]) && !looksLikeHost(parts[2])
    const model = (hasModel ? parts[2] : 'UNASSIGNED').toUpperCase()
    const offset = hasModel ? 1 : 0
    const h = parts[2 + offset] || defaultHost
    const d = parts[3 + offset] || defaultDb
    return { raw, lineCode: l, machineCode: m, modelCode: model, host: h, databaseName: d }
  }

  let host = defaultHost
  let db = defaultDb
  let line = ''
  let machine = ''
  let model = 'UNASSIGNED'

  if (trimmed.startsWith('\\\\') || trimmed.startsWith('//')) {
    const normalized = trimmed.replace(/\//g, '\\').replace(/^\\\\+/, '')
    const segs = normalized.split('\\').filter(Boolean)
    if (segs.length > 0) host = segs[0]
    const leaf = segs.length > 1 ? segs[segs.length - 1] : segs[0]
    line = extractLine(leaf, segs)
    machine = extractMachine(leaf, segs, line)
    if (segs.length > 1) db = segs[1]
  } else if (/^[a-zA-Z]:[\\/]/.test(trimmed)) {
    const segs = trimmed.replace(/\//g, '\\').split('\\').filter(Boolean)
    const leaf = segs.length > 1 ? segs[segs.length - 1] : segs[0]
    line = extractLine(leaf, segs)
    machine = extractMachine(leaf, segs, line)
  } else if (trimmed.includes('/') || trimmed.includes('\\')) {
    const segs = trimmed.replace(/\\/g, '/').split('/').filter(Boolean)
    if (segs[0].includes('.') || segs[0].includes(':')) {
      host = segs[0]
      const leaf = segs.length > 1 ? segs[segs.length - 1] : segs[0]
      line = extractLine(leaf, segs)
      machine = extractMachine(leaf, segs, line)
      if (segs.length > 1) db = segs[1]
    } else {
      const leaf = segs[segs.length - 1]
      line = extractLine(leaf, segs)
      machine = extractMachine(leaf, segs, line)
    }
  } else {
    if (/^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(trimmed)) {
      host = trimmed
      const lastOctet = trimmed.split('.').pop() || '1'
      line = `LINE-${lastOctet.padStart(2, '0')}`
      machine = `AOI-${line}`
    } else {
      line = normalizeLineToken(trimmed)
      machine = `AOI-${line}`
    }
  }

  if (db.includes('{line}')) db = db.replace(/\{line\}/g, line)
  if (db.includes('{n}')) {
    const m = line.match(/\d+/)
    db = db.replace(/\{n\}/g, m ? m[0] : '1')
  }

  const modelMatch = trimmed.match(/(?:^|[^A-Z0-9])([A-Z]\d{2,5})(?:[^A-Z0-9]|$)/i)
  if (modelMatch) model = modelMatch[1].toUpperCase()

  return { raw, lineCode: line, machineCode: machine, modelCode: model, host, databaseName: db }
}

export const BatchConnectionsModal: React.FC<BatchConnectionsModalProps> = ({
  open,
  onClose,
  onNotice,
  onCompleted,
}) => {
  // Multi-Path text
  const [multiPathText, setMultiPathText] = useState(
    `\\\\10.20.1.1\\AOI_Export\\LINE-01
\\\\10.20.1.2\\AOI_Export\\LINE-02
\\\\10.20.1.3\\AOI_Export\\LINE-03`
  )

  // Default DB credentials template
  const [engine, setEngine] = useState('MariaDB')
  const [defaultHost, setDefaultHost] = useState('127.0.0.1')
  const [port, setPort] = useState(3306)
  const [defaultDatabase, setDefaultDatabase] = useState('aoi_{line}')
  const [username, setUsername] = useState('root')
  const [password, setPassword] = useState('')

  // Submitting state
  const [submitting, setSubmitting] = useState(false)
  const [resultSummary, setResultSummary] = useState<BatchProvisionResponse | null>(null)

  // Real-time parsed paths
  const parsedMultiPaths = useMemo(() => {
    if (!multiPathText.trim()) return []
    const lines = multiPathText.split('\n').map((s) => s.trim()).filter(Boolean)
    return lines.map((l) => parseClientPath(l, defaultHost, defaultDatabase))
  }, [multiPathText, defaultHost, defaultDatabase])

  async function handleExecute() {
    if (parsedMultiPaths.length === 0) {
      onNotice({ tone: 'error', message: 'Please enter at least one line or path.' })
      return
    }

    setSubmitting(true)
    setResultSummary(null)
    try {
      const lines: BatchProvisionLineItem[] = parsedMultiPaths.map((p) => ({
        lineCode: p.lineCode,
        machineCode: p.machineCode,
        modelCode: p.modelCode,
        host: p.host,
        databaseName: p.databaseName,
        username: username || null,
        password: password || null,
      }))

      const req: BatchProvisionRequest = {
        engine,
        host: defaultHost,
        port,
        databaseName: defaultDatabase,
        username: username || null,
        password: password || null,
        lines,
        createPipelines: false, // PURELY Database Connections! No pipeline creation here.
      }

      const res = await connectionsApi.batchProvision(req)
      setResultSummary(res)
      onNotice({
        tone: 'success',
        message: `✓ Successfully added/updated ${res.createdConnectionsCount + res.updatedConnectionsCount} Database Connections!`,
      })
      onCompleted()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Batch connection creation failed.' })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title="Batch Add Database Connections (Multi-Path Paste)"
      description="Quickly register database connections for 80+ production lines/machines in one click. These connections serve as the foundation for Overkill Analysis, Leakage Verification, and Query Profiles."
      open={open}
      onClose={onClose}
      wide
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        {/* Detailed Instructions Callout */}
        <div
          style={{
            background: 'rgba(59, 130, 246, 0.08)',
            border: '1px solid rgba(59, 130, 246, 0.25)',
            borderRadius: '6px',
            padding: '12px 14px',
            fontSize: '12px',
            color: '#1d4ed8',
            lineHeight: 1.5,
          }}
        >
          <strong><Icon name="info" className="icon inline-icon" />How Multi-Path Connection Detection Works:</strong>
          <p style={{ margin: '4px 0 6px 0', color: '#475569' }}>
            Paste paths from network shares, local directories, IP addresses, or comma-separated lists (1 line per row). The system automatically parses the Host, Line Code, and Machine Code.
          </p>
          <ul style={{ margin: '0 0 0 16px', padding: 0 }}>
            <li><strong>UNC Network Share:</strong> <code>\\10.20.1.5\AOI_Export\LINE-01</code> → Host: <code>10.20.1.5</code>, Line: <code>LINE-01</code>, Machine: <code>AOI-LINE-01</code></li>
            <li><strong>Local Drive Directory:</strong> <code>D:\AOI_Data\LINE-02\AOI-M2</code> → Line: <code>LINE-02</code>, Machine: <code>AOI-M2</code></li>
            <li><strong>IP Address:</strong> <code>10.20.1.30</code> → Host: <code>10.20.1.30</code>, Line: <code>LINE-30</code></li>
            <li><strong>Custom Format:</strong> <code>LINE-04, AOI-04, D111, 10.20.1.4, aoi_line_4</code></li>
          </ul>
        </div>

        {/* Multi-Path Input Textarea */}
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
            <label className="text-xs font-bold" style={{ color: '#475569' }}>
              Paste Connection Paths (1 line or path per row):
            </label>
            <span className="tag-badge" style={{ background: '#2563eb', color: '#fff', fontWeight: 700, fontSize: '12px' }}>
              {parsedMultiPaths.length} Connections Detected
            </span>
          </div>

          <textarea
            value={multiPathText}
            onChange={(e) => setMultiPathText(e.target.value)}
            rows={7}
            placeholder={`\\\\10.20.1.1\\AOI_Export\\LINE-01\n\\\\10.20.1.2\\AOI_Export\\LINE-02\n...`}
            style={{
              fontFamily: 'monospace',
              fontSize: '12px',
              width: '100%',
              background: '#090d16',
              color: '#e2e8f0',
              padding: '10px',
              borderRadius: '6px',
              border: '1px solid rgba(255,255,255,0.12)',
              whiteSpace: 'pre',
            }}
          />
        </div>

        {/* Real-time Live Preview Table */}
        {parsedMultiPaths.length > 0 && (
          <div style={{ border: '1px solid rgba(255,255,255,0.08)', borderRadius: '6px', overflow: 'hidden' }}>
            <div style={{ background: 'rgba(255,255,255,0.04)', padding: '6px 12px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span className="text-xs font-bold" style={{ color: '#0369a1' }}>
                Live Preview: {parsedMultiPaths.length} Database Connections to be Created / Updated
              </span>
              <span className="text-xs text-muted">Ready to register</span>
            </div>
            <div style={{ maxHeight: '160px', overflowY: 'auto' }}>
              <table className="analytics-data-table" role="table" style={{ margin: 0 }}>
                <thead>
                  <tr>
                    <th style={{ width: '40px' }}>#</th>
                    <th>Line Code</th>
                    <th>Machine Code</th>
                    <th>Model</th>
                    <th>Host / IP</th>
                    <th>Database Name</th>
                    <th>Port</th>
                    <th>Engine</th>
                  </tr>
                </thead>
                <tbody>
                  {parsedMultiPaths.slice(0, 20).map((p, idx) => (
                    <tr key={idx}>
                      <td className="font-mono text-muted text-xs">{idx + 1}</td>
                      <td><strong style={{ color: '#0369a1' }}>{p.lineCode}</strong></td>
                      <td className="font-mono text-xs">{p.machineCode}</td>
                      <td><strong style={{ color: '#b45309' }}>{p.modelCode}</strong></td>
                      <td className="font-mono text-xs text-muted">{p.host}</td>
                      <td className="font-mono text-xs text-muted">{p.databaseName}</td>
                      <td className="font-mono text-xs text-muted">{port}</td>
                      <td className="text-xs text-muted">{engine}</td>
                    </tr>
                  ))}
                  {parsedMultiPaths.length > 20 && (
                    <tr>
                      <td colSpan={8} className="text-center text-muted text-xs" style={{ padding: '6px' }}>
                        ... and {parsedMultiPaths.length - 20} more connections will be created.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Default Database Settings Strip */}
        <div>
          <span className="text-xs font-bold" style={{ color: '#475569', display: 'block', marginBottom: '6px' }}>
            DEFAULT CREDENTIALS & CONNECTION TEMPLATE:
          </span>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: '10px' }}>
            <Field label="Engine">
              <input value={engine} onChange={(e) => setEngine(e.target.value)} placeholder="MariaDB" />
            </Field>
            <Field label="Default Host">
              <input value={defaultHost} onChange={(e) => setDefaultHost(e.target.value)} placeholder="127.0.0.1" />
            </Field>
            <Field label="Port">
              <input
                type="number"
                value={port}
                onChange={(e) => setPort(parseInt(e.target.value, 10) || 3306)}
              />
            </Field>
            <Field label="DB Pattern">
              <input value={defaultDatabase} onChange={(e) => setDefaultDatabase(e.target.value)} placeholder="aoi_{line}" />
            </Field>
            <Field label="Username">
              <input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="root" />
            </Field>
            <Field label="Password">
              <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="••••••••" />
            </Field>
          </div>
        </div>

        {/* Results Banner */}
        {resultSummary && (
          <div
            style={{
              padding: '12px',
              borderRadius: '6px',
              background: 'rgba(16, 185, 129, 0.15)',
              border: '1px solid #10b981',
              color: '#10b981',
              fontSize: '13px',
            }}
          >
            <strong>{resultSummary.message}</strong>
          </div>
        )}

        {/* Bottom Actions */}
        <div className="actions" style={{ justifyContent: 'flex-end', gap: '8px', borderTop: '1px solid rgba(255,255,255,0.08)', paddingTop: '12px' }}>
          <button type="button" className="ghost-button" onClick={onClose}>
            {resultSummary ? 'Close' : 'Cancel'}
          </button>
          <button
            type="button"
            className="primary"
            style={{ padding: '8px 24px' }}
            onClick={() => void handleExecute()}
            disabled={submitting}
          >
            {submitting
              ? 'Saving Connections...'
              : `Add/Update ${parsedMultiPaths.length} Database Connections`}
          </button>
        </div>
      </div>
    </Modal>
  )
}
