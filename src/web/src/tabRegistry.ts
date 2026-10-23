import { lazy } from 'react'
import type { TabId } from './types'

/**
 * Each tab is its own chunk: the browser downloads a tab the first time it is opened (or earlier, see `preloadTab`)
 * instead of shipping every tab with the sign-in screen.
 */
const loaders = {
  overview: () => import('./tabs/OverviewTab').then((m) => ({ default: m.OverviewTab })),
  'overkill-ops': () => import('./tabs/OverkillOpsTab').then((m) => ({ default: m.OverkillOpsTab })),
  'op-input': () => import('./tabs/OpInputTab').then((m) => ({ default: m.OpInputTab })),
  'leakage-ops': () => import('./tabs/LeakageOpsTab').then((m) => ({ default: m.LeakageOpsTab })),
  scans: () => import('./tabs/ScansTab').then((m) => ({ default: m.ScansTab })),
  history: () => import('./tabs/HistoryTab').then((m) => ({ default: m.HistoryTab })),
  products: () => import('./tabs/ProductsTab').then((m) => ({ default: m.ProductsTab })),
  overkill: () => import('./tabs/OverkillTab').then((m) => ({ default: m.OverkillTab })),
  'feature-trends': () => import('./tabs/FeatureTrendsTab').then((m) => ({ default: m.FeatureTrendsTab })),
  sources: () => import('./tabs/SourcesTab').then((m) => ({ default: m.SourcesTab })),
  'classifier-rules': () => import('./tabs/ClassifierRulesTab').then((m) => ({ default: m.ClassifierRulesTab })),
  queries: () => import('./tabs/QueriesTab').then((m) => ({ default: m.QueriesTab })),
  storage: () => import('./tabs/StorageTab').then((m) => ({ default: m.StorageTab })),
  'ai-verification': () => import('./tabs/AiVerificationTab').then((m) => ({ default: m.AiVerificationTab })),
  audit: () => import('./tabs/AuditTab').then((m) => ({ default: m.AuditTab })),
}

export const OverviewTab = lazy(loaders.overview)
export const OverkillOpsTab = lazy(loaders['overkill-ops'])
export const OpInputTab = lazy(loaders['op-input'])
export const LeakageOpsTab = lazy(loaders['leakage-ops'])
export const ScansTab = lazy(loaders.scans)
export const HistoryTab = lazy(loaders.history)
export const ProductsTab = lazy(loaders.products)
export const OverkillTab = lazy(loaders.overkill)
export const FeatureTrendsTab = lazy(loaders['feature-trends'])
export const SourcesTab = lazy(loaders.sources)
export const ClassifierRulesTab = lazy(loaders['classifier-rules'])
export const QueriesTab = lazy(loaders.queries)
export const StorageTab = lazy(loaders.storage)
export const AiVerificationTab = lazy(loaders['ai-verification'])
export const AuditTab = lazy(loaders.audit)

type LoadableTab = keyof typeof loaders

/** Start downloading a tab's chunk without rendering it. Safe to call repeatedly; failures are ignored here and surface on real use. */
export function preloadTab(id: TabId): void {
  const load = loaders[id as LoadableTab]
  if (load) void load().catch(() => undefined)
}

/** After sign-in, fetch the remaining tabs while the browser is idle so a later tab switch is instant. */
export function preloadAllTabs(): () => void {
  const ids = Object.keys(loaders) as LoadableTab[]
  let cancelled = false
  let index = 0
  const schedule = (fn: () => void) => {
    if (typeof window.requestIdleCallback === 'function') window.requestIdleCallback(fn, { timeout: 4000 })
    else globalThis.setTimeout(fn, 600)
  }
  const step = () => {
    if (cancelled || index >= ids.length) return
    preloadTab(ids[index++])
    schedule(step)
  }
  schedule(step)
  return () => {
    cancelled = true
  }
}
