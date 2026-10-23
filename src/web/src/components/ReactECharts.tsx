import { lazy, Suspense, useEffect, useMemo, useRef, useState, type ComponentProps } from 'react'
import type ReactEChartsCore from 'echarts-for-react/esm/core'

const Chart = lazy(() => import('./ReactEChartsImpl'))

type Props = Omit<ComponentProps<typeof ReactEChartsCore>, 'echarts'>

/** Above this many points an entrance animation janks on a modest PC, so it is switched off. */
const ANIMATION_POINT_LIMIT = 3000

function countPoints(option: Props['option']): number {
  const series = (option as { series?: unknown }).series
  const list = Array.isArray(series) ? series : series ? [series] : []
  return list.reduce((total: number, item) => {
    const data = (item as { data?: unknown } | null)?.data
    return total + (Array.isArray(data) ? data.length : 0)
  }, 0)
}

function Placeholder() {
  return <span className="skeleton-line" style={{ height: '100%', width: '100%', borderRadius: 12 }} aria-hidden="true" />
}

/**
 * Drop-in replacement for `echarts-for-react`'s default export, bound to the trimmed ECharts build.
 * The chart (and the ECharts code itself) is only loaded once its box is near the viewport, so a page
 * with many charts does not initialise all of them up front and the sign-in screen does not carry ECharts.
 */
export default function ReactECharts({ style, option, ...rest }: Props) {
  const hostRef = useRef<HTMLDivElement>(null)
  const [visible, setVisible] = useState(false)
  const finalOption = useMemo(
    () => (countPoints(option) > ANIMATION_POINT_LIMIT ? { ...option, animation: false } : option),
    [option],
  )

  useEffect(() => {
    const host = hostRef.current
    if (!host) return
    if (typeof IntersectionObserver === 'undefined') {
      setVisible(true)
      return
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setVisible(true)
          observer.disconnect()
        }
      },
      { rootMargin: '240px' },
    )
    observer.observe(host)
    return () => observer.disconnect()
  }, [])

  return (
    <div ref={hostRef} style={{ width: style?.width ?? '100%', height: style?.height ?? 300 }}>
      {visible ? (
        <Suspense fallback={<Placeholder />}>
          <Chart style={style} option={finalOption} {...rest} />
        </Suspense>
      ) : (
        <Placeholder />
      )}
    </div>
  )
}
