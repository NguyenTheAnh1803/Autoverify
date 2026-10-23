import { lazy, Suspense, useEffect, useMemo, useState } from 'react'
import { connectionsApi, opReconciliationApi } from '../api'
import { Button, Field, MetricCard } from '../ui'
import { TabSkeleton } from '../components/TabSkeleton'

const OpReconciliationTab = lazy(() => import('./OpReconciliationTab').then((m) => ({ default: m.OpReconciliationTab })))
import type { DatabaseConnection, Notice, UploadOpExcelResponse } from '../types'

type InputMode = 'clipboard' | 'csv' | 'excel' | 'analysis'

const SAMPLE = `FOG ID\tDefect Name\tJudge
A0E7670S19B134214228\tDent\tNG
A0B265KC29B125613228\tDamage\tNG`

function splitRow(line: string) {
  if (line.includes('\t')) return line.split('\t').map((value) => value.trim())
  if (line.includes(',')) return line.split(',').map((value) => value.trim())
  if (line.includes(';')) return line.split(';').map((value) => value.trim())
  return line.split(/\s{2,}/).map((value) => value.trim())
}

function isHeader(value: string) {
  const normalized = value.trim().toUpperCase()
  return ['FOG ID', 'FOG_ID', 'FOGID', 'KEYID', 'KEY_ID', 'BARCODE', 'SERIAL', 'ID'].includes(normalized)
}

export function OpInputTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [mode, setMode] = useState<InputMode>('analysis')
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [machineCode, setMachineCode] = useState('')
  const [lineCode, setLineCode] = useState('')
  const [rawText, setRawText] = useState(() => sessionStorage.getItem('autoverify.op_input_draft') ?? '')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState(0)
  const [result, setResult] = useState<UploadOpExcelResponse | null>(null)
  const [previewPage, setPreviewPage] = useState(1)
  const [previewPageSize, setPreviewPageSize] = useState(25)

  useEffect(() => {
    void connectionsApi.list({ page: 1, pageSize: 100 }).then((response) => {
      setConnections(response.items)
      const first = response.items[0]
      if (first) {
        setMachineCode((current) => current || first.machineCode)
        setLineCode((current) => current || first.lineCode)
      }
    }).catch((error) => {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Machine/Line from Connections.' })
    })
  }, [onNotice])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (rawText) sessionStorage.setItem('autoverify.op_input_draft', rawText)
      else sessionStorage.removeItem('autoverify.op_input_draft')
    }, 250)
    return () => window.clearTimeout(timer)
  }, [rawText])

  const previewRows = useMemo(() => {
    const rows: { keyId: string; defect: string; judge: string }[] = []
    const lines = rawText.split(/\r?\n/).map((line) => line.trim()).filter(Boolean)
    for (const line of lines) {
      const parts = splitRow(line)
      if (!parts[0] || isHeader(parts[0])) continue
      const numericKey = Number(parts[0])
      if (/^\d+$/.test(parts[0]) && Number.isFinite(numericKey) && numericKey < 10) continue
      rows.push({ keyId: parts[0], defect: parts[1] ?? '', judge: (parts[2] ?? 'NG').toUpperCase() })
    }
    return rows
  }, [rawText])

  const duplicateCount = useMemo(() => {
    const seen = new Set<string>()
    let duplicates = 0
    for (const row of previewRows) {
      if (seen.has(row.keyId.toUpperCase())) duplicates++
      seen.add(row.keyId.toUpperCase())
    }
    return duplicates
  }, [previewRows])

  const machines = useMemo(
    () => [...new Set(connections.map((connection) => connection.machineCode).filter(Boolean))].sort(),
    [connections],
  )
  const lines = useMemo(
    () => [...new Set(connections.filter((connection) => connection.machineCode === machineCode).map((connection) => connection.lineCode).filter(Boolean))].sort(),
    [connections, machineCode],
  )
  const previewTotalPages = Math.max(1, Math.ceil(previewRows.length / previewPageSize))
  const pagedPreviewRows = useMemo(
    () => previewRows.slice((previewPage - 1) * previewPageSize, previewPage * previewPageSize),
    [previewRows, previewPage, previewPageSize],
  )

  useEffect(() => {
    setPreviewPage(1)
  }, [rawText, previewPageSize])

  useEffect(() => {
    if (!lines.includes(lineCode)) setLineCode(lines[0] ?? '')
  }, [lines, lineCode])

  async function readClipboard() {
    try {
      const text = await navigator.clipboard.readText()
      setRawText(text)
    } catch {
      onNotice({ tone: 'info', message: 'Unable to read clipboard automatically. Press Ctrl+V directly into the input area.' })
    }
  }

  async function importText() {
    if (!rawText.trim() || previewRows.length === 0) {
      onNotice({ tone: 'error', message: 'No valid OP rows to import.' })
      return
    }

    setBusy(true)
    setProgress(15)
    setResult(null)
    try {
      setProgress(45)
      const response = await opReconciliationApi.pasteRecords({ text: rawText, machineCode, lineCode })
      setProgress(100)
      setResult(response)
      onNotice({
        tone: response.rejectedCount > 0 && response.totalRecordsSaved === 0 ? 'error' : 'success',
        message: response.message,
      })
      if (response.totalRecordsSaved > 0) {
        setRawText('')
        sessionStorage.removeItem('autoverify.op_input_draft')
      }
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'OP import failed.' })
      setProgress(0)
    } finally {
      setBusy(false)
    }
  }

  async function importFile() {
    if (!file) {
      onNotice({ tone: 'error', message: 'Select a CSV or Excel file before uploading.' })
      return
    }
    setBusy(true)
    setProgress(1)
    setResult(null)
    try {
      const response = await opReconciliationApi.uploadExcelWithProgress(file, machineCode, lineCode, setProgress)
      setResult(response)
      onNotice({
        tone: response.rejectedCount > 0 && response.totalRecordsSaved === 0 ? 'error' : 'success',
        message: response.message,
      })
      setFile(null)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'File upload failed.' })
      setProgress(0)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tab-stack">
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Product-linked Operator Review Intelligence</p>
            <h2>{mode === 'analysis' ? 'OP Reconciliation Dashboard & Deep-Dive' : 'OP Review Import Tools'}</h2>
            <p className="text-xs text-muted" style={{ marginTop: 4 }}>
              {mode === 'analysis'
                ? 'Monitor OP-to-Product reconciliation, classification rates, defect breakdowns, overkill evidence, and mapping quality.'
                : 'Import OP judgments using Clipboard, CSV, or Excel. Re-importing the same FOG ID updates the current Product review without creating duplicates.'}
            </p>
          </div>
          <span className="tag-badge" style={{ background: '#dbeafe', color: '#1d4ed8' }}>
            {mode === 'analysis' ? 'Live Reconciliation Analytics' : 'Auto-resolved Line from Product'}
          </span>
        </div>

        <div className="module-tabs-bar" style={{ marginTop: 14 }}>
          <div className="module-tabs-list">
            <button type="button" className={`module-tab-item ${mode === 'analysis' ? 'active' : ''}`} onClick={() => setMode('analysis')}>Dashboard & Deep-Dive</button>
            <button type="button" className={`module-tab-item ${mode === 'clipboard' ? 'active' : ''}`} onClick={() => setMode('clipboard')}>Import: Clipboard</button>
            <button type="button" className={`module-tab-item ${mode === 'csv' ? 'active' : ''}`} onClick={() => setMode('csv')}>Import: CSV / TSV</button>
            <button type="button" className={`module-tab-item ${mode === 'excel' ? 'active' : ''}`} onClick={() => setMode('excel')}>Import: Excel XLSX</button>
          </div>
          <span className="module-tab-hint">
            {mode === 'analysis' ? 'Default Operations view' : 'Secondary data intake tool'}
          </span>
        </div>

        {mode !== 'analysis' && <div className="compact-filter-bar" style={{ marginTop: 14 }}>
          <Field label="Machine *" required>
            <select value={machineCode} onChange={(event) => setMachineCode(event.target.value)}>
              <option value="">-- Select Machine --</option>
              {machines.map((machine) => <option key={machine} value={machine}>{machine}</option>)}
            </select>
          </Field>
          <Field label="Line *" required>
            <select value={lineCode} onChange={(event) => setLineCode(event.target.value)} disabled={!machineCode}>
              <option value="">-- Select Line --</option>
              {lines.map((line) => <option key={line} value={line}>{line}</option>)}
            </select>
          </Field>
        </div>}
      </section>

      {(mode === 'clipboard' || mode === 'csv') && (
        <section className="panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Fast Text Import</p>
              <h3>{mode === 'clipboard' ? 'Paste Clipboard Data' : 'Paste CSV / TSV Content'}</h3>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {mode === 'clipboard' && <button type="button" className="secondary" onClick={() => void readClipboard()}>Read Clipboard</button>}
              <button type="button" className="ghost-button" onClick={() => setRawText(SAMPLE)}>Load Sample</button>
              <button type="button" className="ghost-button" onClick={() => setRawText('')}>Clear</button>
            </div>
          </div>

          <textarea
            rows={12}
            value={rawText}
            onChange={(event) => setRawText(event.target.value)}
            placeholder={'FOG ID\tDefect Name\tJudge\nA0E7670...\tDent\tNG'}
            style={{ width: '100%', fontFamily: 'monospace', fontSize: 12, whiteSpace: 'pre' }}
          />

          <div className="metrics-grid" style={{ marginTop: 14 }}>
            <MetricCard label="Valid Rows" value={previewRows.length} hint="Parsed locally" />
            <MetricCard label="Unique FOG IDs" value={new Set(previewRows.map((row) => row.keyId.toUpperCase())).size} hint="Last duplicate wins" />
            <MetricCard label="Duplicates" value={duplicateCount} hint="Will update once" tone={duplicateCount > 0 ? 'warning' : 'success'} />
            <MetricCard label="NG Reviews" value={previewRows.filter((row) => row.judge !== 'OK').length} hint="Compared with Product" tone="info" />
          </div>

          {previewRows.length > 0 && (
            <div style={{ marginTop: 12, minWidth: 0 }}>
              <div className="table-responsive" style={{ maxHeight: 440, width: '100%', overflow: 'auto' }}>
                <table className="analytics-data-table" style={{ tableLayout: 'fixed', width: '100%', minWidth: 680 }}>
                  <colgroup><col style={{ width: 56 }} /><col style={{ width: 250 }} /><col /><col style={{ width: 110 }} /></colgroup>
                  <thead><tr><th>#</th><th>FOG ID</th><th>OP Defect</th><th>OP Judge</th></tr></thead>
                  <tbody>{pagedPreviewRows.map((row, index) => (
                    <tr key={`${row.keyId}-${(previewPage - 1) * previewPageSize + index}`}>
                      <td>{(previewPage - 1) * previewPageSize + index + 1}</td>
                      <td className="font-mono" title={row.keyId} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{row.keyId}</td>
                      <td title={row.defect} style={{ overflowWrap: 'anywhere', wordBreak: 'break-word' }}>{row.defect || ''}</td>
                      <td>{row.judge}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap', marginTop: 10 }}>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <span className="text-xs text-muted">{previewRows.length.toLocaleString()} rows · Page {previewPage}/{previewTotalPages}</span>
                  <select value={previewPageSize} onChange={(event) => setPreviewPageSize(Number(event.target.value))} style={{ width: 90 }}>
                    <option value={25}>25</option><option value={50}>50</option><option value={100}>100</option>
                  </select>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <button type="button" className="ghost-button compact" disabled={previewPage <= 1} onClick={() => setPreviewPage((page) => Math.max(1, page - 1))}>Previous</button>
                  <button type="button" className="ghost-button compact" disabled={previewPage >= previewTotalPages} onClick={() => setPreviewPage((page) => Math.min(previewTotalPages, page + 1))}>Next</button>
                </div>
              </div>
            </div>
          )}

          <div className="actions" style={{ marginTop: 14 }}>
            <Button loading={busy} disabled={previewRows.length === 0 || !machineCode || !lineCode} onClick={() => void importText()}>
              {busy ? 'Synchronizing Product Reviews...' : `Import ${previewRows.length} OP Reviews`}
            </Button>
          </div>
        </section>
      )}

      {mode === 'excel' && (
        <section className="panel">
          <div className="section-heading">
            <div><p className="eyebrow">Streaming File Upload</p><h3>Upload Excel / CSV Review Sheet</h3></div>
          </div>
          <label className="file-drop-zone" style={{ display: 'block', padding: 30, textAlign: 'center', cursor: 'pointer' }}>
            <strong>{file ? file.name : 'Select or drop Excel / CSV file'}</strong>
            <div className="text-xs text-muted" style={{ marginTop: 6 }}>.xlsx, .csv, .tsv, .txt</div>
            <input
              type="file"
              accept=".xlsx,.csv,.tsv,.txt"
              style={{ display: 'none' }}
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <div style={{ marginTop: 16 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 5 }}><span>Upload & processing progress</span><strong>{progress}%</strong></div>
            <div style={{ height: 12, borderRadius: 999, overflow: 'hidden', background: 'rgba(148,163,184,.2)' }}>
              <div style={{ height: '100%', width: `${progress}%`, background: 'linear-gradient(90deg, #2563eb, #10b981)', transition: 'width .2s ease' }} />
            </div>
          </div>
          <div className="actions" style={{ marginTop: 16 }}>
            <Button loading={busy} disabled={!file || !machineCode || !lineCode} onClick={() => void importFile()}>{busy ? 'Uploading & Upserting...' : 'Upload and Synchronize'}</Button>
          </div>
        </section>
      )}

      {result && (
        <section className="metrics-grid">
          <MetricCard label="Created" value={result.createdCount} hint="New Product reviews" tone="success" />
          <MetricCard label="Updated" value={result.updatedCount} hint="Existing reviews replaced" tone="info" />
          <MetricCard label="Rejected" value={result.rejectedCount} hint="Product not found" tone={result.rejectedCount > 0 ? 'danger' : 'success'} />
          <MetricCard label="Mapped Defects" value={result.mappedCount} hint="Dictionary matches" />
        </section>
      )}

      {mode === 'analysis' && (
        <Suspense fallback={<TabSkeleton />}>
          <OpReconciliationTab
            onNotice={onNotice}
            initialMachine={machineCode || 'All'}
            initialLine={lineCode || 'All'}
            mode="analysis"
          />
        </Suspense>
      )}
    </div>
  )
}
