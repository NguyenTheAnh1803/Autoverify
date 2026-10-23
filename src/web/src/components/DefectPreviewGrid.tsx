import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { defectPreviewApi, storageApi } from '../api'
import { formatDate } from '../format'
import { usePolling } from '../lib/usePolling'
import { Modal } from '../ui'
// The analytics dashboard (and its charts) is opened on demand, so it is downloaded the first time it is opened.
const DefectFeatureAnalyticsDashboard = lazy(() =>
  import('./analytics/DefectFeatureAnalyticsDashboard').then((m) => ({ default: m.DefectFeatureAnalyticsDashboard })),
)
import type {
  DefectPreviewFilterSummary,
  DefectPreviewProgress,
  DefectPreviewRecord,
  DefectPreviewSession,
  Notice,
} from '../types'

type PreviewSortId =
  | 'rowIndex'
  | 'keyId'
  | 'defectId'
  | 'defectName'
  | 'judge'
  | 'severity'
  | 'defectType'
  | 'componentName'
  | 'defectTable'
  | 'confidenceScore'
  | 'inspectedAtUtc'

type PreviewSorting = { id: PreviewSortId; desc: boolean }[]

export type DefectPreviewGridProps = {
  sessionId: string
  sourceUri?: string
  keyId?: string | null
  initialSession?: DefectPreviewSession | null
  onClose: () => void
  onNotice?: (notice: Notice) => void
}

export function DefectPreviewGrid({
  sessionId,
  sourceUri,
  keyId,
  initialSession,
  onClose,
  onNotice,
}: DefectPreviewGridProps) {
  // Session & Polling state
  const [sessionStatus, setSessionStatus] = useState<string>(initialSession?.status || 'Pending')
  const [progress, setProgress] = useState<DefectPreviewProgress | null>(
    initialSession
      ? {
          sessionId: initialSession.sessionId,
          status: initialSession.status,
          processedRecords: initialSession.processedRecords,
          totalRecords: initialSession.totalRecords,
          percentComplete:
            initialSession.totalRecords > 0
              ? (initialSession.processedRecords / initialSession.totalRecords) * 100
              : 0,
        }
      : null
  )
  const [progressError, setProgressError] = useState<string | null>(null)
  const deletedRef = useRef(false)

  // Server-side Preview State
  const [items, setItems] = useState<DefectPreviewRecord[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [totalPages, setTotalPages] = useState(1)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [sorting, setSorting] = useState<PreviewSorting>([{ id: 'rowIndex', desc: false }])
  const [loadingRows, setLoadingRows] = useState(false)
  const [fetchError, setFetchError] = useState<string | null>(null)

  const handleSort = (columnId: PreviewSortId) => {
    setSorting((prev) => {
      const current = prev[0]
      if (current && current.id === columnId) {
        return [{ id: columnId, desc: !current.desc }]
      }
      return [{ id: columnId, desc: false }]
    })
    setPage(1)
  }

  // Filter Bar State
  const [searchInput, setSearchInput] = useState('')
  const [debouncedKeyword, setDebouncedKeyword] = useState('')
  const [judgeFilter, setJudgeFilter] = useState<'ALL' | 'NG' | 'OK' | 'REVIEW'>('ALL')
  const [severityFilter, setSeverityFilter] = useState<'ALL' | 'Fatal' | 'Major' | 'Minor'>('ALL')
  const [defectTableFilter, setDefectTableFilter] = useState('')
  const [knownDefectNames, setKnownDefectNames] = useState<string[]>([])

  // Selected single record for inspection modal & live corner preview
  const [selectedRecord, setSelectedRecord] = useState<DefectPreviewRecord | null>(null)
  const [inspectorOpen, setInspectorOpen] = useState(false)
  const [copySuccess, setCopySuccess] = useState(false)

  // Analytics modal & drilldown highlighting
  const [showAnalyticsModal, setShowAnalyticsModal] = useState(false)
  const [analyticsInitialTab, setAnalyticsInitialTab] = useState<'overview' | 'script_condition'>('overview')
  const [highlightedRowIndex, setHighlightedRowIndex] = useState<number | null>(null)

  // Helper to extract image URL from preview record
  const getRecordImageUrl = useCallback((rec: DefectPreviewRecord | null): string | null => {
    if (!rec) return null
    if (
      rec.rawImageUri &&
      (rec.rawImageUri.startsWith('http://') ||
        rec.rawImageUri.startsWith('https://') ||
        rec.rawImageUri.startsWith('/'))
    ) {
      return rec.rawImageUri
    }
    if (
      rec.defectId &&
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(rec.defectId)
    ) {
      return storageApi.defectFileUrl(rec.defectId, 'image')
    }
    return null
  }, [])

  // Auto-show Defect data without needing to click: automatically select 1st row when records load
  useEffect(() => {
    if (items.length > 0 && (!selectedRecord || !items.some((it) => it.id === selectedRecord.id))) {
      setSelectedRecord(items[0])
      setHighlightedRowIndex(items[0].rowIndex)
    }
  }, [items, selectedRecord])

  // Manual refresh trigger
  const [refreshTrigger, setRefreshTrigger] = useState(0)

  // Debounce search input (350ms)
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDebouncedKeyword(searchInput.trim())
      setPage(1)
    }, 350)
    return () => window.clearTimeout(timer)
  }, [searchInput])

  // Scroll highlighted drilldown row into view when present
  useEffect(() => {
    if (highlightedRowIndex == null || loadingRows) return
    const el = document.querySelector('.preview-record-row.row-highlighted')
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    }
  }, [highlightedRowIndex, items, loadingRows])

  // Reset to page 1 whenever filters change
  const handleJudgeChange = (val: 'ALL' | 'NG' | 'OK' | 'REVIEW') => {
    setJudgeFilter(val)
    setPage(1)
  }

  const handleSeverityChange = (val: 'ALL' | 'Fatal' | 'Major' | 'Minor') => {
    setSeverityFilter(val)
    setPage(1)
  }

  const handleTableChange = (val: string) => {
    setDefectTableFilter(val)
    setPage(1)
  }

  // Session Cleanup
  const handleClose = useCallback(async () => {
    if (!deletedRef.current) {
      deletedRef.current = true
      try {
        await defectPreviewApi.deleteSession(sessionId)
      } catch {
        // best effort cleanup
      }
    }
    onClose()
  }, [sessionId, onClose])

  // Progress polling: runs while the session is still ingesting, and pauses while this tab or the browser tab is hidden.
  const isCompleted = sessionStatus === 'Ready' || sessionStatus === 'Failed' || sessionStatus === 'Expired'
  const unmountedRef = useRef(false)
  useEffect(() => {
    unmountedRef.current = false
    return () => {
      unmountedRef.current = true
    }
  }, [])

  const pollProgress = useCallback(async () => {
    try {
      const prog = await defectPreviewApi.getProgress(sessionId)
      if (unmountedRef.current) return

      setProgress(prog)
      setSessionStatus(prog.status)

      if (prog.status === 'Ready') {
        // Trigger reload of rows when ingestion completes
        setRefreshTrigger((v) => v + 1)
        if (onNotice) {
          onNotice({
            tone: 'success',
            message: `Defect preview ready: ${prog.processedRecords.toLocaleString()} records indexed.`,
          })
        }
      } else if (prog.status === 'Failed') {
        setProgressError(prog.errorMessage || 'Defect stream ingestion failed on server.')
      } else if (prog.status === 'Expired') {
        setProgressError('Preview session has expired.')
      }
    } catch (err) {
      if (unmountedRef.current) return
      // Don't treat transient poll error as fatal unless persistent
      console.warn('Progress poll error:', err)
    }
  }, [sessionId, onNotice])

  useEffect(() => {
    if (!isCompleted) void pollProgress()
  }, [isCompleted, pollProgress])

  usePolling(pollProgress, 1500, !isCompleted)

  // Query Rows Effect (Server-side)
  useEffect(() => {
    const controller = new AbortController()
    setLoadingRows(true)
    setFetchError(null)

    const activeSort = sorting[0]
    let sortBy = 'rowIndex'
    if (activeSort) {
      sortBy = activeSort.id
    }
    const sortDir = activeSort?.desc ? 'desc' : 'asc'

    defectPreviewApi
      .queryRows(
        sessionId,
        {
          page,
          pageSize,
          sortBy,
          sortDir,
          keyword: debouncedKeyword || undefined,
          judge: judgeFilter !== 'ALL' ? judgeFilter : undefined,
          severity: severityFilter !== 'ALL' ? severityFilter : undefined,
          defectTable: defectTableFilter ? defectTableFilter : undefined,
        },
        controller.signal
      )
      .then((res) => {
        const nextItems = res.items || []
        setItems(nextItems)
        setTotalCount(res.totalCount || 0)
        setTotalPages(Math.max(1, res.totalPages || 1))
        setKnownDefectNames((prev) => {
          const merged = new Set(prev)
          for (const item of nextItems) {
            if (item.defectName) merged.add(item.defectName)
          }
          return [...merged].sort((a, b) => a.localeCompare(b))
        })
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        const msg = err instanceof Error ? err.message : 'Failed to query defect preview rows'
        setFetchError(msg)
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoadingRows(false)
        }
      })

    return () => {
      controller.abort()
    }
  }, [
    sessionId,
    page,
    pageSize,
    sorting,
    debouncedKeyword,
    judgeFilter,
    severityFilter,
    defectTableFilter,
    refreshTrigger,
  ])

  // Format JSON snippet safely
  const formattedSnippet = useMemo(() => {
    if (!selectedRecord?.featureJsonSnippet) return null
    try {
      const parsed = JSON.parse(selectedRecord.featureJsonSnippet) as unknown
      return JSON.stringify(parsed, null, 2)
    } catch {
      return selectedRecord.featureJsonSnippet
    }
  }, [selectedRecord])

  const copySnippetToClipboard = () => {
    if (!formattedSnippet) return
    void navigator.clipboard.writeText(formattedSnippet).then(() => {
      setCopySuccess(true)
      window.setTimeout(() => setCopySuccess(false), 2000)
    })
  }

  // Summary statistics from filterSummary
  const summary: DefectPreviewFilterSummary | null = progress?.filterSummary ?? null

  return (
    <section className="defect-preview-container" aria-label="Large Scale Defect Preview Grid">
      {/* Header bar with title, source URI, status pill, close */}
      <div className="preview-top-bar">
        <div className="preview-title-block">
          <div className="preview-title-row">
            <span className="preview-sparkle-icon">🔬</span>
            <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700 }}>
              Large-Scale Defect Preview (Server-Side Staged)
            </h3>
            <span className={`status-pill ${sessionStatus.toLowerCase()}`}>
              {sessionStatus === 'Ingesting' ? '⏳ Ingesting Stream...' : sessionStatus}
            </span>
          </div>
          <div className="preview-source-subtext">
            <span>Session: <code className="mono-code">{sessionId}</code></span>
            {keyId && <span> · Key ID: <strong>{keyId}</strong></span>}
            {sourceUri && (
              <span>
                {' '}· Source: <code className="mono-code preview-uri-tag">{sourceUri}</code>
              </span>
            )}
            {(progress?.detectedFormat || initialSession?.detectedFormat) && (
              <span> · Format: <strong>{progress?.detectedFormat || initialSession?.detectedFormat}</strong></span>
            )}
            {(progress?.processingStage || initialSession?.processingStage) && (
              <span> · Stage: <em>{progress?.processingStage || initialSession?.processingStage}</em></span>
            )}
          </div>
        </div>

        <div className="preview-top-actions">
          <button
            type="button"
            className="compact-button primary"
            style={{ background: '#2563eb', borderColor: '#3b82f6', fontWeight: 600 }}
            onClick={() => {
              setAnalyticsInitialTab('script_condition')
              setShowAnalyticsModal(true)
            }}
            title="Paste and evaluate Python AOI script for in-depth defect condition breakdown"
          >
            <span style={{ marginRight: '6px' }}>⚡</span>
            <span>AOI Script Analysis</span>
          </button>
          <button
            type="button"
            className="compact-button primary"
            onClick={() => {
              setAnalyticsInitialTab('overview')
              setShowAnalyticsModal(true)
            }}
            title="Open Feature Analytics Dashboard (SPC, EWMA, CUSUM, PCA, MSA, DOE)"
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
              style={{ marginRight: '6px', verticalAlign: '-2px' }}
            >
              <line x1="18" y1="20" x2="18" y2="10" />
              <line x1="12" y1="20" x2="12" y2="4" />
              <line x1="6" y1="20" x2="6" y2="14" />
            </svg>
            <span>Feature Analytics</span>
          </button>
          <button
            type="button"
            className="compact-button secondary"
            onClick={() => setRefreshTrigger((v) => v + 1)}
            disabled={loadingRows}
            title="Refresh current page"
          >
            🔄 Refresh
          </button>
          <button
            type="button"
            className="compact-button ghost"
            onClick={() => void handleClose()}
            title="Close preview and release server staging table"
          >
            ✕ Close Preview
          </button>
        </div>
      </div>

      {/* Ingestion Progress Banner (visible during Pending or Ingesting) */}
      {(sessionStatus === 'Pending' || sessionStatus === 'Ingesting') && (
        <div className="preview-progress-panel">
          <div className="progress-info-row">
            <div className="progress-text">
              <span className="spinner-sm" />
              <strong>
                {progress?.processingStage
                  ? `Stage [${progress.processingStage}]:`
                  : 'Buffering Defect Stream:'}
              </strong>{' '}
              {progress?.processedRecords.toLocaleString() ?? 0}
              {progress?.totalRecords ? ` / ${progress.totalRecords.toLocaleString()}` : ''} records ingested
              {progress?.percentComplete ? ` (${progress.percentComplete.toFixed(1)}%)` : ''}
              {progress?.detectedFormat && (
                <span className="detected-format-tag" style={{ marginLeft: '8px' }}>
                  Format: <code>{progress.detectedFormat}</code>
                </span>
              )}
            </div>
            <span className="progress-hint">You can search and sort while stream is loading</span>
          </div>
          <div className="progress-bar-track">
            <div
              className="progress-bar-fill animated-stripes"
              style={{
                width: `${Math.min(100, Math.max(5, progress?.percentComplete ?? 10))}%`,
              }}
            />
          </div>
        </div>
      )}

      {/* Error or Expired Banner */}
      {(progressError || sessionStatus === 'Failed' || sessionStatus === 'Expired') && (
        <div className="preview-error-banner" role="alert">
          <span className="error-icon" aria-hidden="true">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </span>
          <div style={{ flex: 1 }}>
            <strong>Session {sessionStatus}:</strong>{' '}
            {progressError || 'The defect preview session encountered an error or has expired.'}
          </div>
          <button
            type="button"
            className="compact-button secondary"
            onClick={() => void handleClose()}
          >
            Close
          </button>
        </div>
      )}

      {(progress?.warningMessage || initialSession?.warningMessage) && !progressError && (
        <div className="preview-key-scope-notice" role="status">
          <strong>Key scope:</strong> {progress?.warningMessage || initialSession?.warningMessage}
        </div>
      )}

      {/* Product & Defect Inspection Context Banner */}
      <div className="preview-product-banner">
        <div className="preview-product-identity">
          <span className="product-icon">📦</span>
          <div>
            <div className="product-title">
              Product Key / ID: <strong className="mono-code">{keyId || items[0]?.keyId || 'Batch / Multi-product Stream'}</strong>
            </div>
            <div className="product-subtitle">
              Defect inspection table ({totalCount > 0 ? `${totalCount.toLocaleString()} defect rows` : '1 Product multi-defect records'})
            </div>
          </div>
        </div>

        {summary && (
          <div className="preview-summary-chips">
            <div className="summary-chip total" title="Total defects for this product">
              <span className="chip-label">Total Defects</span>
              <span className="chip-value">{summary.total.toLocaleString()}</span>
            </div>
            <div className="summary-chip ng" title="Defects judged NG">
              <span className="chip-label">NG Defect</span>
              <span className="chip-value">{summary.ngCount.toLocaleString()}</span>
            </div>
            <div className="summary-chip ok" title="Defects judged OK">
              <span className="chip-label">OK</span>
              <span className="chip-value">{summary.okCount.toLocaleString()}</span>
            </div>
            {Object.entries(summary.severityBreakdown || {}).map(([sev, count]) => (
              <div key={sev} className="summary-chip sev" title={`Severity: ${sev}`}>
                <span className="chip-label">{sev}</span>
                <span className="chip-value">{count.toLocaleString()}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Filter Toolbar */}
      <div className="preview-filter-toolbar">
        <div className="filter-group search-group">
          <span className="filter-icon">🔍</span>
          <input
            type="text"
            placeholder="Search defect name, component, defect ID..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            className="search-input"
            aria-label="Search defect records"
          />
          {searchInput && (
            <button
              type="button"
              className="clear-search-button"
              onClick={() => setSearchInput('')}
              aria-label="Clear search"
            >
              ✕
            </button>
          )}
        </div>

        <div className="filter-group">
          <span className="filter-label">Defect:</span>
          <select
            value={debouncedKeyword && items.some((item) => item.defectName === debouncedKeyword) ? debouncedKeyword : ''}
            onChange={(e) => {
              setSearchInput(e.target.value)
              setPage(1)
            }}
            aria-label="Filter by defect name"
          >
            <option value="">All defect names</option>
            {knownDefectNames.map((name) => (
              <option key={name} value={name}>{name}</option>
            ))}
          </select>
        </div>

        {/* Judge Pills */}
        <div className="filter-group pill-group">
          <span className="filter-label">Judge:</span>
          {(['ALL', 'NG', 'OK', 'REVIEW'] as const).map((judge) => (
            <button
              key={judge}
              type="button"
              className={`filter-pill ${judgeFilter === judge ? 'active' : ''}`}
              onClick={() => handleJudgeChange(judge)}
            >
              {judge}
            </button>
          ))}
        </div>

        {/* Severity Pills */}
        <div className="filter-group pill-group">
          <span className="filter-label">Severity:</span>
          {(['ALL', 'Fatal', 'Major', 'Minor'] as const).map((sev) => (
            <button
              key={sev}
              type="button"
              className={`filter-pill ${severityFilter === sev ? 'active' : ''}`}
              onClick={() => handleSeverityChange(sev)}
            >
              {sev}
            </button>
          ))}
        </div>

        {/* Defect Table filter */}
        <div className="filter-group">
          <input
            type="text"
            placeholder="Defect table (e.g. AOI_DEFECTS)..."
            value={defectTableFilter}
            onChange={(e) => handleTableChange(e.target.value)}
            className="filter-input-small"
            aria-label="Filter by defect table"
          />
        </div>

        <div className="filter-group preview-sort-control">
          <span className="filter-label">Sort:</span>
          <select
            value={`${sorting[0]?.id || 'rowIndex'}:${sorting[0]?.desc ? 'desc' : 'asc'}`}
            onChange={(e) => {
              const [id, direction] = e.target.value.split(':') as [PreviewSortId, 'asc' | 'desc']
              setSorting([{ id, desc: direction === 'desc' }])
              setPage(1)
            }}
            aria-label="Sort preview records"
          >
            <option value="rowIndex:asc">Row: first to last</option>
            <option value="rowIndex:desc">Row: last to first</option>
            <option value="defectName:asc">Defect name: A-Z</option>
            <option value="defectName:desc">Defect name: Z-A</option>
            <option value="judge:asc">Judge</option>
            <option value="severity:desc">Severity</option>
            <option value="inspectedAtUtc:desc">Newest inspected</option>
            <option value="inspectedAtUtc:asc">Oldest inspected</option>
          </select>
        </div>

        {/* Reset filters button */}
        {(searchInput || judgeFilter !== 'ALL' || severityFilter !== 'ALL' || defectTableFilter) && (
          <button
            type="button"
            className="compact-button ghost"
            onClick={() => {
              setSearchInput('')
              setJudgeFilter('ALL')
              setSeverityFilter('ALL')
              setDefectTableFilter('')
              setPage(1)
            }}
          >
            Reset Filters
          </button>
        )}
      </div>

      {/* Excel-style Defect Table with Responsive Horizontal Scroll & Sticky Header */}
      <div className="preview-grid-wrapper excel-view">
        {loadingRows && (
          <div className="grid-loading-overlay">
            <div className="spinner-md" />
            <span>Loading defect list...</span>
          </div>
        )}

        {fetchError && !loadingRows && (
          <div className="grid-fetch-error">
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
                <line x1="12" y1="9" x2="12" y2="13" />
                <line x1="12" y1="17" x2="12.01" y2="17" />
              </svg>
              Error: {fetchError}
            </span>
            <button
              type="button"
              className="compact-button secondary"
              onClick={() => setRefreshTrigger((v) => v + 1)}
            >
              Retry
            </button>
          </div>
        )}

        <div className="preview-excel-scroll-container">
          <table className="preview-excel-table" role="table">
            <thead>
              <tr>
                <th className="excel-th col-index sortable" onClick={() => handleSort('rowIndex')}>
                  <span>#</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'rowIndex' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-key sortable" onClick={() => handleSort('keyId')}>
                  <span>Product / Key ID</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'keyId' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-defect-id sortable" onClick={() => handleSort('defectId')}>
                  <span>Defect ID</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'defectId' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-name sortable" onClick={() => handleSort('defectName')}>
                  <span>Defect Name</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'defectName' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-judge sortable" onClick={() => handleSort('judge')}>
                  <span>Judge</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'judge' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-severity sortable" onClick={() => handleSort('severity')}>
                  <span>Severity</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'severity' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-type sortable" onClick={() => handleSort('defectType')}>
                  <span>Type</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'defectType' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-component sortable" onClick={() => handleSort('componentName')}>
                  <span>Component</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'componentName' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-coords">
                  <span>Coord (X, Y)</span>
                </th>
                <th className="excel-th col-table sortable" onClick={() => handleSort('defectTable')}>
                  <span>Table</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'defectTable' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-confidence sortable" onClick={() => handleSort('confidenceScore')}>
                  <span>Confidence</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'confidenceScore' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-time sortable" onClick={() => handleSort('inspectedAtUtc')}>
                  <span>Inspected Time</span>
                  <span className="sort-indicator">{sorting[0]?.id === 'inspectedAtUtc' ? (sorting[0].desc ? ' ▼' : ' ▲') : ' ↕'}</span>
                </th>
                <th className="excel-th col-actions">
                  <span>Image / Inspect</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 && !loadingRows && !fetchError ? (
                <tr>
                  <td colSpan={13} className="empty-preview-cell">
                    <div className="empty-state-content">
                      <span style={{ fontSize: '24px' }}>🔍</span>
                      <p style={{ margin: '4px 0 0', fontWeight: 600 }}>No defects match the current filter criteria</p>
                      <span style={{ fontSize: '12px', color: '#64748b' }}>
                        Try adjusting keywords or clearing active filter constraints.
                      </span>
                    </div>
                  </td>
                </tr>
              ) : (
                items.map((record) => {
                  const imageUrl = getRecordImageUrl(record)
                  const judge = (record.judge || '').toUpperCase()
                  const judgeClass = judge === 'NG' ? 'badge-ng' : judge === 'OK' ? 'badge-ok' : 'badge-review'
                  const severity = record.severity || ''
                  const severityClass = ['fatal', 'major', 'minor'].includes(severity.toLowerCase())
                    ? `badge-${severity.toLowerCase()}`
                    : 'badge-default'

                  const isSelected = selectedRecord?.id === record.id
                  const isHighlighted = highlightedRowIndex === record.rowIndex

                  return (
                    <tr
                      key={record.id}
                      className={[
                        'excel-row',
                        isSelected ? 'row-selected' : '',
                        isHighlighted ? 'row-highlighted' : '',
                      ].filter(Boolean).join(' ')}
                      onClick={() => {
                        setSelectedRecord(record)
                        setHighlightedRowIndex(record.rowIndex)
                      }}
                      onDoubleClick={() => {
                        setSelectedRecord(record)
                        setHighlightedRowIndex(record.rowIndex)
                        setInspectorOpen(true)
                      }}
                    >
                      <td className="cell-index mono-code text-center">#{record.rowIndex}</td>
                      <td className="cell-key">
                        <span className="product-key-badge mono-code" title={record.keyId || keyId || undefined}>
                          {record.keyId || keyId || '—'}
                        </span>
                      </td>
                      <td className="cell-defect-id mono-code" title={record.defectId || undefined}>
                        {record.defectId || '—'}
                      </td>
                      <td className="cell-name" title={record.defectName || undefined}>
                        <strong>{record.defectName || 'Unnamed defect'}</strong>
                      </td>
                      <td className="cell-judge text-center">
                        <span className={`preview-badge ${judgeClass}`}>{judge || '—'}</span>
                      </td>
                      <td className="cell-severity text-center">
                        <span className={`preview-badge ${severityClass}`}>{severity || '—'}</span>
                      </td>
                      <td className="cell-type" title={record.defectType || undefined}>{record.defectType || '—'}</td>
                      <td className="cell-component" title={record.componentName || undefined}>{record.componentName || '—'}</td>
                      <td className="cell-coords mono-code text-center">
                        {record.coordinateX != null || record.coordinateY != null
                          ? `${record.coordinateX?.toFixed(1) ?? '?'}, ${record.coordinateY?.toFixed(1) ?? '?'}`
                          : '—'}
                      </td>
                      <td className="cell-table mono-code" title={record.defectTable || undefined}>{record.defectTable || '—'}</td>
                      <td className="cell-confidence mono-code text-right">
                        {record.confidenceScore != null ? `${(record.confidenceScore * 100).toFixed(1)}%` : '—'}
                      </td>
                      <td className="cell-time">{record.inspectedAtUtc ? formatDate(record.inspectedAtUtc) : '—'}</td>
                      <td className="cell-actions text-center" onClick={(e) => e.stopPropagation()}>
                        <div className="excel-cell-actions">
                          {imageUrl ? (
                            <a
                              href={imageUrl}
                              download={`defect_${record.defectId || record.rowIndex}.jpg`}
                              target="_blank"
                              rel="noreferrer"
                              className="compact-button secondary excel-action-btn"
                              title="Download defect image"
                            >
                              ⬇️ Image
                            </a>
                          ) : null}
                          <button
                            type="button"
                            className="compact-button secondary excel-action-btn"
                            onClick={() => {
                              setSelectedRecord(record)
                              setHighlightedRowIndex(record.rowIndex)
                              setInspectorOpen(true)
                            }}
                            title="Inspect feature values and raw JSON"
                          >
                            🔍 Details
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Server-Side Pagination & Page Size Control */}
      <div className="preview-pagination-bar">
        <div className="pagination-info">
          <span>
            Page <strong>{page}</strong> of <strong>{totalPages}</strong>
            {' · '}
            <strong>{totalCount.toLocaleString()}</strong> matching defect records
          </span>
          {sessionStatus === 'Ingesting' && (
            <span className="live-indexing-tag">⚡ Live indexing in progress</span>
          )}
        </div>

        <div className="pagination-controls">
          <label className="page-size-label">
            Rows per page:
            <select
              value={pageSize}
              onChange={(e) => {
                setPageSize(Number(e.target.value))
                setPage(1)
              }}
              className="page-size-select"
            >
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
            </select>
          </label>

          <div className="page-nav-buttons">
            <button
              type="button"
              className="compact-button ghost"
              disabled={page <= 1 || loadingRows}
              onClick={() => setPage(1)}
              title="First Page"
            >
              « First
            </button>
            <button
              type="button"
              className="compact-button ghost"
              disabled={page <= 1 || loadingRows}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              title="Previous Page"
            >
              ‹ Prev
            </button>
            <button
              type="button"
              className="compact-button ghost"
              disabled={page >= totalPages || loadingRows}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              title="Next Page"
            >
              Next ›
            </button>
            <button
              type="button"
              className="compact-button ghost"
              disabled={page >= totalPages || loadingRows}
              onClick={() => setPage(totalPages)}
              title="Last Page"
            >
              Last »
            </button>
          </div>
        </div>
      </div>

      {/* Record Quick Inspector Modal */}
      {selectedRecord && inspectorOpen && (
        <Modal
          title={`Defect Inspector: #${selectedRecord.rowIndex} (${selectedRecord.defectId || 'No ID'})`}
          description={`Inspecting defect record from table ${selectedRecord.defectTable || 'Unknown'}`}
          open={inspectorOpen}
          onClose={() => setInspectorOpen(false)}
        >
          <div className="defect-detail-modal-body">
            {/* Meta Grid */}
            <div className="defect-detail-meta-grid">
              <div className="meta-item">
                <span className="meta-label">Defect ID</span>
                <span className="meta-value mono-code">{selectedRecord.defectId || '—'}</span>
              </div>
              <div className="meta-item">
                <span className="meta-label">Defect Name</span>
                <span className="meta-value font-bold">{selectedRecord.defectName || '—'}</span>
              </div>
              <div className="meta-item">
                <span className="meta-label">Judge / Severity</span>
                <div className="meta-value" style={{ display: 'flex', gap: '6px' }}>
                  <span className={`preview-badge badge-${selectedRecord.judge?.toLowerCase() || 'default'}`}>
                    {selectedRecord.judge || '—'}
                  </span>
                  <span className={`preview-badge badge-${selectedRecord.severity?.toLowerCase() || 'default'}`}>
                    {selectedRecord.severity || '—'}
                  </span>
                </div>
              </div>
              <div className="meta-item">
                <span className="meta-label">Component</span>
                <span className="meta-value">{selectedRecord.componentName || '—'}</span>
              </div>
              <div className="meta-item">
                <span className="meta-label">Coordinates (X, Y)</span>
                <span className="meta-value mono-code">
                  X: {selectedRecord.coordinateX != null ? selectedRecord.coordinateX.toFixed(2) : '—'},
                  Y: {selectedRecord.coordinateY != null ? selectedRecord.coordinateY.toFixed(2) : '—'}
                </span>
              </div>
              <div className="meta-item">
                <span className="meta-label">Confidence Score</span>
                <span className="meta-value font-bold">
                  {selectedRecord.confidenceScore != null
                    ? `${(selectedRecord.confidenceScore * 100).toFixed(2)}%`
                    : '—'}
                </span>
              </div>
              <div className="meta-item">
                <span className="meta-label">Inspected Time</span>
                <span className="meta-value">
                  {selectedRecord.inspectedAtUtc ? formatDate(selectedRecord.inspectedAtUtc) : '—'}
                </span>
              </div>
              <div className="meta-item">
                <span className="meta-label">Key ID / Product</span>
                <span className="meta-value mono-code">{selectedRecord.keyId || '—'}</span>
              </div>
            </div>

            {/* Direct Links / Stream triggers */}
            <div className="defect-detail-links">
              {Boolean(
                selectedRecord.defectId &&
                  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
                    selectedRecord.defectId
                  )
              ) && (
                <>
                  <a
                    href={storageApi.defectFileUrl(selectedRecord.defectId!, 'feature')}
                    target="_blank"
                    rel="noreferrer"
                    className="compact-button secondary"
                  >
                    ⚡ Stream Raw Feature File
                  </a>
                  <a
                    href={storageApi.defectFileUrl(selectedRecord.defectId!, 'image')}
                    target="_blank"
                    rel="noreferrer"
                    className="compact-button secondary"
                  >
                    🖼️ View Defect Image
                  </a>
                </>
              )}
              {Boolean(
                selectedRecord.rawImageUri &&
                  (selectedRecord.rawImageUri.startsWith('http://') ||
                    selectedRecord.rawImageUri.startsWith('https://'))
              ) && (
                <a
                  href={selectedRecord.rawImageUri!}
                  target="_blank"
                  rel="noreferrer"
                  className="compact-button secondary"
                >
                  🖼️ View Raw Image Link
                </a>
              )}
              {Boolean(
                selectedRecord.rawFeatureUri &&
                  (selectedRecord.rawFeatureUri.startsWith('http://') ||
                    selectedRecord.rawFeatureUri.startsWith('https://'))
              ) && (
                <a
                  href={selectedRecord.rawFeatureUri!}
                  target="_blank"
                  rel="noreferrer"
                  className="compact-button secondary"
                >
                  ⚡ Open Raw Feature URL
                </a>
              )}
              {selectedRecord.rawFeatureUri &&
                !selectedRecord.rawFeatureUri.startsWith('http://') &&
                !selectedRecord.rawFeatureUri.startsWith('https://') && (
                  <span className="raw-uri-note">
                    Raw URI: <code className="mono-code">{selectedRecord.rawFeatureUri}</code>
                  </span>
                )}
            </div>

            {/* Feature JSON Snippet */}
            <div className="snippet-section">
              <div className="snippet-header">
                <span className="snippet-title">Feature JSON Snippet</span>
                {formattedSnippet && (
                  <button
                    type="button"
                    className="compact-button ghost"
                    onClick={copySnippetToClipboard}
                  >
                    {copySuccess ? '✓ Copied!' : '📋 Copy JSON'}
                  </button>
                )}
              </div>
              <pre className="snippet-pre mono-code">
                {formattedSnippet || 'No feature JSON snippet recorded for this defect.'}
              </pre>
            </div>

            <div className="actions" style={{ justifyContent: 'flex-end', marginTop: 12 }}>
              <button
                type="button"
                className="compact-button secondary"
                onClick={() => setInspectorOpen(false)}
              >
                Close Details
              </button>
            </div>
          </div>
        </Modal>
      )}

      {showAnalyticsModal && (
        <Suspense fallback={null}>
          <DefectFeatureAnalyticsDashboard
            sessionId={sessionId}
            initialTab={analyticsInitialTab}
            onClose={() => setShowAnalyticsModal(false)}
            onSelectRowIndex={(rowIndex) => {
              console.debug(`[DrillDown Diagnostic] targetRowIndex=${rowIndex}, pageSize=${pageSize}, current page=${page}`)
              const targetPage = Math.floor((rowIndex - 1) / pageSize) + 1

              // If active sorting or filtering is applied, natural rowIndex-to-page mapping
              // is misleading because the server-side query orders/filters rows differently.
              // Reset sort and filters to default so the target page accurately displays the row.
              const hasActiveFilterOrCustomSort =
                judgeFilter !== 'ALL' ||
                severityFilter !== 'ALL' ||
                Boolean(defectTableFilter) ||
                Boolean(debouncedKeyword) ||
                sorting.some((s) => s.id !== 'rowIndex' || s.desc)

              if (hasActiveFilterOrCustomSort) {
                setJudgeFilter('ALL')
                setSeverityFilter('ALL')
                setDefectTableFilter('')
                setSearchInput('')
                setDebouncedKeyword('')
                setSorting([{ id: 'rowIndex', desc: false }])
              }

              if (page !== targetPage) {
                setPage(targetPage)
              }
              setHighlightedRowIndex(rowIndex)
              setShowAnalyticsModal(false)
              onNotice?.({
                tone: 'info',
                message: hasActiveFilterOrCustomSort
                  ? `Navigated to defect record at row #${rowIndex} (Page ${targetPage}, filters/sort reset to natural sequence).`
                  : `Navigated to defect record at row #${rowIndex} (Page ${targetPage}).`,
              })
            }}
          />
        </Suspense>
      )}
    </section>
  )
}
