import React, { useState } from 'react'
import type {
  SessionScopeFacetsResponse,
  AnalyticsScopeFilter,
  ClassifierRuleDto,
} from '../../types'

export type AnalyticsScopePanelProps = {
  facets: SessionScopeFacetsResponse | null
  scope: AnalyticsScopeFilter
  onChangeScope: (scope: AnalyticsScopeFilter) => void
  resolvedRule: ClassifierRuleDto | null
  ruleResolutionKind?: string | null
  ruleResolutionScore?: number | null
  ruleResolutionMessage?: string | null
  loadingRule?: boolean
  totalRecords?: number
  onResetScope: () => void
}

export const AnalyticsScopePanel: React.FC<AnalyticsScopePanelProps> = ({
  facets,
  scope,
  onChangeScope,
  resolvedRule,
  ruleResolutionKind,
  ruleResolutionScore,
  ruleResolutionMessage,
  loadingRule,
  totalRecords,
  onResetScope,
}) => {
  const [isExpanded, setIsExpanded] = useState<boolean>(true)
  const [showThresholdsJson, setShowThresholdsJson] = useState<boolean>(false)

  const hasActiveFilters = Boolean(
    scope.productKey ||
    scope.lineCode ||
    scope.machineCode ||
    scope.stationCode ||
    scope.classifierRuleCode ||
    scope.classifierRuleVersion ||
    scope.defectFamily ||
    scope.defectType ||
    scope.timeFromUtc ||
    scope.timeToUtc
  )

  const handleFieldChange = (field: keyof AnalyticsScopeFilter, value: string) => {
    onChangeScope({
      ...scope,
      [field]: value.trim() ? value : null,
    })
  }

  const renderFacetOptions = (
    items: Array<{ value: string; count: number }> | undefined,
    placeholder: string
  ) => {
    return (
      <>
        <option value="">{placeholder}</option>
        {items?.map((item) => {
          const isUnknown = !item.value || item.value === 'Unknown' || item.value === 'Unassigned'
          const label = isUnknown ? `Unknown / Unassigned (${item.count.toLocaleString()})` : `${item.value} (${item.count.toLocaleString()})`
          return (
            <option key={item.value || 'unknown'} value={item.value}>
              {label}
            </option>
          )
        })}
      </>
    )
  }

  return (
    <div className="analytics-scope-panel">
      <div className="analytics-scope-header">
        <div className="analytics-scope-title-group">
          <svg className="analytics-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />
          </svg>
          <span className="analytics-scope-title">Analytical Scope & Classifier Context</span>
          {totalRecords !== undefined && (
            <span className="analytics-scope-count-badge font-mono">
              {totalRecords.toLocaleString()} Obs in Session
            </span>
          )}
          {hasActiveFilters && (
            <span className="analytics-scope-active-indicator">
              Filtered Scope Active
            </span>
          )}
        </div>

        <div className="analytics-scope-actions">
          {hasActiveFilters && (
            <button
              type="button"
              className="btn btn-xs btn-outline"
              onClick={onResetScope}
              title="Reset all analytical scope filters"
            >
              Reset Scope
            </button>
          )}
          <button
            type="button"
            className="btn btn-xs btn-outline"
            onClick={() => setIsExpanded(!isExpanded)}
            aria-expanded={isExpanded}
            aria-label={isExpanded ? 'Collapse scope filters' : 'Expand scope filters'}
          >
            {isExpanded ? 'Hide Filters' : 'Show Filters'}
          </button>
        </div>
      </div>

      {isExpanded && (
        <div className="analytics-scope-content">
          <div className="analytics-scope-grid">
            {/* Product Key */}
            <div className="form-group">
              <label htmlFor="scope-product-key" className="text-xs font-semibold">
                Product Key
              </label>
              <input
                id="scope-product-key"
                type="text"
                className="form-control text-xs"
                placeholder="All Product Keys"
                value={scope.productKey || ''}
                onChange={(e) => handleFieldChange('productKey', e.target.value)}
              />
            </div>

            {/* Line */}
            <div className="form-group">
              <label htmlFor="scope-line" className="text-xs font-semibold">
                Line Code
              </label>
              <select
                id="scope-line"
                className="form-control text-xs"
                value={scope.lineCode || ''}
                onChange={(e) => handleFieldChange('lineCode', e.target.value)}
              >
                {renderFacetOptions(facets?.lines, 'All Production Lines')}
              </select>
            </div>

            {/* Machine */}
            <div className="form-group">
              <label htmlFor="scope-machine" className="text-xs font-semibold">
                Machine Code
              </label>
              <select
                id="scope-machine"
                className="form-control text-xs"
                value={scope.machineCode || ''}
                onChange={(e) => handleFieldChange('machineCode', e.target.value)}
              >
                {renderFacetOptions(facets?.machines, 'All Machines')}
              </select>
            </div>

            {/* Station */}
            <div className="form-group">
              <label htmlFor="scope-station" className="text-xs font-semibold">
                Station Code
              </label>
              <select
                id="scope-station"
                className="form-control text-xs"
                value={scope.stationCode || ''}
                onChange={(e) => handleFieldChange('stationCode', e.target.value)}
              >
                {renderFacetOptions(facets?.stations, 'All Stations')}
              </select>
            </div>

            {/* Classifier Rule */}
            <div className="form-group">
              <label htmlFor="scope-rule" className="text-xs font-semibold">
                Classifier Rule
              </label>
              <select
                id="scope-rule"
                className="form-control text-xs"
                value={scope.classifierRuleCode || ''}
                onChange={(e) => handleFieldChange('classifierRuleCode', e.target.value)}
              >
                {renderFacetOptions(facets?.classifierRules, 'All Classifier Rules')}
              </select>
            </div>

            {/* Classifier Version */}
            <div className="form-group">
              <label htmlFor="scope-rule-version" className="text-xs font-semibold">
                Rule Version
              </label>
              <select
                id="scope-rule-version"
                className="form-control text-xs"
                value={scope.classifierRuleVersion || ''}
                onChange={(e) => handleFieldChange('classifierRuleVersion', e.target.value)}
              >
                {renderFacetOptions(facets?.classifierRuleVersions, 'All Rule Versions')}
              </select>
            </div>

            {/* Defect Family */}
            <div className="form-group">
              <label htmlFor="scope-family" className="text-xs font-semibold">
                Defect Family
              </label>
              <select
                id="scope-family"
                className="form-control text-xs"
                value={scope.defectFamily || ''}
                onChange={(e) => handleFieldChange('defectFamily', e.target.value)}
              >
                {renderFacetOptions(facets?.defectFamilies, 'All Defect Families')}
              </select>
            </div>

            {/* Defect Type / Class */}
            <div className="form-group">
              <label htmlFor="scope-type" className="text-xs font-semibold">
                Defect Class / Type
              </label>
              <select
                id="scope-type"
                className="form-control text-xs"
                value={scope.defectType || ''}
                onChange={(e) => handleFieldChange('defectType', e.target.value)}
              >
                {renderFacetOptions(facets?.defectTypes, 'All Defect Types')}
              </select>
            </div>

            {/* Time From */}
            <div className="form-group">
              <label htmlFor="scope-time-from" className="text-xs font-semibold">
                Inspected From (UTC)
              </label>
              <input
                id="scope-time-from"
                type="datetime-local"
                className="form-control text-xs"
                value={scope.timeFromUtc ? scope.timeFromUtc.slice(0, 16) : ''}
                onChange={(e) =>
                  handleFieldChange(
                    'timeFromUtc',
                    e.target.value ? new Date(e.target.value).toISOString() : ''
                  )
                }
              />
            </div>

            {/* Time To */}
            <div className="form-group">
              <label htmlFor="scope-time-to" className="text-xs font-semibold">
                Inspected To (UTC)
              </label>
              <input
                id="scope-time-to"
                type="datetime-local"
                className="form-control text-xs"
                value={scope.timeToUtc ? scope.timeToUtc.slice(0, 16) : ''}
                onChange={(e) =>
                  handleFieldChange(
                    'timeToUtc',
                    e.target.value ? new Date(e.target.value).toISOString() : ''
                  )
                }
              />
            </div>
          </div>

          {/* Classifier Rule Context Overlay */}
          <div className="analytics-rule-context-card">
            <div className="rule-context-header">
              <div className="rule-context-title">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                </svg>
                <span>Machine/Line Classifier Rule Evidence</span>
              </div>
              {loadingRule && <span className="text-xs text-muted">Resolving rule...</span>}
            </div>

            {resolvedRule ? (
              <div className="rule-context-body">
                <div className="rule-badge-row">
                  <span className="rule-code-badge font-mono font-bold">
                    {resolvedRule.ruleCode}
                  </span>
                  <span className="rule-version-badge font-mono">
                    v{resolvedRule.version}
                  </span>
                  <span className="rule-name-label">{resolvedRule.ruleName}</span>
                  {ruleResolutionKind && (
                    <span className="badge-info font-mono text-xs">
                      Match: {ruleResolutionKind} (Score {ruleResolutionScore ?? 0})
                    </span>
                  )}
                  <span className={`status-pill ${resolvedRule.isActive ? 'pill-success' : 'pill-warning'}`}>
                    {resolvedRule.isActive ? 'Active' : 'Inactive'}
                  </span>
                </div>

                <div className="rule-metadata-grid">
                  <div>
                    <span className="meta-label">Line:</span>{' '}
                    <span className="meta-val font-mono">{resolvedRule.lineCode || 'Any'}</span>
                  </div>
                  <div>
                    <span className="meta-label">Machine:</span>{' '}
                    <span className="meta-val font-mono">{resolvedRule.machineCode || 'Any'}</span>
                  </div>
                  <div>
                    <span className="meta-label">Station:</span>{' '}
                    <span className="meta-val font-mono">{resolvedRule.stationCode || 'Any'}</span>
                  </div>
                  <div>
                    <span className="meta-label">Family / Class:</span>{' '}
                    <span className="meta-val font-mono">
                      {resolvedRule.defectFamily || 'Any'} / {resolvedRule.defectClass || 'Any'}
                    </span>
                  </div>
                  <div>
                    <span className="meta-label">Effective Window:</span>{' '}
                    <span className="meta-val font-mono text-xs">
                      {resolvedRule.effectiveFromUtc ? new Date(resolvedRule.effectiveFromUtc).toLocaleDateString() : 'Immediate'}
                      {' \u2192 '}
                      {resolvedRule.effectiveToUtc ? new Date(resolvedRule.effectiveToUtc).toLocaleDateString() : 'Present'}
                    </span>
                  </div>
                  {resolvedRule.thresholdsConfigJson && resolvedRule.thresholdsConfigJson !== '{}' && (
                    <div>
                      <button
                        type="button"
                        className="btn btn-xs btn-outline"
                        onClick={() => setShowThresholdsJson(!showThresholdsJson)}
                      >
                        {showThresholdsJson ? 'Hide Thresholds' : 'View Configured Thresholds'}
                      </button>
                    </div>
                  )}
                </div>

                {showThresholdsJson && resolvedRule.thresholdsConfigJson && (
                  <pre className="rule-thresholds-preview font-mono text-xs">
                    {resolvedRule.thresholdsConfigJson}
                  </pre>
                )}
              </div>
            ) : (
              <div className="rule-context-empty">
                <span className="text-muted text-xs">
                  {ruleResolutionMessage || 'No specific classifier rule resolved for the current scope. Showing session-wide evidence.'}
                </span>
              </div>
            )}

            {/* Non-truth Evidence Reminder */}
            <div className="analytics-evidence-disclaimer">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <circle cx="12" cy="12" r="10" />
                <line x1="12" y1="16" x2="12" y2="12" />
                <line x1="12" y1="8" x2="12.01" y2="8" />
              </svg>
              <span>
                Evidence overlay notice: Observed classifier rules and unsupervised models reflect machine-configured logic and statistical clustering. They do not constitute ground-truth defect confirmation. Classification correctness requires independent reviewed truth labels.
              </span>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
