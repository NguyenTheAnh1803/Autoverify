import ReactEChartsCore from 'echarts-for-react/esm/core'
import type { ComponentProps } from 'react'
import echarts from '../lib/echarts'

type Props = Omit<ComponentProps<typeof ReactEChartsCore>, 'echarts'>

/** The actual chart. Kept in its own file so ECharts is downloaded only when a chart first scrolls into view. */
export default function ReactEChartsImpl(props: Props) {
  return <ReactEChartsCore echarts={echarts} {...props} />
}
