/** Placeholder shown while a tab's code is downloading. Same outer spacing as real content so the page does not jump. */
export function TabSkeleton() {
  return (
    <div className="tab-skeleton" role="status" aria-live="polite" aria-busy="true">
      <span className="sr-only">Loading…</span>
      <span className="skeleton-line tab-skeleton-title" />
      <div className="tab-skeleton-cards" aria-hidden="true">
        <span className="skeleton-line tab-skeleton-card" />
        <span className="skeleton-line tab-skeleton-card" />
        <span className="skeleton-line tab-skeleton-card" />
        <span className="skeleton-line tab-skeleton-card" />
      </div>
      <span className="skeleton-line tab-skeleton-panel" aria-hidden="true" />
    </div>
  )
}
