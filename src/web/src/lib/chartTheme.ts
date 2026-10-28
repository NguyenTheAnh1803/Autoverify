/**
 * One light, industrial look for every chart: muted palette, hairline grid, no glow, straight lines.
 * The theme is registered as `autoverify` in `lib/echarts.ts`. The helpers below build the pieces that
 * quality charts need (time axis, measurement line, control / spec lines, zoom, compact tooltip) so each
 * chart does not hand-roll its own colours and axis styles.
 */

import { STATUS } from './status'

export const CHART_THEME_NAME = 'autoverify'

/** Same stack as index.css. Chart labels are never smaller than 11px (canvas has no zoom-friendly rem). */
const CHART_FONT = '"Segoe UI", system-ui, -apple-system, Arial, sans-serif'

/** Palette. `bad` is reserved for out-of-control / failed points; `spec` for USL/LSL. */
export const CHART = {
  series: ['#2f6f9f', '#3a9d8a', '#c8862b', '#6b7a90', '#8a5a9c', '#b4546a', '#5b8c3a', '#9a6b3c'],
  primary: STATUS.info.fill,
  good: STATUS.ok.fill,
  warn: STATUS.overkill.fill,
  bad: STATUS.ng.fill,
  muted: '#6b7a90',
  text: '#1e293b',
  textMuted: '#5b6b82',
  axis: '#9aa7b8',
  grid: '#e6ebf2',
  bandA: 'rgba(192,57,43,.06)',
  bandB: 'rgba(200,134,43,.07)',
  bandC: 'rgba(58,157,138,.06)',
} as const

const axisBase = {
  axisLine: { show: true, lineStyle: { color: CHART.axis, width: 1 } },
  axisTick: { show: true, length: 3, lineStyle: { color: CHART.axis } },
  axisLabel: { color: CHART.textMuted, fontSize: 11, hideOverlap: true },
  splitLine: { show: true, lineStyle: { color: CHART.grid, width: 1 } },
  nameTextStyle: { color: CHART.textMuted, fontSize: 11 },
  splitArea: { show: false },
}

export const chartTheme = {
  color: [...CHART.series],
  backgroundColor: 'transparent',
  animationDuration: 300,
  animationDurationUpdate: 200,
  // Canvas text cannot inherit from CSS, so the app's sans stack is spelled out; tabular figures keep axis values aligned.
  textStyle: { color: CHART.text, fontFamily: CHART_FONT },
  title: {
    textStyle: { color: CHART.text, fontSize: 13, fontWeight: 600 },
    subtextStyle: { color: CHART.textMuted, fontSize: 11 },
  },
  grid: { containLabel: true },
  categoryAxis: { ...axisBase, splitLine: { ...axisBase.splitLine, show: false } },
  valueAxis: {
    ...axisBase,
    axisLine: { ...axisBase.axisLine, show: false },
    axisTick: { ...axisBase.axisTick, show: false },
  },
  timeAxis: { ...axisBase, splitLine: { ...axisBase.splitLine, show: false } },
  logAxis: axisBase,
  line: { smooth: false, symbol: 'circle', symbolSize: 4, lineStyle: { width: 1.5 } },
  scatter: { symbolSize: 6, itemStyle: { opacity: 0.8 } },
  bar: { barMaxWidth: 28, itemStyle: { borderRadius: 0 } },
  legend: {
    textStyle: { color: '#475569', fontSize: 11 },
    itemWidth: 14,
    itemHeight: 8,
    icon: 'roundRect',
  },
  tooltip: {
    backgroundColor: '#ffffff',
    borderColor: '#cbd5e1',
    borderWidth: 1,
    padding: [6, 8],
    textStyle: { color: CHART.text, fontSize: 12 },
    extraCssText: 'border-radius:4px;box-shadow:0 2px 6px rgba(15,23,42,.12);',
    axisPointer: {
      lineStyle: { color: CHART.axis, type: 'dashed', width: 1 },
      crossStyle: { color: CHART.axis },
      label: { backgroundColor: '#475569', color: '#ffffff' },
    },
  },
  dataZoom: {
    borderColor: CHART.grid,
    backgroundColor: 'rgba(241,245,249,.6)',
    fillerColor: 'rgba(47,111,159,.14)',
    handleColor: CHART.primary,
    textStyle: { color: CHART.textMuted, fontSize: 11 },
    dataBackground: {
      lineStyle: { color: CHART.axis, width: 1 },
      areaStyle: { color: 'rgba(154,167,184,.18)' },
    },
  },
  visualMap: { textStyle: { color: CHART.textMuted, fontSize: 11 } },
  toolbox: { iconStyle: { borderColor: CHART.textMuted }, emphasis: { iconStyle: { borderColor: CHART.primary } } },
}

const esc = (value: unknown) =>
  String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string)

/** Compact tooltip body: a bold title, then label / value rows. Values are HTML-escaped. */
export function tooltipHtml(title: string, rows: Array<[label: string, value: unknown]>, footer?: string) {
  const body = rows
    .map(([label, value]) =>
      `<tr><td style="color:${CHART.textMuted};padding:1px 12px 1px 0">${esc(label)}</td>` +
      `<td style="text-align:right;font-variant-numeric:tabular-nums">${esc(value)}</td></tr>`)
    .join('')
  return `<div style="font-weight:600;margin-bottom:3px">${esc(title)}</div>` +
    `<table style="border-collapse:collapse">${body}</table>` +
    (footer ? `<div style="margin-top:3px;color:${CHART.textMuted}">${esc(footer)}</div>` : '')
}

/** Real time axis (values are epoch ms). Labels thin themselves out instead of rotating and overlapping. */
export function timeAxis(extra: Record<string, unknown> = {}) {
  return {
    type: 'time',
    axisLabel: {
      hideOverlap: true,
      rotate: 0,
      formatter: {
        year: '{yyyy}',
        month: '{MM}-{dd}',
        day: '{MM}-{dd}',
        hour: '{HH}:{mm}',
        minute: '{HH}:{mm}',
        second: '{HH}:{mm}:{ss}',
        millisecond: '{HH}:{mm}:{ss}',
        none: '{MM}-{dd} {HH}:{mm}',
      },
    },
    ...extra,
  }
}

export function valueAxis(name?: string, extra: Record<string, unknown> = {}) {
  return { type: 'value', name, scale: true, ...extra }
}

/** Measurement series: straight segments, thin line, no area. Large series are down-sampled for speed. */
export function measureLine(name: string, data: unknown[], extra: Record<string, unknown> = {}) {
  return {
    name,
    type: 'line',
    data,
    smooth: false,
    showSymbol: data.length <= 120,
    symbolSize: 4,
    sampling: data.length > 2000 ? 'lttb' : undefined,
    lineStyle: { width: 1.25 },
    ...extra,
  }
}

export type RefLineKind = 'mean' | 'control' | 'spec'

const REF_STYLE: Record<RefLineKind, { color: string; type: 'solid' | 'dashed'; width: number }> = {
  mean: { color: CHART.muted, type: 'solid', width: 1 },
  control: { color: CHART.warn, type: 'dashed', width: 1 },
  spec: { color: CHART.bad, type: 'solid', width: 1.25 },
}

/** Horizontal reference lines (mean, UCL/LCL, USL/LSL). Entries with a non-finite value are skipped. */
export function refLines(lines: Array<{ name: string; value: number | null | undefined; kind?: RefLineKind }>, digits = 3) {
  const data = lines
    .filter((line) => typeof line.value === 'number' && Number.isFinite(line.value))
    .map((line) => {
      const style = REF_STYLE[line.kind ?? 'control']
      return {
        name: line.name,
        yAxis: line.value as number,
        lineStyle: style,
        label: {
          color: style.color,
          formatter: (p: { value?: unknown }) => `${line.name} ${Number(p.value).toFixed(digits)}`,
          position: 'insideEndTop',
          fontSize: 11,
        },
      }
    })
  return { silent: true, symbol: 'none', animation: false, data }
}

/** Mouse-wheel / drag zoom plus a slim slider under the plot. Reserve ~34px of grid.bottom for the slider. */
export function zoom(axisIndex = 0) {
  return [
    { type: 'inside', xAxisIndex: axisIndex, filterMode: 'none' },
    { type: 'slider', xAxisIndex: axisIndex, height: 16, bottom: 6, filterMode: 'none', brushSelect: false },
  ]
}

/** Save-as-image + reset buttons. */
export function toolbox() {
  return {
    right: 8,
    top: 0,
    itemSize: 13,
    feature: { restore: { title: 'Reset' }, saveAsImage: { title: 'Save', pixelRatio: 2 } },
  }
}

/** Shaded SPC zones: C within 1σ, B 1–2σ, A 2–3σ either side of the centre line (σ taken from the control limits). */
export function spcZones(cl: number, ucl: number, lcl: number) {
  const sigma = (ucl - cl) / 3
  if (!(sigma > 0) || !Number.isFinite(sigma)) return undefined
  const band = (lo: number, hi: number, color: string) => [{ yAxis: lo, itemStyle: { color } }, { yAxis: hi }]
  return {
    silent: true,
    data: [
      band(cl + 2 * sigma, ucl, CHART.bandA),
      band(cl + sigma, cl + 2 * sigma, CHART.bandB),
      band(cl - sigma, cl + sigma, CHART.bandC),
      band(cl - 2 * sigma, cl - sigma, CHART.bandB),
      band(lcl, cl - 2 * sigma, CHART.bandA),
    ],
  }
}
