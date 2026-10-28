import { memo } from 'react'
import { imageCrawlerApi } from '../../api'
import { activatable } from '../../lib/activatable'
import { statusSolid, type StatusName } from '../../lib/status'
import { LazyImage } from '../LazyImage'
import { Icon, StatusPill } from '../../ui'
import type { AnomalyThumbnailItem, Notice } from '../../types'
import { defaultVpOf, originalVpOf, recrawlCardVp, selectCardVp, useVpEntry, vpKey } from './vpImageStore'

/**
 * One defect image in the Overkill gallery. Its camera-angle state lives in the VP store under this card's key,
 * so switching or crawling an angle re-renders this card only, not the gallery or the tab around it.
 */
export const ThumbnailCard = memo(function ThumbnailCard({
  item,
  inspectBusy,
  onZoom,
  onInspect,
  onNotice,
}: {
  item: AnomalyThumbnailItem
  inspectBusy: boolean
  onZoom: (item: AnomalyThumbnailItem, vp: string) => void
  onInspect: (item: AnomalyThumbnailItem, vp: string) => void
  onNotice: (notice: Notice) => void
}) {
  const vpState = useVpEntry(vpKey(item))
  const isNa = item.anomalyCategory.toLowerCase() === 'na' || item.anomalyCategory === 'N/A'
  const isLeakage = item.anomalyCategory.toLowerCase() === 'leakage'
  const isRealNg = item.anomalyCategory.toLowerCase() === 'realng'
  const isOverkill = item.anomalyCategory.toLowerCase() === 'overkill'
  const isConflict = item.anomalyCategory.toLowerCase() === 'suspectconflict' || item.decisionRoute === 'HYBRID_CONFLICT'
  const isOpReviewed = item.opReviewMark === 'Overkill' || item.opReviewMark === 'Real Defect'

  const badgeTone: StatusName = isRealNg ? 'real' : isLeakage ? 'leakage' : isOverkill ? 'overkill' : isConflict ? 'info' : isNa ? 'na' : 'ok'
  const badgeLabel = isOpReviewed
    ? (item.opReviewMark === 'Real Defect' ? 'REAL DEFECT (OP)' : 'OVERKILL (OP)')
    : isRealNg
    ? (item.decisionRoute === 'HYBRID_CONSENSUS' ? 'REAL NG (CONSENSUS)' : item.decisionRoute === 'AI_DEEPLEARNING' ? 'REAL NG (AI)' : 'REAL NG (RULE)')
    : isOverkill
    ? (item.decisionRoute === 'HYBRID_CONSENSUS' ? 'OVERKILL (CONSENSUS)' : item.decisionRoute === 'AI_DEEPLEARNING' ? 'OVERKILL (AI: OK)' : 'OVERKILL (RULE)')
    : isConflict
    ? 'CONFLICT SUSPECT'
    : isLeakage
    ? 'LEAKAGE'
    : isNa
    ? 'N/A'
    : 'CLEAN OK'

  const originalVp = originalVpOf(item)
  const activeVp = vpState.selected || defaultVpOf(item)
  const activeImgSrc = vpState.images[activeVp] ||
    vpState.images[activeVp === originalVp ? 'original' : ''] ||
    (activeVp === '0'
      ? (item.thumbnailUrl || (item.defectId ? imageCrawlerApi.previewUrl(item.defectId, false, '0') : ''))
      : (item.defectId ? imageCrawlerApi.previewUrl(item.defectId, false, activeVp) : ''))
  const isCardLoading = vpState.loading !== null


  return (
      <article
        className="panel vstack"
        style={{
          padding: 0,
          overflow: 'hidden',
          border: `1px solid ${badgeTone === 'ok' ? 'var(--slate-200)' : `var(--status-${badgeTone}-border)`}`,
        }}
      >
        {/* Thumbnail Image Container */}
        <div
          style={{
            position: 'relative',
            height: '160px',
            background: 'var(--slate-50)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            cursor: activeImgSrc ? 'pointer' : 'default',
          }}
          {...activatable(() => onZoom(item, activeVp), { disabled: !activeImgSrc, label: activeImgSrc ? `Enlarge image of ${item.keyId}` : undefined })}
          title={activeImgSrc ? 'Enlarge image' : 'No image available'}
        >
          {isCardLoading ? (
            <div className="text-info text-xs" style={{ textAlign: 'center' }}>
              <div><Icon name="clock" className="icon inline-icon" />Loading image...</div>
              <div className="text-xs text-muted">VP{vpState.loading} from PC</div>
            </div>
          ) : activeImgSrc ? (
            <LazyImage
              src={activeImgSrc}
              alt={item.defectName ?? 'Defect'}
              style={{
                maxWidth: '100%',
                maxHeight: '100%',
                objectFit: 'contain',
              }}
            />
          ) : (
            <div className="text-soft text-xs" style={{ textAlign: 'center' }}>
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                <circle cx="8.5" cy="8.5" r="1.5" />
                <polyline points="21 15 16 10 5 21" />
              </svg>
              <div className="mt-1">No thumbnail image</div>
            </div>
          )}

          {/* Status Badge Overlay */}
          <span
            className="tag-badge font-bold text-xs"
            style={{
              position: 'absolute',
              top: '8px',
              left: '8px',
              ...statusSolid(badgeTone),
              padding: '3px 8px',
              borderRadius: '4px',
              backdropFilter: 'blur(4px)',
            }}
          >
            {badgeLabel}
          </span>

          {isOpReviewed && (
            <span
              className="tag-badge font-bold text-xs"
              style={{
                position: 'absolute',
                top: '38px',
                left: '8px',
                background: item.opReviewMark === 'Overkill' ? 'var(--status-overkill)' : 'var(--status-real)',
                color: '#fff',
                border: '1px solid var(--slate-200)',
              }}
            >
              OP: {item.opReviewMark}
            </span>
          )}

          {/* Real Defect Camera Angle Badge */}
          <span
            className="tag-badge font-bold text-xs"
            style={{
              position: 'absolute',
              top: '8px',
              right: '8px',
              background: 'var(--status-real)',
              color: '#fff',
              padding: '2px 7px',
              borderRadius: '4px',
              backdropFilter: 'blur(4px)',
              border: '1px solid rgba(255, 255, 255, 0.7)',
              boxShadow: '0 1px 3px rgba(0,0,0,0.4)',
            }}
            title={`Original VP from image_path: VP${originalVp}`}
          >
            Original VP: VP{originalVp}
          </span>

          {/* Station Judge vs Rule Verdict vs AI Verdict Overlay */}
          <div
            className="gap-xs"
            style={{
              position: 'absolute',
              bottom: '6px',
              right: '6px',
              display: 'flex',
              flexWrap: 'wrap',
              justifyContent: 'flex-end',
            }}
          >
            <span
              className="tag-badge text-xs font-bold"
              style={{
                background: item.stationJudge === 'NG' ? 'var(--status-ng)' : 'var(--status-ok)',
                color: '#fff',
              }}
              title={`Station Judgment: ${item.stationJudge}`}
            >
              Station: {item.stationJudge}
            </span>
            <span
              className="tag-badge text-xs font-bold"
              style={{
                background: item.ruleVerdict === 'TrueDefect' || item.ruleVerdict === 'RealNG'
                  ? 'var(--status-real)'
                  : (item.ruleCode && item.ruleCode !== 'NO_SCRIPT' ? 'var(--status-overkill)' : 'var(--slate-500)'),
                color: '#fff',
              }}
              title={item.ruleCode && item.ruleCode !== 'NO_SCRIPT' ? `Rule [${item.ruleCode}]: ${item.ruleVerdict}` : 'No script configured'}
            >
              Rule: {item.ruleVerdict === 'TrueDefect' || item.ruleVerdict === 'RealNG' ? 'NG' : (item.ruleCode && item.ruleCode !== 'NO_SCRIPT' ? 'OK' : 'N/A')}
            </span>
            {item.aiVerdict ? (
              <span
                className="tag-badge text-xs font-bold"
                style={{
                  background: item.aiVerdict === 'NG' ? 'var(--status-ng)' : 'var(--status-ok)',
                  color: '#fff',
                }}
                title={`AI Deep Learning Verdict: ${item.aiVerdict}${item.aiAnomalyScore != null ? ` (score: ${item.aiAnomalyScore.toFixed(2)})` : ''}`}
              >
                AI: {item.aiVerdict}
              </span>
            ) : item.aiStatus === 'Submitted' ? (
              <span
                className="tag-badge text-xs font-bold"
                style={{ background: 'var(--slate-500)', color: '#fff' }}
                title="AI Verification in progress..."
              >
                AI: Pending
              </span>
            ) : null}
          </div>
        </div>

        {/* Card Content Body */}
        <div className="vstack gap-sm" style={{ padding: '12px', flex: 1 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
            <div>
              <strong className="mono-code text-sm text-strong">
                {item.keyId}
              </strong>
              {item.componentName && (
                <span className="text-xs text-muted" style={{ marginLeft: '6px' }}>
                  ({item.componentName})
                </span>
              )}
              {item.panelId ? (
                <div className="text-xs text-muted" style={{ marginTop: '2px' }}>
                  Panel: <span className="mono-code">{item.panelId}</span>
                </div>
              ) : null}
            </div>
            <span className="text-xs font-mono text-muted">{item.lineCode}</span>
          </div>

          <div className="hstack gap-sm">
            <strong className="text-xs text-soft">{item.defectName || 'Unnamed defect'}</strong>
            {item.defectFamily && <StatusPill value={item.defectFamily} />}
          </div>

          {item.decisionRoute && (
            <div className="hstack gap-xs text-xs" style={{ alignItems: 'center' }}>
              <span className="text-muted" style={{ fontSize: '11px' }}>Route:</span>
              <span
                className="tag-badge text-xs font-medium"
                style={{
                  background: item.decisionRoute === 'HYBRID_CONSENSUS' ? 'var(--blue-100)' :
                              item.decisionRoute === 'AI_DEEPLEARNING' ? 'var(--status-info-bg)' :
                              item.decisionRoute === 'RULE_FAST_PATH' ? 'var(--status-real-bg)' :
                              item.decisionRoute === 'HYBRID_CONFLICT' ? 'var(--amber-100)' : 'var(--slate-100)',
                  color: item.decisionRoute === 'HYBRID_CONSENSUS' ? 'var(--blue-800)' :
                         item.decisionRoute === 'AI_DEEPLEARNING' ? 'var(--blue-700)' :
                         item.decisionRoute === 'RULE_FAST_PATH' ? 'var(--status-real-text)' :
                         item.decisionRoute === 'HYBRID_CONFLICT' ? 'var(--amber-800)' : 'var(--slate-700)',
                  padding: '1px 6px',
                  borderRadius: '3px',
                }}
              >
                {item.decisionRoute === 'HYBRID_CONSENSUS' && '⚖️ Consensus (Rule + AI)'}
                {item.decisionRoute === 'AI_DEEPLEARNING' && '🧠 AI Deep Learning'}
                {item.decisionRoute === 'RULE_FAST_PATH' && '⚡ Rule Fast-Path'}
                {item.decisionRoute === 'HYBRID_CONFLICT' && '⚠️ Rule vs AI Conflict'}
                {item.decisionRoute === 'OP_OVERRIDE' && '👤 Operator Ground Truth'}
                {item.decisionRoute === 'UNCLASSIFIED' && '❓ Unclassified'}
              </span>
            </div>
          )}

          {(item.productNameDefect || item.productDefectName) && (
            <div className="text-xs text-info">
              Product defect: <strong>{item.productNameDefect || item.productDefectName}</strong>
              {item.productDefectName && item.productDefectName !== item.productNameDefect ? ` · ${item.productDefectName}` : ''}
            </div>
          )}

          {/* Decision Reason */}
          <p
            className="text-xs text-muted"
            style={{
              margin: '4px 0',
              lineHeight: 1.4,
              display: '-webkit-box',
              WebkitLineClamp: 2,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden',
            }}
            title={item.decisionReason}
          >
            {item.decisionReason}
          </p>

          {/* Feature Chips */}
          {item.keyFeatures && Object.keys(item.keyFeatures).length > 0 && (
            <div className="gap-xs" style={{ display: 'flex', flexWrap: 'wrap', marginTop: 'auto' }}>
              {Object.entries(item.keyFeatures)
                .slice(0, 3)
                .map(([k, v]) => (
                  <span
                    key={k}
                    className="font-mono text-xs text-soft"
                    style={{
                      background: 'var(--slate-50)',
                      padding: '1px 6px',
                      borderRadius: '3px',
                    }}
                  >
                    {k}: <b>{typeof v === 'number' ? v.toFixed(1) : v}</b>
                  </span>
                ))}
            </div>
          )}

          {/* Camera Angle Selector Bar */}
          {item.defectId && (
            <div className="hstack justify-between gap-xs flex-wrap" style={{ padding: '6px 0', borderTop: '1px solid var(--slate-200)' }}>
              <span className="text-xs text-muted font-semibold">Camera Angle:</span>
              <div className="hstack flex-wrap" style={{ gap: '3px' }}>
                <button
                  className="text-xs"
                  type="button"
                  onClick={() => void selectCardVp(item, '0', false, onNotice)}
                  disabled={vpState.loading === '0'}
                  style={{
                    padding: '2px 6px',
                    fontWeight: activeVp === '0' ? 700 : 500,
                    borderRadius: '4px',
                    cursor: 'pointer',
                    border: activeVp === '0' ? '1px solid var(--blue-800)' : '1px solid var(--slate-200)',
                    background: activeVp === '0' ? 'var(--blue-800)' : 'var(--slate-50)',
                    color: activeVp === '0' ? '#ffffff' : 'var(--slate-600)',
                  }}
                  title="VP0 (Default thumbnail)"
                >
                  VP0
                </button>

                <button
                  className="text-xs font-bold"
                  type="button"
                  onClick={() => void selectCardVp(item, originalVp, true, onNotice)}
                  disabled={vpState.loading === originalVp}
                  style={{
                    padding: '2px 8px',
                    borderRadius: '4px',
                    cursor: 'pointer',
                    border: (activeVp === 'original' || activeVp === originalVp) ? '1.5px solid var(--status-real-border)' : '1px solid var(--status-real-border)',
                    background: (activeVp === 'original' || activeVp === originalVp) ? 'var(--status-real)' : 'var(--status-real-bg)',
                    color: (activeVp === 'original' || activeVp === originalVp) ? '#ffffff' : 'var(--status-real-text)',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '3px',
                  }}
                  title={`Load original VP from image_path (VP${originalVp})`}
                >
                  <span>VP{originalVp} (Path original)</span>
                  {vpState.loading === originalVp ? (
                    <span className="text-xs"><Icon name="clock" className="icon inline-icon" /></span>
                  ) : vpState.images.original ? (
                    <span style={{ width: '5px', height: '5px', borderRadius: '50%', background: 'var(--status-ok)', display: 'inline-block' }} />
                  ) : null}
                </button>

                {['1', '2', '3', '4'].map((vp) => (
                  <button
                    className="text-xs"
                    key={vp}
                    type="button"
                    onClick={() => void selectCardVp(item, vp, false, onNotice)}
                    disabled={vpState.loading === vp}
                    style={{
                      padding: '2px 5px',
                      fontWeight: activeVp === vp ? 700 : 500,
                      borderRadius: '4px',
                      cursor: 'pointer',
                      border: activeVp === vp ? '1px solid var(--blue-800)' : '1px solid var(--slate-200)',
                      background: activeVp === vp ? 'var(--blue-800)' : 'transparent',
                      color: activeVp === vp ? '#ffffff' : 'var(--slate-500)',
                    }}
                    title={`Load camera angle VP${vp}`}
                  >
                    VP{vp}
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* Card Actions */}
          <div
            className="gap-sm"
            style={{
              display: 'flex',
              marginTop: '6px',
              paddingTop: '6px',
              borderTop: '1px solid var(--slate-200)',
            }}
          >
            <button
              type="button"
              className="ghost-button compact text-xs"
              style={{ flex: 1 }}
              onClick={() => onZoom(item, activeVp)}
            >
              <Icon name="search" className="icon inline-icon" />Zoom
            </button>
            {item.defectId && (
              <button
                type="button"
                className="secondary compact text-xs text-real"
                style={{
                  padding: '3px 8px',
                  background: activeVp === 'original' ? 'var(--status-real-bg)' : 'var(--status-real-bg)',
                  borderColor: 'var(--status-real-border)',
                }}
                onClick={() => void recrawlCardVp(item, activeVp === 'original' ? originalVp : activeVp, activeVp === 'original', onNotice)}
                title={activeVp === 'original' ? `Force crawl path original VP${originalVp}` : `Force crawl angle VP${activeVp} from PC API`}
              >
                <Icon name="operations" className="icon inline-icon" />Crawl {activeVp === 'original' ? `Path VP${originalVp}` : `VP${activeVp}`}
              </button>
            )}
            <button
              type="button"
              className="secondary compact text-xs"
              style={{ flex: 1 }}
              onClick={() => onInspect(item, activeVp)}
              disabled={inspectBusy}
            >
              <Icon name="search" className="icon inline-icon" />Inspect
            </button>
          </div>
        </div>
      </article>
  )
})
