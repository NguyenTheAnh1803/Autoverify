import React, { useState, useEffect, useRef, useCallback } from 'react'
import {
  defectAnalyticsApi,
  classifierRulesApi,
} from '../../api'
import type {
  FeatureSchemaReadinessResponse,
  SessionScopeFacetsResponse,
  AnalyticsScopeFilter,
  ClassifierRuleDto,
  AnalyticsJob,
  AnalyticsJobType,
  CreateAnalyticsJobRequest,
  SpcResult,
  EwmaResult,
  CusumResult,
  PcaResult,
  MsaResult,
  DoeResult,
  FeatureRelationshipResult,
  KMeansResult,
  GaussianMixtureResult,
  IsolationForestResult,
  ScriptConditionResult,
} from '../../types'
import { ReadinessOverview } from './ReadinessOverview'
import { AnalyticsScopePanel } from './AnalyticsScopePanel'
import { AnalyticsConfigPanel } from './AnalyticsConfigPanel'
import { JobLifecycleBar } from './JobLifecycleBar'
import { SpcCharts } from './SpcCharts'
import { EwmaChart, CusumChart } from './TimeSeriesCharts'
import { PcaCharts } from './PcaCharts'
import { MsaChart } from './MsaChart'
import { DoeParetoChart } from './DoeParetoChart'
import { FeatureRelationshipChart } from './FeatureRelationshipChart'
import { KMeansChart } from './KMeansChart'
import { GmmChart } from './GmmChart'
import { IsolationForestChart } from './IsolationForestChart'
import { ScriptConditionChart } from './ScriptConditionChart'

export type DefectFeatureAnalyticsDashboardProps = {
  sessionId: string
  initialTab?: DashboardTab
  onClose: () => void
  onSelectRowIndex: (rowIndex: number) => void
}

export type DashboardTab =
  | 'overview'
  | 'spc'
  | 'ewma'
  | 'cusum'
  | 'pca'
  | 'msa'
  | 'doe'
  | 'relationship'
  | 'kmeans'
  | 'gmm'
  | 'isolation_forest'
  | 'script_condition'
  | 'history'

export const DefectFeatureAnalyticsDashboard: React.FC<DefectFeatureAnalyticsDashboardProps> = ({
  sessionId,
  initialTab,
  onClose,
  onSelectRowIndex,
}) => {
  const [activeTab, setActiveTab] = useState<DashboardTab>(initialTab || 'overview')
  const [readinessData, setReadinessData] = useState<FeatureSchemaReadinessResponse | null>(null)
  const [loadingReadiness, setLoadingReadiness] = useState<boolean>(true)
  const [readinessError, setReadinessError] = useState<string | null>(null)

  // Scope and Facets state
  const [facetsData, setFacetsData] = useState<SessionScopeFacetsResponse | null>(null)
  const [selectedScope, setSelectedScope] = useState<AnalyticsScopeFilter>({})
  const [resolvedRule, setResolvedRule] = useState<ClassifierRuleDto | null>(null)
  const [ruleResolutionKind, setRuleResolutionKind] = useState<string | null>(null)
  const [ruleResolutionScore, setRuleResolutionScore] = useState<number | null>(null)
  const [ruleResolutionMessage, setRuleResolutionMessage] = useState<string | null>(null)
  const [loadingRule, setLoadingRule] = useState<boolean>(false)

  // Jobs state
  const [jobList, setJobList] = useState<AnalyticsJob[]>([])
  const [activeJob, setActiveJob] = useState<AnalyticsJob | null>(null)
  const [submittingJob, setSubmittingJob] = useState<boolean>(false)
  const [cancellingJob, setCancellingJob] = useState<boolean>(false)
  const [deletingJob, setDeletingJob] = useState<boolean>(false)

  // Tracking refs for strict mode & polling safety
  const pollTimerRef = useRef<number | null>(null)
  const activeJobIdRef = useRef<string | null>(null)

  useEffect(() => {
    activeJobIdRef.current = activeJob?.jobId || null
  }, [activeJob?.jobId])

  // 1. Load schema readiness
  const loadReadiness = useCallback(
    async (signal?: AbortSignal) => {
      setLoadingReadiness(true)
      setReadinessError(null)
      try {
        const data = await defectAnalyticsApi.getSchemaReadiness(sessionId, signal)
        setReadinessData(data)
      } catch (err) {
        if (signal?.aborted) return
        setReadinessError(err instanceof Error ? err.message : 'Failed to load feature schema readiness.')
      } finally {
        if (!signal?.aborted) {
          setLoadingReadiness(false)
        }
      }
    },
    [sessionId]
  )

  // 2. Load scope facets
  const loadFacets = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const facets = await defectAnalyticsApi.getScopeFacets(sessionId, signal)
        setFacetsData(facets)
      } catch {
        // Non-blocking if facets call fails
      }
    },
    [sessionId]
  )

  // 3. Load job list
  const loadJobs = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const jobs = await defectAnalyticsApi.listJobs(sessionId, signal)
        setJobList(jobs)
        // If no active job is selected yet, pick the latest completed or computing job
        if (!activeJobIdRef.current && jobs.length > 0) {
          const latestJob = jobs[0]
          const full = await defectAnalyticsApi.getJob(sessionId, latestJob.jobId, signal)
          setActiveJob(full)
        }
      } catch {
        // Silently handle abort or initial list fetch error
      }
    },
    [sessionId]
  )

  // Initial load
  useEffect(() => {
    const controller = new AbortController()
    void loadReadiness(controller.signal)
    void loadFacets(controller.signal)
    void loadJobs(controller.signal)

    return () => {
      controller.abort()
      if (pollTimerRef.current) {
        window.clearTimeout(pollTimerRef.current)
      }
    }
  }, [loadReadiness, loadFacets, loadJobs])

  // 4. Resolve Classifier Rule when scope changes
  useEffect(() => {
    const controller = new AbortController()
    setLoadingRule(true)
    classifierRulesApi
      .resolveRule(
        {
          productKey: selectedScope.productKey || undefined,
          lineCode: selectedScope.lineCode || undefined,
          machineCode: selectedScope.machineCode || undefined,
          stationCode: selectedScope.stationCode || undefined,
          defectFamily: selectedScope.defectFamily || undefined,
          defectClass: selectedScope.defectClass || selectedScope.defectType || undefined,
          ruleCode: selectedScope.classifierRuleCode || undefined,
          effectiveTimeUtc: selectedScope.timeFromUtc || undefined,
        },
        controller.signal
      )
      .then((res) => {
        setResolvedRule(res.rule || null)
        setRuleResolutionKind(res.matchResolutionKind || null)
        setRuleResolutionScore(res.specificityScore)
        setRuleResolutionMessage(res.message || null)
      })
      .catch(() => {
        // Silently handle
      })
      .finally(() => {
        setLoadingRule(false)
      })

    return () => {
      controller.abort()
    }
  }, [
    selectedScope.productKey,
    selectedScope.lineCode,
    selectedScope.machineCode,
    selectedScope.stationCode,
    selectedScope.defectFamily,
    selectedScope.defectClass,
    selectedScope.defectType,
    selectedScope.classifierRuleCode,
    selectedScope.timeFromUtc,
  ])

  // 5. Polling loop for running active job
  const pollActiveJob = useCallback(
    (jobId: string) => {
      if (pollTimerRef.current) {
        window.clearTimeout(pollTimerRef.current)
        pollTimerRef.current = null
      }

      const poll = async () => {
        try {
          const updated = await defectAnalyticsApi.getJob(sessionId, jobId)
          setActiveJob(updated)

          // Also update in jobList
          setJobList((prev) =>
            prev.map((j) => (j.jobId === updated.jobId ? updated : j))
          )

          if (updated.status === 'Queued' || updated.status === 'Computing') {
            pollTimerRef.current = window.setTimeout(() => void poll(), 1200)
          }
        } catch {
          // Retry on intermittent failure
          pollTimerRef.current = window.setTimeout(() => void poll(), 2500)
        }
      }

      void poll()
    },
    [sessionId]
  )

  // Trigger polling if activeJob is running
  useEffect(() => {
    if (activeJob && (activeJob.status === 'Queued' || activeJob.status === 'Computing')) {
      pollActiveJob(activeJob.jobId)
    }

    return () => {
      if (pollTimerRef.current) {
        window.clearTimeout(pollTimerRef.current)
      }
    }
  }, [activeJob, pollActiveJob])

  // 6. Actions: Create Job
  const handleSubmitJob = async (request: CreateAnalyticsJobRequest) => {
    setSubmittingJob(true)
    try {
      const created = await defectAnalyticsApi.createJob(sessionId, request)
      setActiveJob(created)
      setJobList((prev) => [created, ...prev.filter((j) => j.jobId !== created.jobId)])
      pollActiveJob(created.jobId)
    } catch (err) {
      alert(`Job submission failed: ${err instanceof Error ? err.message : 'Unknown error'}`)
    } finally {
      setSubmittingJob(false)
    }
  }

  // 7. Actions: Cancel Job
  const handleCancelJob = async () => {
    if (!activeJob) return
    setCancellingJob(true)
    try {
      await defectAnalyticsApi.cancelJob(sessionId, activeJob.jobId)
      const updated = await defectAnalyticsApi.getJob(sessionId, activeJob.jobId)
      setActiveJob(updated)
      setJobList((prev) =>
        prev.map((j) => (j.jobId === updated.jobId ? updated : j))
      )
    } catch (err) {
      alert(`Failed to cancel job: ${err instanceof Error ? err.message : 'Unknown error'}`)
    } finally {
      setCancellingJob(false)
    }
  }

  // 8. Actions: Delete Job
  const handleDeleteJob = async (jobId: string) => {
    if (!confirm('Are you sure you want to delete this analytics job run?')) return
    setDeletingJob(true)
    try {
      await defectAnalyticsApi.deleteJob(sessionId, jobId)
      setJobList((prev) => prev.filter((j) => j.jobId !== jobId))
      if (activeJob?.jobId === jobId) {
        setActiveJob(null)
      }
    } catch (err) {
      alert(`Failed to delete job: ${err instanceof Error ? err.message : 'Unknown error'}`)
    } finally {
      setDeletingJob(false)
    }
  }

  // 9. Actions: Select Job from History
  const handleSelectJob = async (jobId: string) => {
    try {
      const full = await defectAnalyticsApi.getJob(sessionId, jobId)
      setActiveJob(full)
      // Switch active tab to the job's analysis type
      const norm = full.analysisType.toUpperCase()
      if (norm === 'SPC') setActiveTab('spc')
      else if (norm === 'EWMA') setActiveTab('ewma')
      else if (norm === 'CUSUM') setActiveTab('cusum')
      else if (norm === 'PCA') setActiveTab('pca')
      else if (norm === 'MSA') setActiveTab('msa')
      else if (norm === 'DOE') setActiveTab('doe')
      else if (norm === 'FEATURE_RELATIONSHIP') setActiveTab('relationship')
      else if (norm === 'KMEANS') setActiveTab('kmeans')
      else if (norm === 'GMM') setActiveTab('gmm')
      else if (norm === 'ISOLATION_FOREST') setActiveTab('isolation_forest')
    } catch (err) {
      alert(`Failed to load job result: ${err instanceof Error ? err.message : 'Unknown error'}`)
    }
  }

  // Handle family selection from overview matrix
  const handleSelectFamily = (family: AnalyticsJobType) => {
    const norm = (family as string).toUpperCase()
    if (norm === 'SPC') setActiveTab('spc')
    else if (norm === 'EWMA') setActiveTab('ewma')
    else if (norm === 'CUSUM') setActiveTab('cusum')
    else if (norm === 'PCA') setActiveTab('pca')
    else if (norm === 'MSA') setActiveTab('msa')
    else if (norm === 'DOE') setActiveTab('doe')
    else if (norm === 'FEATURE_RELATIONSHIP') setActiveTab('relationship')
    else if (norm === 'KMEANS') setActiveTab('kmeans')
    else if (norm === 'GMM') setActiveTab('gmm')
    else if (norm === 'ISOLATION_FOREST') setActiveTab('isolation_forest')
  }

  // Helpers to get readiness by case-insensitive key
  const getReadiness = (key: string) => {
    if (!readinessData?.readiness) return undefined
    return (
      readinessData.readiness[key] ||
      readinessData.readiness[key.toUpperCase()] ||
      readinessData.readiness[key.toLowerCase()]
    )
  }

  const isJobActiveForTab = (tabType: string) => {
    if (!activeJob) return false
    return activeJob.analysisType.toUpperCase() === tabType.toUpperCase()
  }

  return (
    <div
      className="analytics-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="analytics-dashboard-title"
    >
      <div className="analytics-modal">
        {/* Header */}
        <header className="analytics-modal-header">
          <div className="analytics-header-title">
            <svg
              className="analytics-icon"
              width="22"
              height="22"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <line x1="18" y1="20" x2="18" y2="10" />
              <line x1="12" y1="20" x2="12" y2="4" />
              <line x1="6" y1="20" x2="6" y2="14" />
            </svg>
            <h2 id="analytics-dashboard-title">
              Defect Feature Analytics & Machine-Line Classifier Dashboard
            </h2>
            <span className="session-tag font-mono text-xs">
              Session: {sessionId.slice(0, 8)}...
            </span>
          </div>

          <button
            type="button"
            className="btn-close"
            onClick={onClose}
            aria-label="Close Analytics Dashboard"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </header>

        {/* Global Scope Selector Panel */}
        <AnalyticsScopePanel
          facets={facetsData}
          scope={selectedScope}
          onChangeScope={setSelectedScope}
          resolvedRule={resolvedRule}
          ruleResolutionKind={ruleResolutionKind}
          ruleResolutionScore={ruleResolutionScore}
          ruleResolutionMessage={ruleResolutionMessage}
          loadingRule={loadingRule}
          totalRecords={facetsData?.totalRecords ?? readinessData?.totalRecords}
          onResetScope={() => setSelectedScope({})}
        />

        {/* Navigation Tabs */}
        <nav className="analytics-nav-tabs" aria-label="Analytics Analysis Families">
          <button
            type="button"
            className={`tab-btn ${activeTab === 'overview' ? 'active' : ''}`}
            onClick={() => setActiveTab('overview')}
          >
            Readiness & Audit
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'relationship' ? 'active' : ''}`}
            onClick={() => setActiveTab('relationship')}
          >
            Feature Relationships
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'kmeans' ? 'active' : ''}`}
            onClick={() => setActiveTab('kmeans')}
          >
            K-Means
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'gmm' ? 'active' : ''}`}
            onClick={() => setActiveTab('gmm')}
          >
            GMM
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'isolation_forest' ? 'active' : ''}`}
            onClick={() => setActiveTab('isolation_forest')}
          >
            Isolation Forest
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'spc' ? 'active' : ''}`}
            onClick={() => setActiveTab('spc')}
          >
            SPC & Capability
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'ewma' ? 'active' : ''}`}
            onClick={() => setActiveTab('ewma')}
          >
            EWMA Drift
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'cusum' ? 'active' : ''}`}
            onClick={() => setActiveTab('cusum')}
          >
            CUSUM Shift
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'pca' ? 'active' : ''}`}
            onClick={() => setActiveTab('pca')}
          >
            PCA Multivariate
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'msa' ? 'active' : ''}`}
            onClick={() => setActiveTab('msa')}
          >
            MSA Gage R&R
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'doe' ? 'active' : ''}`}
            onClick={() => setActiveTab('doe')}
          >
            DOE Pareto
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'script_condition' ? 'active' : ''}`}
            onClick={() => setActiveTab('script_condition')}
          >
            Script Analysis
          </button>
          <button
            type="button"
            className={`tab-btn ${activeTab === 'history' ? 'active' : ''}`}
            onClick={() => setActiveTab('history')}
          >
            History ({jobList.length})
          </button>
        </nav>

        {/* Modal Main Body */}
        <div className="analytics-modal-body">
          {/* Active Job Lifecycle & History Controls */}
          {(activeJob != null || jobList.length > 0) && (
            <JobLifecycleBar
              currentJob={activeJob}
              jobList={jobList}
              onSelectJob={handleSelectJob}
              onCancelJob={handleCancelJob}
              onDeleteJob={handleDeleteJob}
              cancelling={cancellingJob}
              deleting={deletingJob}
            />
          )}

          {loadingReadiness && !readinessData ? (
            <div className="analytics-loading-state">
              <div className="spinner" />
              <span>Auditing telemetry feature schema & assessing readiness...</span>
            </div>
          ) : readinessError ? (
            <div className="analytics-error-state">
              <span className="error-title">Failed to load analytics readiness:</span>
              <p>{readinessError}</p>
              <button
                type="button"
                className="btn btn-secondary mt-2"
                onClick={() => void loadReadiness()}
              >
                Retry Audit
              </button>
            </div>
          ) : readinessData ? (
            <>
              {/* TAB 1: OVERVIEW & READINESS */}
              {activeTab === 'overview' && (
                <ReadinessOverview
                  readinessData={readinessData}
                  onSelectFamily={handleSelectFamily}
                />
              )}

              {/* TAB: FEATURE RELATIONSHIPS */}
              {activeTab === 'relationship' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="FEATURE_RELATIONSHIP"
                    features={readinessData.features}
                    readiness={getReadiness('FEATURE_RELATIONSHIP')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('FEATURE_RELATIONSHIP') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <FeatureRelationshipChart
                        result={activeJob.result as FeatureRelationshipResult}
                        facets={facetsData}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: K-MEANS */}
              {activeTab === 'kmeans' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="KMEANS"
                    features={readinessData.features}
                    readiness={getReadiness('KMEANS')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('KMEANS') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <KMeansChart
                        result={activeJob.result as KMeansResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: GMM */}
              {activeTab === 'gmm' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="GMM"
                    features={readinessData.features}
                    readiness={getReadiness('GMM')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('GMM') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <GmmChart
                        result={activeJob.result as GaussianMixtureResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: ISOLATION FOREST */}
              {activeTab === 'isolation_forest' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="ISOLATION_FOREST"
                    features={readinessData.features}
                    readiness={getReadiness('ISOLATION_FOREST')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('ISOLATION_FOREST') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <IsolationForestChart
                        result={activeJob.result as IsolationForestResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: SPC & CAPABILITY */}
              {activeTab === 'spc' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="SPC"
                    features={readinessData.features}
                    readiness={getReadiness('SPC')}
                    detectedRuleVersions={readinessData.detectedRuleVersions}
                    scopeWarnings={readinessData.scopeWarnings}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('SPC') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <SpcCharts
                        result={activeJob.result as SpcResult}
                        isNgOnlyDataset={readinessData.isNgOnlyDataset}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: EWMA */}
              {activeTab === 'ewma' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="EWMA"
                    features={readinessData.features}
                    readiness={getReadiness('EWMA')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('EWMA') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <EwmaChart
                        result={activeJob.result as EwmaResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: CUSUM */}
              {activeTab === 'cusum' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="CUSUM"
                    features={readinessData.features}
                    readiness={getReadiness('CUSUM')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('CUSUM') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <CusumChart
                        result={activeJob.result as CusumResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: PCA */}
              {activeTab === 'pca' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="PCA"
                    features={readinessData.features}
                    readiness={getReadiness('PCA')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('PCA') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <PcaCharts
                        result={activeJob.result as PcaResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: MSA */}
              {activeTab === 'msa' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="MSA"
                    features={readinessData.features}
                    readiness={getReadiness('MSA')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('MSA') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <MsaChart result={activeJob.result as MsaResult} />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: DOE */}
              {activeTab === 'doe' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="DOE"
                    features={readinessData.features}
                    readiness={getReadiness('DOE')}
                    scope={selectedScope}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('DOE') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <DoeParetoChart
                        result={activeJob.result as DoeResult}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: SCRIPT CONDITION ANALYSIS */}
              {activeTab === 'script_condition' && (
                <div className="analytics-tab-content">
                  <AnalyticsConfigPanel
                    family="SCRIPT_CONDITION"
                    features={readinessData.features}
                    readiness={getReadiness('SCRIPT_CONDITION')}
                    scope={selectedScope}
                    resolvedRule={resolvedRule}
                    onSubmitJob={handleSubmitJob}
                    submitting={submittingJob}
                  />

                  {isJobActiveForTab('SCRIPT_CONDITION') && activeJob && activeJob.status === 'Completed' && activeJob.result != null ? (
                    <div className="mt-4">
                      <ScriptConditionChart
                        result={activeJob.result as ScriptConditionResult}
                        sessionId={sessionId}
                        jobId={activeJob.jobId}
                        onSelectRowIndex={onSelectRowIndex}
                      />
                    </div>
                  ) : null}
                </div>
              )}

              {/* TAB: JOB HISTORY */}
              {activeTab === 'history' && (
                <div className="analytics-history-view">
                  <div className="section-header">
                    <h4>Session Computation History</h4>
                  </div>
                  {jobList.length === 0 ? (
                    <div className="analytics-empty-note">
                      No analytics jobs have been submitted for this session yet.
                    </div>
                  ) : (
                    <div className="table-responsive">
                      <table className="analytics-data-table" role="table">
                        <thead>
                          <tr>
                            <th>Job ID</th>
                            <th>Analysis Type</th>
                            <th>Status</th>
                            <th>Progress</th>
                            <th>Created At</th>
                            <th>Actions</th>
                          </tr>
                        </thead>
                        <tbody>
                          {jobList.map((job) => (
                            <tr
                              key={job.jobId}
                              className={activeJob?.jobId === job.jobId ? 'selected-row' : ''}
                            >
                              <td className="font-mono text-xs font-bold">{job.jobId}</td>
                              <td className="font-mono">{job.analysisType}</td>
                              <td>
                                <span className={`job-status-pill status-${job.status.toLowerCase()}`}>
                                  {job.status}
                                </span>
                              </td>
                              <td className="font-mono">{job.progressPercent}%</td>
                              <td className="font-mono text-xs">
                                {new Date(job.createdAtUtc).toLocaleString()}
                              </td>
                              <td>
                                <div className="btn-group">
                                  <button
                                    type="button"
                                    className="btn btn-secondary btn-sm"
                                    onClick={() => void handleSelectJob(job.jobId)}
                                  >
                                    View / Activate
                                  </button>
                                  <button
                                    type="button"
                                    className="btn btn-ghost btn-sm text-rose-400"
                                    onClick={() => void handleDeleteJob(job.jobId)}
                                  >
                                    Delete
                                  </button>
                                </div>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}
            </>
          ) : null}
        </div>
      </div>
    </div>
  )
}
