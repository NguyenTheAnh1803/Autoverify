import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import './App.css'
import { ApiError, authApi, setUnauthorizedHandler } from './api'
import { TabPane } from './components/TabPane'
import { ConfirmProvider } from './confirm'
import { preloadAllTabs, preloadTab } from './tabRegistry'
import { Button, Icon, NoticeBanner } from './ui'
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

const DEFAULT_SUB_TABS: Record<MainSectionId, TabId> = {
  dashboard: 'overview',
  operations: 'overkill-ops',
  products: 'products',
  settings: 'classifier-rules',
}

/** Tabs kept mounted (hidden) after their first visit. The oldest-opened one is released past this limit. */
const MAX_KEPT_TABS = 5

/** `#/operations/leakage-ops` -> section + tab. Anything that is not a known pair returns null. */
function parseRoute(hash: string): { section: MainSectionId; tab: TabId } | null {
  const [, sectionId, tabId] = hash.split('/')
  const section = SECTIONS.find((item) => item.id === sectionId)
  if (!section) return null
  if (!section.subTabs) return { section: section.id, tab: DEFAULT_SUB_TABS[section.id] }
  const tab = section.subTabs.find((item) => item.id === tabId)
  return { section: section.id, tab: tab ? tab.id : DEFAULT_SUB_TABS[section.id] }
}

function formatRoute(section: MainSectionId, tab: TabId): string {
  return section === 'dashboard' ? '#/dashboard' : `#/${section}/${tab}`
}

function App() {
  const [token, setToken] = useState(() => localStorage.getItem('autoverify.token') ?? '')
  const [user, setUser] = useState<UserProfile | null>(null)
  const [loginError, setLoginError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (token) localStorage.setItem('autoverify.token', token)
    else localStorage.removeItem('autoverify.token')
  }, [token])

  // Any request that comes back 401 ends the session once, with a reason, instead of leaving every panel showing an error.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      setUser(null)
      setToken('')
      setLoginError('Your session has expired. Sign in again to continue.')
    })
    return () => setUnauthorizedHandler(null)
  }, [])

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
      .catch((error: unknown) => {
        if (cancelled) return
        // A rejected or expired token must never open the workspace.
        if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
          setToken('')
          setLoginError('Your session has expired. Sign in again to continue.')
          return
        }
        // Older API builds have no /auth/me (or it is briefly unreachable): keep the session with a generic profile.
        setUser({ username: 'admin', displayName: 'Administrator', roles: ['Administrator'] })
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
    } catch (error) {
      // Only a rejected credential is the user's fault; an unreachable or failing server needs a different next step.
      const rejected = error instanceof ApiError && (error.status === 400 || error.status === 401 || error.status === 403)
      setLoginError(rejected ? 'Invalid username or password.' : error instanceof Error ? error.message : 'Sign-in failed. Try again.')
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

  return (
    <ConfirmProvider>
      <Workspace user={user} onLogout={() => void logout()} />
    </ConfirmProvider>
  )
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
  const [showPassword, setShowPassword] = useState(false)
  // Prefilled credentials exist only for local development; a production build starts with empty fields.
  const prefill = import.meta.env.DEV
  return (
    <main className="login-page">
      <section className="login-card">
        <p className="eyebrow">AutoVerify System</p>
        <h1>Operational verification console</h1>
        <p className="muted">Sign in to import ID lists, run verification scans, and investigate anomalies.</p>
        <form onSubmit={(event) => void onLogin(event)} className="login-form">
          <label>
            Username
            <input name="username" defaultValue={prefill ? 'admin' : ''} autoComplete="username" required />
          </label>
          <label>
            Password
            <span className="password-field">
              <input
                name="password"
                defaultValue={prefill ? 'admin!' : ''}
                type={showPassword ? 'text' : 'password'}
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                className="password-toggle"
                onClick={() => setShowPassword((value) => !value)}
                aria-pressed={showPassword}
              >
                {showPassword ? 'Hide' : 'Show'}
              </button>
            </span>
          </label>
          {loginError ? <div className="form-error" role="alert">{loginError}</div> : null}
          <Button type="submit" loading={busy}>
            {busy ? 'Signing in…' : 'Log in'}
          </Button>
        </form>
        {prefill ? <p className="security-note">Development build: sign-in is prefilled with the temporary MVP account.</p> : null}
      </section>
    </main>
  )
}

function Workspace({ user, onLogout }: { user: UserProfile; onLogout: () => void }) {
  // The address bar decides where to start, so a reload or a shared link lands on the same tab.
  const [initialRoute] = useState(() => parseRoute(window.location.hash))
  const [section, setSection] = useState<MainSectionId>(initialRoute?.section ?? 'dashboard')
  const [activeSubTab, setActiveSubTab] = useState<Record<MainSectionId, TabId>>(() => ({
    ...DEFAULT_SUB_TABS,
    ...(initialRoute ? { [initialRoute.section]: initialRoute.tab } : {}),
  }))
  const [notice, setNotice] = useState<Notice | null>(null)
  const onNotice = useCallback((next: Notice) => setNotice(next), [])

  const currentTabId = section === 'dashboard' ? 'overview' : activeSubTab[section]
  const currentTabRef = useRef<TabId>(currentTabId)
  const scrollByTab = useRef<Partial<Record<TabId, number>>>({})

  const navigate = useCallback((nextSection: MainSectionId, nextTab: TabId) => {
    scrollByTab.current[currentTabRef.current] = window.scrollY
    setSection(nextSection)
    setActiveSubTab((prev) => (prev[nextSection] === nextTab ? prev : { ...prev, [nextSection]: nextTab }))
  }, [])

  const handleAnalyzeDefects = useCallback(
    (_handoff: ProductHandoff) => navigate('products', 'feature-trends'),
    [navigate],
  )

  // Success and info messages fade after a few seconds. An error stays until it is dismissed or replaced, so it can still be read.
  useEffect(() => {
    if (!notice || notice.tone === 'error') return
    const timer = window.setTimeout(() => setNotice(null), 6000)
    return () => window.clearTimeout(timer)
  }, [notice])

  const currentSection = useMemo(() => SECTIONS.find((item) => item.id === section) ?? SECTIONS[0], [section])
  const currentSubTab = useMemo(
    () => currentSection.subTabs?.find((sub) => sub.id === currentTabId) ?? null,
    [currentSection, currentTabId],
  )

  function handleSectionChange(newSection: MainSectionId) {
    navigate(newSection, newSection === 'dashboard' ? 'overview' : activeSubTab[newSection])
  }

  // Warm the chunk of a section's current tab as soon as the pointer or keyboard reaches its button.
  function preloadSection(sectionId: MainSectionId) {
    preloadTab(sectionId === 'dashboard' ? 'overview' : activeSubTab[sectionId])
  }

  // Once signed in, fetch the remaining tabs in idle time so a later switch is instant.
  useEffect(() => {
    const timer = setTimeout(() => {
      preloadAllTabs()
    }, 4000)
    return () => clearTimeout(timer)
  }, [])

  function handleSubTabChange(newTab: TabId) {
    navigate(section, newTab)
  }

  // WAI-ARIA tabs: arrow keys move between tabs, Home/End jump to the ends; only the selected tab is a tab stop.
  function handleTabKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const tabs = currentSection.subTabs
    if (!tabs) return
    let next = -1
    if (event.key === 'ArrowRight') next = (index + 1) % tabs.length
    else if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length
    else if (event.key === 'Home') next = 0
    else if (event.key === 'End') next = tabs.length - 1
    if (next < 0) return
    event.preventDefault()
    handleSubTabChange(tabs[next].id)
    window.requestAnimationFrame(() => document.getElementById(`tab-${tabs[next].id}`)?.focus())
  }

  // The tab strip scrolls sideways when it does not fit; keep the selected tab in view.
  useEffect(() => {
    document.getElementById(`tab-${currentTabId}`)?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  }, [currentTabId])

  // Keep the address bar in step with the tab, and follow Back/Forward and edited links.
  useEffect(() => {
    currentTabRef.current = currentTabId
    const next = formatRoute(section, currentTabId)
    if (window.location.hash === next) return
    if (window.location.hash.startsWith('#/')) window.location.hash = next
    else window.history.replaceState(null, '', next)
  }, [section, currentTabId])

  useEffect(() => {
    function onHashChange() {
      const route = parseRoute(window.location.hash)
      if (route) navigate(route.section, route.tab)
    }
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [navigate])

  // Tabs opened so far stay mounted (hidden): their filters, pages and data are still there when the user returns.
  const [openedTabs, setOpenedTabs] = useState<TabId[]>([currentTabId])
  const visibleTabs = useMemo(() => {
    const kept = openedTabs.includes(currentTabId) ? openedTabs : [...openedTabs, currentTabId]
    return kept.length > MAX_KEPT_TABS
      ? kept.filter((id, index) => id === currentTabId || kept.length - index <= MAX_KEPT_TABS)
      : kept
  }, [openedTabs, currentTabId])
  useEffect(() => {
    setOpenedTabs((prev) => (prev.length === visibleTabs.length && prev.every((id, i) => id === visibleTabs[i]) ? prev : visibleTabs))
  }, [visibleTabs])

  // Back on a tab: put the page back where it was, or at the top for a tab seen the first time.
  useEffect(() => {
    window.scrollTo(0, scrollByTab.current[currentTabId] ?? 0)
  }, [currentTabId])

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
              onPointerEnter={() => preloadSection(sec.id)}
              onFocus={() => preloadSection(sec.id)}
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
          <section className="module-tabs-bar" aria-label={`${currentSection.label} pages`}>
            <div className="module-tabs-list" role="tablist" aria-label={currentSection.label}>
              {currentSection.subTabs.map((sub, index) => (
                <button
                  key={sub.id}
                  id={`tab-${sub.id}`}
                  type="button"
                  role="tab"
                  aria-controls={`panel-${sub.id}`}
                  tabIndex={currentTabId === sub.id ? 0 : -1}
                  onKeyDown={(event) => handleTabKeyDown(event, index)}
                  className={`module-tab-item ${currentTabId === sub.id ? 'active' : ''}`}
                  onClick={() => handleSubTabChange(sub.id)}
                  onPointerEnter={() => preloadTab(sub.id)}
                  onFocus={() => preloadTab(sub.id)}
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
          </section>
        ) : null}

        {/* Content Presentation Area */}
        <main id="main-content" className="workspace-content">
          <NoticeBanner notice={notice} onDismiss={() => setNotice(null)} />

          {/* Each tab is its own chunk (TabPane shows a skeleton while it downloads) and stays mounted once opened. */}
          {visibleTabs.map((id) => (
            <TabPane
              key={id}
              id={id}
              active={id === currentTabId}
              onNotice={onNotice}
              onAnalyzeDefects={handleAnalyzeDefects}
              onNavigateTab={navigate}
            />
          ))}
        </main>
      </div>
    </div>
  )
}

export default App
