/**
 * ECharts with only the parts this app uses. `import 'echarts'` pulls in every chart, component and renderer
 * (~1 MB); registering by hand keeps the chart chunk much smaller.
 * If a chart option ever needs something not listed here, ECharts logs "Component xxx is used but not imported":
 * add the matching component/chart below.
 */
import * as echarts from 'echarts/core'
import { BarChart, HeatmapChart, LineChart, PieChart, ScatterChart } from 'echarts/charts'
import {
  AxisPointerComponent,
  DataZoomComponent,
  GraphicComponent,
  GridComponent,
  LegendComponent,
  MarkAreaComponent,
  MarkLineComponent,
  TitleComponent,
  ToolboxComponent,
  TooltipComponent,
  VisualMapComponent,
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { CHART_THEME_NAME, chartTheme } from './chartTheme'

echarts.use([
  BarChart,
  LineChart,
  ScatterChart,
  PieChart,
  HeatmapChart,
  AxisPointerComponent,
  DataZoomComponent,
  GraphicComponent,
  GridComponent,
  LegendComponent,
  MarkAreaComponent,
  MarkLineComponent,
  TitleComponent,
  ToolboxComponent,
  TooltipComponent,
  VisualMapComponent,
  CanvasRenderer,
])

echarts.registerTheme(CHART_THEME_NAME, chartTheme)

export default echarts
