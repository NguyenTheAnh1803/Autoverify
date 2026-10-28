import { useCallback, useEffect, useState } from 'react'
import { imageCrawlerApi } from '../../api'
import { Icon, ImageViewer, Modal } from '../../ui'
import type { AnomalyThumbnailItem, Notice } from '../../types'
import { cacheVpImage, getVpEntry, originalVpOf, useVpEntry, vpKey } from './vpImageStore'

/** Full-size evidence image for one gallery item, with every camera angle. Shares the card's fetched images. */
export function ThumbnailZoomModal({
  item,
  initialVp,
  onClose,
  onInspect,
  onNotice,
}: {
  item: AnomalyThumbnailItem
  initialVp: string
  onClose: () => void
  /** close this dialog and open the feature inspector for the item at the given angle */
  onInspect: (item: AnomalyThumbnailItem, vp: string) => void
  onNotice: (notice: Notice) => void
}) {
  const key = vpKey(item)
  const zoomVpImages = useVpEntry(key).images
  const [zoomVp, setZoomVp] = useState(initialVp)
  const [zoomLoading, setZoomLoading] = useState(false)

  const loadAngle = useCallback(async (vp: string, requestOriginal = false) => {
    if (!item.defectId) return
    const angle = requestOriginal ? 'original' : vp
    setZoomVp(angle)
    if (getVpEntry(key).images[angle]) return
    setZoomLoading(true)
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: angle, forceRefresh: false })
      if (res.success) cacheVpImage(key, angle, res.imageUrl || imageCrawlerApi.previewUrl(item.defectId, false, angle))
    } catch {
      // The viewer falls back to the preview URL for this angle.
    } finally {
      setZoomLoading(false)
    }
  }, [item.defectId, key])

  // Opening on an angle that has not been fetched yet fetches it.
  useEffect(() => {
    if (initialVp !== '0' && !getVpEntry(key).images[initialVp]) void loadAngle(initialVp, initialVp === 'original')
  }, [initialVp, key, loadAngle])

  async function recrawl() {
    if (!item.defectId) return
    setZoomLoading(true)
    try {
      const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: zoomVp, forceRefresh: true })
      if (res.success) {
        cacheVpImage(key, zoomVp, `${imageCrawlerApi.previewUrl(item.defectId, true, zoomVp)}&t=${Date.now()}`)
        onNotice({ tone: 'success', message: `Force crawled VP${zoomVp} image!` })
      } else {
        onNotice({ tone: 'error', message: `Crawl failed: ${res.errorMessage || 'Unknown error'}` })
      }
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to crawl image' })
    } finally {
      setZoomLoading(false)
    }
  }

  const zoomOriginalVp = originalVpOf(item)
  const currentZoomImgSrc = zoomVpImages[zoomVp] ||
    (zoomVp === '0'
      ? (item.thumbnailUrl || (item.defectId ? imageCrawlerApi.previewUrl(item.defectId, false, '0') : ''))
      : (item.defectId ? imageCrawlerApi.previewUrl(item.defectId, false, zoomVp) : ''))

  return (
<Modal
  title={`Evidence Image Inspection · ${item.keyId} · VP${zoomVp}`}
  description={`Station Judgment: ${item.stationJudge} · Rule Verdict: ${item.ruleVerdict} · Category: ${item.anomalyCategory}`}
  open
  onClose={onClose}
  wide
>
  <div className="vstack" style={{ gap: '14px' }}>
    {/* Camera Angle Selector Bar */}
    {item.defectId && (
      <div
        className="hstack justify-between flex-wrap gap-md"
        style={{
          background: 'var(--slate-50)',
          padding: '8px 12px',
          borderRadius: '6px',
          border: '1px solid var(--slate-200)',
        }}
      >
        <div className="hstack gap-sm flex-wrap">
          <span className="text-xs font-bold text-muted">Camera Angle:</span>
          <button
            className="text-xs font-bold"
            type="button"
            disabled={zoomLoading && zoomVp === 'original'}
            onClick={() => void loadAngle(zoomOriginalVp, true)}
            style={{
              padding: '4px 12px',
              borderRadius: '6px',
              cursor: 'pointer',
              border: zoomVp === 'original' ? '1.5px solid var(--status-real-border)' : '1px solid var(--status-real-border)',
              background: zoomVp === 'original' ? 'var(--status-real)' : 'var(--status-real-bg)',
              color: '#fff',
              display: 'inline-flex',
              alignItems: 'center',
              gap: '5px',
            }}
            title={`Load original VP from image_path (VP${zoomOriginalVp})`}
          >
            <span>Load Path Original VP (VP{zoomOriginalVp})</span>
            {zoomLoading && zoomVp === 'original' ? <span><Icon name="clock" className="icon inline-icon" /></span> : null}
          </button>

          <span style={{ width: '1px', height: '16px', background: 'var(--slate-100)', margin: '0 2px' }} />

          {['0', '1', '2', '3', '4'].map((vp) => {
            const isSelected = zoomVp === vp
            const isOriginal = vp === zoomOriginalVp
            return (
              <button
                className="text-xs"
                key={vp}
                type="button"
                onClick={() => void loadAngle(vp, false)}
                style={{
                  padding: '4px 9px',
                  fontWeight: isSelected ? 700 : 500,
                  borderRadius: '6px',
                  cursor: 'pointer',
                  border: isSelected ? '1.5px solid var(--status-info-border)' : isOriginal ? '1px dashed var(--status-real-border)' : '1px solid var(--slate-200)',
                  background: isSelected ? 'var(--blue-700)' : isOriginal ? 'var(--status-real-bg)' : 'var(--slate-50)',
                  color: isSelected ? '#fff' : isOriginal ? 'var(--status-real-text)' : 'var(--slate-600)',
                }}
              >
                {vp === '0' ? 'VP0 (Default)' : isOriginal ? `VP${vp} (Path Angle)` : `VP${vp}`}
              </button>
            )
          })}
        </div>

        <button
          type="button"
          className="secondary compact text-xs"
          style={{ padding: '4px 10px', background: 'var(--slate-100)' }}
          disabled={zoomLoading}
          onClick={() => void recrawl()}
        >
          {zoomLoading ? 'Loading...' : zoomVp === 'original' ? `Force Crawl Path Original VP${zoomOriginalVp}` : `Force Crawl VP${zoomVp}`}
        </button>
      </div>
    )}

    <ImageViewer
      src={currentZoomImgSrc}
      alt={`${item.keyId} · ${item.defectName || 'Defect Visual'} · VP${zoomVp}`}
      maxHeight="520px"
    />

    <div className="gap-lg" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(200px, 100%), 1fr))' }}>
      <div>
        <span className="text-xs text-muted">Product:</span>
        <div><strong className="mono-code">{item.keyId}</strong></div>
      </div>
      <div>
        <span className="text-xs text-muted">Panel ID:</span>
        <div><strong className="mono-code">{item.panelId || '—'}</strong></div>
      </div>
      <div>
        <span className="text-xs text-muted">Category:</span>
        <div>
          <span className="tag-badge" style={{ background: item.anomalyCategory === 'Overkill' ? 'var(--status-overkill)' : 'var(--status-ng)', color: '#fff' }}>
            {item.anomalyCategory}
          </span>
        </div>
      </div>
      <div>
        <span className="text-xs text-muted">Defect & Family:</span>
        <div><strong>{item.defectName}</strong> ({item.defectFamily})</div>
      </div>
      <div>
        <span className="text-xs text-muted">Coordinates:</span>
        <div className="font-mono">{item.coordinates || '—'}</div>
      </div>
      <div>
        <span className="text-xs text-muted">Classifier Rule:</span>
        <div className="font-mono text-xs">{item.ruleCode} v{item.ruleVersion}</div>
      </div>
    </div>

    <div style={{ background: 'var(--slate-50)', padding: '12px', borderRadius: '6px' }}>
      <span className="text-xs text-muted">Decision Explanation from Rule Classifier Engine:</span>
      <p className="mt-1 text-sm text-soft">{item.decisionReason}</p>
    </div>

    <div className="actions" style={{ justifyContent: 'flex-end' }}>
      <button
        type="button"
        className="secondary"
        onClick={() => onInspect(item, zoomVp)}
      >
        <Icon name="search" className="icon inline-icon" />Inspect Features & JSON
      </button>
      <button type="button" className="ghost-button" onClick={onClose}>
        Close
      </button>
    </div>
  </div>
</Modal>
  )
}
