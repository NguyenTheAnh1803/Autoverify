import { useEffect, useRef } from 'react'
import { useTabActive } from '../tabActive'

/**
 * Calls `callback` every `intervalMs` while `enabled`, the tab is the one on screen, and the browser tab is visible.
 * - The next call is scheduled only after the previous one finished, so slow requests never pile up.
 * - Coming back to a hidden tab or browser tab runs a refresh at once if the data is older than the interval.
 * - Errors are the callback's business; a rejected promise does not stop the loop.
 * It never calls `callback` on mount: callers load their first data themselves.
 */
export function usePolling(callback: () => unknown, intervalMs: number, enabled = true): void {
  const active = useTabActive()
  const callbackRef = useRef(callback)
  const lastRunRef = useRef(0) // set when the hook mounts, so a fresh mount waits a full interval

  useEffect(() => {
    callbackRef.current = callback
  })

  useEffect(() => {
    lastRunRef.current = Date.now()
  }, [])

  useEffect(() => {
    if (!enabled || !active) return
    let stopped = false
    let timer: number | undefined

    const schedule = (delay: number) => {
      window.clearTimeout(timer)
      timer = window.setTimeout(() => void tick(), Math.max(0, delay))
    }
    const tick = async () => {
      if (stopped || document.hidden) return // a hidden page resumes through `visibilitychange`
      try {
        await callbackRef.current()
      } catch {
        // keep polling
      }
      lastRunRef.current = Date.now()
      if (!stopped) schedule(intervalMs)
    }
    const onVisibility = () => {
      if (stopped || document.hidden) return
      schedule(intervalMs - (Date.now() - lastRunRef.current))
    }

    document.addEventListener('visibilitychange', onVisibility)
    schedule(intervalMs - (Date.now() - lastRunRef.current))
    return () => {
      stopped = true
      window.clearTimeout(timer)
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [enabled, active, intervalMs])
}
