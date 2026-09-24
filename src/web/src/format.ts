import type { FormEvent } from 'react'

export function formatPercent(value?: number) {
  return `${Math.round((value ?? 0) * 1000) / 10}%`
}

export function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString()
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
