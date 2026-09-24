import { cloneElement, isValidElement, useEffect, useId, useMemo, useRef, useState, type ReactElement, type ReactNode } from 'react'
import type { DefectRecord, Notice } from './types'
import { fetchDefectFeatureContent, storageApi, imageCrawlerApi } from './api'

export function Icon({ name, className = 'icon' }: { name: string; className?: string }) {
  const paths: Record<string, string> = {
    overview: 'M4 13h6V4H4zm10 7h6V4h-6zM4 20h6v-5H4zm10-9h6V4h-6z',
    dashboard: 'M3 3h7v9H3zm11 0h7v5h-7zm0 9h7v9h-7zM3 16h7v5H3z',
    operations: 'M13 10V3L4 14h7v7l9-11h-7z',
    analysis: 'M9 19v-6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm0 0V9a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v10m-6 0a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2m0 0V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-2a2 2 0 0 1-2-2z',
    settings: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zm7.4 1.5-.9 1.6-1.8-.4a7 7 0 0 1-1.3.8l-.3 1.8h-1.8l-.3-1.8a7 7 0 0 1-1.3-.8l-1.8.4-.9-1.6 1.5-1.1a7 7 0 0 1 0-1.6l-1.5-1.1.9-1.6 1.8.4a7 7 0 0 1 1.3-.8l.3-1.8h1.8l.3 1.8a7 7 0 0 1 1.3.8l1.8-.4.9 1.6-1.5 1.1a7 7 0 0 1 0 1.6l1.5 1.1z',
    import: 'M12 3v12m0 0-4-4m4 4 4-4M5 21h14',
    scans: 'M4 6h16M4 12h10M4 18h7M17 15l2 2 4-4',
    products: 'M4 7h16v12H4zM8 7V5h8v2',
    overkill: 'M12 3l8 4v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V7z',
    leakage: 'M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5',
    queries: 'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01',
    history: 'M3 12a9 9 0 1 0 9-9M12 7v5l3 2',
    sources: 'M4 7a4 4 0 0 1 4-4h8a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4z',
    storage: 'M3 7h18M3 12h18M3 17h18M7 7v10',
    rules: 'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2M9 5a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2M9 5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2m-6 9 2 2 4-4',
    audit: 'M12 8v4l3 2M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z',
    close: 'M6 6l12 12M18 6 6 18',
    plus: 'M12 5v14M5 12h14',
    search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16zm10 2-4.35-4.35',
  }
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true">
      <path
        d={paths[name] ?? paths.close}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

export function Field({
  label,
  children,
  hint,
  required,
  className,
}: {
  label: string
  children: ReactNode
  hint?: string
  required?: boolean
  className?: string
}) {
  const id = useId()
  const control = isValidElement(children)
    ? cloneElement(children as ReactElement<{ id?: string }>, { id })
    : children
  return (
    <label className={`field${className ? ` ${className}` : ''}`}>
      <span>
        {label}
        {required ? <abbr title="Required">*</abbr> : null}
      </span>
      {control}
      {hint ? <small className="field-hint">{hint}</small> : null}
    </label>
  )
}

export function MetricCard({
  label,
  value,
  tone = 'neutral',
  hint,
}: {
  label: string
  value: string | number
  tone?: 'neutral' | 'success' | 'danger' | 'warning' | 'info'
  hint?: string
}) {
  return (
    <article className={`metric-card ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      {hint ? <small>{hint}</small> : null}
    </article>
  )
}

export function StatusPill({ value }: { value: string }) {
  const tone = value.toLowerCase()
  const className =
    tone.includes('healthy') || tone.includes('pass') || tone.includes('ok') || tone.includes('success')
      ? 'healthy'
      : tone.includes('warn') || tone.includes('review') || tone.includes('overkill')
        ? 'warning'
        : tone.includes('fail') || tone.includes('unhealthy') || tone.includes('ng') || tone.includes('leak')
          ? 'unhealthy'
          : 'unknown'
  return <span className={`health-pill ${className}`}>{value}</span>
}

export function JudgeBadge({ value }: { value: string }) {
  const tone = value.toLowerCase()
  const className = tone.includes('ok') || tone === 'pass' ? 'ok' : tone.includes('ng') || tone === 'fail' ? 'ng' : 'review'
  return <span className={`judge-badge ${className}`}>{value}</span>
}

export function DataTable({
  title,
  eyebrow,
  columns,
  rows,
  empty = 'No records.',
  actions,
  selectedRow,
  onRowClick,
}: {
  title?: string
  eyebrow?: string
  columns: string[]
  rows: { id: string; cells: ReactNode[]; selected?: boolean }[]
  empty?: string
  actions?: ReactNode
  selectedRow?: string
  onRowClick?: (id: string) => void
}) {
  return (
    <section className="panel table-panel">
      {(title || actions) && (
        <div className="section-heading">
          <div>
            {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
            {title ? <h2>{title}</h2> : null}
          </div>
          {actions}
        </div>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {columns.map((column) => (
                <th key={column}>{column}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="empty-cell">
                  {empty}
                </td>
              </tr>
            ) : (
              rows.map((row) => (
                <tr
                  key={row.id}
                  className={row.selected || selectedRow === row.id ? 'selected-row' : undefined}
                  onClick={onRowClick ? () => onRowClick(row.id) : undefined}
                >
                  {row.cells.map((cell, index) => (
                    <td key={`${row.id}-${index}`}>{cell}</td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}

export function Pagination({
  page,
  totalPages,
  totalCount,
  onPage,
}: {
  page: number
  totalPages: number
  totalCount: number
  onPage: (page: number) => void
}) {
  const safeTotalPages = Math.max(totalPages, 1)
  const [jumpPage, setJumpPage] = useState<string>(String(page))

  useEffect(() => {
    setJumpPage(String(page))
  }, [page])

  function handleJump(e?: React.FormEvent) {
    if (e) e.preventDefault()
    const target = parseInt(jumpPage.trim(), 10)
    if (!isNaN(target)) {
      const clamped = Math.max(1, Math.min(target, safeTotalPages))
      setJumpPage(String(clamped))
      if (clamped !== page) {
        onPage(clamped)
      }
    } else {
      setJumpPage(String(page))
    }
  }

  return (
    <div
      className="pagination"
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        gap: '8px',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '10px 4px',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
        <span>
          Page <strong>{page}</strong> of <strong>{safeTotalPages}</strong> &bull;{' '}
          <strong>{totalCount.toLocaleString()}</strong> records
        </span>
        <span
          className="tag-badge"
          style={{
            background: '#eff6ff',
            color: '#1d4ed8',
            borderColor: '#bfdbfe',
            fontSize: '10px',
            fontWeight: 600,
          }}
          title="Latest data always displayed on first page"
        >
          ⚡ Latest First
        </span>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
        <button
          type="button"
          className="ghost-button compact"
          style={{ fontSize: '11px', fontWeight: page === 1 ? 700 : 400 }}
          disabled={page <= 1}
          onClick={() => onPage(1)}
          title="Go to first page (Latest data)"
        >
          « First (Page 1)
        </button>

        <button
          type="button"
          className="ghost-button compact"
          disabled={page <= 1}
          onClick={() => onPage(page - 1)}
          title="Previous page"
        >
          ‹ Prev
        </button>

        {/* Page jump input */}
        <form onSubmit={handleJump} style={{ display: 'inline-flex', alignItems: 'center', gap: '4px' }}>
          <span style={{ fontSize: '11px', color: '#52627a' }}>Page:</span>
          <input
            type="number"
            min={1}
            max={safeTotalPages}
            value={jumpPage}
            onChange={(e) => setJumpPage(e.target.value)}
            onBlur={() => handleJump()}
            style={{
              width: '56px',
              padding: '2px 6px',
              textAlign: 'center',
              fontSize: '11px',
              fontFamily: 'monospace',
              fontWeight: 600,
              borderRadius: '4px',
              border: '1px solid #cbd5e1',
              background: '#ffffff',
              color: '#0f172a',
              boxShadow: 'inset 0 1px 2px rgba(15, 23, 42, 0.06)',
            }}
            title={`Enter page number (1 - ${safeTotalPages}) and press Enter`}
          />
          <button
            type="submit"
            className="secondary compact"
            style={{
              fontSize: '10px',
              padding: '2px 8px',
              fontWeight: 700,
              background: '#eff6ff',
              color: '#1d4ed8',
              borderColor: '#bfdbfe',
            }}
            title="Go to entered page"
          >
            Go
          </button>
        </form>

        <button
          type="button"
          className="ghost-button compact"
          disabled={page >= safeTotalPages}
          onClick={() => onPage(page + 1)}
          title="Next page"
        >
          Next ›
        </button>

        <button
          type="button"
          className="ghost-button compact"
          disabled={page >= safeTotalPages}
          onClick={() => onPage(safeTotalPages)}
          title="Go to last page"
        >
          Last »
        </button>
      </div>
    </div>
  )
}

export function Modal({
  title,
  description,
  open,
  onClose,
  children,
  wide,
}: {
  title: string
  description?: string
  open: boolean
  onClose: () => void
  children: ReactNode
  wide?: boolean
}) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose

  useEffect(() => {
    if (!open) return
    const previous = document.activeElement as HTMLElement | null
    const node = dialogRef.current

    // Lock background scroll when modal is open
    const originalOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    // Only focus the first focusable element when opening if focus is currently outside dialog
    if (node && (!document.activeElement || !node.contains(document.activeElement))) {
      const focusable = node.querySelector<HTMLElement>('input, select, textarea, button')
      focusable?.focus()
    }

    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape') onCloseRef.current()
      if (event.key !== 'Tab' || !node) return
      const items = [...node.querySelectorAll<HTMLElement>('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')].filter(
        (el) => !el.hasAttribute('disabled'),
      )
      if (items.length === 0) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => {
      document.body.style.overflow = originalOverflow
      window.removeEventListener('keydown', onKey)
      previous?.focus()
    }
  }, [open])

  if (!open) return null

  return (
    <div className="modal-scrim" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <div
        ref={dialogRef}
        className={`modal-card ${wide ? 'wide' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
      >
        <header className="modal-header">
          <div>
            <h2 id={titleId}>{title}</h2>
            {description ? <p className="muted">{description}</p> : null}
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close dialog">
            <Icon name="close" />
          </button>
        </header>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  )
}

export function NoticeBanner({ notice }: { notice: Notice | null }) {
  if (!notice) return null
  return <div className={`notice ${notice.tone}`} role="status">{notice.message}</div>
}

export function ImageViewer({
  src,
  alt = 'Visual Inspection',
  maxHeight = '520px',
  onSave,
}: {
  src?: string | null
  alt?: string
  maxHeight?: string
  onSave?: () => void
}) {
  const [scale, setScale] = useState(1)
  const [position, setPosition] = useState({ x: 0, y: 0 })
  const [isDragging, setIsDragging] = useState(false)
  const [dragStart, setDragStart] = useState({ x: 0, y: 0 })
  const containerRef = useRef<HTMLDivElement>(null)

  // Reset transform when src changes
  useEffect(() => {
    setScale(1)
    setPosition({ x: 0, y: 0 })
  }, [src])

  useEffect(() => {
    const el = containerRef.current
    if (!el) return

    function onNativeWheel(e: WheelEvent) {
      e.preventDefault()
      e.stopPropagation()
      if (e.deltaY < 0) {
        setScale((prev) => Math.min(prev + 0.25, 5))
      } else {
        setScale((prev) => {
          const next = Math.max(prev - 0.25, 0.5)
          if (next <= 1) setPosition({ x: 0, y: 0 })
          return next
        })
      }
    }

    el.addEventListener('wheel', onNativeWheel, { passive: false })
    return () => {
      el.removeEventListener('wheel', onNativeWheel)
    }
  }, [])

  function handleZoomIn() {
    setScale((prev) => Math.min(prev + 0.25, 5))
  }

  function handleZoomOut() {
    setScale((prev) => {
      const next = Math.max(prev - 0.25, 0.5)
      if (next <= 1) {
        setPosition({ x: 0, y: 0 })
      }
      return next
    })
  }

  function handleReset() {
    setScale(1)
    setPosition({ x: 0, y: 0 })
  }

  function handleMouseDown(e: React.MouseEvent) {
    if (e.button !== 0) return // Left click only
    setIsDragging(true)
    setDragStart({ x: e.clientX - position.x, y: e.clientY - position.y })
  }

  function handleMouseMove(e: React.MouseEvent) {
    if (!isDragging) return
    setPosition({
      x: e.clientX - dragStart.x,
      y: e.clientY - dragStart.y,
    })
  }

  function handleMouseUp() {
    setIsDragging(false)
  }

  async function handleSaveImage() {
    if (!src) return
    if (onSave) {
      onSave()
      return
    }
    try {
      // If data URL or direct link, trigger clean download
      const fileName = alt.replace(/[^a-zA-Z0-9_-]/g, '_') + '.jpg'
      const response = await fetch(src)
      const blob = await response.blob()
      const blobUrl = window.URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = blobUrl
      link.download = fileName
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      window.URL.revokeObjectURL(blobUrl)
    } catch {
      // Fallback
      window.open(src, '_blank')
    }
  }

  if (!src) {
    return (
      <div
        style={{
          background: '#020617',
          borderRadius: '8px',
          padding: '24px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: '280px',
          color: '#64748b',
        }}
      >
        Original image file not found or not yet crawled.
      </div>
    )
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        borderRadius: '8px',
        overflow: 'hidden',
        border: '1px solid rgba(255,255,255,0.1)',
        background: '#020617',
      }}
    >
      {/* Viewer Toolbar: Zoom / Pan Controls / Save Image */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          padding: '8px 12px',
          background: 'rgba(255,255,255,0.04)',
          borderBottom: '1px solid rgba(255,255,255,0.08)',
          flexWrap: 'wrap',
          gap: '8px',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
          <button
            type="button"
            className="secondary compact"
            onClick={handleZoomIn}
            title="Zoom In"
            style={{ fontSize: '12px', fontWeight: 700, padding: '3px 9px' }}
          >
            🔍+
          </button>
          <button
            type="button"
            className="secondary compact"
            onClick={handleZoomOut}
            title="Zoom Out"
            style={{ fontSize: '12px', fontWeight: 700, padding: '3px 9px' }}
          >
            🔍-
          </button>
          <button
            type="button"
            className="ghost-button compact"
            onClick={handleReset}
            title="Reset Zoom & Position"
            style={{ fontSize: '11px', padding: '3px 8px' }}
          >
            ↺ 100%
          </button>
          <span style={{ fontSize: '11px', color: '#94a3b8', marginLeft: '4px', fontFamily: 'monospace' }}>
            {Math.round(scale * 100)}%
          </span>
          <span style={{ fontSize: '10px', color: '#64748b', marginLeft: '8px' }}>
            (Scroll to Zoom · Click & Drag to Pan)
          </span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <button
            type="button"
            className="primary compact"
            onClick={() => void handleSaveImage()}
            title="Save image to computer"
            style={{ background: '#0284c7', fontSize: '11px', padding: '4px 12px', fontWeight: 600 }}
          >
            💾 Save Image (.jpg)
          </button>
          <a
            href={src}
            target="_blank"
            rel="noreferrer"
            className="ghost-button compact"
            title="Open image in new tab"
            style={{ fontSize: '11px', padding: '3px 8px' }}
          >
            ↗ New Tab
          </a>
        </div>
      </div>

      {/* Interactive Canvas */}
      <div
        ref={containerRef}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        style={{
          height: maxHeight,
          width: '100%',
          overflow: 'hidden',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          position: 'relative',
          cursor: isDragging ? 'grabbing' : scale > 1 ? 'grab' : 'default',
          userSelect: 'none',
          touchAction: 'none',
          overscrollBehavior: 'contain',
        }}
      >
        <img
          src={src}
          alt={alt}
          draggable={false}
          style={{
            maxWidth: '100%',
            maxHeight: '100%',
            objectFit: 'contain',
            transform: `translate(${position.x}px, ${position.y}px) scale(${scale})`,
            transition: isDragging ? 'none' : 'transform 0.1s ease-out',
            transformOrigin: 'center center',
          }}
        />
      </div>
    </div>
  )
}

export function EmptyState({ title, body, action }: { title: string; body: string; action?: ReactNode }) {
  return (
    <div className="empty-state">
      <h3>{title}</h3>
      <p>{body}</p>
      {action}
    </div>
  )
}

export function extractOriginalDefectVp(defect: DefectRecord): string {
  const path = defect.imageUri || defect.imageUrl || ''
  if (path) {
    const folderMatch = path.match(/[\\/]VP0*(\d+)[\\/]/i)
    if (folderMatch && folderMatch[1]) {
      const val = folderMatch[1].replace(/^0+/, '')
      if (val) return val
    }
    const fileMatch = path.match(/VP0*(\d+)[_.-]/i)
    if (fileMatch && fileMatch[1]) {
      const val = fileMatch[1].replace(/^0+/, '')
      if (val) return val
    }
  }

  return '0'
}

export function DefectFeatureInspector({ defect, initialVp }: { defect: DefectRecord; initialVp?: string }) {
  const [viewMode, setViewMode] = useState<'structured' | 'raw'>('structured')
  const [activeTab, setActiveTab] = useState<'feature' | 'customFeature'>('feature')
  const [remoteFeature, setRemoteFeature] = useState<Record<string, unknown> | null>(null)
  const [remoteCustomFeature, setRemoteCustomFeature] = useState<Record<string, unknown> | null>(null)
  const [loadingRemote, setLoadingRemote] = useState(false)
  const [remoteError, setRemoteError] = useState<string | null>(null)

  const parsedData = useMemo(() => {
    function tryParse(str?: string | null): Record<string, unknown> | null {
      if (!str || !str.trim()) return null
      try {
        const val = JSON.parse(str)
        return typeof val === 'object' && val !== null && !Array.isArray(val) ? (val as Record<string, unknown>) : null
      } catch {
        return null
      }
    }
    return {
      feature: remoteFeature ?? tryParse(defect.featureJson),
      customFeature: remoteCustomFeature ?? tryParse(defect.customFeatureJson),
    }
  }, [defect.featureJson, defect.customFeatureJson, remoteFeature, remoteCustomFeature])

  const availableTabs = useMemo(() => {
    const list: Array<{ id: 'feature' | 'customFeature'; label: string }> = []
    if (defect.featureJson || defect.featureUrl) list.push({ id: 'feature', label: 'Feature JSON (mcl_defect)' })
    if (defect.customFeatureJson || defect.customFeatureUrl) list.push({ id: 'customFeature', label: 'Custom Feature JSON' })
    return list
  }, [defect.featureJson, defect.featureUrl, defect.customFeatureJson, defect.customFeatureUrl])

  useEffect(() => {
    if (availableTabs.length > 0 && !availableTabs.some((t) => t.id === activeTab)) {
      setActiveTab(availableTabs[0].id)
    }
  }, [availableTabs, activeTab])

  const currentJsonText =
    activeTab === 'feature'
      ? (defect.featureJson || (remoteFeature ? JSON.stringify(remoteFeature, null, 2) : null))
      : (defect.customFeatureJson || (remoteCustomFeature ? JSON.stringify(remoteCustomFeature, null, 2) : null))

  const currentParsed = parsedData[activeTab]
  const currentRemoteUrl = activeTab === 'feature' ? defect.featureUrl : defect.customFeatureUrl

  async function handleLoadRemoteContent() {
    setLoadingRemote(true)
    setRemoteError(null)
    try {
      const type = activeTab === 'feature' ? 'feature' : 'custom'
      const content = await fetchDefectFeatureContent(defect.id, type)
      if (activeTab === 'feature') {
        setRemoteFeature(content)
      } else {
        setRemoteCustomFeature(content)
      }
    } catch (err) {
      setRemoteError(err instanceof Error ? err.message : 'Failed to retrieve remote feature JSON.')
    } finally {
      setLoadingRemote(false)
    }
  }

  const metrics = useMemo(() => {
    const src = parsedData.feature || parsedData.customFeature || {}
    const getVal = (key: string): unknown => src[key] ?? null
    return {
      realX: getVal('G001_RealX') ?? defect.coordinateX ?? null,
      realY: getVal('G002_RealY') ?? defect.coordinateY ?? null,
      realWidth: getVal('G013_RealWidth') ?? null,
      realHeight: getVal('G014_RealHeight') ?? null,
      pixelX: getVal('G005_PxlX') ?? null,
      pixelY: getVal('G006_PxlY') ?? null,
      pixelWidth: getVal('G009_PxlWidth') ?? null,
      pixelHeight: getVal('G010_PxlHeight') ?? null,
      area: getVal('G017_Area') ?? null,
    }
  }, [parsedData, defect.coordinateX, defect.coordinateY])

  const originalVp = useMemo(() => extractOriginalDefectVp(defect), [defect])
  const [selectedVp, setSelectedVp] = useState<string>(() => initialVp || '0')
  const [vpImages, setVpImages] = useState<Record<string, string>>(() => ({
    '0': defect.imageUrl || (defect.imageUri ? imageCrawlerApi.previewUrl(defect.id, false, '0') : ''),
  }))
  const [loadingVp, setLoadingVp] = useState<string | null>(null)
  const [crawling, setCrawling] = useState(false)
  const [crawlNotice, setCrawlNotice] = useState<string | null>(null)
  const [imageZoomed, setImageZoomed] = useState(false)

  const currentImgSrc = vpImages[selectedVp] || ''

  useEffect(() => {
    if (initialVp && initialVp !== '0' && !vpImages[initialVp]) {
      if (initialVp === 'original') {
        void handleSelectOriginalVp()
      } else {
        void handleSelectVp(initialVp)
      }
    }
  }, [initialVp, originalVp])

  async function handleSelectVp(vp: string) {
    setSelectedVp(vp)
    setCrawlNotice(null)

    // VP0 is default; if already cached or has URL, don't re-crawl unless empty
    if (vpImages[vp]) {
      return
    }

    // Lazy load on click: fetch and preview requested VP
    setLoadingVp(vp)
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: defect.id, customVp: vp, forceRefresh: false })
      if (res.success) {
        const newUrl = res.imageUrl || imageCrawlerApi.previewUrl(defect.id, false, vp)
        setVpImages((prev) => ({ ...prev, [vp]: newUrl }))
        setCrawlNotice(`✓ Loaded VP${vp} (${res.sizeBytes.toLocaleString()} bytes in ${res.latencyMs}ms)`)
      } else {
        setCrawlNotice(`✗ VP${vp} load failed: ${res.errorMessage || 'No image found'}`)
      }
    } catch (err) {
      setCrawlNotice(`✗ Error loading VP${vp}: ${err instanceof Error ? err.message : 'Failed to fetch image'}`)
    } finally {
      setLoadingVp(null)
    }
  }

  async function handleSelectOriginalVp() {
    setSelectedVp('original')
    setCrawlNotice(null)
    if (vpImages.original) return
    setLoadingVp('original')
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: defect.id, customVp: 'original', forceRefresh: false })
      if (res.success) {
        const newUrl = res.imageUrl || imageCrawlerApi.previewUrl(defect.id, false, 'original')
        setVpImages((prev) => ({ ...prev, original: newUrl }))
      } else {
        setCrawlNotice(`Failed to load original image in ZIP: ${res.errorMessage || 'Unknown error'}`)
      }
    } catch (err) {
      setCrawlNotice(err instanceof Error ? err.message : 'Failed to load original image in ZIP')
    } finally {
      setLoadingVp(null)
    }
  }

  async function handleForceRefreshVp() {
    setCrawling(true)
    setCrawlNotice(null)
    try {
      const requestVp = selectedVp === 'original' ? 'original' : selectedVp
      const res = await imageCrawlerApi.crawlDefect({ defectId: defect.id, customVp: requestVp, forceRefresh: true })
      if (res.success) {
        const newUrl = `${imageCrawlerApi.previewUrl(defect.id, true, requestVp)}&t=${Date.now()}`
        setVpImages((prev) => ({ ...prev, [selectedVp]: newUrl }))
        setCrawlNotice(`✓ Force refreshed ${selectedVp === 'original' ? `path original (VP${originalVp})` : `VP${selectedVp}`} (${res.sizeBytes.toLocaleString()} bytes in ${res.latencyMs}ms)!`)
      } else {
        setCrawlNotice(`✗ Crawl failed: ${res.errorMessage || 'Unknown error'}`)
      }
    } catch (err) {
      setCrawlNotice(`✗ Error: ${err instanceof Error ? err.message : 'Failed to crawl image'}`)
    } finally {
      setCrawling(false)
    }
  }

  return (
    <div className="defect-feature-inspector" style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
      {/* 1. Defect & Spec Context Card */}
      <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '10px 14px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <strong style={{ fontSize: '15px', color: '#0f172a' }}>{defect.defectName}</strong>
            {defect.judge && <JudgeBadge value={defect.judge} />}
            {defect.severity && <StatusPill value={defect.severity} />}
            {defect.defectTable && <span className="tag-badge">Table: {defect.defectTable}</span>}
            {defect.modelCode && (
              <span className="tag-badge" style={{ background: '#f0fdf4', color: '#166534', borderColor: '#bbf7d0', fontWeight: 600 }}>
                Model: {defect.modelCode}
              </span>
            )}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
            {defect.priority != null && (
              <span className="clean-pill" style={{ background: '#fef3c7', color: '#92400e', fontWeight: 600 }}>
                Priority: {defect.priority}
              </span>
            )}
            {defect.featureUrl?.includes('#') ? (
              <span className="tag-badge" style={{ background: '#f5f3ff', color: '#6d28d9', borderColor: '#ddd6fe', fontWeight: 600 }}>
                📦 Unified Defect Pack
              </span>
            ) : (defect.featureUrl || defect.imageUrl) ? (
              <span className="tag-badge" style={{ background: '#e0f2fe', color: '#0369a1', borderColor: '#bae6fd', fontWeight: 600 }}>
                🌊 SeaweedFS Offloaded
              </span>
            ) : null}
          </div>
        </div>

        {/* Defect Thumbnail & Multi-VP Image Crawler Section */}
        {(defect.imageUri || defect.imageUrl) && (
          <div style={{ background: '#ffffff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '12px', marginTop: '10px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
            {/* VP Navigation Bar: VP0 (Default) + Path Original + VP 1, 2, 3, 4 */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '8px', borderBottom: '1px solid #e2e8f0', paddingBottom: '8px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
                <span style={{ fontSize: '11px', fontWeight: 700, color: '#475569', marginRight: '2px' }}>Camera Angle:</span>

                {/* 🎯 Real VP Button */}
                <button
                  type="button"
                  disabled={loadingVp === 'original'}
                  onClick={() => void handleSelectOriginalVp()}
                  style={{
                    padding: '3px 12px',
                    fontSize: '11px',
                    fontWeight: 700,
                    borderRadius: '6px',
                    cursor: 'pointer',
                    border: selectedVp === 'original' ? '1.5px solid #7c3aed' : '1px solid #c4b5fd',
                    background: selectedVp === 'original' ? '#7c3aed' : '#f5f3ff',
                    color: selectedVp === 'original' ? '#ffffff' : '#6d28d9',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '5px',
                    boxShadow: selectedVp === 'original' ? '0 1px 3px rgba(124, 58, 237, 0.3)' : 'none',
                    transition: 'all 0.15s ease',
                  }}
                  title={`Load original VP from image_path (VP${originalVp})`}
                >
                  <span>Load Path Original (VP{originalVp})</span>
                  {loadingVp === 'original' ? (
                    <span style={{ fontSize: '10px' }}>⌛</span>
                  ) : vpImages.original ? (
                    <span style={{ width: '6px', height: '6px', borderRadius: '50%', background: selectedVp === 'original' ? '#4ade80' : '#16a34a', display: 'inline-block' }} title="Image loaded" />
                  ) : (
                    <span style={{ fontSize: '10px' }}>⚡</span>
                  )}
                </button>

                <span style={{ width: '1px', height: '18px', background: '#cbd5e1', margin: '0 2px' }} />

                {/* Individual VPs */}
                {['0', '1', '2', '3', '4'].map((vp) => {
                  const isSelected = selectedVp === vp
                  const isLoaded = Boolean(vpImages[vp])
                  const isLoading = loadingVp === vp
                  const isOriginal = vp === originalVp
                  return (
                    <button
                      key={vp}
                      type="button"
                      disabled={isLoading}
                      onClick={() => void handleSelectVp(vp)}
                      style={{
                        padding: '3px 8px',
                        fontSize: '11px',
                        fontWeight: isSelected ? 700 : 500,
                        borderRadius: '6px',
                        cursor: 'pointer',
                        border: isSelected ? '1.5px solid #2563eb' : isOriginal ? '1px dashed #a78bfa' : '1px solid #cbd5e1',
                        background: isSelected ? '#eff6ff' : isOriginal ? '#faf5ff' : '#f8fafc',
                        color: isSelected ? '#1d4ed8' : isOriginal ? '#7c3aed' : '#475569',
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '4px',
                        transition: 'all 0.15s ease',
                      }}
                      title={vp === '0' ? 'VP0 (Thumbnail - Default)' : isOriginal ? `VP${vp}: Browse image at same angle as image_path` : `VP${vp}: Angle browse image, click to load`}
                    >
                      <span>{vp === '0' ? 'VP0 (Default)' : isOriginal ? `VP${vp} (Path Angle)` : `VP${vp}`}</span>
                      {isLoading ? (
                        <span style={{ fontSize: '9px', color: '#2563eb' }}>⌛</span>
                      ) : isLoaded ? (
                        <span style={{ width: '6px', height: '6px', borderRadius: '50%', background: '#16a34a', display: 'inline-block' }} title="Image loaded" />
                      ) : (
                        <span style={{ fontSize: '10px', color: '#94a3b8' }}>⚡</span>
                      )}
                    </button>
                  )
                })}
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <button
                  type="button"
                  className="secondary compact"
                  style={{ fontSize: '11px', padding: '3px 8px', background: '#f1f5f9', borderColor: '#cbd5e1', color: '#334155' }}
                  disabled={crawling || loadingVp !== null}
                  onClick={() => void handleForceRefreshVp()}
                  title="Re-crawl image from PC API for selected VP"
                >
                  {crawling ? 'Crawling...' : `⚡ Force Crawl VP${selectedVp}`}
                </button>
              </div>
            </div>

            {/* Image Preview & Details Row */}
            <div style={{ display: 'flex', gap: '14px', alignItems: 'center', flexWrap: 'wrap' }}>
              <div
                style={{
                  width: '130px',
                  height: '105px',
                  background: '#090d16',
                  borderRadius: '6px',
                  overflow: 'hidden',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  border: '1px solid #334155',
                  flexShrink: 0,
                  position: 'relative',
                  cursor: currentImgSrc ? 'pointer' : 'default',
                }}
                onClick={() => currentImgSrc && setImageZoomed(true)}
                title={currentImgSrc ? 'Click to enlarge image' : 'No image available'}
              >
                {loadingVp === selectedVp ? (
                  <div style={{ color: '#60a5fa', fontSize: '11px', textAlign: 'center', padding: '6px' }}>
                    <div>⌛ Loading...</div>
                    <div style={{ fontSize: '9px', color: '#94a3b8' }}>VP{selectedVp} from PC</div>
                  </div>
                ) : currentImgSrc ? (
                  <>
                    <img
                      src={currentImgSrc}
                      alt={`${defect.defectName} (VP${selectedVp})`}
                      style={{ width: '100%', height: '100%', objectFit: 'contain' }}
                      onError={(e) => {
                        e.currentTarget.style.display = 'none'
                      }}
                    />
                    <span style={{ position: 'absolute', bottom: '2px', right: '4px', fontSize: '9px', fontWeight: 700, color: '#fff', background: 'rgba(0,0,0,0.6)', padding: '1px 4px', borderRadius: '3px' }}>
                      VP{selectedVp}
                    </span>
                  </>
                ) : (
                  <div style={{ color: '#64748b', textAlign: 'center', fontSize: '10px', padding: '4px' }}>
                    <div>VP{selectedVp} not loaded</div>
                    <div style={{ fontSize: '9px', color: '#3b82f6', marginTop: '2px' }}>Click to load</div>
                  </div>
                )}
              </div>

              <div style={{ flex: 1, minWidth: '220px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '6px' }}>
                  <span style={{ fontSize: '11px', fontWeight: 700, color: '#334155' }}>
                    🖼️ Image Path (PC) & Active Angle: <span style={{ color: '#2563eb' }}>VP{selectedVp}</span>
                  </span>
                  {currentImgSrc && (
                    <button
                      type="button"
                      className="compact"
                      style={{ fontSize: '11px', padding: '2px 6px', background: '#f8fafc', border: '1px solid #cbd5e1', cursor: 'pointer' }}
                      onClick={() => setImageZoomed(true)}
                    >
                      🔍 Enlarge Image
                    </button>
                  )}
                </div>
                <code style={{ display: 'block', margin: '4px 0', fontSize: '11px', color: '#1e293b', background: '#f1f5f9', padding: '4px 8px', borderRadius: '4px', wordBreak: 'break-all' }}>
                  {defect.imageUri || defect.imageUrl || 'No image_path recorded'}
                </code>
                {crawlNotice && (
                  <div style={{ fontSize: '11px', fontWeight: 600, color: crawlNotice.startsWith('✓') ? '#16a34a' : '#dc2626', marginTop: '4px' }}>
                    {crawlNotice}
                  </div>
                )}
              </div>
            </div>

            {imageZoomed && currentImgSrc && (
              <div
                style={{
                  position: 'fixed',
                  top: 0,
                  left: 0,
                  right: 0,
                  bottom: 0,
                  background: 'rgba(0,0,0,0.85)',
                  zIndex: 9999,
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  padding: '20px',
                }}
                onClick={() => setImageZoomed(false)}
              >
                <div style={{ marginBottom: '10px', color: '#fff', fontSize: '14px', fontWeight: 600 }}>
                  {defect.defectName} — Angle VP{selectedVp} (Click anywhere to close)
                </div>
                <img
                  src={currentImgSrc}
                  alt={`${defect.defectName} VP${selectedVp}`}
                  style={{ maxWidth: '90vw', maxHeight: '80vh', objectFit: 'contain', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.2)' }}
                />
              </div>
            )}
          </div>
        )}

        {/* Hyperlinks & Image Bar */}
        {(defect.featureUrl || defect.customFeatureUrl || defect.imageUrl) && (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '10px', marginTop: '8px', padding: '6px 10px', background: '#ffffff', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '11px' }}>
            {defect.featureUrl && (
              <div>
                <span className="muted">Feature SeaweedFS: </span>
                <a href={defect.featureUrl} target="_blank" rel="noreferrer" style={{ color: '#0284c7', textDecoration: 'underline' }}>
                  {defect.featureUrl.length > 50 ? defect.featureUrl.slice(0, 48) + '...' : defect.featureUrl}
                </a>
                <a
                  href={storageApi.defectFileUrl(defect.id, 'feature')}
                  target="_blank"
                  rel="noreferrer"
                  style={{ marginLeft: '6px', color: '#0369a1', fontWeight: 600, textDecoration: 'underline' }}
                  title="Stream & decompress on-demand"
                >
                  [Stream File]
                </a>
              </div>
            )}
            {defect.customFeatureUrl && (
              <div>
                <span className="muted">Custom Feature: </span>
                <a href={defect.customFeatureUrl} target="_blank" rel="noreferrer" style={{ color: '#0284c7', textDecoration: 'underline' }}>
                  {defect.customFeatureUrl.length > 50 ? defect.customFeatureUrl.slice(0, 48) + '...' : defect.customFeatureUrl}
                </a>
                <a
                  href={storageApi.defectFileUrl(defect.id, 'custom')}
                  target="_blank"
                  rel="noreferrer"
                  style={{ marginLeft: '6px', color: '#0369a1', fontWeight: 600, textDecoration: 'underline' }}
                  title="Stream & decompress on-demand"
                >
                  [Stream File]
                </a>
              </div>
            )}
            {defect.imageUrl && (
              <div>
                <span className="muted">Defect Image: </span>
                <a href={defect.imageUrl} target="_blank" rel="noreferrer" style={{ color: '#16a34a', textDecoration: 'underline', fontWeight: 600 }}>
                  🖼️ View Photo in SeaweedFS
                </a>
                <a
                  href={storageApi.defectImagesUrl(defect.id)}
                  target="_blank"
                  rel="noreferrer"
                  style={{ marginLeft: '6px', color: '#047857', fontWeight: 600, textDecoration: 'underline' }}
                  title="Stream image binary on-demand"
                >
                  [Stream Image]
                </a>
              </div>
            )}
            {defect.keyId && (
              <div>
                <span className="muted">Defect Pack: </span>
                <a
                  href={storageApi.productDefectPackUrl(defect.keyId)}
                  target="_blank"
                  rel="noreferrer"
                  style={{ color: '#7c3aed', fontWeight: 600, textDecoration: 'underline' }}
                  title="Stream & decompress unified defect pack for this Product ID"
                >
                  📦 [All Defects Pack JSON]
                </a>
              </div>
            )}
          </div>
        )}

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '14px', marginTop: '8px', fontSize: '12px' }}>
          {defect.reason && (
            <div>
              <span className="muted">Reason: </span>
              <strong style={{ color: '#dc2626' }}>{defect.reason}</strong>
            </div>
          )}
          {defect.specName && (
            <div>
              <span className="muted">Spec: </span>
              <strong>{defect.specName}</strong>
              {defect.specJudge && (
                <span style={{ marginLeft: '4px', fontWeight: 600, color: defect.specJudge === 'NG' ? '#dc2626' : '#16a34a' }}>
                  ({defect.specJudge})
                </span>
              )}
              {defect.specReason && <span style={{ marginLeft: '4px', color: '#64748b' }}>- {defect.specReason}</span>}
            </div>
          )}
          {defect.eqpName && (
            <div>
              <span className="muted">Equipment: </span>
              <strong>{defect.eqpName}</strong>
              {defect.emNo && <span style={{ marginLeft: '4px', color: '#64748b' }}>(Station/Op: {defect.emNo})</span>}
            </div>
          )}
          {defect.componentName && (
            <div>
              <span className="muted">Component: </span>
              <strong>{defect.componentName}</strong>
            </div>
          )}
        </div>
      </div>

      {/* 2. Key Coordinates & Dimension Metric Pills */}
      {(metrics.realX != null || metrics.realY != null || metrics.realWidth != null || metrics.pixelX != null) && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: '8px' }}>
          {metrics.realX != null && (
            <div className="metric-pill-box">
              <span className="metric-pill-label">G001 Real X</span>
              <span className="metric-pill-val">{String(metrics.realX)}</span>
            </div>
          )}
          {metrics.realY != null && (
            <div className="metric-pill-box">
              <span className="metric-pill-label">G002 Real Y</span>
              <span className="metric-pill-val">{String(metrics.realY)}</span>
            </div>
          )}
          {(metrics.realWidth != null || metrics.realHeight != null) && (
            <div className="metric-pill-box">
              <span className="metric-pill-label">Real W × H</span>
              <span className="metric-pill-val">
                {String(metrics.realWidth ?? '—')} × {String(metrics.realHeight ?? '—')}
              </span>
            </div>
          )}
          {(metrics.pixelX != null || metrics.pixelY != null) && (
            <div className="metric-pill-box">
              <span className="metric-pill-label">Pixel (X, Y)</span>
              <span className="metric-pill-val">
                ({String(metrics.pixelX ?? '—')}, {String(metrics.pixelY ?? '—')})
              </span>
            </div>
          )}
          {(metrics.pixelWidth != null || metrics.pixelHeight != null) && (
            <div className="metric-pill-box">
              <span className="metric-pill-label">Pixel W × H</span>
              <span className="metric-pill-val">
                {String(metrics.pixelWidth ?? '—')} × {String(metrics.pixelHeight ?? '—')}
              </span>
            </div>
          )}
          {metrics.area != null && (
            <div className="metric-pill-box">
              <span className="metric-pill-label">G017 Area</span>
              <span className="metric-pill-val">{String(metrics.area)}</span>
            </div>
          )}
        </div>
      )}

      {/* 3. JSON Payload Viewer (Tabs + Structured Table / Raw Code Toggle) */}
      {availableTabs.length > 0 ? (
        <div className="json-card" style={{ marginTop: '4px' }}>
          <div className="json-card-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={{ display: 'flex', gap: '6px' }}>
              {availableTabs.map((tab) => (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveTab(tab.id)}
                  style={{
                    padding: '4px 10px',
                    fontSize: '11px',
                    fontWeight: activeTab === tab.id ? 700 : 400,
                    background: activeTab === tab.id ? '#334155' : 'transparent',
                    color: activeTab === tab.id ? '#f8fafc' : '#94a3b8',
                    border: 'none',
                    borderRadius: '4px',
                    cursor: 'pointer',
                  }}
                >
                  {tab.label}
                </button>
              ))}
            </div>

            <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
              <button
                type="button"
                onClick={() => setViewMode(viewMode === 'structured' ? 'raw' : 'structured')}
                style={{
                  padding: '3px 8px',
                  fontSize: '11px',
                  background: 'rgba(255, 255, 255, 0.1)',
                  color: '#38bdf8',
                  border: '1px solid rgba(56, 189, 248, 0.3)',
                  borderRadius: '4px',
                  cursor: 'pointer',
                }}
              >
                {viewMode === 'structured' ? '🔍 Raw JSON' : '📊 Structured Table'}
              </button>
            </div>
          </div>

          {!currentJsonText && currentRemoteUrl ? (
            <div style={{ padding: '24px', textAlign: 'center', background: '#0f172a', borderRadius: '0 0 6px 6px' }}>
              <p style={{ color: '#94a3b8', margin: '0 0 12px', fontSize: '13px' }}>
                This defect payload was offloaded to SeaweedFS (<code>{currentRemoteUrl}</code>) to keep the MariaDB database lean and fast.
              </p>
              {remoteError && (
                <p style={{ color: '#f87171', margin: '0 0 12px', fontSize: '12px' }}>
                  Error: {remoteError}
                </p>
              )}
              <div style={{ display: 'flex', justifyContent: 'center', gap: '10px' }}>
                <button
                  type="button"
                  className="primary compact-button"
                  disabled={loadingRemote}
                  onClick={() => void handleLoadRemoteContent()}
                >
                  {loadingRemote ? 'Loading from SeaweedFS...' : '⚡ Load & Inspect JSON Content'}
                </button>
                <a
                  href={currentRemoteUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="secondary compact-button"
                  style={{ display: 'inline-flex', alignItems: 'center', textDecoration: 'none', color: '#f8fafc' }}
                >
                  🔗 Open Directly in Browser
                </a>
              </div>
            </div>
          ) : viewMode === 'structured' && currentParsed ? (
            <div style={{ maxHeight: '320px', overflowY: 'auto', padding: '8px' }}>
              <table className="mini-preview-table" style={{ width: '100%', color: '#f1f5f9' }}>
                <thead>
                  <tr style={{ borderBottom: '1px solid #334155' }}>
                    <th style={{ color: '#94a3b8', textAlign: 'left', padding: '6px 8px' }}>Metric / Key</th>
                    <th style={{ color: '#94a3b8', textAlign: 'left', padding: '6px 8px' }}>Value</th>
                  </tr>
                </thead>
                <tbody>
                  {Object.entries(currentParsed).map(([k, v]) => (
                    <tr key={k} style={{ borderBottom: '1px solid #1e293b' }}>
                      <td style={{ fontFamily: 'monospace', color: '#38bdf8', padding: '4px 8px', fontSize: '11px' }}>{k}</td>
                      <td style={{ fontFamily: 'monospace', color: '#f8fafc', padding: '4px 8px', fontSize: '11px' }}>
                        {typeof v === 'object' && v !== null ? JSON.stringify(v) : String(v ?? '')}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <pre className="json-pre-block" style={{ maxHeight: '320px' }}>
              {currentParsed ? JSON.stringify(currentParsed, null, 2) : currentJsonText || '{}'}
            </pre>
          )}
        </div>
      ) : (
        <div style={{ padding: '14px', background: '#f8fafc', border: '1px dashed #cbd5e1', borderRadius: '8px', textAlign: 'center', color: '#64748b', fontSize: '12px' }}>
          No JSON feature payload stored for this defect.
        </div>
      )}
    </div>
  )
}
