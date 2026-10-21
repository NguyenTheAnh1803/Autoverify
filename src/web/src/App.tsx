import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import './App.css'
import { authApi } from './api'
import { AiVerificationTab } from './tabs/AiVerificationTab'
import { AuditTab } from './tabs/AuditTab'
import { ClassifierRulesTab } from './tabs/ClassifierRulesTab'
import { FeatureTrendsTab } from './tabs/FeatureTrendsTab'
import { HistoryTab } from './tabs/HistoryTab'
import { ImportTab } from './tabs/ImportTab'
import { LeakageOpsTab } from './tabs/LeakageOpsTab'
import { OverkillOpsTab } from './tabs/OverkillOpsTab'
import { OverkillTab } from './tabs/OverkillTab'
import { OpInputTab } from './tabs/OpInputTab'
import { OverviewTab } from './tabs/OverviewTab'
import { ProductsTab } from './tabs/ProductsTab'
import { QueriesTab } from './tabs/QueriesTab'
import { ScansTab } from './tabs/ScansTab'
import { SourcesTab } from './tabs/SourcesTab'
import { StorageTab } from './tabs/StorageTab'
import { Icon, NoticeBanner } from './ui'
import type { Notice, ProductHandoff, TabId, UserProfile } from './types'

export type MainSectionId = 'dashboard' | 'operations' | 'products' | 'settings'

export interface NavSection {
  id: MainSectionId
  label: string
  icon: string
  description: string
  subTabs?: { id: TabId; label: string; icon: string; description: string }[]
}

const SECTIONS: NavSection[] = [
  {
    id: 'dashboard',
    label: 'Dashboard',
    icon: 'dashboard',
    description: 'System overview, metrics, and trends',
  },
  {
    id: 'operations',
    label: 'Operations',
    icon: 'operations',
    description: 'ID import, scan execution, and verification history',
    subTabs: [
      { id: 'overkill-ops', label: 'Overkill Operations', icon: 'overkill', description: 'Auto-machine, Station Judge N/NG/NA verification, scan lists' },
      { id: 'op-input', label: 'OP Reconciliation', icon: 'analysis', description: 'Dashboard, reconciliation deep-dive, and OP review import tools' },
      { id: 'leakage-ops', label: 'Leakage Operations', icon: 'leakage', description: 'Line feature mapping, Station Judge G/OK verification, scan lists' },
      { id: 'scans', label: 'Scan Executions', icon: 'scans', description: 'Trigger scans and inspect verification results' },
      { id: 'history', label: 'Verification History', icon: 'history', description: 'Overkill and Leakage history audit' },
    ],
  },
  {
    id: 'products',
    label: 'Products & Analysis',
    icon: 'analysis',
    description: 'Product catalog, defects, and overkill investigation',
    subTabs: [
      { id: 'products', label: 'Products & Defects', icon: 'products', description: 'Product records, multi-table scan, and defect JSON' },
      { id: 'overkill', label: 'Overkill Analysis', icon: 'overkill', description: 'System NG share, suspect candidates, and guidance' },
      { id: 'feature-trends', label: 'Feature Trends', icon: 'analysis', description: 'Feature time-series, drift, SPC, correlation, and predictive analysis' },
    ],
  },
  {
    id: 'settings',
    label: 'Configuration & Data',
    icon: 'settings',
    description: 'Connections, queries, storage, and health audit',
    subTabs: [
      { id: 'classifier-rules', label: 'Classifier Rules & Scripts', icon: 'rules', description: 'AOI script management, batch JSON import, and interactive rule simulator' },
      { id: 'sources', label: 'Connections & Sources', icon: 'sources', description: 'Database connections and data sources' },
      { id: 'ai-verification', label: 'AI Verification', icon: 'overkill', description: 'Master Server connection, station mappings and automatic sending to the AI' },
      { id: 'queries', label: 'Query Profiles', icon: 'queries', description: 'MariaDB queries and SQL parameters' },
      { id: 'storage', label: 'Object Storage', icon: 'storage', description: 'SeaweedFS payloads and defect images' },
      { id: 'audit', label: 'Audit & Health', icon: 'audit', description: 'Service health check and full audit log' },
    ],
  },
]

function App() {
  const [token, setToken] = useState(() => localStorage.getItem('autoverify.token') ?? '')
  const [user, setUser] = useState<UserProfile | null>(null)
  const [loginError, setLoginError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (token) localStorage.setItem('autoverify.token', token)
    else localStorage.removeItem('autoverify.token')
  }, [token])

  useEffect(() => {
    if (!token) {
      setUser(null)
      return
    }
    let cancelled = false
    void authApi.me()
      .then((profile) => {
        if (!cancelled) setUser(profile)
      })
      .catch(() => {
        if (!cancelled) setUser({ username: 'admin', displayName: 'Administrator', roles: ['Administrator'] })
      })
    return () => {
      cancelled = true
    }
  }, [token])

  async function login(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setLoginError('')
    setBusy(true)
    const form = new FormData(event.currentTarget)
    try {
      const data = await authApi.login(String(form.get('username') ?? ''), String(form.get('password') ?? ''))
      setToken(data.accessToken)
      setUser(data.user)
    } catch {
      setLoginError('Invalid username or password.')
    } finally {
      setBusy(false)
    }
  }

  async function logout() {
    try {
      await authApi.logout()
    } catch {
      // Session is cleared locally even if the API call fails.
    }
    setUser(null)
    setToken('')
  }

  if (!token || !user) {
    return <LoginScreen loginError={loginError} busy={busy} onLogin={login} />
  }

  return <Workspace user={user} onLogout={() => void logout()} />
}

function LoginScreen({
  loginError,
  busy,
  onLogin,
}: {
  loginError: string
  busy: boolean
  onLogin: (event: FormEvent<HTMLFormElement>) => Promise<void>
}) {
  return (
    <main className="login-page">
      <section className="login-card">
        <p className="eyebrow">AutoVerify System</p>
        <h1>Operational verification console</h1>
        <p className="muted">Sign in to import ID lists, run verification scans, and investigate anomalies.</p>
        <form onSubmit={(event) => void onLogin(event)} className="login-form">
          <label>
            Username
            <input name="username" defaultValue="admin" autoComplete="username" required />
          </label>
          <label>
            Password
            <input name="password" defaultValue="admin!" type="password" autoComplete="current-password" required />
          </label>
          {loginError ? <div className="form-error">{loginError}</div> : null}
          <button type="submit" disabled={busy}>
            {busy ? 'Signing in…' : 'Log in'}
          </button>
        </form>
        <p className="security-note">Temporary MVP credentials: admin / admin!. Replace before production.</p>
      </section>
    </main>
  )
}

function Workspace({ user, onLogout }: { user: UserProfile; onLogout: () => void }) {
  const [section, setSection] = useState<MainSectionId>('dashboard')
  const [activeSubTab, setActiveSubTab] = useState<Record<MainSectionId, TabId>>({
    dashboard: 'overview',
    operations: 'overkill-ops',
    products: 'products',
    settings: 'classifier-rules',
  })
  const [notice, setNotice] = useState<Notice | null>(null)
  const onNotice = useCallback((next: Notice) => setNotice(next), [])

  function handleAnalyzeDefects(_handoff: ProductHandoff) {
    setSection('products')
    setActiveSubTab((prev) => ({ ...prev, products: 'feature-trends' }))
  }

  useEffect(() => {
    if (!notice) return
    const timer = window.setTimeout(() => setNotice(null), 6000)
    return () => window.clearTimeout(timer)
  }, [notice])

  const currentSection = useMemo(() => SECTIONS.find((item) => item.id === section) ?? SECTIONS[0], [section])
  const currentTabId = section === 'dashboard' ? 'overview' : activeSubTab[section]
  const currentSubTab = useMemo(
    () => currentSection.subTabs?.find((sub) => sub.id === currentTabId) ?? null,
    [currentSection, currentTabId],
  )

  function handleSectionChange(newSection: MainSectionId) {
    setSection(newSection)
  }

  function handleSubTabChange(newTab: TabId) {
    setActiveSubTab((prev) => ({ ...prev, [section]: newTab }))
  }

  return (
    <div className="app-shell-vertical">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>

      {/* Left Vertical Navigation Sidebar */}
      <aside className="sidebar-vertical">
        <div className="brand">
          <span>AV</span>
          <div>
            <strong>AutoVerify</strong>
            <small>VERIFICATION SYSTEM</small>
          </div>
        </div>

        <div className="sidebar-section-title">MAIN NAVIGATION</div>
        <nav className="sidebar-nav" aria-label="Primary Navigation">
          {SECTIONS.map((sec) => (
            <button
              key={sec.id}
              type="button"
              className={`sidebar-nav-item ${section === sec.id ? 'active' : ''}`}
              onClick={() => handleSectionChange(sec.id)}
              aria-current={section === sec.id ? 'page' : undefined}
            >
              <span className="sidebar-nav-icon">
                <Icon name={sec.icon} />
              </span>
              <div className="sidebar-nav-text">
                <span className="nav-title">{sec.label}</span>
                <span className="nav-sub-count">
                  {sec.subTabs ? `${sec.subTabs.length} features` : 'Overview'}
                </span>
              </div>
            </button>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="sidebar-status">
            <span className="status-dot" />
            <span>API /api/v1 Connected</span>
          </div>
        </div>
      </aside>

      {/* Main Workspace Area */}
      <div className="workspace-wrapper">
        {/* Workspace Top Header */}
        <header className="workspace-topbar">
          <div className="topbar-heading">
            <p className="eyebrow">{currentSection.description}</p>
            <h1>{currentSection.label}</h1>
          </div>
          <div className="user-panel">
            <span className="user-avatar" aria-hidden="true">
              {user.displayName.slice(0, 1)}
            </span>
            <span className="user-name">{user.displayName}</span>
            <button type="button" className="ghost-button compact" onClick={onLogout}>
              Log out
            </button>
          </div>
        </header>

        {/* Horizontal Module Sub-tabs Bar */}
        {currentSection.subTabs ? (
          <div className="module-tabs-bar">
            <div className="module-tabs-list" role="tablist">
              {currentSection.subTabs.map((sub) => (
                <button
                  key={sub.id}
                  type="button"
                  role="tab"
                  className={`module-tab-item ${currentTabId === sub.id ? 'active' : ''}`}
                  onClick={() => handleSubTabChange(sub.id)}
                  aria-selected={currentTabId === sub.id}
                >
                  <Icon name={sub.icon} />
                  <span>{sub.label}</span>
                </button>
              ))}
            </div>
            {currentSubTab?.description ? (
              <span className="module-tab-hint">{currentSubTab.description}</span>
            ) : null}
          </div>
        ) : null}

        {/* Content Presentation Area */}
        <main id="main-content" className="workspace-content">
          <NoticeBanner notice={notice} />

          {/* Dynamic Views rendering */}
          {currentTabId === 'overview' ? <OverviewTab onNotice={onNotice} /> : null}
          {currentTabId === 'overkill-ops' ? <OverkillOpsTab onNotice={onNotice} /> : null}
          {currentTabId === 'op-input' ? <OpInputTab onNotice={onNotice} /> : null}
          {currentTabId === 'leakage-ops' ? <LeakageOpsTab onNotice={onNotice} /> : null}
          {currentTabId === 'import' ? <ImportTab onNotice={onNotice} /> : null}
          {currentTabId === 'scans' ? <ScansTab onNotice={onNotice} /> : null}
          {currentTabId === 'history' ? <HistoryTab onNotice={onNotice} /> : null}
          {currentTabId === 'products' ? (
            <ProductsTab onNotice={onNotice} onAnalyzeDefects={handleAnalyzeDefects} />
          ) : null}
          {currentTabId === 'overkill' ? (
            <OverkillTab
              onNotice={onNotice}
              onNavigateTab={(sec, tab) => {
                setSection(sec)
                setActiveSubTab((prev) => ({ ...prev, [sec]: tab }))
              }}
            />
          ) : null}
          {currentTabId === 'feature-trends' ? <FeatureTrendsTab onNotice={onNotice} /> : null}
          {currentTabId === 'sources' ? <SourcesTab onNotice={onNotice} /> : null}
          {currentTabId === 'classifier-rules' ? <ClassifierRulesTab onNotice={onNotice} /> : null}
          {currentTabId === 'queries' ? <QueriesTab onNotice={onNotice} /> : null}
          {currentTabId === 'storage' ? <StorageTab onNotice={onNotice} /> : null}
          {currentTabId === 'ai-verification' ? <AiVerificationTab onNotice={onNotice} /> : null}
          {currentTabId === 'audit' ? <AuditTab onNotice={onNotice} /> : null}
        </main>
      </div>
    </div>
  )
}

export default App
