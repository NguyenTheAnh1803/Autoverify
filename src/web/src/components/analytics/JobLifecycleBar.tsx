import React from 'react'
import type { AnalyticsJob } from '../../types'

export type JobLifecycleBarProps = {
  currentJob: AnalyticsJob | null
  jobList: AnalyticsJob[]
  onSelectJob: (jobId: string) => void
  onCancelJob: (jobId: string) => void
  onDeleteJob: (jobId: string) => void
  cancelling: boolean
  deleting: boolean
}

export const JobLifecycleBar: React.FC<JobLifecycleBarProps> = ({
  currentJob,
  jobList,
  onSelectJob,
  onCancelJob,
  onDeleteJob,
  cancelling,
  deleting,
}) => {
  if (!currentJob && jobList.length === 0) {
    return null
  }

  const isRunning =
    currentJob?.status === 'Queued' || currentJob?.status === 'Computing'

  return (
    <div className="job-lifecycle-bar">
      <div className="job-lifecycle-main">
        {currentJob ? (
          <div className="job-info-group">
            <span className="job-id-tag font-mono">
              Job: {currentJob.jobId.slice(0, 8)}...
            </span>
            <span className="job-type-badge font-mono">
              {currentJob.analysisType}
            </span>
            <span
              className={`job-status-pill status-${currentJob.status.toLowerCase()}`}
            >
              {currentJob.status}
            </span>
            {isRunning && (
              <div className="job-progress-wrapper" title={`${currentJob.progressPercent}%`}>
                <div
                  className="job-progress-fill"
                  style={{ transform: `scaleX(${Math.min(100, Math.max(5, currentJob.progressPercent)) / 100})` }}
                />
                <span className="job-progress-text">{currentJob.progressPercent}%</span>
              </div>
            )}
            {currentJob.errorMessage && (
              <span className="job-error-text" title={currentJob.errorMessage}>
                Error: {currentJob.errorMessage}
              </span>
            )}
          </div>
        ) : (
          <span className="job-idle-text">No active job selected</span>
        )}

        <div className="job-actions-group">
          {isRunning && currentJob && (
            <button
              type="button"
              className="btn btn-danger btn-sm"
              onClick={() => onCancelJob(currentJob.jobId)}
              disabled={cancelling}
            >
              {cancelling ? 'Cancelling...' : 'Cancel Computation'}
            </button>
          )}

          {currentJob && !isRunning && (
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => onDeleteJob(currentJob.jobId)}
              disabled={deleting}
              title="Delete this job record"
            >
              {deleting ? 'Deleting...' : 'Delete Job'}
            </button>
          )}

          {jobList.length > 1 && (
            <div className="job-history-select-group">
              <label htmlFor="history-select" className="sr-only">
                Job History
              </label>
              <select
                id="history-select"
                className="job-history-dropdown"
                value={currentJob?.jobId || ''}
                onChange={(e) => onSelectJob(e.target.value)}
              >
                {jobList.map((j) => (
                  <option key={j.jobId} value={j.jobId}>
                    [{j.analysisType}] {j.status} - {new Date(j.createdAtUtc).toLocaleTimeString()}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>
      </div>

      {currentJob?.warnings && currentJob.warnings.length > 0 && (
        <div className="job-warnings-ticker">
          <span className="warning-badge gap-xs" style={{ display: 'inline-flex', alignItems: 'center' }}>
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
            Notice:
          </span>
          <span className="warning-text">{currentJob.warnings.join(' | ')}</span>
        </div>
      )}
    </div>
  )
}
