import { useCallback, useEffect, useMemo, useState } from 'react'
import { classifierRulesApi } from '../api'
import { formatDateTime } from '../format'
import { RuleOptimizerModal } from '../components/RuleOptimizerModal'
import { Field, Modal, StatusPill } from '../ui'
import type {
  ClassifierRuleDto,
  CreateClassifierRuleRequest,
  Notice,
  TestClassifierRuleResponse,
} from '../types'

export function ClassifierRulesTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [rules, setRules] = useState<ClassifierRuleDto[]>([])
  const [loading, setLoading] = useState(false)
  const [search, setSearch] = useState('')

  // Modals
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [showImportJsonModal, setShowImportJsonModal] = useState(false)
  const [showTestModal, setShowTestModal] = useState(false)
  const [editingRule, setEditingRule] = useState<ClassifierRuleDto | null>(null)
  const [inspectingRule, setInspectingRule] = useState<ClassifierRuleDto | null>(null)
  const [optimizingRule, setOptimizingRule] = useState<ClassifierRuleDto | null>(null)

  // Tester state
  const [testRuleId, setTestRuleId] = useState<string>('')
  const [testScriptText, setTestScriptText] = useState<string>('')
  const [testFeaturesJson, setTestFeaturesJson] = useState<string>(
    JSON.stringify(
      {
        G010_VpID: 1,
        G008_LightID: 1,
        G009_RoiID: 24,
        G062_OrgGrayMax: 35,
        G064_OrgGrayStdDev: 4,
        G020_PxlArea: 110,
        G017_PxlWidth: 35,
        G018_PxlHeight: 35,
        G038_PrcGrayMin: 30,
        V003_VP_IP: 1,
        MLClass01: 95,
      },
      null,
      2
    )
  )
  const [testingBusy, setTestingBusy] = useState(false)
  const [testResult, setTestResult] = useState<TestClassifierRuleResponse | null>(null)

  // Batch import state
  const [importJsonText, setImportJsonText] = useState<string>(`{
  "Rules": [
    {
      "Priority": 0,
      "Name": "Light_Fail",
      "GUID": "{5c117490-152e-4bae-a9a9-11326689753b}",
      "DefectTypeID": "{c79df3f9-4f5a-43d3-821c-7f62c2cc03ba}",
      "Script": "#MCL Block1\\r\\nMCL_Position1 = (G010_VpID==1) and (G008_LightID ==1) and (G009_RoiID == 24 or G009_RoiID == 25)\\r\\nMCL_Light1 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)\\r\\nMCL_Block1 = MCL_Position1 and MCL_Light1\\r\\n\\r\\n#MCL Block2\\r\\nMCL_Position2 = (G010_VpID==1) and (G008_LightID ==2) and (G009_RoiID == 3 or G009_RoiID == 4)\\r\\nMCL_Light2 = (G062_OrgGrayMax < 30 and G064_OrgGrayStdDev < 5)\\r\\nMCL_Block2 = MCL_Position2 and MCL_Light2\\r\\n\\r\\nreturn MCL_Block1 or MCL_Block2"
    },
    {
      "Priority": 1,
      "Name": "DIC_Crack",
      "GUID": "{925918e0-9daa-4ca2-b30b-48b7842a0a97}",
      "DefectTypeID": "{12084a83-674f-4380-803b-071fa389f4e6}",
      "Script": "if( G010_VpID != 1):\\r\\n\\treturn False\\r\\n\\r\\nCond1 = ( G020_PxlArea > 90 )\\r\\nCond2 = ( G038_PrcGrayMin < 45 )\\r\\nDL_Cond = MLClass01 > 90\\r\\n\\r\\nreturn Cond1 and Cond2 and DL_Cond"
    }
  ]
}`)
  const [importingBusy, setImportingBusy] = useState(false)

  // Single rule form state
  const [formData, setFormData] = useState<CreateClassifierRuleRequest>({
    ruleCode: '',
    ruleName: '',
    version: '1.0.0',
    priority: 0,
    defectClass: '',
    defectFamily: 'Optical',
    machineCode: '',
    lineCode: '',
    stationCode: '',
    scriptContent: '',
    isActive: true,
  })

  const loadRules = useCallback(async () => {
    setLoading(true)
    try {
      const items = await classifierRulesApi.listRules()
      // Sort by Priority ASC, then Name
      items.sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0) || a.ruleName.localeCompare(b.ruleName))
      setRules(items)
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to load classifier rules.' })
    } finally {
      setLoading(false)
    }
  }, [onNotice])

  useEffect(() => {
    void loadRules()
  }, [loadRules])

  const filteredRules = useMemo(() => {
    if (!search.trim()) return rules
    const s = search.toLowerCase()
    return rules.filter(
      (r) =>
        r.ruleCode.toLowerCase().includes(s) ||
        r.ruleName.toLowerCase().includes(s) ||
        (r.defectClass && r.defectClass.toLowerCase().includes(s)) ||
        (r.defectFamily && r.defectFamily.toLowerCase().includes(s)) ||
        (r.scriptContent && r.scriptContent.toLowerCase().includes(s))
    )
  }, [rules, search])

  // Open Tester for specific rule
  function handleOpenTester(rule?: ClassifierRuleDto) {
    if (rule) {
      setTestRuleId(rule.id)
      setTestScriptText(rule.scriptContent || '')
    } else if (rules.length > 0) {
      const r = rules[0]
      setTestRuleId(r.id)
      setTestScriptText(r.scriptContent || '')
    }
    setTestResult(null)
    setShowTestModal(true)
  }

  // Run Test
  async function handleRunTest() {
    setTestingBusy(true)
    setTestResult(null)
    try {
      const res = await classifierRulesApi.testRule({
        ruleId: testRuleId || undefined,
        scriptContent: testScriptText.trim() || undefined,
        featuresJson: testFeaturesJson.trim() || undefined,
      })
      setTestResult(res)
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Test execution failed.' })
    } finally {
      setTestingBusy(false)
    }
  }

  // Batch import JSON
  async function handleImportJson() {
    setImportingBusy(true)
    try {
      const parsed = JSON.parse(importJsonText) as {
        Rules?: Array<{ Priority: number; Name: string; GUID?: string; DefectTypeID?: string; Script?: string }>
      }
      const rawRules = parsed.Rules || (Array.isArray(parsed) ? parsed : null)
      if (!rawRules || rawRules.length === 0) {
        throw new Error('Valid "Rules" array not found in JSON payload.')
      }

      const mapped = rawRules.map((r) => ({
        priority: r.Priority ?? 0,
        name: r.Name,
        guid: r.GUID,
        defectTypeId: r.DefectTypeID,
        script: r.Script,
      }))

      const result = await classifierRulesApi.importAoiRulesJson({ rules: mapped })
      onNotice({ tone: 'success', message: `✓ Successfully imported ${result.length} Rules into the system!` })
      setShowImportJsonModal(false)
      await loadRules()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Import JSON failed.' })
    } finally {
      setImportingBusy(false)
    }
  }

  // Save single rule (Create or Edit)
  async function handleSaveRule() {
    if (!formData.ruleCode.trim() || !formData.ruleName.trim()) {
      onNotice({ tone: 'error', message: 'Rule Code and Rule Name are required.' })
      return
    }

    try {
      if (editingRule) {
        await classifierRulesApi.updateRule(editingRule.id, {
          ruleName: formData.ruleName,
          scriptContent: formData.scriptContent,
          priority: formData.priority,
          defectTypeId: formData.defectTypeId,
          isActive: formData.isActive,
          description: formData.description,
        })
        onNotice({ tone: 'success', message: `✓ Updated Rule ${editingRule.ruleCode} successfully!` })
      } else {
        await classifierRulesApi.createRule(formData)
        onNotice({ tone: 'success', message: `✓ Created Rule ${formData.ruleCode} successfully!` })
      }
      setShowCreateModal(false)
      setEditingRule(null)
      await loadRules()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to save rule.' })
    }
  }

  // Delete rule
  async function handleDeleteRule(rule: ClassifierRuleDto) {
    if (!window.confirm(`Are you sure you want to delete Rule [${rule.ruleCode}]?`)) return
    try {
      await classifierRulesApi.deleteRule(rule.id)
      onNotice({ tone: 'success', message: `✓ Deleted Rule ${rule.ruleCode} successfully!` })
      await loadRules()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to delete rule.' })
    }
  }

  return (
    <div className="tab-stack">
      {/* Top Controls Header */}
      <section className="panel">
        <div className="section-heading">
          <div>
            <p className="eyebrow">AOI Recipe & Machine Classifier Rules</p>
            <h2>Classifier Rules & Script Management</h2>
            <p className="text-xs text-muted" style={{ marginTop: '4px' }}>
              Configure defect classification scripts (Light_Fail, DIC_Crack...), batch import AOI JSON rules, and test logic interactively.
            </p>
          </div>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="primary"
              style={{ background: '#2563eb', borderColor: '#3b82f6', fontWeight: 600 }}
              onClick={() => handleOpenTester()}
            >
              ⚡ Interactive Rule Tester
            </button>
            <button
              type="button"
              className="secondary"
              style={{ background: '#059669', borderColor: '#10b981', color: '#fff', fontWeight: 600 }}
              onClick={() => setShowImportJsonModal(true)}
            >
              📥 Import AOI Rules JSON
            </button>
            <button
              type="button"
              className="secondary"
              onClick={() => {
                setEditingRule(null)
                setFormData({
                  ruleCode: '',
                  ruleName: '',
                  version: '1.0.0',
                  priority: rules.length,
                  defectClass: '',
                  defectFamily: 'Optical',
                  machineCode: '',
                  lineCode: '',
                  stationCode: '',
                  scriptContent: '',
                  isActive: true,
                })
                setShowCreateModal(true)
              }}
            >
              + Add New Rule
            </button>
          </div>
        </div>

        {/* Filter bar */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'center', marginTop: '14px' }}>
          <input
            type="text"
            placeholder="Search by rule code, name, family, script..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{ maxWidth: 360, fontSize: '13px', padding: '6px 12px' }}
          />
          <span className="text-xs text-muted">
            Showing <strong>{filteredRules.length}</strong> / <strong>{rules.length}</strong> rules
          </span>
          <button type="button" className="ghost-button compact ml-auto" onClick={() => void loadRules()} disabled={loading}>
            {loading ? 'Loading...' : 'Refresh'}
          </button>
        </div>
      </section>

      {/* Rules Table */}
      <section className="panel">
        <div className="table-responsive">
          <table className="analytics-data-table" role="table">
            <thead>
              <tr>
                <th style={{ width: '80px' }}>Priority</th>
                <th>Rule Code / Name</th>
                <th>Family / Class</th>
                <th>Defect Type ID</th>
                <th>Script Status</th>
                <th>Updated</th>
                <th style={{ width: '220px', textAlign: 'right' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredRules.map((rule) => {
                const hasScript = Boolean(rule.scriptContent?.trim())
                return (
                  <tr key={rule.id}>
                    <td>
                      <span
                        className="tag-badge font-mono"
                        style={{
                          background: rule.priority === 0 ? 'rgba(245, 158, 11, 0.2)' : 'rgba(96, 165, 250, 0.15)',
                          color: rule.priority === 0 ? '#f59e0b' : '#60a5fa',
                          fontWeight: 700,
                          fontSize: '11px',
                        }}
                      >
                        Priority {rule.priority ?? 0}
                      </span>
                    </td>
                    <td>
                      <div>
                        <strong className="mono-code" style={{ fontSize: '13px', color: '#e2e8f0' }}>
                          {rule.ruleCode}
                        </strong>
                      </div>
                      <small className="text-muted">{rule.ruleName}</small>
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: '4px', alignItems: 'center' }}>
                        {rule.defectFamily && <StatusPill value={rule.defectFamily} />}
                        <strong className="text-xs">{rule.defectClass || '—'}</strong>
                      </div>
                    </td>
                    <td className="font-mono text-xs text-muted">
                      {rule.defectTypeId || rule.id.slice(0, 8) + '...'}
                    </td>
                    <td>
                      {hasScript ? (
                        <span
                          className="tag-badge"
                          style={{
                            background: 'rgba(34, 197, 94, 0.15)',
                            color: '#22c55e',
                            fontWeight: 600,
                            fontSize: '11px',
                            cursor: 'pointer',
                          }}
                          onClick={() => setInspectingRule(rule)}
                          title="Click to view script content"
                        >
                          ✓ Script Present ({rule.scriptContent?.split('\n').length} lines)
                        </span>
                      ) : (
                        <span className="tag-badge" style={{ background: 'rgba(239, 68, 68, 0.15)', color: '#ef4444', fontSize: '11px' }}>
                          No Script (N/A)
                        </span>
                      )}
                    </td>
                    <td className="font-mono text-xs text-muted">
                      {formatDateTime(rule.updatedAtUtc || rule.createdAtUtc)}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      <div style={{ display: 'flex', gap: '6px', justifyContent: 'flex-end' }}>
                        <button
                          type="button"
                          className="ghost-button compact"
                          style={{ fontSize: '11px', color: '#10b981', fontWeight: 600 }}
                          onClick={() => setOptimizingRule(rule)}
                          title="Auto-calculate Gold Thresholds and run What-If simulations"
                        >
                          🎯 Optimize
                        </button>
                        <button
                          type="button"
                          className="ghost-button compact"
                          style={{ fontSize: '11px', color: '#38bdf8' }}
                          onClick={() => handleOpenTester(rule)}
                          title="Run script tester for this rule"
                        >
                          ⚡ Test
                        </button>
                        <button
                          type="button"
                          className="ghost-button compact"
                          style={{ fontSize: '11px' }}
                          onClick={() => {
                            setEditingRule(rule)
                            setFormData({
                              ruleCode: rule.ruleCode,
                              ruleName: rule.ruleName,
                              version: rule.version,
                              priority: rule.priority ?? 0,
                              defectClass: rule.defectClass || '',
                              defectFamily: rule.defectFamily || 'Optical',
                              machineCode: rule.machineCode || '',
                              lineCode: rule.lineCode || '',
                              stationCode: rule.stationCode || '',
                              scriptContent: rule.scriptContent || '',
                              defectTypeId: rule.defectTypeId || '',
                              description: rule.description || '',
                              isActive: rule.isActive,
                            })
                            setShowCreateModal(true)
                          }}
                        >
                          ✏️ Edit
                        </button>
                        <button
                          type="button"
                          className="ghost-button compact"
                          style={{ fontSize: '11px', color: '#ef4444' }}
                          onClick={() => void handleDeleteRule(rule)}
                        >
                          🗑️
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })}
              {filteredRules.length === 0 && (
                <tr>
                  <td colSpan={7} className="text-center text-muted" style={{ padding: '36px' }}>
                    No Classifier Rules found. Click <strong>Import AOI Rules JSON</strong> to load rules from AOI machines.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {/* MODAL 1: INTERACTIVE SCRIPT TESTER (SIMULATOR) */}
      {showTestModal && (
        <Modal
          title="⚡ Interactive AOI Script Tester"
          description="Test classifier scripts against simulated or sampled feature values to inspect True/False decisions and individual condition breakdowns."
          open={showTestModal}
          onClose={() => setShowTestModal(false)}
          wide
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            {/* Rule Selector toolbar */}
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
              <Field label="Load Existing Rule">
                <select
                  value={testRuleId}
                  onChange={(e) => {
                    const id = e.target.value
                    setTestRuleId(id)
                    const r = rules.find((x) => x.id === id)
                    if (r?.scriptContent) {
                      setTestScriptText(r.scriptContent)
                    }
                  }}
                  style={{ minWidth: 200 }}
                >
                  <option value="">-- Select Rule to Load Script --</option>
                  {rules.map((r) => (
                    <option key={r.id} value={r.id}>
                      Priority {r.priority ?? 0}: {r.ruleName}
                    </option>
                  ))}
                </select>
              </Field>

              <div style={{ marginTop: 'auto', display: 'flex', gap: '6px' }}>
                <button
                  type="button"
                  className="ghost-button compact"
                  onClick={() => {
                    setTestFeaturesJson(
                      JSON.stringify(
                        {
                          G010_VpID: 1,
                          G008_LightID: 1,
                          G009_RoiID: 24,
                          G062_OrgGrayMax: 35,
                          G064_OrgGrayStdDev: 4,
                        },
                        null,
                        2
                      )
                    )
                  }}
                >
                  Sample Features: Light_Fail (Pass NG)
                </button>
                <button
                  type="button"
                  className="ghost-button compact"
                  onClick={() => {
                    setTestFeaturesJson(
                      JSON.stringify(
                        {
                          G010_VpID: 1,
                          G008_LightID: 1,
                          G009_RoiID: 1,
                          G131_Real_DicToLeft: 100,
                          G132_Real_DicToRigt: 100,
                          G133_Real_DicToTop: 100,
                          G134_Real_DicToBot: 100,
                          G063_OrgGrayMin: 100,
                          V003_VP_IP: 1,
                          G020_PxlArea: 100,
                          G017_PxlWidth: 30,
                          G018_PxlHeight: 30,
                          G038_PrcGrayMin: 30,
                          MLClass01: 95,
                        },
                        null,
                        2
                      )
                    )
                  }}
                >
                  Sample Features: DIC_Crack (Pass NG)
                </button>
              </div>
            </div>

            {/* Two Column Layout: Left Script, Right Features */}
            <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr', gap: '16px' }}>
              <div>
                <label className="text-xs font-bold" style={{ color: '#93c5fd', display: 'block', marginBottom: '6px' }}>
                  Python-like Classifier Script to Test:
                </label>
                <textarea
                  value={testScriptText}
                  onChange={(e) => setTestScriptText(e.target.value)}
                  rows={14}
                  placeholder="# Enter script..."
                  style={{
                    fontFamily: 'monospace',
                    fontSize: '12px',
                    width: '100%',
                    background: '#090d16',
                    color: '#e2e8f0',
                    padding: '10px',
                    borderRadius: '6px',
                    border: '1px solid rgba(255,255,255,0.1)',
                  }}
                />
              </div>

              <div>
                <label className="text-xs font-bold" style={{ color: '#93c5fd', display: 'block', marginBottom: '6px' }}>
                  Observation Features (Key-Value JSON):
                </label>
                <textarea
                  value={testFeaturesJson}
                  onChange={(e) => setTestFeaturesJson(e.target.value)}
                  rows={14}
                  placeholder='{ "G010_VpID": 1, ... }'
                  style={{
                    fontFamily: 'monospace',
                    fontSize: '12px',
                    width: '100%',
                    background: '#090d16',
                    color: '#e2e8f0',
                    padding: '10px',
                    borderRadius: '6px',
                    border: '1px solid rgba(255,255,255,0.1)',
                  }}
                />
              </div>
            </div>

            {/* Test Action */}
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
              <button
                type="button"
                className="primary"
                style={{ background: '#2563eb', padding: '8px 24px', fontWeight: 600 }}
                onClick={() => void handleRunTest()}
                disabled={testingBusy || !testScriptText.trim()}
              >
                {testingBusy ? 'Testing...' : '⚡ Run Script Test'}
              </button>
            </div>

            {/* Test Result Display */}
            {testResult && (
              <div
                style={{
                  background: testResult.finalResult ? 'rgba(168, 85, 247, 0.1)' : 'rgba(245, 158, 11, 0.1)',
                  border: `1px solid ${testResult.finalResult ? '#a855f7' : '#f59e0b'}`,
                  borderRadius: '8px',
                  padding: '16px',
                  marginTop: '8px',
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                  <div>
                    <span className="text-xs text-muted">OVERALL EVALUATION VERDICT:</span>
                    <h3 style={{ margin: '4px 0', color: testResult.finalResult ? '#a855f7' : '#f59e0b' }}>
                      {testResult.verdict}
                    </h3>
                  </div>
                  <span
                    className="tag-badge"
                    style={{
                      background: testResult.finalResult ? '#a855f7' : '#f59e0b',
                      color: '#fff',
                      fontSize: '14px',
                      padding: '6px 14px',
                      fontWeight: 700,
                    }}
                  >
                    {testResult.finalResult ? 'TRUE (REAL DEFECT)' : 'FALSE (N/A)'}
                  </span>
                </div>

                {/* Condition Results Chips */}
                {testResult.conditionResults && Object.keys(testResult.conditionResults).length > 0 && (
                  <div style={{ marginTop: '10px' }}>
                    <span className="text-xs text-muted" style={{ display: 'block', marginBottom: '6px' }}>
                      CONDITION BREAKDOWN:
                    </span>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                      {Object.entries(testResult.conditionResults).map(([name, val]) => (
                        <div
                          key={name}
                          style={{
                            padding: '4px 10px',
                            borderRadius: '4px',
                            fontSize: '11px',
                            fontWeight: 600,
                            fontFamily: 'monospace',
                            background: val ? 'rgba(34, 197, 94, 0.2)' : 'rgba(239, 68, 68, 0.2)',
                            color: val ? '#22c55e' : '#ef4444',
                            border: `1px solid ${val ? '#22c55e' : '#ef4444'}`,
                          }}
                        >
                          {name}: {val ? 'TRUE' : 'FALSE'}
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Guard Clauses */}
                {testResult.guardResults && Object.keys(testResult.guardResults).length > 0 && (
                  <div style={{ marginTop: '10px' }}>
                    <span className="text-xs text-muted" style={{ display: 'block', marginBottom: '6px' }}>
                      GUARD CLAUSES (EARLY FILTERS):
                    </span>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
                      {Object.entries(testResult.guardResults).map(([name, val]) => (
                        <span
                          key={name}
                          className="font-mono text-xs"
                          style={{
                            color: val ? '#22c55e' : '#ef4444',
                            background: 'rgba(255,255,255,0.05)',
                            padding: '2px 6px',
                            borderRadius: '3px',
                          }}
                        >
                          {name}: {val ? 'PASS' : 'FAIL'}
                        </span>
                      ))}
                    </div>
                  </div>
                )}

                {/* Feature Values Echo */}
                {testResult.featureValues && Object.keys(testResult.featureValues).length > 0 && (
                  <div style={{ marginTop: '12px' }}>
                    <span className="text-xs text-muted" style={{ display: 'block', marginBottom: '6px' }}>
                      FEATURE VALUES EVALUATED:
                    </span>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: '4px' }}>
                      {Object.entries(testResult.featureValues).map(([k, v]) => (
                        <div
                          key={k}
                          style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            padding: '2px 6px',
                            background: 'rgba(255,255,255,0.03)',
                            borderRadius: '3px',
                            fontSize: '11px',
                          }}
                        >
                          <span style={{ color: '#94a3b8' }}>{k}</span>
                          <strong className="font-mono">{v != null ? v.toFixed(1) : 'N/A'}</strong>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* MODAL 2: BATCH IMPORT RULES JSON */}
      {showImportJsonModal && (
        <Modal
          title="📥 Import Classifier Rules from AOI Machine JSON"
          description='Paste the JSON exported from the AOI machine (containing "Rules": [{ "Priority": 0, "Name": "...", "Script": "..." }]). All rules will be persisted to the database.'
          open={showImportJsonModal}
          onClose={() => setShowImportJsonModal(false)}
          wide
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <textarea
              value={importJsonText}
              onChange={(e) => setImportJsonText(e.target.value)}
              rows={16}
              style={{
                fontFamily: 'monospace',
                fontSize: '12px',
                width: '100%',
                background: '#090d16',
                color: '#e2e8f0',
                padding: '12px',
                borderRadius: '6px',
                border: '1px solid rgba(255,255,255,0.1)',
                whiteSpace: 'pre',
              }}
            />

            <div className="actions" style={{ justifyContent: 'flex-end' }}>
              <button type="button" className="ghost-button" onClick={() => setShowImportJsonModal(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="primary"
                style={{ background: '#059669', borderColor: '#10b981' }}
                onClick={() => void handleImportJson()}
                disabled={importingBusy || !importJsonText.trim()}
              >
                {importingBusy ? 'Importing...' : '✓ Confirm & Import All Rules'}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* MODAL 3: CREATE / EDIT RULE */}
      {showCreateModal && (
        <Modal
          title={editingRule ? `Edit Rule: ${editingRule.ruleCode}` : 'Create Classifier Rule'}
          description="Configure defect class, priority (0 first, then 1, 2...), and Python-like classifier script."
          open={showCreateModal}
          onClose={() => setShowCreateModal(false)}
          wide
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
              <Field label="Rule Code *" required>
                <input
                  type="text"
                  value={formData.ruleCode}
                  onChange={(e) => setFormData({ ...formData, ruleCode: e.target.value })}
                  placeholder="e.g. Light_Fail, DIC_Crack..."
                  disabled={Boolean(editingRule)}
                />
              </Field>

              <Field label="Display Name *" required>
                <input
                  type="text"
                  value={formData.ruleName}
                  onChange={(e) => setFormData({ ...formData, ruleName: e.target.value })}
                  placeholder="e.g. Light_Fail"
                />
              </Field>

              <Field label="Priority (0 evaluates first, then 1, 2...)">
                <input
                  type="number"
                  min={0}
                  value={formData.priority ?? 0}
                  onChange={(e) => setFormData({ ...formData, priority: parseInt(e.target.value, 10) || 0 })}
                />
              </Field>

              <Field label="Defect Class">
                <input
                  type="text"
                  value={formData.defectClass ?? ''}
                  onChange={(e) => setFormData({ ...formData, defectClass: e.target.value })}
                  placeholder="e.g. Light_Fail, DIC_Crack..."
                />
              </Field>

              <Field label="Defect Family">
                <select
                  value={formData.defectFamily ?? 'Optical'}
                  onChange={(e) => setFormData({ ...formData, defectFamily: e.target.value })}
                >
                  <option value="Optical">Optical (AOI Vision)</option>
                  <option value="Solder">Solder (SMT Joints)</option>
                  <option value="Component">Component</option>
                  <option value="ForeignMaterial">Foreign Material / Dust</option>
                </select>
              </Field>

              <Field label="Defect Type ID (GUID)">
                <input
                  type="text"
                  value={formData.defectTypeId ?? ''}
                  onChange={(e) => setFormData({ ...formData, defectTypeId: e.target.value })}
                  placeholder="{c79df3f9-4f5a-43d3-821c-7f62c2cc03ba}"
                />
              </Field>
            </div>

            <Field label="Python-like Classifier Script">
              <textarea
                value={formData.scriptContent ?? ''}
                onChange={(e) => setFormData({ ...formData, scriptContent: e.target.value })}
                rows={10}
                placeholder="Cond1 = (G020_PxlArea > 90)... return Cond1"
                style={{
                  fontFamily: 'monospace',
                  fontSize: '12px',
                  width: '100%',
                  background: '#090d16',
                  color: '#e2e8f0',
                  padding: '10px',
                  borderRadius: '6px',
                  border: '1px solid rgba(255,255,255,0.1)',
                }}
              />
            </Field>

            <div className="actions" style={{ justifyContent: 'flex-end' }}>
              <button type="button" className="ghost-button" onClick={() => setShowCreateModal(false)}>
                Cancel
              </button>
              <button type="button" className="primary" onClick={() => void handleSaveRule()}>
                {editingRule ? 'Save Changes' : 'Create Rule'}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* MODAL 4: SCRIPT INSPECTOR */}
      {inspectingRule && (
        <Modal
          title={`Script Inspector · ${inspectingRule.ruleName} (Priority ${inspectingRule.priority ?? 0})`}
          description={`Rule Code: ${inspectingRule.ruleCode} · Classification: ${inspectingRule.defectClass || '—'}`}
          open={Boolean(inspectingRule)}
          onClose={() => setInspectingRule(null)}
          wide
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
            <pre
              style={{
                background: '#090d16',
                color: '#e2e8f0',
                padding: '16px',
                borderRadius: '8px',
                fontSize: '12px',
                lineHeight: 1.5,
                overflowX: 'auto',
                border: '1px solid rgba(255,255,255,0.1)',
              }}
            >
              {inspectingRule.scriptContent || '# No script content'}
            </pre>

            <div className="actions" style={{ justifyContent: 'flex-end' }}>
              <button
                type="button"
                className="secondary"
                onClick={() => {
                  const r = inspectingRule
                  setInspectingRule(null)
                  handleOpenTester(r)
                }}
              >
                ⚡ Open in Script Tester
              </button>
              <button type="button" className="ghost-button" onClick={() => setInspectingRule(null)}>
                Close
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* MODAL 5: RULE OPTIMIZER & WHAT-IF SIMULATOR */}
      {optimizingRule && (
        <RuleOptimizerModal
          rule={optimizingRule}
          open={Boolean(optimizingRule)}
          onClose={() => setOptimizingRule(null)}
          onNotice={onNotice}
          onRuleUpdated={() => void loadRules()}
        />
      )}
    </div>
  )
}
