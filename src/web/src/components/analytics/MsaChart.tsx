import React, { useMemo } from 'react'
import ReactECharts from '../ReactECharts'
import type { MsaResult } from '../../types'

export type MsaChartProps = {
  result: MsaResult
}

export const MsaChart: React.FC<MsaChartProps> = ({ result }) => {
  const {
    featureName,
    partCount,
    operatorCount,
    repeatCount,
    totalMeasurements,
    isBalanced,
    interactionSignificant,
    wasInteractionPooled,
    numberOfDistinctCategories,
    statusAssessment,
    anovaTable,
    varianceComponents,
    warnings,
    blockers,
  } = result

  // Build Variance Components Comparison Chart
  const option = useMemo(() => {
    if (!varianceComponents || varianceComponents.length === 0) return null

    const categories = varianceComponents.map((v) => v.source)
    const pctContrib = varianceComponents.map((v) => (v.percentContribution * 100).toFixed(2))
    const pctStudyVar = varianceComponents.map((v) => (v.percentStudyVar * 100).toFixed(2))

    return {
      title: {
        text: `Gage R&R Variance Components - ${featureName}`,
        left: 'left',
      },
      tooltip: {
        trigger: 'axis',
        formatter: (params: Array<{ seriesName: string; value: string; axisValue: string }>) => `
          <div style="font-family:var(--font-mono);">
            <div><strong>${params[0].axisValue}</strong></div>
            <div>% Contribution (Variance): ${params[0].value}%</div>
            <div>% Study Variation (SD): ${params[1]?.value}%</div>
          </div>
        `,
      },
      legend: {
        data: ['% Contribution (Variance)', '% Study Variation (SD)'],
        right: '20px',
        top: '5px',
      },
      grid: { left: 12, right: 24, top: 44, bottom: 14 },
      xAxis: {
        type: 'value',
        max: 100,
        axisLabel: { formatter: '{value}%' },
      },
      yAxis: {
        type: 'category',
        data: categories,
      },
      series: [
        {
          name: '% Contribution (Variance)',
          type: 'bar',
          data: pctContrib,
          itemStyle: { color: '#2f6f9f' },
        },
        {
          name: '% Study Variation (SD)',
          type: 'bar',
          data: pctStudyVar,
          itemStyle: { color: '#c8862b' },
        },
      ],
    }
  }, [varianceComponents, featureName])

  let ndcColor = 'badge-danger'
  let ndcRating = 'Unacceptable (NDC < 2)'
  if (numberOfDistinctCategories >= 5) {
    ndcColor = 'badge-success'
    ndcRating = 'Acceptable (NDC >= 5)'
  } else if (numberOfDistinctCategories >= 2) {
    ndcColor = 'badge-warning'
    ndcRating = 'Marginal / Conditional (2 <= NDC < 5)'
  }

  return (
    <div className="analytics-msa-view">
      {blockers && blockers.length > 0 && (
        <div className="analytics-alert-banner error my-2">
          <strong>MSA Execution Blocked:</strong> {blockers.join(' | ')}
        </div>
      )}

      {warnings && warnings.length > 0 && (
        <div className="analytics-alert-banner warning my-2 text-xs">
          <strong>MSA Diagnostic Warnings:</strong> {warnings.join(' | ')}
        </div>
      )}

      <div className="analytics-summary-cards my-3">
        <div className="analytics-stat-card">
          <span className="stat-label">Measurement Design</span>
          <span className="stat-value font-mono text-base">
            {partCount} Parts × {operatorCount} Operators × {repeatCount} Runs
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Total Sample Size</span>
          <span className="stat-value font-mono">{totalMeasurements}</span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">Design Balance</span>
          <span className={`stat-badge ${isBalanced ? 'badge-info' : 'badge-warning'}`}>
            {isBalanced ? 'Balanced Design' : 'Unbalanced (Type III SS)'}
          </span>
        </div>
        <div className="analytics-stat-card">
          <span className="stat-label">NDC (Distinct Categories)</span>
          <span className="stat-value font-mono">{numberOfDistinctCategories}</span>
          <span className={`stat-badge ${ndcColor} mt-1`}>{ndcRating}</span>
        </div>
      </div>

      {statusAssessment && (
        <div className="analytics-assessment-box my-2">
          <strong>AIAG Gage Assessment:</strong> {statusAssessment}
          {wasInteractionPooled && (
            <span className="ml-2 text-xs text-sky-400">
              (Interaction was statistically non-significant and pooled into repeatability error).
            </span>
          )}
        </div>
      )}

      {option && (
        <div className="echarts-container-card mt-3">
          <ReactECharts
            option={option}
            style={{ height: '320px', width: '100%' }}
          />
        </div>
      )}

      {/* Two-Way ANOVA Table */}
      <div className="msa-anova-section mt-4">
        <div className="section-header">
          <h4>Two-Way ANOVA Table with Interaction (Part × Operator)</h4>
        </div>
        <div className="table-responsive">
          <table className="analytics-data-table" role="table">
            <thead>
              <tr>
                <th>Source of Variation</th>
                <th className="num">DF</th>
                <th className="num">Sum of Squares (SS)</th>
                <th className="num">Mean Square (MS)</th>
                <th className="num">F-Statistic</th>
                <th className="num">P-Value</th>
                <th>Interpretation</th>
              </tr>
            </thead>
            <tbody>
              {anovaTable.map((row) => {
                const isSig = row.pValue !== null && row.pValue !== undefined && row.pValue < 0.05
                return (
                  <tr key={row.source}>
                    <td className="font-bold">{row.source}</td>
                    <td className="num font-mono">{row.degreesOfFreedom}</td>
                    <td className="num font-mono">{row.sumOfSquares.toFixed(4)}</td>
                    <td className="num font-mono">{row.meanSquare.toFixed(4)}</td>
                    <td className="num font-mono">
                      {row.fStatistic !== null && row.fStatistic !== undefined ? row.fStatistic.toFixed(3) : '-'}
                    </td>
                    <td className={`num font-mono ${isSig ? 'text-rose-400 font-bold' : ''}`}>
                      {row.pValue !== null && row.pValue !== undefined ? row.pValue.toFixed(4) : '-'}
                    </td>
                    <td>
                      {row.source.includes('Operator') && isSig ? (
                        <span className="pill-warning">Operator bias detected</span>
                      ) : row.source.includes('Part') && isSig ? (
                        <span className="pill-success">Part-to-part variation detected</span>
                      ) : row.source.includes('*') ? (
                        <span>{interactionSignificant ? 'Significant interaction' : 'Non-significant'}</span>
                      ) : (
                        '-'
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
