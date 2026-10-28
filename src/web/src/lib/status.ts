/**
 * Inspection verdict colours: one hue per verdict, used the same way in badges, table cells, chips and charts.
 * `fill` is for dots, bars and solid badges (white text on it is ≥4.5:1); `text` is for coloured words on white;
 * `bg` / `border` are the tinted surface of a chip or callout.
 * The same values are exposed to CSS as `--status-<name>[-text|-bg|-border]` in App.css; keep the two in step.
 * Canvas (ECharts) cannot resolve CSS variables, which is why the hex values live here too.
 */
export const STATUS = {
  ok: { fill: '#25806a', text: '#1d6b58', bg: '#ecf7f3', border: '#b9dfd3' },
  ng: { fill: '#c9433a', text: '#ad2b21', bg: '#fdeeec', border: '#f3c1bb' },
  real: { fill: '#7b4fa6', text: '#64388d', bg: '#f4eefa', border: '#d8c7ea' },
  overkill: { fill: '#a5640b', text: '#8f540a', bg: '#fdf3e3', border: '#efd09a' },
  leakage: { fill: '#8f1d2c', text: '#7a1524', bg: '#f9e8ea', border: '#e2aab2' },
  na: { fill: '#5f6e84', text: '#4f5d72', bg: '#f1f4f8', border: '#d3dae4' },
  info: { fill: '#2f6f9f', text: '#245a83', bg: '#eaf2f9', border: '#bdd5ea' },
} as const

export type StatusName = keyof typeof STATUS

/** Tinted chip: the verdict colour as words on its pale surface, with a matching hairline. */
export function statusChip(name: StatusName) {
  return { background: `var(--status-${name}-bg)`, color: `var(--status-${name}-text)`, border: `1px solid var(--status-${name}-border)` }
}

/** Solid badge, kept for the one state on a row that has to stand out (white on the verdict colour). */
export function statusSolid(name: StatusName) {
  return { background: `var(--status-${name})`, color: '#ffffff', border: `1px solid var(--status-${name})` }
}
