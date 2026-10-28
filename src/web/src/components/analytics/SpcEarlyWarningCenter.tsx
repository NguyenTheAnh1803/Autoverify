import React, { useCallback, useEffect, useState } from 'react'
import { productionFeatureApi } from '../../api'
import { formatDateTime } from '../../format'
import { Button, Icon } from '../../ui'
import type { Notice, SpcEarlyWarningDashboardResponse } from '../../types'

export type SpcEarlyWarningCenterProps = {
  onNotice?: (notice: Notice) => void
}

export const SpcEarlyWarningCenter: React.FC<SpcEarlyWarningCenterProps> = ({ onNotice }) => {
  const [data, setData] = useState<SpcEarlyWarningDashboardResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [activeTab, setActiveTab] = useState<'alerts' | 'benchmarks'>('alerts')

  // Filter state
  const [selectedMachine, setSelectedMachine] = useState<string>('All')
  const [selectedLine, setSelectedLine] = useState<string>('All')
  const [lookbackCount, setLookbackCount] = useState<number>(400)

  const loadEarlyWarnings = useCallback(async () => {
    setLoading(true)
    try {
      const res = await productionFeatureApi.getEarlyWarnings({
        machineCode: selectedMachine === 'All' ? undefined : selectedMachine,
        lineCode: selectedLine === 'All' ? undefined : selectedLine,
        lookbackCount,
      })
      setData(res)
    } catch (err) {
      if (onNotice) {
        onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to load early warnings.' })
      }
    } finally {
      setLoading(false)
    }
  }, [selectedMachine, selectedLine, lookbackCount, onNotice])

  useEffect(() => {
    void loadEarlyWarnings()
  }, [loadEarlyWarnings])

  const alerts = data?.alerts ?? []
  const benchmarks = data?.benchmarks ?? []
  const criticalCount = data?.criticalCount ?? 0
  const distinctMachines = Array.from(new Set(benchmarks.map((b) => b.machineCode).filter(Boolean))) as string[]
  const distinctLines = Array.from(new Set(benchmarks.map((b) => b.lineCode).filter(Boolean))) as string[]
  const warningCount = data?.warningCount ?? 0

  const overallHealth = criticalCount > 0
    ? { text: 'CRITICAL ATTENTION', tone: 'unhealthy', color: 'var(--status-ng)', bg: 'var(--status-ng-bg)' }
    : warningCount > 0
    ? { text: 'PROCESS DRIFT DETECTED', tone: 'warning', color: 'var(--amber-700)', bg: 'var(--amber-100)' }
    : { text: 'STABLE (IN-CONTROL)', tone: 'healthy', color: 'var(--status-ok)', bg: 'var(--status-ok-bg)' }

  return (
    <section
      className="panel vstack gap-md"
      style={{
        background: '#fff',
        border: '1px solid var(--slate-200)',
        borderRadius: '10px',
        padding: '20px',
        boxShadow: '0 2px 8px rgba(0,0,0,0.04)',
      }}
    >
      {/* SECTION HEADER & SUMMARY COUNTERS */}
      <div className="hstack justify-between flex-wrap gap-md" style={{ alignItems: 'flex-start' }}>
        <div>
          <div className="hstack gap-xs" style={{ alignItems: 'center' }}>
            <span className="tag-badge text-xs font-bold" style={{ background: 'var(--blue-100)', color: 'var(--blue-800)' }}>
              PHASE 3: PREDICTIVE DEFENSE
            </span>
            <span className="text-xs font-bold text-muted">REAL-TIME SPC & DRIFT WARNING</span>
          </div>
          <h3 style={{ margin: '4px 0 2px 0', fontSize: '18px' }}>
            Early Warning Center & Process Capability Benchmarks
          </h3>
          <p className="text-xs text-muted" style={{ margin: 0, maxWidth: '640px' }}>
            Giám sát xu hướng trôi dạt (Drift) đặc tính quang học thời gian thực qua 8 Nelson Rules, phát hiện suy giảm đèn chiếu / hao mòn đồ gá trước khi phát sinh lỗi Overkill.
          </p>
        </div>

        {/* Health status badge */}
        <div className="hstack gap-sm" style={{ alignItems: 'center' }}>
          <div
            className="hstack gap-xs"
            style={{
              padding: '6px 14px',
              borderRadius: '20px',
              background: overallHealth.bg,
              color: overallHealth.color,
              fontWeight: 'bold',
              fontSize: '12px',
              border: `1px solid ${overallHealth.color}`,
            }}
          >
            <span style={{ fontSize: '14px' }}>
              {criticalCount > 0 ? '🔴' : warningCount > 0 ? '🟡' : '🟢'}
            </span>
            <span>{overallHealth.text}</span>
          </div>

          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => void loadEarlyWarnings()}
            loading={loading}
            title="Làm mới cảnh báo ngay"
          >
            <Icon name="refresh" className="icon inline-icon" /> Làm mới
          </Button>
        </div>
      </div>

      {/* METRIC PILL CARDS */}
      <div
        className="gap-md"
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
          marginTop: '6px',
        }}
      >
        <div style={{ background: 'var(--status-ng-bg)', border: '1px solid var(--status-ng-border)', padding: '12px', borderRadius: '8px' }}>
          <span className="text-xs font-bold text-danger">CRITICAL (RULE 1 BEYOND 3σ)</span>
          <div className="text-2xl font-bold" style={{ color: 'var(--status-ng)', marginTop: '2px' }}>
            {criticalCount}
          </div>
          <small className="text-xs text-muted">Điểm đo đột biến bất thường</small>
        </div>

        <div style={{ background: 'var(--amber-50)', border: '1px solid var(--amber-200)', padding: '12px', borderRadius: '8px' }}>
          <span className="text-xs font-bold text-warning" style={{ color: 'var(--amber-800)' }}>PROCESS DRIFT (RULES 2 & 3)</span>
          <div className="text-2xl font-bold" style={{ color: 'var(--amber-700)', marginTop: '2px' }}>
            {warningCount}
          </div>
          <small className="text-xs text-muted">Trôi 9 mẫu hoặc trượt 6 mẫu</small>
        </div>

        <div style={{ background: 'var(--slate-50)', border: '1px solid var(--slate-200)', padding: '12px', borderRadius: '8px' }}>
          <span className="text-xs font-bold text-muted">EVALUATED SAMPLES</span>
          <div className="text-2xl font-bold text-strong" style={{ marginTop: '2px' }}>
            {data?.totalEvaluatedRecords ?? 0}
          </div>
          <small className="text-xs text-muted">Dữ liệu quan sát gần nhất</small>
        </div>

        <div style={{ background: 'var(--blue-50)', border: '1px solid var(--blue-200)', padding: '12px', borderRadius: '8px' }}>
          <span className="text-xs font-bold text-info" style={{ color: 'var(--blue-800)' }}>MONITORED FEATURES</span>
          <div className="text-2xl font-bold" style={{ color: 'var(--blue-700)', marginTop: '2px' }}>
            {benchmarks.length}
          </div>
          <small className="text-xs text-muted">Cụm đặc trưng máy & chuyền</small>
        </div>
      </div>

      {/* FILTER & TAB CONTROLS */}
      <div
        className="hstack justify-between flex-wrap gap-sm"
        style={{
          borderBottom: '1px solid var(--slate-200)',
          paddingBottom: '12px',
          marginTop: '10px',
        }}
      >
        <div className="hstack gap-xs">
          <button
            type="button"
            className={`ghost-button compact ${activeTab === 'alerts' ? 'active' : ''}`}
            style={{ fontWeight: activeTab === 'alerts' ? 600 : 400 }}
            onClick={() => setActiveTab('alerts')}
          >
            <Icon name="warning" className="icon inline-icon" /> Cảnh Báo Sớm ({alerts.length})
          </button>
          <button
            type="button"
            className={`ghost-button compact ${activeTab === 'benchmarks' ? 'active' : ''}`}
            style={{ fontWeight: activeTab === 'benchmarks' ? 600 : 400 }}
            onClick={() => setActiveTab('benchmarks')}
          >
            <Icon name="target" className="icon inline-icon" /> Năng Lực Quá Trình Cpk ({benchmarks.length})
          </button>
        </div>

        <div className="hstack gap-sm flex-wrap" style={{ alignItems: 'center' }}>
          {distinctMachines.length > 0 && (
            <div className="hstack gap-xs" style={{ alignItems: 'center' }}>
              <span className="text-xs text-muted">Máy:</span>
              <select
                value={selectedMachine}
                onChange={(e) => setSelectedMachine(e.target.value)}
                style={{ fontSize: '12px', padding: '3px 8px' }}
              >
                <option value="All">Tất cả máy</option>
                {distinctMachines.map((m) => (
                  <option key={m} value={m}>{m}</option>
                ))}
              </select>
            </div>
          )}
          {distinctLines.length > 0 && (
            <div className="hstack gap-xs" style={{ alignItems: 'center' }}>
              <span className="text-xs text-muted">Chuyền:</span>
              <select
                value={selectedLine}
                onChange={(e) => setSelectedLine(e.target.value)}
                style={{ fontSize: '12px', padding: '3px 8px' }}
              >
                <option value="All">Tất cả chuyền</option>
                {distinctLines.map((l) => (
                  <option key={l} value={l}>{l}</option>
                ))}
              </select>
            </div>
          )}
          <div className="hstack gap-xs" style={{ alignItems: 'center' }}>
            <span className="text-xs text-muted">Cỡ mẫu:</span>
            <select
              value={lookbackCount}
              onChange={(e) => setLookbackCount(parseInt(e.target.value, 10) || 400)}
              style={{ fontSize: '12px', padding: '3px 8px' }}
            >
              <option value={100}>100 mẫu</option>
              <option value={200}>200 mẫu</option>
              <option value={400}>400 mẫu</option>
              <option value={800}>800 mẫu</option>
            </select>
          </div>
          <span className="text-xs text-muted font-mono">
            Đánh giá lúc: {formatDateTime(data?.evaluatedAtUtc)}
          </span>
        </div>
      </div>

      {/* TAB CONTENT A: PREDICTIVE DRIFT ALERTS */}
      {activeTab === 'alerts' && (
        <div className="vstack gap-md">
          {alerts.length === 0 ? (
            <div className="text-center text-muted" style={{ padding: '36px', background: 'var(--slate-50)', borderRadius: '8px' }}>
              <span style={{ fontSize: '24px', display: 'block', marginBottom: '6px' }}>🛡️</span>
              <strong className="text-ok">Tất cả thông số đo quang học đang ổn định hoàn hảo.</strong>
              <p className="text-xs text-muted" style={{ margin: '4px 0 0 0' }}>
                Không phát hiện vi phạm Nelson Rules (Rules 1-8) hay hiện tượng trôi dạt thông số nào trong {data?.totalEvaluatedRecords ?? 0} mẫu quan sát gần nhất.
              </p>
            </div>
          ) : (
            <div className="vstack gap-sm">
              {alerts.map((alert) => {
                const isCrit = alert.severity === 'Critical'
                return (
                  <div
                    key={alert.alertId}
                    style={{
                      background: isCrit ? 'var(--status-ng-bg)' : 'var(--amber-50)',
                      border: `1px solid ${isCrit ? 'var(--status-ng-border)' : 'var(--amber-200)'}`,
                      borderRadius: '8px',
                      padding: '14px',
                    }}
                  >
                    <div className="hstack justify-between flex-wrap gap-sm" style={{ alignItems: 'center', marginBottom: '6px' }}>
                      <div className="hstack gap-sm" style={{ alignItems: 'center' }}>
                        <span
                          className="tag-badge text-xs font-bold"
                          style={{
                            background: isCrit ? 'var(--status-ng)' : 'var(--amber-600)',
                            color: '#fff',
                          }}
                        >
                          {alert.severity.toUpperCase()}
                        </span>
                        <strong className="mono-code text-sm text-strong">{alert.featureName}</strong>
                        <span className="text-xs font-mono text-muted">
                          [{alert.machineCode} · {alert.lineCode}]
                        </span>
                        <span
                          className="tag-badge text-xs"
                          style={{
                            background: '#fff',
                            border: '1px solid var(--slate-200)',
                            color: 'var(--slate-800)',
                          }}
                        >
                          {alert.ruleName}
                        </span>
                      </div>

                      <div className="hstack gap-md text-xs">
                        <span>Trung tâm (CL): <b className="font-mono">{alert.centerLine}</b></span>
                        <span style={{ color: isCrit ? 'var(--status-ng)' : 'var(--amber-800)' }}>
                          Hiện tại: <b className="font-mono text-sm">{alert.currentValue}</b>
                        </span>
                        <span>Độ lệch (σ): <b className="font-mono">{alert.sigma}</b></span>
                      </div>
                    </div>

                    <div className="vstack gap-xs" style={{ background: '#fff', padding: '10px 12px', borderRadius: '6px', border: '1px solid rgba(0,0,0,0.06)' }}>
                      <div className="text-xs" style={{ lineHeight: 1.4 }}>
                        <span className="text-muted font-semibold">Chẩn đoán nguyên nhân: </span>
                        <span>{alert.rootCauseHypothesis}</span>
                      </div>
                      <div className="text-xs" style={{ lineHeight: 1.4, color: 'var(--blue-800)' }}>
                        <span className="font-semibold">Khuyến nghị xử lý: </span>
                        <strong>{alert.recommendedAction}</strong>
                      </div>
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>
      )}

      {/* TAB CONTENT B: CPK BENCHMARKS MATRIX */}
      {activeTab === 'benchmarks' && (
        <div className="table-responsive">
          <table className="analytics-data-table" role="table">
            <thead>
              <tr>
                <th>Line & Máy</th>
                <th>Đặc trưng đo lường</th>
                <th>Cỡ mẫu</th>
                <th>Trung bình (Mean)</th>
                <th>Độ lệch (σ)</th>
                <th>Cp</th>
                <th>Cpk</th>
                <th>Đánh giá Năng lực</th>
                <th>Hiện trạng Trôi dạt</th>
              </tr>
            </thead>
            <tbody>
              {benchmarks.length === 0 ? (
                <tr>
                  <td colSpan={9} className="text-center text-muted" style={{ padding: '24px' }}>
                    Chưa có đủ mẫu số liệu đặc trưng để tính toán năng lực quá trình.
                  </td>
                </tr>
              ) : (
                benchmarks.map((b, idx) => {
                  const isWorldClass = b.capabilityStatus === 'WorldClass'
                  const isAdequate = b.capabilityStatus === 'Adequate'
                  const isRisky = b.capabilityStatus === 'Risky'

                  return (
                    <tr key={`${b.machineCode}_${b.lineCode}_${b.featureName}_${idx}`}>
                      <td className="font-mono font-bold">
                        {b.machineCode} <span className="text-muted">({b.lineCode})</span>
                      </td>
                      <td className="font-mono text-strong">{b.featureName}</td>
                      <td className="font-mono text-xs">{b.sampleSize}</td>
                      <td className="font-mono text-xs">{b.mean}</td>
                      <td className="font-mono text-xs">{b.sigma}</td>
                      <td className="font-mono text-xs">{b.cp != null ? b.cp.toFixed(2) : '—'}</td>
                      <td className="font-mono font-bold text-sm" style={{ color: isWorldClass ? 'var(--status-ok)' : isAdequate ? 'var(--blue-600)' : isRisky ? 'var(--amber-700)' : 'var(--status-ng)' }}>
                        {b.cpk != null ? b.cpk.toFixed(2) : '—'}
                      </td>
                      <td>
                        <span
                          className="tag-badge text-xs font-bold"
                          style={{
                            background: isWorldClass ? 'var(--status-ok-bg)' : isAdequate ? 'var(--blue-50)' : isRisky ? 'var(--amber-100)' : 'var(--status-ng-bg)',
                            color: isWorldClass ? 'var(--status-ok-text)' : isAdequate ? 'var(--blue-800)' : isRisky ? 'var(--amber-800)' : 'var(--status-ng-text)',
                          }}
                        >
                          {isWorldClass && '🌟 Đẳng cấp (Cpk ≥ 1.67)'}
                          {isAdequate && '✓ Đạt chuẩn (1.33 - 1.67)'}
                          {isRisky && '⚠️ Có rủi ro (1.0 - 1.33)'}
                          {b.capabilityStatus === 'Incapable' && '❌ Không đạt (< 1.0)'}
                        </span>
                      </td>
                      <td>
                        {b.hasActiveDrift ? (
                          <span className="tag-badge text-xs" style={{ background: 'var(--amber-100)', color: 'var(--amber-800)' }}>
                            ⚠️ Đang trôi ({b.totalViolations} lỗi)
                          </span>
                        ) : (
                          <span className="tag-badge text-xs" style={{ background: 'var(--status-ok-bg)', color: 'var(--status-ok-text)' }}>
                            ✓ Ổn định
                          </span>
                        )}
                      </td>
                    </tr>
                  )
                })
              )}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
