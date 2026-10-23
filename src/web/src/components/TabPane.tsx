import { memo, Suspense } from 'react'
import { TabActiveContext } from '../tabActive'
import {
  AiVerificationTab,
  AuditTab,
  ClassifierRulesTab,
  FeatureTrendsTab,
  HistoryTab,
  LeakageOpsTab,
  OpInputTab,
  OverkillOpsTab,
  OverkillTab,
  OverviewTab,
  ProductsTab,
  QueriesTab,
  ScansTab,
  SourcesTab,
  StorageTab,
} from '../tabRegistry'
import type { Notice, ProductHandoff, TabId } from '../types'
import type { MainSectionId } from '../App'
import { ErrorBoundary } from './ErrorBoundary'
import { TabSkeleton } from './TabSkeleton'

type TabPaneProps = {
  id: TabId
  active: boolean
  onNotice: (notice: Notice) => void
  onAnalyzeDefects: (handoff: ProductHandoff) => void
  onNavigateTab: (section: MainSectionId, tab: TabId) => void
}

const TAB_LABELS: Record<TabId, string> = {
  overview: 'Dashboard',
  import: 'Import',
  'overkill-ops': 'Overkill Operations',
  'op-input': 'OP Reconciliation',
  'leakage-ops': 'Leakage Operations',
  scans: 'Scan Executions',
  products: 'Products & Defects',
  overkill: 'Overkill Analysis',
  'feature-trends': 'Feature Trends',
  queries: 'Query Profiles',
  history: 'Verification History',
  sources: 'Connections & Sources',
  'classifier-rules': 'Classifier Rules & Scripts',
  storage: 'Object Storage',
  audit: 'Audit & Health',
  'ai-verification': 'AI Verification',
}

function renderTab({ id, onNotice, onAnalyzeDefects, onNavigateTab }: TabPaneProps) {
  switch (id) {
    case 'overview': return <OverviewTab onNotice={onNotice} />
    case 'overkill-ops': return <OverkillOpsTab onNotice={onNotice} />
    case 'op-input': return <OpInputTab onNotice={onNotice} />
    case 'leakage-ops': return <LeakageOpsTab onNotice={onNotice} />
    case 'scans': return <ScansTab onNotice={onNotice} />
    case 'history': return <HistoryTab onNotice={onNotice} />
    case 'products': return <ProductsTab onNotice={onNotice} onAnalyzeDefects={onAnalyzeDefects} />
    case 'overkill': return <OverkillTab onNotice={onNotice} onNavigateTab={onNavigateTab} />
    case 'feature-trends': return <FeatureTrendsTab onNotice={onNotice} />
    case 'sources': return <SourcesTab onNotice={onNotice} />
    case 'classifier-rules': return <ClassifierRulesTab onNotice={onNotice} />
    case 'queries': return <QueriesTab onNotice={onNotice} />
    case 'storage': return <StorageTab onNotice={onNotice} />
    case 'ai-verification': return <AiVerificationTab onNotice={onNotice} />
    case 'audit': return <AuditTab onNotice={onNotice} />
    default: return null
  }
}

/**
 * One tab's mount point. Once opened it stays mounted but hidden, so filters, pages and loaded data survive a switch.
 * It is memoised: a hidden tab does not re-render when the shell re-renders (a new notice, a different active tab).
 */
export const TabPane = memo(function TabPane(props: TabPaneProps) {
  return (
    <div
      hidden={!props.active}
      data-tab={props.id}
      id={`panel-${props.id}`}
      role={props.id === 'overview' ? undefined : 'tabpanel'}
      aria-labelledby={props.id === 'overview' ? undefined : `tab-${props.id}`}
    >
      <TabActiveContext.Provider value={props.active}>
        <ErrorBoundary label={TAB_LABELS[props.id]}>
          <Suspense fallback={<TabSkeleton />}>{renderTab(props)}</Suspense>
        </ErrorBoundary>
      </TabActiveContext.Provider>
    </div>
  )
})
