import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5057/api/v1'

type UserProfile = {
  username: string
  displayName: string
  roles: string[]
}

type DashboardSummary = {
  totalScans: number
  totalProcessedItems: number
  passRate: number
  verificationFailureRate: number
  technicalErrorRate: number
  anomalyRate: number
  healthySources: number
  totalSources: number
  lastScanAtUtc: string | null
}

type TrendBucket = {
  date: string
  processedItems: number
  passRate: number
  verificationFailureRate: number
  technicalErrorRate: number
  anomalyRate: number
}

type ImportPreview = {
  acceptedCount: number
  rejectedCount: number
  duplicateCount: number
  checksum: string
  acceptedRows: { rowNumber: number; entityId: string }[]
  rejectedRows: { rowNumber: number; rawValue: string; reason: string }[]
}

type ScanList = {
  id: string
  name: string
  mappingName: string
  acceptedCount: number
  rejectedCount: number
  checksum: string
  createdAtUtc: string
  createdBy: string
}

type DataSource = {
  id: string
  name: string
  type: string
  environment: string
  health: string
  averageLatencyMs: number
  enabled: boolean
}

type ScanExecution = {
  id: string
  name: string
  scanListName: string
  status: string
  sourceNames: string[]
  totalItems: number
  passedItems: number
  warningItems: number
  failedItems: number
  technicalErrorItems: number
  anomalyItems: number
  startedAtUtc: string
  completedAtUtc: string
}

type VerificationResult = {
  id: string
  entityId: string
  sourceName: string
  status: string
  severity: string
  ruleCode: string
  message: string
  anomalyScore: number
  completedAtUtc: string
}

function App() {
  const [token, setToken] = useState(() => localStorage.getItem('autoverify.token') ?? '')
  const [user, setUser] = useState<UserProfile | null>(() => token ? { username: 'admin', displayName: 'Administrator', roles: ['Administrator'] } : null)
  const [loginError, setLoginError] = useState('')

  useEffect(() => {
    if (token) {
      localStorage.setItem('autoverify.token', token)
    } else {
      localStorage.removeItem('autoverify.token')
    }
  }, [token])

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoginError('')
    const form = new FormData(event.currentTarget)
    const response = await fetch(`${API_BASE_URL}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: form.get('username'), password: form.get('password') }),
    })

    if (!response.ok) {
      setLoginError('Invalid username or password.')
      return
    }

    const data = (await response.json()) as { accessToken: string; user: UserProfile }
    setToken(data.accessToken)
    setUser(data.user)
  }

  if (!token || !user) {
    return <LoginScreen loginError={loginError} onLogin={login} />
  }

  return <Workspace user={user} onLogout={() => {
    setUser(null)
    setToken('')
  }} />
}

function LoginScreen({ loginError, onLogin }: { loginError: string; onLogin: (event: FormEvent<HTMLFormElement>) => Promise<void> }) {
  return (
    <main className="login-page">
      <section className="login-card">
        <p className="eyebrow">AutoVerify System</p>
        <h1>Operational verification console</h1>
        <p className="muted">Sign in to import ID lists, run verification scans, and investigate anomalies.</p>
        <form onSubmit={onLogin} className="login-form">
          <label>
            Username
            <input name="username" defaultValue="admin" autoComplete="username" />
          </label>
          <label>
            Password
            <input name="password" defaultValue="admin!" type="password" autoComplete="current-password" />
          </label>
          {loginError && <div className="form-error">{loginError}</div>}
          <button type="submit">Log in</button>
        </form>
        <p className="security-note">Temporary MVP credentials: admin / admin!. Replace this with managed secrets before production.</p>
      </section>
    </main>
  )
}

function Workspace({ user, onLogout }: { user: UserProfile; onLogout: () => void }) {
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [trends, setTrends] = useState<TrendBucket[]>([])
  const [scanLists, setScanLists] = useState<ScanList[]>([])
  const [sources, setSources] = useState<DataSource[]>([])
  const [executions, setExecutions] = useState<ScanExecution[]>([])
  const [results, setResults] = useState<VerificationResult[]>([])
  const [rawText, setRawText] = useState('CUS-1001\nCUS-1002\nCUS-1002\nCUS-1003\n   ')
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [scanName, setScanName] = useState('Manual verification scan')
  const [selectedScanListId, setSelectedScanListId] = useState('')
  const [notice, setNotice] = useState('')

  async function loadAll() {
    const [summaryResponse, trendsResponse, scanListsResponse, sourcesResponse, executionsResponse] = await Promise.all([
      fetch(`${API_BASE_URL}/dashboard/summary`),
      fetch(`${API_BASE_URL}/dashboard/trends`),
      fetch(`${API_BASE_URL}/scan-lists`),
      fetch(`${API_BASE_URL}/data-sources`),
      fetch(`${API_BASE_URL}/scan-executions`),
    ])

    const nextScanLists = (await scanListsResponse.json()) as ScanList[]
    const nextExecutions = (await executionsResponse.json()) as ScanExecution[]
    setSummary((await summaryResponse.json()) as DashboardSummary)
    setTrends((await trendsResponse.json()) as TrendBucket[])
    setScanLists(nextScanLists)
    setSources((await sourcesResponse.json()) as DataSource[])
    setExecutions(nextExecutions)
    setSelectedScanListId((current) => current || nextScanLists[0]?.id || '')

    if (nextExecutions[0]) {
      const resultsResponse = await fetch(`${API_BASE_URL}/scan-executions/${nextExecutions[0].id}/results`)
      setResults((await resultsResponse.json()) as VerificationResult[])
    }
  }

  useEffect(() => {
    void loadAll()
  }, [])

  async function previewImport() {
    setNotice('')
    const response = await fetch(`${API_BASE_URL}/imports/preview`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rawText, mappingName: 'entityId' }),
    })
    setPreview((await response.json()) as ImportPreview)
  }

  async function saveScanList() {
    const name = `Imported IDs ${new Date().toISOString().slice(0, 16)}`
    const response = await fetch(`${API_BASE_URL}/scan-lists`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, mappingName: 'entityId', rawText }),
    })
    const created = (await response.json()) as ScanList
    setNotice(`Created scan list '${created.name}' with ${created.acceptedCount} accepted IDs.`)
    await loadAll()
    setSelectedScanListId(created.id)
  }

  async function startScan() {
    if (!selectedScanListId) {
      setNotice('Create or select a scan list before starting a scan.')
      return
    }

    const enabledSources = sources.filter((source) => source.enabled).slice(0, 3).map((source) => source.id)
    const response = await fetch(`${API_BASE_URL}/scan-executions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ scanListId: selectedScanListId, name: scanName, sourceIds: enabledSources }),
    })
    const created = (await response.json()) as ScanExecution
    setNotice(`Started and completed MVP simulated scan '${created.name}'.`)
    await loadAll()
  }

  return (
    <main className="workspace">
      <header className="topbar">
        <div>
          <p className="eyebrow">AutoVerify MVP</p>
          <h1>Verification and anomaly monitoring</h1>
        </div>
        <div className="user-panel">
          <span>{user.displayName}</span>
          <button type="button" onClick={onLogout}>Log out</button>
        </div>
      </header>

      {notice && <div className="notice">{notice}</div>}

      <section className="grid metrics-grid">
        <MetricCard label="Total scans" value={summary?.totalScans ?? 0} />
        <MetricCard label="Processed IDs" value={summary?.totalProcessedItems ?? 0} />
        <MetricCard label="Pass rate" value={formatPercent(summary?.passRate)} tone="success" />
        <MetricCard label="Failure rate" value={formatPercent(summary?.verificationFailureRate)} tone="danger" />
        <MetricCard label="Technical error" value={formatPercent(summary?.technicalErrorRate)} tone="warning" />
        <MetricCard label="Anomaly rate" value={formatPercent(summary?.anomalyRate)} tone="info" />
      </section>

      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Dashboard</p>
            <h2>Daily verification trend</h2>
          </div>
          <span>{summary?.healthySources ?? 0} / {summary?.totalSources ?? 0} sources healthy</span>
        </div>
        <div className="trend-chart" aria-label="Daily processed ID trend">
          {trends.map((bucket) => (
            <div key={bucket.date} className="trend-column">
              <div className="trend-bar" style={{ height: `${Math.max(12, bucket.processedItems / 4)}px` }} />
              <span>{new Date(bucket.date).getUTCDate()}</span>
            </div>
          ))}
        </div>
      </section>

      <section className="content-grid">
        <article className="panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">ID Import</p>
              <h2>Paste list and mapping</h2>
            </div>
            <span>Mapping: entityId</span>
          </div>
          <textarea value={rawText} onChange={(event) => setRawText(event.target.value)} />
          <div className="actions">
            <button type="button" onClick={previewImport}>Preview import</button>
            <button type="button" className="secondary" onClick={saveScanList}>Create scan list</button>
          </div>
          {preview && (
            <div className="preview-box">
              <strong>{preview.acceptedCount}</strong> accepted, <strong>{preview.rejectedCount}</strong> rejected
              <code>{preview.checksum.slice(0, 18)}...</code>
            </div>
          )}
        </article>

        <article className="panel">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Execution</p>
              <h2>Run verification scan</h2>
            </div>
          </div>
          <label>
            Scan list
            <select value={selectedScanListId} onChange={(event) => setSelectedScanListId(event.target.value)}>
              {scanLists.map((scanList) => <option key={scanList.id} value={scanList.id}>{scanList.name}</option>)}
            </select>
          </label>
          <label>
            Scan name
            <input value={scanName} onChange={(event) => setScanName(event.target.value)} />
          </label>
          <button type="button" onClick={startScan}>Start scan</button>
        </article>
      </section>

      <section className="content-grid">
        <DataTable title="Source registry" eyebrow="Sources" columns={["Name", "Type", "Health", "Latency", "Enabled"]} rows={sources.map((source) => [source.name, source.type, source.health, `${source.averageLatencyMs} ms`, source.enabled ? 'Yes' : 'No'])} />
        <DataTable title="Scan history" eyebrow="History" columns={["Name", "List", "Status", "Processed", "Failed", "Anomalies"]} rows={executions.map((execution) => [execution.name, execution.scanListName, execution.status, execution.totalItems, execution.failedItems, execution.anomalyItems])} />
      </section>

      <DataTable title="Latest verification results" eyebrow="Investigation" columns={["Entity ID", "Source", "Status", "Severity", "Rule", "Score", "Message"]} rows={results.slice(0, 12).map((result) => [result.entityId, result.sourceName, result.status, result.severity, result.ruleCode, result.anomalyScore.toFixed(2), result.message])} />
    </main>
  )
}

function MetricCard({ label, value, tone = 'neutral' }: { label: string; value: string | number; tone?: string }) {
  return (
    <article className={`metric-card ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

function DataTable({ title, eyebrow, columns, rows }: { title: string; eyebrow: string; columns: string[]; rows: (string | number)[][] }) {
  return (
    <section className="panel table-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">{eyebrow}</p>
          <h2>{title}</h2>
        </div>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>{columns.map((column) => <th key={column}>{column}</th>)}</tr>
          </thead>
          <tbody>
            {rows.map((row, index) => <tr key={`${title}-${index}`}>{row.map((cell, cellIndex) => <td key={cellIndex}>{cell}</td>)}</tr>)}
          </tbody>
        </table>
      </div>
    </section>
  )
}

function formatPercent(value?: number) {
  return `${Math.round((value ?? 0) * 1000) / 10}%`
}

export default App
