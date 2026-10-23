import { useState, type CSSProperties } from 'react'

/**
 * An image that loads when it nears the viewport, decodes off the main thread, and says so when it cannot be loaded
 * (instead of leaving an empty box). Changing `src` resets the failed state.
 */
export function LazyImage({ src, alt, style }: { src: string; alt: string; style?: CSSProperties }) {
  const [failed, setFailed] = useState<{ src: string; attempt: number } | null>(null)
  const [attempt, setAttempt] = useState(0)
  const hasFailed = failed?.src === src && failed.attempt === attempt

  if (hasFailed) {
    return (
      <span className="lazy-image-failed" role="img" aria-label={`${alt}: image could not be loaded`}>
        Image unavailable
        <button type="button" className="secondary" onClick={() => setAttempt((value) => value + 1)}>
          Retry
        </button>
      </span>
    )
  }
  return (
    <img
      key={attempt}
      src={src}
      alt={alt}
      loading="lazy"
      decoding="async"
      style={style}
      onError={() => setFailed({ src, attempt })}
    />
  )
}
