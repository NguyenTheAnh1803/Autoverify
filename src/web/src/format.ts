import type { FormEvent } from 'react'

export function formatPercent(value?: number) {
  return `${Math.round((value ?? 0) * 1000) / 10}%`
}

/**
 * The API stores times in UTC. A value without a zone designator ("2026-10-21T08:00:00", which is what a database
 * DateTime of unspecified kind serialises to) would be read by `new Date()` as the viewer's local time and be hours off,
 * so it is read as UTC here. A value that carries a zone ("Z", "+07:00") is used as it is, and a plain date ("2026-10-21") stays a date.
 */
export function parseServerTime(value?: string | null): number | null {
  if (!value) return null
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return null
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(value)
  const ms = Date.parse(hasZone ? value : `${value.replace(' ', 'T')}Z`)
  return Number.isNaN(ms) ? null : ms
}

const pad = (n: number) => String(n).padStart(2, '0')

/**
 * One timestamp format everywhere: 24-hour `YYYY-MM-DD HH:mm:ss` in the viewer's own time zone.
 * A missing value is `—`; a value that is not a time (or only a date) is shown as it came.
 */
export function formatDate(value?: string | null) {
  if (!value) return '—'
  const ms = parseServerTime(value)
  if (ms === null) return value
  const d = new Date(ms)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

export function formatDateTime(value?: string | null) {
  return formatDate(value)
}

export function formatBytes(value: number) {
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  return `${(value / (1024 * 1024)).toFixed(1)} MB`
}

export function formString(event: FormEvent<HTMLFormElement>, name: string) {
  return String(new FormData(event.currentTarget).get(name) ?? '').trim()
}

export function formNumber(event: FormEvent<HTMLFormElement>, name: string, fallback = 0) {
  const raw = String(new FormData(event.currentTarget).get(name) ?? '')
  const value = Number(raw)
  return Number.isFinite(value) ? value : fallback
}

export function paginate<T>(items: T[], page: number, pageSize = 25) {
  const totalCount = items.length
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const safePage = Math.min(Math.max(page, 1), totalPages)
  const start = (safePage - 1) * pageSize
  return {
    items: items.slice(start, start + pageSize),
    page: safePage,
    totalPages,
    totalCount,
    pageSize,
  }
}

export type CanonicalFormat = 'auto' | 'json' | 'jsonl' | 'gzip' | 'zip'

/**
 * Submits canonical format names directly ('json' | 'jsonl' | 'gzip' | 'zip'),
 * or undefined for 'auto' to allow server-side payload/extension resolution.
 */
export function mapCanonicalToBackendFormat(format: CanonicalFormat): string | undefined {
  return format === 'auto' ? undefined : format
}

export function inferCanonicalFormat(uriOrName: string): CanonicalFormat {
  const lower = uriOrName.toLowerCase().split('#')[0]
  if (lower.endsWith('.zip')) return 'zip'
  if (lower.endsWith('.gz')) return 'gzip'
  if (lower.endsWith('.jsonl') || lower.endsWith('.ndjson')) return 'jsonl'
  if (lower.endsWith('.json')) return 'json'
  return 'auto'
}
