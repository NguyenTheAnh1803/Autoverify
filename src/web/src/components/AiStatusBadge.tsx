import type { AiDefectResult, AiFinalStatus, AiProductSummary } from '../types'

const LABELS: Record<AiFinalStatus, { text: string; tone: 'healthy' | 'warning' | 'unhealthy' | 'unknown'; hint: string }> = {
  OverkillConfirmed: { text: 'Overkill (AI: OK)', tone: 'healthy', hint: 'Station judged NG, AI found every image OK: confirmed false alarm.' },
  ConsistentNg: { text: 'NG confirmed', tone: 'unhealthy', hint: 'Station judged NG and AI found an NG image.' },
  LeakageConfirmed: { text: 'Leakage (AI: NG)', tone: 'warning', hint: 'Station judged OK but AI found an NG image: confirmed escape.' },
  ConsistentOk: { text: 'OK confirmed', tone: 'healthy', hint: 'Station judged OK and AI found every image OK.' },
  AiUnavailable: { text: 'AI unavailable', tone: 'unknown', hint: 'No usable AI answer for every image; the rule-based result stands.' },
  NoImages: { text: 'No images', tone: 'unknown', hint: 'Nothing to judge.' },
  Pending: { text: 'Pending…', tone: 'unknown', hint: 'Waiting for the Master.' },
}

function aiStatusLabel(status: AiFinalStatus) {
  return LABELS[status] ?? { text: status, tone: 'unknown' as const, hint: '' }
}

/** Combined AI result of one product, with the per-image counts as a tooltip. */
export function AiStatusBadge({ summary }: { summary?: AiProductSummary }) {
  if (!summary) return <span className="muted" title="Not sent to the AI">—</span>
  const label = aiStatusLabel(summary.aiStatus)
  const counts = `${summary.done} judged (${summary.ng} NG, ${summary.ok} OK) · ${summary.pending} pending · ${summary.unavailable} unavailable`
  const score = summary.maxAnomalyScore != null ? ` · max score ${summary.maxAnomalyScore.toFixed(2)}` : ''
  return (
    <span className={`health-pill ${label.tone}`} title={`${label.hint}\n${counts}${score}`}>
      {label.text}
    </span>
  )
}

/** The AI's answer for one defect image: the verdict when there is one, otherwise why there is none. */
export function AiDefectVerdict({ result }: { result?: AiDefectResult }) {
  if (!result) return <span className="muted" title="Not sent to the AI">—</span>
  if (result.status === 'Done' && result.verdict) {
    const detail = [
      result.anomalyScore != null ? `score ${result.anomalyScore.toFixed(2)}` : null,
      result.defectClass || null,
      result.clientMode ? `mode ${result.clientMode}` : null,
    ].filter(Boolean).join(' · ')
    return (
      <span title={detail}>
        <span className={`health-pill ${result.verdict === 'NG' ? 'unhealthy' : 'healthy'}`}>AI: {result.verdict}</span>
        {detail ? <small className="muted" style={{ display: 'block' }}>{detail}</small> : null}
      </span>
    )
  }
  if (result.status === 'Submitted') return <span className="health-pill unknown">Pending…</span>
  const reason = result.error || (result.status === 'Rejected' ? 'Rejected by the Master.' : 'No verdict.')
  return <span className="health-pill unknown" title={reason}>{result.status === 'Rejected' ? 'Rejected' : 'Unavailable'}</span>
}
