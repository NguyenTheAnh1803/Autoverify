import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from 'react'
import { classifierRulesApi } from '../api'
import { useConfirm } from '../confirm'
import { formatDateTime } from '../format'
// Opened on demand, so its code is downloaded only when a rule is first optimised.
const RuleOptimizerModal = lazy(() => import('../components/RuleOptimizerModal').then((m) => ({ default: m.RuleOptimizerModal })))
import { Button, DebouncedInput, Field, Icon, Modal, StatusPill } from '../ui'
import { VisualRuleBuilder } from '../components/VisualRuleBuilder'
import { activatable } from '../lib/activatable'
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

  const [loadError, setLoadError] = useState<string | null>(null)

  const loadRules = useCallback(async () => {
    setLoading(true)
    setLoadError(null)
    try {
      const items = await classifierRulesApi.listRules()
      // Sort by Priority ASC, then Name
      items.sort((a, b) => (a.priority ?? 0) - (b.priority ?? 0) || a.ruleName.localeCompare(b.ruleName))
      setRules(items)
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Failed to load classifier rules.')
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
      onNotice({ tone: 'success', message: `Successfully imported ${result.length} Rules into the system!` })
      setShowImportJsonModal(false)
      await loadRules()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Import JSON failed.' })
    } finally {
      setImportingBusy(false)
    }
  }

  // Save single rule (Create or Edit)
  // A second click while the first save is in flight would create the rule twice.
  const [savingRule, setSavingRule] = useState(false)

  async function handleSaveRule() {
    if (savingRule) return
    if (!formData.ruleCode.trim() || !formData.ruleName.trim()) {
      onNotice({ tone: 'error', message: 'Rule Code and Rule Name are required.' })
      return
    }

    setSavingRule(true)
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
        onNotice({ tone: 'success', message: `Updated Rule ${editingRule.ruleCode} successfully!` })
      } else {
        await classifierRulesApi.createRule(formData)
        onNotice({ tone: 'success', message: `Created Rule ${formData.ruleCode} successfully!` })
      }
      setShowCreateModal(false)
      setEditingRule(null)
      await loadRules()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to save rule.' })
    } finally {
      setSavingRule(false)
    }
  }

  // Delete rule
  const confirm = useConfirm()

  async function handleDeleteRule(rule: ClassifierRuleDto) {
    const ok = await confirm({
      title: `Delete rule ${rule.ruleCode}?`,
      message: 'The rule stops being applied to new inspections. This cannot be undone.',
      confirmLabel: 'Delete rule',
      tone: 'danger',
    })
    if (!ok) return
    try {
      await classifierRulesApi.deleteRule(rule.id)
      onNotice({ tone: 'success', message: `Deleted Rule ${rule.ruleCode} successfully!` })
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
            <p className="text-xs text-muted mt-1">
              Configure defect classification scripts (Light_Fail, DIC_Crack...), batch import AOI JSON rules, and test logic interactively.
            </p>
          </div>
          <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="primary font-semibold"
              style={{ background: 'var(--blue-600)', borderColor: 'var(--status-info-border)' }}
              onClick={() => handleOpenTester()}
            >
              <Icon name="operations" className="icon inline-icon" />Interactive Rule Tester
            </button>
            <button
              type="button"
              className="secondary font-semibold"
              style={{ background: 'var(--status-ok)', borderColor: 'var(--status-ok-border)', color: '#fff' }}
              onClick={() => setShowImportJsonModal(true)}
            >
              <Icon name="import" className="icon inline-icon" />Import AOI Rules JSON
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
        <div className="hstack gap-lg" style={{ marginTop: '14px' }}>
          <DebouncedInput
            type="text"
            placeholder="Search by rule code, name, family, script..."
            value={search}
            onValueChange={setSearch}
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
                <th className="text-right" style={{ width: '220px' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredRules.map((rule) => {
                const hasScript = Boolean(rule.scriptContent?.trim())
                return (
                  <tr key={rule.id}>
                    <td>
                      <span
                        className="tag-badge font-mono font-bold text-xs"
                        style={{
                          background: rule.priority === 0 ? 'var(--status-overkill-bg)' : 'var(--status-info-bg)',
                          color: rule.priority === 0 ? 'var(--status-overkill-text)' : 'var(--status-info-text)',
                        }}
                      >
                        Priority {rule.priority ?? 0}
                      </span>
                    </td>
                    <td>
                      <div>
                        <strong className="mono-code text-sm text-strong">
                          {rule.ruleCode}
                        </strong>
                      </div>
                      <small className="text-muted">{rule.ruleName}</small>
                    </td>
                    <td>
                      <div className="hstack gap-xs">
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
                            background: 'var(--status-ok-bg)',
                            color: 'var(--status-ok-text)',
                            fontWeight: 600,
                            fontSize: '12px',
                            cursor: 'pointer',
                          }}
                          {...activatable(() => setInspectingRule(rule))}
                          title="View script content"
                        >
                          <Icon name="check" className="icon inline-icon" />Script Present ({rule.scriptContent?.split('\n').length} lines)
                        </span>
                      ) : (
                        <span className="tag-badge text-ng text-xs" style={{ background: 'var(--status-ng-bg)' }}>
                          No Script (N/A)
                        </span>
                      )}
                    </td>
                    <td className="font-mono text-xs text-muted">
                      {formatDateTime(rule.updatedAtUtc || rule.createdAtUtc)}
                    </td>
                    <td className="text-right">
                      <div className="gap-sm" style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <button
                          type="button"
                          className="ghost-button compact text-xs text-ok font-semibold"
                          onClick={() => setOptimizingRule(rule)}
                          title="Auto-calculate Gold Thresholds and run What-If simulations"
                        >
                          <Icon name="target" className="icon inline-icon" />Optimize
                        </button>
                        <button
                          type="button"
                          className="ghost-button compact text-xs text-info"
                          onClick={() => handleOpenTester(rule)}
                          title="Run script tester for this rule"
                        >
                          <Icon name="operations" className="icon inline-icon" />Test
                        </button>
                        <button
                          type="button"
                          className="ghost-button compact text-xs text-info"
                          onClick={() => {
                            setEditingRule(null)
                            setFormData({
                              ruleCode: `${rule.ruleCode}_v2`,
                              ruleName: `${rule.ruleName} (v2)`,
                              version: '1.1.0',
                              priority: rule.priority ?? 0,
                              defectClass: rule.defectClass || '',
                              defectFamily: rule.defectFamily || 'Optical',
                              machineCode: rule.machineCode || '',
                              lineCode: rule.lineCode || '',
                              stationCode: rule.stationCode || '',
                              scriptContent: rule.scriptContent || '',
                              defectTypeId: rule.defectTypeId || '',
                              description: `Cloned from ${rule.ruleCode}`,
                              isActive: true,
                            })
                            setShowCreateModal(true)
                          }}
                          title="Clone as new version"
                        >
                          <Icon name="copy" className="icon inline-icon" />Clone
                        </button>
                        <button
                          type="button"
                          className="ghost-button compact text-xs"
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
                          <Icon name="edit" className="icon inline-icon" />Edit
                        </button>
                        <Button
                          variant="danger"
                          size="sm"
                          aria-label={`Delete rule ${rule.ruleCode}`}
                          onClick={() => void handleDeleteRule(rule)}
                        >
                          Delete
                        </Button>
                      </div>
                    </td>
                  </tr>
                )
              })}
              {filteredRules.length === 0 && loading && rules.length === 0 && (
                <tr aria-hidden="true">
                  <td colSpan={7} style={{ padding: '24px' }}><span className="skeleton-line" /></td>
                </tr>
              )}
              {filteredRules.length === 0 && !loading && loadError && rules.length === 0 && (
                <tr>
                  <td colSpan={7} className="error-cell" role="alert" style={{ padding: '28px', textAlign: 'center' }}>
                    <strong>The rules could not be loaded.</strong>
                    <span className="error-cell-detail">{loadError}</span>
                    <Button size="sm" variant="secondary" onClick={() => void loadRules()}>Try again</Button>
                  </td>
                </tr>
              )}
              {filteredRules.length === 0 && !loading && !loadError && (
                <tr>
                  <td colSpan={7} className="text-center text-muted" style={{ padding: '36px' }}>
                    {rules.length > 0
                      ? 'No rule matches this search.'
                      : <>No Classifier Rules found. Click <strong>Import AOI Rules JSON</strong> to load rules from AOI machines.</>}
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
          title="Interactive AOI Script Tester"
          description="Test classifier scripts against simulated or sampled feature values to inspect True/False decisions and individual condition breakdowns."
          open={showTestModal}
          onClose={() => setShowTestModal(false)}
          wide
        >
          <div className="vstack gap-xl">
            {/* Rule Selector toolbar */}
            <div className="hstack gap-md flex-wrap">
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

              <div className="gap-sm" style={{ marginTop: 'auto', display: 'flex' }}>
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
            <div className="gap-xl" style={{ display: 'grid', gridTemplateColumns: '1.2fr 1fr' }}>
              <div>
                <label className="text-xs font-bold text-info" style={{ display: 'block', marginBottom: '6px' }}>
                  Python-like Classifier Script to Test:
                </label>
                <textarea
                  className="font-mono text-xs w-full text-strong"
                  value={testScriptText}
                  onChange={(e) => setTestScriptText(e.target.value)}
                  rows={14}
                  placeholder="# Enter script..."
                  style={{
                    background: 'var(--slate-50)',
                    padding: '10px',
                    borderRadius: '6px',
                    border: '1px solid var(--slate-200)',
                  }}
                />
              </div>

              <div>
                <label className="text-xs font-bold text-info" style={{ display: 'block', marginBottom: '6px' }}>
                  Observation Features (Key-Value JSON):
                </label>
                <textarea
                  className="font-mono text-xs w-full text-strong"
                  value={testFeaturesJson}
                  onChange={(e) => setTestFeaturesJson(e.target.value)}
                  rows={14}
                  placeholder='{ "G010_VpID": 1, ... }'
                  style={{
                    background: 'var(--slate-50)',
                    padding: '10px',
                    borderRadius: '6px',
                    border: '1px solid var(--slate-200)',
                  }}
                />
              </div>
            </div>

            {/* Test Action */}
            <div className="gap-md" style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button
                type="button"
                className="primary font-semibold"
                style={{ background: 'var(--blue-600)', padding: '8px 24px' }}
                onClick={() => void handleRunTest()}
                disabled={testingBusy || !testScriptText.trim()}
              >
                {testingBusy ? 'Testing...' : 'Run Script Test'}
              </button>
            </div>

            {/* Test Result Display */}
            {testResult && (
              <div
                className="mt-2"
                style={{
                  background: testResult.finalResult ? 'var(--status-real-bg)' : 'var(--status-overkill-bg)',
                  border: `1px solid ${testResult.finalResult ? 'var(--status-real-border)' : 'var(--status-overkill-border)'}`,
                  borderRadius: '8px',
                  padding: '16px',
                }}
              >
                <div className="hstack justify-between" style={{ marginBottom: '12px' }}>
                  <div>
                    <span className="text-xs text-muted">OVERALL EVALUATION VERDICT:</span>
                    <h3 style={{ margin: '4px 0', color: testResult.finalResult ? 'var(--status-real-text)' : 'var(--status-overkill-text)' }}>
                      {testResult.verdict}
                    </h3>
                  </div>
                  <span
                    className="tag-badge text-base font-bold"
                    style={{
                      background: testResult.finalResult ? 'var(--status-real)' : 'var(--status-overkill)',
                      color: '#fff',
                      padding: '6px 14px',
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
                    <div className="gap-sm" style={{ display: 'flex', flexWrap: 'wrap' }}>
                      {Object.entries(testResult.conditionResults).map(([name, val]) => (
                        <div
                          className="text-xs font-semibold font-mono"
                          key={name}
                          style={{
                            padding: '4px 10px',
                            borderRadius: '4px',
                            background: val ? 'var(--status-ok-bg)' : 'var(--status-ng-bg)',
                            color: val ? 'var(--status-ok-text)' : 'var(--status-ng-text)',
                            border: `1px solid ${val ? 'var(--status-ok-border)' : 'var(--status-ng-border)'}`,
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
                    <div className="gap-sm" style={{ display: 'flex', flexWrap: 'wrap' }}>
                      {Object.entries(testResult.guardResults).map(([name, val]) => (
                        <span
                          key={name}
                          className="font-mono text-xs"
                          style={{
                            color: val ? 'var(--status-ok-text)' : 'var(--status-ng-text)',
                            background: 'var(--slate-50)',
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
                  <div className="mt-3">
                    <span className="text-xs text-muted" style={{ display: 'block', marginBottom: '6px' }}>
                      FEATURE VALUES EVALUATED:
                    </span>
                    <div className="gap-xs" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(180px, 100%), 1fr))' }}>
                      {Object.entries(testResult.featureValues).map(([k, v]) => (
                        <div
                          className="text-xs"
                          key={k}
                          style={{
                            display: 'flex',
                            justifyContent: 'space-between',
                            padding: '2px 6px',
                            background: 'var(--slate-50)',
                            borderRadius: '3px',
                          }}
                        >
                          <span className="text-muted">{k}</span>
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
          title="Import Classifier Rules from AOI Machine JSON"
          description='Paste the JSON exported from the AOI machine (containing "Rules": [{ "Priority": 0, "Name": "...", "Script": "..." }]). All rules will be persisted to the database.'
          open={showImportJsonModal}
          onClose={() => setShowImportJsonModal(false)}
          wide
        >
          <div className="vstack" style={{ gap: '14px' }}>
            <textarea
              className="font-mono text-xs w-full text-strong"
              value={importJsonText}
              onChange={(e) => setImportJsonText(e.target.value)}
              rows={16}
              aria-label="AOI rules JSON"
              style={{
                background: 'var(--slate-50)',
                padding: '12px',
                borderRadius: '6px',
                border: '1px solid var(--slate-200)',
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
                style={{ background: 'var(--status-ok)', borderColor: 'var(--status-ok-border)' }}
                onClick={() => void handleImportJson()}
                disabled={importingBusy || !importJsonText.trim()}
              >
                {importingBusy ? 'Importing...' : 'Confirm & Import All Rules'}
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
          <div className="vstack" style={{ gap: '14px' }}>
            <div className="gap-lg" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr' }}>
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

            <VisualRuleBuilder
              script={formData.scriptContent ?? ''}
              onChange={(newScript) => setFormData({ ...formData, scriptContent: newScript })}
            />

            <div className="actions" style={{ justifyContent: 'flex-end' }}>
              <button type="button" className="ghost-button" onClick={() => setShowCreateModal(false)}>
                Cancel
              </button>
              <button type="button" className="primary" onClick={() => void handleSaveRule()} disabled={savingRule} aria-busy={savingRule || undefined}>
                {savingRule ? 'Saving…' : editingRule ? 'Save Changes' : 'Create Rule'}
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
          <div className="vstack gap-lg">
            <pre
              className="text-strong text-xs"
              style={{
                background: 'var(--slate-50)',
                padding: '16px',
                borderRadius: '8px',
                lineHeight: 1.5,
                overflowX: 'auto',
                border: '1px solid var(--slate-200)',
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
                <Icon name="operations" className="icon inline-icon" />Open in Script Tester
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
        <Suspense fallback={null}>
          <RuleOptimizerModal
            rule={optimizingRule}
            open={Boolean(optimizingRule)}
            onClose={() => setOptimizingRule(null)}
            onNotice={onNotice}
            onRuleUpdated={() => void loadRules()}
          />
        </Suspense>
      )}
    </div>
  )
}
