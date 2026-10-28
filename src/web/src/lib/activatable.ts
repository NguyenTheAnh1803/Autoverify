import type { KeyboardEvent } from 'react'

/**
 * Props for an element that is clicked but is not a <button> (a table row, a filter tile, a drop zone):
 * it takes keyboard focus, and Enter/Space activate it, but not when the key press comes from a control inside it.
 * A table row keeps its row semantics and reports selection with aria-selected; anything else is announced as a button,
 * with aria-pressed when it toggles.
 */
export function activatable(
  onActivate: () => void,
  options: { row?: boolean; selected?: boolean; label?: string; disabled?: boolean } = {},
) {
  const { row = false, selected, label, disabled = false } = options
  return {
    tabIndex: disabled ? -1 : 0,
    role: row ? undefined : 'button',
    'aria-selected': row ? selected : undefined,
    'aria-pressed': row ? undefined : selected,
    'aria-disabled': disabled || undefined,
    'aria-label': label,
    onClick: disabled ? undefined : onActivate,
    onKeyDown: (event: KeyboardEvent<HTMLElement>) => {
      if (disabled || event.target !== event.currentTarget) return
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault()
        onActivate()
      }
    },
  }
}
