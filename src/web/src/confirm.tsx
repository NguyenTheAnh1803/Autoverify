import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { Button, Modal } from './ui'

export type ConfirmOptions = {
  title: string
  message?: string
  /** Label of the confirming button, e.g. "Delete rule". Say what will happen rather than "OK". */
  confirmLabel?: string
  /** `danger` styles the confirming button red; use it for anything that cannot be undone. */
  tone?: 'danger' | 'default'
}

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

// Outside the provider (a test, a stray render) fall back to the browser's own dialog so a delete is never silent.
const ConfirmContext = createContext<ConfirmFn>((options) =>
  Promise.resolve(window.confirm(options.message ? `${options.title}\n\n${options.message}` : options.title)),
)

/** `const confirm = useConfirm(); if (!(await confirm({ title: 'Delete rule R1?', tone: 'danger' }))) return` */
export function useConfirm(): ConfirmFn {
  return useContext(ConfirmContext)
}

/**
 * One confirmation dialog for the whole app, replacing `window.confirm`: it is styled like the rest of the UI,
 * it does not freeze the page, it names the action on its button, and Cancel has focus so Enter never confirms by accident.
 */
export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [request, setRequest] = useState<(ConfirmOptions & { id: number }) | null>(null)
  const resolverRef = useRef<((value: boolean) => void) | null>(null)
  const counterRef = useRef(0)

  const confirm = useCallback<ConfirmFn>((options) => {
    // A second request while one is open cancels the first.
    resolverRef.current?.(false)
    return new Promise<boolean>((resolve) => {
      resolverRef.current = resolve
      counterRef.current += 1
      setRequest({ ...options, id: counterRef.current })
    })
  }, [])

  const settle = useCallback((value: boolean) => {
    resolverRef.current?.(value)
    resolverRef.current = null
    setRequest(null)
  }, [])

  const value = useMemo(() => confirm, [confirm])
  const danger = request?.tone === 'danger'

  return (
    <ConfirmContext.Provider value={value}>
      {children}
      {request ? (
        <Modal key={request.id} title={request.title} open onClose={() => settle(false)}>
          {request.message ? <p className="confirm-message">{request.message}</p> : null}
          <div className="actions">
            <Button variant="ghost" autoFocus onClick={() => settle(false)}>
              Cancel
            </Button>
            <Button variant={danger ? 'danger' : 'primary'} className={danger ? 'danger-solid' : ''} onClick={() => settle(true)}>
              {request.confirmLabel ?? (danger ? 'Delete' : 'Confirm')}
            </Button>
          </div>
        </Modal>
      ) : null}
    </ConfirmContext.Provider>
  )
}
