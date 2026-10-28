import { useCallback, useSyncExternalStore } from 'react'
import { imageCrawlerApi } from '../../api'
import type { AnomalyThumbnailItem, Notice } from '../../types'

/**
 * Camera-angle (VP) state for each gallery card: which angle is shown, the image URLs already fetched per angle,
 * and the angle currently loading. The card and the zoom dialog read and write the same entry, and each card
 * subscribes to its own key only, so loading an angle on one card re-renders that card and nothing else.
 * Entries outlive the Overkill tab, so images fetched once are not fetched again when the user comes back.
 */
export type VpEntry = {
  /** angle picked by the user ('0'…'4' or 'original'); undefined = the card's default angle */
  selected?: string
  /** fetched image URL per angle key */
  images: Record<string, string>
  /** angle key being fetched, or null */
  loading: string | null
}

const EMPTY: VpEntry = { images: {}, loading: null }
const entries = new Map<string, VpEntry>()
const listeners = new Map<string, Set<() => void>>()

export const vpKey = (item: AnomalyThumbnailItem) => `${item.keyId}_${item.defectId || ''}`

export function getVpEntry(key: string): VpEntry {
  return entries.get(key) ?? EMPTY
}

function updateVpEntry(key: string, change: (entry: VpEntry) => VpEntry) {
  entries.set(key, change(getVpEntry(key)))
  listeners.get(key)?.forEach((notify) => notify())
}

export function useVpEntry(key: string): VpEntry {
  const subscribe = useCallback(
    (notify: () => void) => {
      let set = listeners.get(key)
      if (!set) listeners.set(key, (set = new Set()))
      set.add(notify)
      return () => {
        set.delete(notify)
        if (set.size === 0) listeners.delete(key)
      }
    },
    [key],
  )
  return useSyncExternalStore(subscribe, () => getVpEntry(key))
}

export function cacheVpImage(key: string, angle: string, url: string) {
  updateVpEntry(key, (entry) => ({ ...entry, images: { ...entry.images, [angle]: url } }))
}

/** Original VP is defined strictly by the source image_path, not classifier features. */
export function originalVpOf(item: AnomalyThumbnailItem): string {
  if (item.originalVp != null && item.originalVp !== '') return item.originalVp
  if (item.imageUri) {
    const folderMatch = item.imageUri.match(/[\\/]VP0*(\d+)[\\/]/i)
    if (folderMatch?.[1]) return String(parseInt(folderMatch[1], 10))
    const fileMatch = item.imageUri.match(/VP0*(\d+)[_.-]/i)
    if (fileMatch?.[1]) return String(parseInt(fileMatch[1], 10))
  }
  return '0'
}

/** The angle a card shows before the user picks one: the path's own angle when it has one, else VP0. */
export function defaultVpOf(item: AnomalyThumbnailItem) {
  const original = originalVpOf(item)
  return original && original !== '0' ? original : '0'
}

/** Show an angle on a card, fetching it from the PC image API the first time. */
export async function selectCardVp(item: AnomalyThumbnailItem, vp: string, requestOriginal: boolean, onNotice: (notice: Notice) => void) {
  if (!item.defectId) return
  const key = vpKey(item)
  const angle = requestOriginal ? 'original' : vp
  updateVpEntry(key, (entry) => ({ ...entry, selected: angle }))
  if (getVpEntry(key).images[angle]) return

  updateVpEntry(key, (entry) => ({ ...entry, loading: angle }))
  try {
    const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: angle, forceRefresh: false })
    if (res.success) {
      cacheVpImage(key, angle, res.imageUrl || imageCrawlerApi.previewUrl(item.defectId, false, angle))
      onNotice({ tone: 'success', message: `Loaded VP${vp} image for Defect ${item.defectName || item.keyId} (${res.sizeBytes.toLocaleString()} bytes)` })
    } else {
      onNotice({ tone: 'error', message: `Failed to load VP${vp} image: ${res.errorMessage || 'Unknown error'}` })
    }
  } catch (err) {
    onNotice({ tone: 'error', message: err instanceof Error ? err.message : `Failed to load VP${vp} image` })
  } finally {
    updateVpEntry(key, (entry) => ({ ...entry, loading: null }))
  }
}

/** Re-crawl an angle from the PC even when it is cached, and show it on the card. */
export async function recrawlCardVp(item: AnomalyThumbnailItem, vp: string, requestOriginal: boolean, onNotice: (notice: Notice) => void) {
  if (!item.defectId) return
  const key = vpKey(item)
  const angle = requestOriginal ? 'original' : vp
  updateVpEntry(key, (entry) => ({ ...entry, loading: angle }))
  try {
    const res = await imageCrawlerApi.crawlDefect({ defectId: item.defectId, customVp: angle, forceRefresh: true })
    if (res.success) {
      cacheVpImage(key, angle, `${imageCrawlerApi.previewUrl(item.defectId, true, angle)}&t=${Date.now()}`)
      updateVpEntry(key, (entry) => ({ ...entry, selected: angle }))
      onNotice({ tone: 'success', message: `Crawled VP${vp} image for ${item.defectName || item.keyId} (${res.sizeBytes.toLocaleString()} bytes in ${res.latencyMs}ms)!` })
    } else {
      onNotice({ tone: 'error', message: `Failed to crawl VP${vp}: ${res.errorMessage || 'Unknown error'}` })
    }
  } catch (err) {
    onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to crawl image' })
  } finally {
    updateVpEntry(key, (entry) => ({ ...entry, loading: null }))
  }
}
