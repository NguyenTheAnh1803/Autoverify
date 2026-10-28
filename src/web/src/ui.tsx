import { cloneElement, isValidElement, memo, useCallback, useEffect, useId, useMemo, useRef, useState, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactElement, type ReactNode } from 'react'
import type { DefectRecord, Notice } from './types'
import { fetchDefectFeatureContent, storageApi, imageCrawlerApi } from './api'
import { LazyImage } from './components/LazyImage'

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
    refresh: 'M21 12a9 9 0 0 1-15.5 6.3L3 16M3 12a9 9 0 0 1 15.5-6.3L21 8M21 3v5h-5M3 21v-5h5',
    warning: 'M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0zM12 9v4M12 17h.01',
    clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 7v5l3 2',
    check: 'M20 6 9 17l-5-5',
    play: 'M7 4l13 8-13 8z',
    package: 'M21 8 12 3 3 8v8l9 5 9-5zM3 8l9 5 9-5M12 13v8',
    clipboard: 'M9 3h6v3H9zM7 5H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2M8 12h8M8 16h5',
    target: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM12 12h.01',
    info: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 16v-4M12 8h.01',
    image: 'M4 5h16v14H4zM8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM20 15l-5-5L5 19',
    save: 'M5 3h11l3 3v15H5zM8 3v5h7V3M8 21v-7h8v7',
    link: 'M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1',
    star: 'M12 3l2.9 5.9 6.5.9-4.7 4.6 1.1 6.5L12 17.8 6.2 20.9l1.1-6.5L2.6 9.8l6.5-.9z',
    edit: 'M4 20h4L19 9l-4-4L4 16zM13 7l4 4',
    trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
    flask: 'M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2h12.4a1.5 1.5 0 0 0 1.3-2L14 9V3',
    folder: 'M3 6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
    book: 'M4 4h6a3 3 0 0 1 2 1 3 3 0 0 1 2-1h6v15h-6a3 3 0 0 0-2 1 3 3 0 0 0-2-1H4z',
    lock: 'M6 11h12v9H6zM8 11V8a4 4 0 0 1 8 0v3',
    help: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.7.4-1 .9-1 1.7M12 17h.01',
    download: 'M12 4v11m-4-4 4 4 4-4M5 20h14',
    external: 'M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5',
    sort: 'M8 9l4-4 4 4M8 15l4 4 4-4',
    'sort-asc': 'M8 14l4-4 4 4',
    'sort-desc': 'M8 10l4 4 4-4',
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

/** Shown in place of a chart or a list that failed to load; says so and offers a retry instead of an empty "no data" message. */
export function LoadFailure({ title, error, onRetry }: { title: string; error?: string | null; onRetry?: () => void }) {
  return (
    <div className="dashboard-report-empty" role="alert">
      <strong>{title}</strong>
      {error ? <span>{error}</span> : null}
      {onRetry ? <Button variant="secondary" size="sm" onClick={onRetry}>Try again</Button> : null}
    </div>
  )
}

/** Placeholder for a chart while its data loads, the same height as the chart so the page does not jump. */
export function ChartSkeleton({ height = 340 }: { height?: number }) {
  return <span className="skeleton-line chart-skeleton" style={{ height }} role="status" aria-label="Loading chart" />
}

/** Where a list is in its life: first load, loaded, or failed. Lets a screen say "could not load" instead of "no records". */
export type LoadStatus = 'loading' | 'ready' | 'error'

/**
 * Tracks the load state of one list. Call `begin()` before a user-initiated load, `succeed()` after it, `fail(error)` in the catch.
 * A background refresh should skip `begin()`, so rows already on screen are not replaced by placeholders.
 */
export function useLoadStatus() {
  const [state, setState] = useState<{ status: LoadStatus; error?: string }>({ status: 'loading' })
  const begin = useCallback(() => setState((current) => (current.status === 'ready' ? current : { status: 'loading' })), [])
  const succeed = useCallback(() => setState({ status: 'ready' }), [])
  const fail = useCallback(
    (error: unknown) => setState({ status: 'error', error: error instanceof Error ? error.message : 'The request failed.' }),
    [],
  )
  return { status: state.status, error: state.error, begin, succeed, fail }
}

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'

const BUTTON_VARIANT_CLASS: Record<ButtonVariant, string> = {
  primary: 'primary',
  secondary: 'secondary',
  ghost: 'ghost-button',
  danger: 'danger',
}

/**
 * The one button. `primary` is for the main action of a block (one per block), `secondary` and `ghost` for the rest,
 * `danger` for destructive actions. `loading` shows a spinner, disables the button and tells assistive tech it is busy,
 * so a click gets feedback straight away and cannot be repeated while the request runs.
 */
export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  className,
  disabled,
  type = 'button',
  children,
  ...rest
}: Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className'> & {
  variant?: ButtonVariant
  size?: 'md' | 'sm'
  loading?: boolean
  className?: string
}) {
  const classes = [BUTTON_VARIANT_CLASS[variant], size === 'sm' ? 'btn-sm' : '', className ?? ''].filter(Boolean).join(' ')
  return (
    <button {...rest} type={type} className={classes} disabled={disabled || loading} aria-busy={loading || undefined}>
      {children}
    </button>
  )
}

/**
 * A labelled form control. The label, the hint and the error are wired to the control (`aria-describedby`,
 * `aria-invalid`, `aria-required`), so a screen reader announces them and the error is read when it appears.
 */
export function Field({
  label,
  children,
  hint,
  error,
  required,
  className,
}: {
  label: string
  children: ReactNode
  hint?: string
  error?: string
  required?: boolean
  className?: string
}) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`
  const describedBy = [hint ? hintId : '', error ? errorId : ''].filter(Boolean).join(' ') || undefined
  const control = isValidElement(children)
    ? cloneElement(children as ReactElement<Record<string, unknown>>, {
        id,
        'aria-describedby': describedBy,
        'aria-invalid': error ? true : undefined,
        'aria-required': required ? true : undefined,
      })
    : children
  return (
    <label className={`field${className ? ` ${className}` : ''}`}>
      <span>
        {label}
        {required ? <abbr title="Required">*</abbr> : null}
      </span>
      {control}
      {hint ? <small id={hintId} className="field-hint">{hint}</small> : null}
      {error ? <small id={errorId} className="field-error" role="alert">{error}</small> : null}
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

export const DataTable = memo(function DataTable({
  title,
  eyebrow,
  columns,
  rows,
  empty = 'No records.',
  actions,
  selectedRow,
  onRowClick,
  status = 'ready',
  error,
  onRetry,
}: {
  title?: string
  eyebrow?: string
  columns: string[]
  rows: { id: string; cells: ReactNode[]; selected?: boolean }[]
  empty?: string
  actions?: ReactNode
  selectedRow?: string
  onRowClick?: (id: string) => void
  /** `loading` shows placeholder rows, `error` says the data could not be loaded. Both beat "No records", which would be untrue. */
  status?: LoadStatus
  error?: string
  onRetry?: () => void
}) {
  const failed = status === 'error'
  return (
    <section className="panel table-panel" aria-busy={status === 'loading' || undefined}>
      {(title || actions) && (
        <div className="section-heading">
          <div>
            {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
            {title ? <h2>{title}</h2> : null}
          </div>
          {actions}
        </div>
      )}
      {failed && rows.length > 0 ? (
        <div className="table-stale-banner" role="alert">
          <span>Could not refresh this list{error ? `: ${error}` : '.'} The rows below are from the last successful load.</span>
          {onRetry ? <Button size="sm" variant="secondary" onClick={onRetry}>Retry</Button> : null}
        </div>
      ) : null}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {columns.map((column, index) => (
                // A column with no title is the row-actions column; give it a name for screen readers.
                <th key={`${column}-${index}`} scope="col">{column || <span className="sr-only">Actions</span>}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && status === 'loading' ? (
              [0, 1, 2].map((index) => (
                <tr key={`loading-${index}`} className="skeleton-row" aria-hidden="true">
                  <td colSpan={columns.length}><span className="skeleton-line" /></td>
                </tr>
              ))
            ) : rows.length === 0 && failed ? (
              <tr>
                <td colSpan={columns.length} className="empty-cell error-cell" role="alert">
                  <strong>This list could not be loaded.</strong>
                  {error ? <span className="error-cell-detail">{error}</span> : null}
                  {onRetry ? <Button size="sm" variant="secondary" onClick={onRetry}>Try again</Button> : null}
                </td>
              </tr>
            ) : rows.length === 0 ? (
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
                  aria-selected={onRowClick ? row.selected || selectedRow === row.id : undefined}
                  tabIndex={onRowClick ? 0 : undefined}
                  onClick={onRowClick ? () => onRowClick(row.id) : undefined}
                  onKeyDown={
                    onRowClick
                      ? (event) => {
                          // A clickable row is also a keyboard target; Enter/Space must not fire from controls inside it.
                          if ((event.key === 'Enter' || event.key === ' ') && event.target === event.currentTarget) {
                            event.preventDefault()
                            onRowClick(row.id)
                          }
                        }
                      : undefined
                  }
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
})

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
    <div className="pagination">
      <div className="pagination-summary">
        <span>
          Page <strong>{page}</strong> of <strong>{safeTotalPages}</strong> &bull;{' '}
          <strong>{totalCount.toLocaleString()}</strong> records
        </span>
        <span
          className="tag-badge text-xs font-semibold"
          style={{
            background: '#eff6ff',
            color: '#1d4ed8',
            borderColor: '#bfdbfe',
          }}
          title="Latest data always displayed on first page"
        >
          <Icon name="operations" className="icon inline-icon" />Latest First
        </span>
      </div>

      <div className="pagination-controls">
        <button
          type="button"
          className="pagination-btn"
          disabled={page <= 1}
          onClick={() => onPage(1)}
          title="Go to first page (Latest data)"
        >
          « First (Page 1)
        </button>

        <button
          type="button"
          className="pagination-btn"
          disabled={page <= 1}
          onClick={() => onPage(page - 1)}
          title="Previous page"
        >
          ‹ Prev
        </button>

        {/* Page jump composite input */}
        <form onSubmit={handleJump} className="pagination-jump">
          <span className="pagination-jump-label">Page</span>
          <input
            type="number"
            min={1}
            max={safeTotalPages}
            value={jumpPage}
            onChange={(e) => setJumpPage(e.target.value)}
            onBlur={() => handleJump()}
            className="pagination-jump-input"
            aria-label="Page number"
            title={`Enter page number (1 - ${safeTotalPages}) and press Enter`}
          />
          <button
            type="submit"
            className="pagination-jump-btn"
            title="Go to entered page"
          >
            Go
          </button>
        </form>

        <button
          type="button"
          className="pagination-btn"
          disabled={page >= safeTotalPages}
          onClick={() => onPage(page + 1)}
          title="Next page"
        >
          Next ›
        </button>

        <button
          type="button"
          className="pagination-btn"
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

/**
 * A text box for filters and searches. It keeps what is typed in its own state, so a keystroke redraws only the box,
 * and tells the page once typing pauses (or at once on Enter / leaving the box). Setting `value` from outside
 * (a Reset button) replaces the text.
 */
export function DebouncedInput({
  value,
  onValueChange,
  delay = 250,
  onBlur,
  onKeyDown,
  ...rest
}: Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'> & {
  value: string
  onValueChange: (value: string) => void
  delay?: number
}) {
  const [local, setLocal] = useState(value)
  const sentRef = useRef(value)
  const callbackRef = useRef(onValueChange)
  useEffect(() => {
    callbackRef.current = onValueChange
  })

  useEffect(() => {
    if (value !== sentRef.current) {
      sentRef.current = value
      setLocal(value)
    }
  }, [value])

  function send(next: string) {
    if (next === sentRef.current) return
    sentRef.current = next
    callbackRef.current(next)
  }

  useEffect(() => {
    if (local === sentRef.current) return
    const timer = window.setTimeout(() => send(local), delay)
    return () => window.clearTimeout(timer)
  }, [local, delay])

  return (
    <input
      {...rest}
      value={local}
      onChange={(event) => setLocal(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter') send(event.currentTarget.value)
        onKeyDown?.(event)
      }}
      onBlur={(event) => {
        send(event.currentTarget.value)
        onBlur?.(event)
      }}
    />
  )
}

export function NoticeBanner({ notice, onDismiss }: { notice: Notice | null; onDismiss?: () => void }) {
  if (!notice) return null
  return (
    <div className={`notice ${notice.tone}`} role={notice.tone === 'error' ? 'alert' : 'status'}>
      <span className="notice-text">{notice.message}</span>
      {onDismiss ? (
        <button type="button" className="notice-close" onClick={onDismiss} aria-label="Dismiss message">
          <Icon name="close" />
        </button>
      ) : null}
    </div>
  )
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
      // An error page saved as .jpg is worse than no download: fall back to opening the image instead.
      if (!response.ok) throw new Error(`Image request failed (${response.status})`)
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
        className="hstack"
        style={{
          background: '#020617',
          borderRadius: '8px',
          padding: '24px',
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
      className="vstack"
      style={{
        borderRadius: '8px',
        overflow: 'hidden',
        border: '1px solid rgba(255,255,255,0.1)',
        background: '#020617',
      }}
    >
      {/* Viewer Toolbar: Zoom / Pan Controls / Save Image */}
      <div
        className="hstack justify-between flex-wrap gap-md"
        style={{
          padding: '8px 12px',
          background: 'rgba(255,255,255,0.04)',
          borderBottom: '1px solid rgba(255,255,255,0.08)',
        }}
      >
        <div className="hstack gap-sm">
          <button
            type="button"
            className="secondary compact text-xs font-bold"
            onClick={handleZoomIn}
            title="Zoom In"
            style={{ padding: '3px 9px' }}
          >
            <Icon name="search" className="icon inline-icon" />+
          </button>
          <button
            type="button"
            className="secondary compact text-xs font-bold"
            onClick={handleZoomOut}
            title="Zoom Out"
            style={{ padding: '3px 9px' }}
          >
            <Icon name="search" className="icon inline-icon" />-
          </button>
          <button
            type="button"
            className="ghost-button compact text-xs"
            onClick={handleReset}
            title="Reset Zoom & Position"
            style={{ padding: '3px 8px' }}
          >
            <Icon name="refresh" className="icon inline-icon" />100%
          </button>
          <span className="text-xs font-mono" style={{ color: '#94a3b8', marginLeft: '4px' }}>
            {Math.round(scale * 100)}%
          </span>
          <span className="text-xs" style={{ color: '#64748b', marginLeft: '8px' }}>
            (Scroll to Zoom · Click & Drag to Pan)
          </span>
        </div>

        <div className="hstack gap-md">
          <button
            type="button"
            className="primary compact text-xs font-semibold"
            onClick={() => void handleSaveImage()}
            title="Save image to computer"
            style={{ background: '#0369a1', padding: '4px 12px' }}
          >
            <Icon name="save" className="icon inline-icon" />Save Image (.jpg)
          </button>
          <a
            href={src}
            target="_blank"
            rel="noreferrer"
            className="ghost-button compact text-xs"
            title="Open image in new tab"
            style={{ padding: '3px 8px' }}
          >
            <Icon name="external" className="icon inline-icon" />New Tab
          </a>
        </div>
      </div>

      {/* Interactive Canvas */}
      <div
        className="hstack w-full"
        ref={containerRef}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        style={{
          height: maxHeight,
          overflow: 'hidden',
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
  // What the last action reported; ok drives the colour, so the text needs no ✓ / ✗ prefix.
  const [crawlNotice, setCrawlNotice] = useState<{ ok: boolean; text: string } | null>(null)
  const [imageZoomed, setImageZoomed] = useState(false)

  // The enlarged image covers the whole page: Escape closes it, as it does every other dialog in the app.
  useEffect(() => {
    if (!imageZoomed) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setImageZoomed(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [imageZoomed])

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
        setCrawlNotice({ ok: true, text: `Loaded VP${vp} (${res.sizeBytes.toLocaleString()} bytes in ${res.latencyMs}ms)` })
      } else {
        setCrawlNotice({ ok: false, text: `VP${vp} load failed: ${res.errorMessage || 'No image found'}` })
      }
    } catch (err) {
      setCrawlNotice({ ok: false, text: `Error loading VP${vp}: ${err instanceof Error ? err.message : 'Failed to fetch image'}` })
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
        setCrawlNotice({ ok: false, text: `Failed to load original image in ZIP: ${res.errorMessage || 'Unknown error'}` })
      }
    } catch (err) {
      setCrawlNotice({ ok: false, text: err instanceof Error ? err.message : 'Failed to load original image in ZIP' })
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
        setCrawlNotice({ ok: true, text: `Force refreshed ${selectedVp === 'original' ? `path original (VP${originalVp})` : `VP${selectedVp}`} (${res.sizeBytes.toLocaleString()} bytes in ${res.latencyMs}ms)!` })
      } else {
        setCrawlNotice({ ok: false, text: `Crawl failed: ${res.errorMessage || 'Unknown error'}` })
      }
    } catch (err) {
      setCrawlNotice({ ok: false, text: `Error: ${err instanceof Error ? err.message : 'Failed to crawl image'}` })
    } finally {
      setCrawling(false)
    }
  }

  return (
    <div className="defect-feature-inspector vstack gap-lg">
      {/* 1. Defect & Spec Context Card */}
      <div style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '8px', padding: '10px 14px' }}>
        <div className="hstack justify-between flex-wrap gap-md">
          <div className="hstack gap-md">
            <strong style={{ fontSize: '15px', color: '#0f172a' }}>{defect.defectName}</strong>
            {defect.judge && <JudgeBadge value={defect.judge} />}
            {defect.severity && <StatusPill value={defect.severity} />}
            {defect.defectTable && <span className="tag-badge">Table: {defect.defectTable}</span>}
            {defect.modelCode && (
              <span className="tag-badge font-semibold" style={{ background: '#f0fdf4', color: '#166534', borderColor: '#bbf7d0' }}>
                Model: {defect.modelCode}
              </span>
            )}
          </div>
          <div className="hstack gap-sm flex-wrap">
            {defect.priority != null && (
              <span className="clean-pill font-semibold" style={{ background: '#fef3c7', color: '#92400e' }}>
                Priority: {defect.priority}
              </span>
            )}
            {defect.featureUrl?.includes('#') ? (
              <span className="tag-badge font-semibold" style={{ background: '#f5f3ff', color: '#6d28d9', borderColor: '#ddd6fe' }}>
                <Icon name="package" className="icon inline-icon" />Unified Defect Pack
              </span>
            ) : (defect.featureUrl || defect.imageUrl) ? (
              <span className="tag-badge font-semibold" style={{ background: '#e0f2fe', color: '#0369a1', borderColor: '#bae6fd' }}>
                <Icon name="leakage" className="icon inline-icon" />SeaweedFS Offloaded
              </span>
            ) : null}
          </div>
        </div>

        {/* Defect Thumbnail & Multi-VP Image Crawler Section */}
        {(defect.imageUri || defect.imageUrl) && (
          <div className="vstack" style={{ background: '#ffffff', border: '1px solid #cbd5e1', borderRadius: '8px', padding: '12px', marginTop: '10px', gap: '10px' }}>
            {/* VP Navigation Bar: VP0 (Default) + Path Original + VP 1, 2, 3, 4 */}
            <div className="hstack justify-between flex-wrap gap-md" style={{ borderBottom: '1px solid #e2e8f0', paddingBottom: '8px' }}>
              <div className="hstack gap-sm flex-wrap">
                <span className="text-xs font-bold" style={{ color: '#475569', marginRight: '2px' }}>Camera Angle:</span>

                {/* Real VP button */}
                <button
                  className="text-xs font-bold"
                  type="button"
                  disabled={loadingVp === 'original'}
                  onClick={() => void handleSelectOriginalVp()}
                  style={{
                    padding: '3px 12px',
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
                    <span className="text-xs"><Icon name="clock" className="icon inline-icon" /></span>
                  ) : vpImages.original ? (
                    <span style={{ width: '6px', height: '6px', borderRadius: '50%', background: selectedVp === 'original' ? '#4ade80' : '#16a34a', display: 'inline-block' }} title="Image loaded" />
                  ) : (
                    <span className="text-xs"><Icon name="operations" className="icon inline-icon" /></span>
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
                      className="text-xs gap-xs"
                      key={vp}
                      type="button"
                      disabled={isLoading}
                      onClick={() => void handleSelectVp(vp)}
                      style={{
                        padding: '3px 8px',
                        fontWeight: isSelected ? 700 : 500,
                        borderRadius: '6px',
                        cursor: 'pointer',
                        border: isSelected ? '1.5px solid #2563eb' : isOriginal ? '1px dashed #a78bfa' : '1px solid #cbd5e1',
                        background: isSelected ? '#eff6ff' : isOriginal ? '#faf5ff' : '#f8fafc',
                        color: isSelected ? '#1d4ed8' : isOriginal ? '#7c3aed' : '#475569',
                        display: 'inline-flex',
                        alignItems: 'center',
                        transition: 'all 0.15s ease',
                      }}
                      title={vp === '0' ? 'VP0 (Thumbnail - Default)' : isOriginal ? `VP${vp}: Browse image at same angle as image_path` : `VP${vp}: Angle browse image, click to load`}
                    >
                      <span>{vp === '0' ? 'VP0 (Default)' : isOriginal ? `VP${vp} (Path Angle)` : `VP${vp}`}</span>
                      {isLoading ? (
                        <span className="text-xs" style={{ color: '#2563eb' }}><Icon name="clock" className="icon inline-icon" /></span>
                      ) : isLoaded ? (
                        <span style={{ width: '6px', height: '6px', borderRadius: '50%', background: '#16a34a', display: 'inline-block' }} title="Image loaded" />
                      ) : (
                        <span className="text-xs" style={{ color: '#94a3b8' }}><Icon name="operations" className="icon inline-icon" /></span>
                      )}
                    </button>
                  )
                })}
              </div>

              <div className="hstack gap-sm">
                <button
                  type="button"
                  className="secondary compact text-xs"
                  style={{ padding: '3px 8px', background: '#f1f5f9', borderColor: '#cbd5e1', color: '#334155' }}
                  disabled={crawling || loadingVp !== null}
                  onClick={() => void handleForceRefreshVp()}
                  title="Re-crawl image from PC API for selected VP"
                >
                  {crawling ? 'Crawling...' : `Force Crawl VP${selectedVp}`}
                </button>
              </div>
            </div>

            {/* Image Preview & Details Row */}
            <div className="hstack flex-wrap" style={{ gap: '14px' }}>
              <div
                className="hstack"
                style={{
                  width: '130px',
                  height: '105px',
                  background: '#090d16',
                  borderRadius: '6px',
                  overflow: 'hidden',
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
                  <div className="text-xs" style={{ color: '#60a5fa', textAlign: 'center', padding: '6px' }}>
                    <div><Icon name="clock" className="icon inline-icon" />Loading...</div>
                    <div className="text-xs" style={{ color: '#94a3b8' }}>VP{selectedVp} from PC</div>
                  </div>
                ) : currentImgSrc ? (
                  <>
                    <LazyImage
                      src={currentImgSrc}
                      alt={`${defect.defectName} (VP${selectedVp})`}
                      style={{ width: '100%', height: '100%', objectFit: 'contain' }}
                    />
                    <span className="text-xs font-bold" style={{ position: 'absolute', bottom: '2px', right: '4px', color: '#fff', background: 'rgba(0,0,0,0.6)', padding: '1px 4px', borderRadius: '3px' }}>
                      VP{selectedVp}
                    </span>
                  </>
                ) : (
                  <div className="text-xs" style={{ color: '#64748b', textAlign: 'center', padding: '4px' }}>
                    <div>VP{selectedVp} not loaded</div>
                    <div className="text-xs" style={{ color: '#3b82f6', marginTop: '2px' }}>Click to load</div>
                  </div>
                )}
              </div>

              <div style={{ flex: 1, minWidth: '220px' }}>
                <div className="hstack justify-between flex-wrap gap-sm">
                  <span className="text-xs font-bold" style={{ color: '#334155' }}>
                    <Icon name="image" className="icon inline-icon" />Image Path (PC) & Active Angle: <span style={{ color: '#2563eb' }}>VP{selectedVp}</span>
                  </span>
                  {currentImgSrc && (
                    <button
                      type="button"
                      className="compact text-xs"
                      style={{ padding: '2px 6px', background: '#f8fafc', border: '1px solid #cbd5e1', cursor: 'pointer' }}
                      onClick={() => setImageZoomed(true)}
                    >
                      <Icon name="search" className="icon inline-icon" />Enlarge Image
                    </button>
                  )}
                </div>
                <code className="text-xs" style={{ display: 'block', margin: '4px 0', color: '#1e293b', background: '#f1f5f9', padding: '4px 8px', borderRadius: '4px', wordBreak: 'break-all' }}>
                  {defect.imageUri || defect.imageUrl || 'No image_path recorded'}
                </code>
                {crawlNotice && (
                  <div className={`text-xs font-semibold mt-1 ${crawlNotice.ok ? 'text-ok' : 'text-ng'}`} role="status">
                    {crawlNotice.text}
                  </div>
                )}
              </div>
            </div>

            {imageZoomed && currentImgSrc && (
              <div
                className="hstack"
                style={{
                  position: 'fixed',
                  top: 0,
                  left: 0,
                  right: 0,
                  bottom: 0,
                  background: 'rgba(0,0,0,0.85)',
                  zIndex: 9999,
                  flexDirection: 'column',
                  justifyContent: 'center',
                  padding: '20px',
                }}
                role="dialog"
                aria-modal="true"
                aria-label={`${defect.defectName}, angle VP${selectedVp}`}
                onClick={() => setImageZoomed(false)}
              >
                <div className="text-base font-semibold" style={{ marginBottom: '10px', color: '#fff' }}>
                  {defect.defectName} — Angle VP{selectedVp} (click anywhere or press Esc to close)
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
          <div className="mt-2 text-xs" style={{ display: 'flex', flexWrap: 'wrap', gap: '10px', padding: '6px 10px', background: '#ffffff', borderRadius: '6px', border: '1px solid #e2e8f0' }}>
            {defect.featureUrl && (
              <div>
                <span className="muted">Feature SeaweedFS: </span>
                <a href={defect.featureUrl} target="_blank" rel="noreferrer" style={{ color: '#0369a1', textDecoration: 'underline' }}>
                  {defect.featureUrl.length > 50 ? defect.featureUrl.slice(0, 48) + '...' : defect.featureUrl}
                </a>
                <a
                  className="font-semibold"
                  href={storageApi.defectFileUrl(defect.id, 'feature')}
                  target="_blank"
                  rel="noreferrer"
                  style={{ marginLeft: '6px', color: '#0369a1', textDecoration: 'underline' }}
                  title="Stream & decompress on-demand"
                >
                  [Stream File]
                </a>
              </div>
            )}
            {defect.customFeatureUrl && (
              <div>
                <span className="muted">Custom Feature: </span>
                <a href={defect.customFeatureUrl} target="_blank" rel="noreferrer" style={{ color: '#0369a1', textDecoration: 'underline' }}>
                  {defect.customFeatureUrl.length > 50 ? defect.customFeatureUrl.slice(0, 48) + '...' : defect.customFeatureUrl}
                </a>
                <a
                  className="font-semibold"
                  href={storageApi.defectFileUrl(defect.id, 'custom')}
                  target="_blank"
                  rel="noreferrer"
                  style={{ marginLeft: '6px', color: '#0369a1', textDecoration: 'underline' }}
                  title="Stream & decompress on-demand"
                >
                  [Stream File]
                </a>
              </div>
            )}
            {defect.imageUrl && (
              <div>
                <span className="muted">Defect Image: </span>
                <a className="font-semibold" href={defect.imageUrl} target="_blank" rel="noreferrer" style={{ color: '#16a34a', textDecoration: 'underline' }}>
                  <Icon name="image" className="icon inline-icon" />View Photo in SeaweedFS
                </a>
                <a
                  className="font-semibold"
                  href={storageApi.defectImagesUrl(defect.id)}
                  target="_blank"
                  rel="noreferrer"
                  style={{ marginLeft: '6px', color: '#047857', textDecoration: 'underline' }}
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
                  className="font-semibold"
                  href={storageApi.productDefectPackUrl(defect.keyId)}
                  target="_blank"
                  rel="noreferrer"
                  style={{ color: '#7c3aed', textDecoration: 'underline' }}
                  title="Stream & decompress unified defect pack for this Product ID"
                >
                  <Icon name="package" className="icon inline-icon" />[All Defects Pack JSON]
                </a>
              </div>
            )}
          </div>
        )}

        <div className="mt-2 text-xs" style={{ display: 'flex', flexWrap: 'wrap', gap: '14px' }}>
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
                <span className="font-semibold" style={{ marginLeft: '4px', color: defect.specJudge === 'NG' ? '#dc2626' : '#16a34a' }}>
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
        <div className="gap-md" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(130px, 100%), 1fr))' }}>
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
        <div className="json-card mt-1">
          <div className="json-card-header hstack justify-between">
            <div className="gap-sm" style={{ display: 'flex' }}>
              {availableTabs.map((tab) => (
                <button
                  className="text-xs"
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveTab(tab.id)}
                  style={{
                    padding: '4px 10px',
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

            <div className="hstack gap-sm">
              <button
                className="text-xs"
                type="button"
                onClick={() => setViewMode(viewMode === 'structured' ? 'raw' : 'structured')}
                style={{
                  padding: '3px 8px',
                  background: 'rgba(255, 255, 255, 0.1)',
                  color: '#38bdf8',
                  border: '1px solid rgba(56, 189, 248, 0.3)',
                  borderRadius: '4px',
                  cursor: 'pointer',
                }}
              >
                {viewMode === 'structured' ? 'Raw JSON' : 'Structured Table'}
              </button>
            </div>
          </div>

          {!currentJsonText && currentRemoteUrl ? (
            <div style={{ padding: '24px', textAlign: 'center', background: '#0f172a', borderRadius: '0 0 6px 6px' }}>
              <p className="text-sm" style={{ color: '#94a3b8', margin: '0 0 12px' }}>
                This defect payload was offloaded to SeaweedFS (<code>{currentRemoteUrl}</code>) to keep the MariaDB database lean and fast.
              </p>
              {remoteError && (
                <p className="text-xs" style={{ color: '#f87171', margin: '0 0 12px' }}>
                  Error: {remoteError}
                </p>
              )}
              <div style={{ display: 'flex', justifyContent: 'center', gap: '10px' }}>
                <Button type="button" onClick={() => void handleLoadRemoteContent()} size="sm" loading={loadingRemote}>
                  {loadingRemote ? 'Loading from SeaweedFS...' : 'Load & Inspect JSON Content'}
                </Button>
                <a
                  href={currentRemoteUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="secondary compact-button"
                  style={{ display: 'inline-flex', alignItems: 'center', textDecoration: 'none', color: '#f8fafc' }}
                >
                  <Icon name="link" className="icon inline-icon" />Open Directly in Browser
                </a>
              </div>
            </div>
          ) : viewMode === 'structured' && currentParsed ? (
            <div style={{ maxHeight: '320px', overflowY: 'auto', padding: '8px' }}>
              <table className="mini-preview-table w-full" style={{ color: '#f1f5f9' }}>
                <thead>
                  <tr style={{ borderBottom: '1px solid #334155' }}>
                    <th style={{ color: '#94a3b8', textAlign: 'left', padding: '6px 8px' }}>Metric / Key</th>
                    <th style={{ color: '#94a3b8', textAlign: 'left', padding: '6px 8px' }}>Value</th>
                  </tr>
                </thead>
                <tbody>
                  {Object.entries(currentParsed).map(([k, v]) => (
                    <tr key={k} style={{ borderBottom: '1px solid #1e293b' }}>
                      <td className="font-mono text-xs" style={{ color: '#38bdf8', padding: '4px 8px' }}>{k}</td>
                      <td className="font-mono text-xs" style={{ color: '#f8fafc', padding: '4px 8px' }}>
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
        <div className="text-xs" style={{ padding: '14px', background: '#f8fafc', border: '1px dashed #cbd5e1', borderRadius: '8px', textAlign: 'center', color: '#64748b' }}>
          No JSON feature payload stored for this defect.
        </div>
      )}
    </div>
  )
}
