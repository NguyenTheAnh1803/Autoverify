import { Fragment, useMemo, useState, type FormEvent } from 'react'
import { saveOverkillPipelineConfig, testOverkillPipelineMapping } from '../../api'
import { Button, Field, Icon, Modal } from '../../ui'
import type {
  DatabaseConnection,
  DefectBypassRule,
  DefectTableMapping,
  Notice,
  OverkillPipelineConfig,
  SaveOverkillPipelineConfigRequest,
  TestPipelineMappingRequest,
  TestPipelineMappingResponse,
} from '../../types'

type PipelineFormState = {
  id?: string
  name: string
  databaseConnectionId: string
  machineCode: string
  lineCode: string
  modelCode: string
  enabled: boolean
  scheduleOption: '60' | '120' | '180' | 'custom'
  scheduleIntervalMinutes: number
  productTableName: string
  idColumnName: string
  fkIdColumnName: string
  judgeColumnName: string
  judgeFilterStr: string
  includeOkProducts: boolean
  timestampColumnName: string
  dateTimeInspectionColumnName: string
  zoneColumnName: string
  defectNameColumnName: string
  lookbackHours: number
  batchSize: number
  offloadDefectsToSeaweedFs: boolean
  defectTables: DefectTableMapping[]
  imageCrawlerApiUrl: string
  autoCrawlImages: boolean
  imageCrawlerVpMode: string
  // Customizable Defect Bypass Rule State
  bypassRuleEnabled: boolean
  bypassConjunction: 'AND' | 'OR'
  bypassNoDefectField: string
  bypassNoDefectOperator: string
  bypassNoDefectValue: string
  bypassNameConditionEnabled: boolean
  bypassNameField: string
  bypassNameOperator: string
  bypassNameValuesStr: string
  bypassCaseInsensitive: boolean
  bypassNullPolicy: 'PreserveRow' | 'MatchAsNoDefect' | 'Strict'
}

const unaryBypassOperators = new Set(['is_null', 'is_not_null', 'is_empty', 'is_not_empty', 'is_null_or_empty'])
const listBypassOperators = new Set(['in', 'not_in'])

function formatBypassValues(rawValues: string) {
  const values = rawValues.split(',').map((value) => value.trim()).filter(Boolean)
  return values.length > 0 ? values.map((value) => `'${value}'`).join(', ') : '...'
}

function formatNoDefectCondition(field: string, operator: string, rawValue: string) {
  const normalizedOperator = operator.toLowerCase()
  const displayOperator = normalizedOperator.replaceAll('_', ' ').toUpperCase()
  if (unaryBypassOperators.has(normalizedOperator)) return `[${field || 'Judge'} ${displayOperator}]`
  if (listBypassOperators.has(normalizedOperator)) {
    return `[${field || 'Judge'} ${displayOperator} (${formatBypassValues(rawValue)})]`
  }
  const comparisonOperator = normalizedOperator === 'not_equals' ? '!=' : '='
  return `[${field || 'Judge'} ${comparisonOperator} '${rawValue.trim() || '...'}']`
}

function formatNameCondition(field: string, operator: string, rawValues: string) {
  return `[${field || 'DefectName'} ${operator || 'NOT IN'} (${formatBypassValues(rawValues)})]`
}

function createStandardDefectTable(tableName: string, fkProdId = 'fk_prod_id'): DefectTableMapping {
  const cleanName = tableName.trim()
  return {
    tableName: cleanName,
    displayName: cleanName ? cleanName.replace(/_/g, ' ').toUpperCase() : '',
    fkColumnName: fkProdId || 'fk_prod_id',
    defectNameColumn: 'name',
    judgeColumn: 'judge',
    featureColumn: 'feature',
    customFeatureColumn: 'custom_feature',
    reasonColumn: 'reason',
    imageUriColumn: 'image_path',
    eqpNameColumn: 'eqpname',
    emnoColumn: 'emno',
    specNameColumn: 'spec_name',
    specJudgeColumn: 'spec_judge',
    specReasonColumn: 'spec_reason',
    priorityColumn: 'priority',
    defectTypeColumn: '',
    severityColumn: '',
    componentNameColumn: '',
    coordXColumn: '',
    coordYColumn: '',
    offloadToStorage: true,
  }
}

/** The form as it opens: defaults from the first MariaDB connection for a new pipeline, or the saved pipeline's values. */
function initialPipelineForm(p: OverkillPipelineConfig | null, connections: DatabaseConnection[]): PipelineFormState {
  if (!p) {
    const mariaDbConnections = connections.filter((c) => !c.engine || c.engine.toLowerCase().includes('maria'))
    const defaultConn = mariaDbConnections[0] || connections[0]
    return {
      name: '',
      databaseConnectionId: defaultConn?.id ?? '',
      machineCode: (defaultConn?.machineCode && defaultConn.machineCode !== 'UNASSIGNED') ? defaultConn.machineCode : (defaultConn?.machineCode || 'MACHINE-A'),
      lineCode: (defaultConn?.lineCode && defaultConn.lineCode !== 'UNASSIGNED') ? defaultConn.lineCode : (defaultConn?.lineCode || 'LINE-1'),
      modelCode: defaultConn?.modelCode || 'UNASSIGNED',
      enabled: true,
      scheduleOption: '120',
      scheduleIntervalMinutes: 120,
      productTableName: 'Product',
      idColumnName: 'ID',
      fkIdColumnName: 'fk_prod_id',
      judgeColumnName: 'Judge',
      judgeFilterStr: 'N, NG, G, OK',
      includeOkProducts: true,
      timestampColumnName: 'datetime_inspection',
      dateTimeInspectionColumnName: 'datetime_inspection',
      zoneColumnName: 'zone',
      defectNameColumnName: 'defect_name',
      lookbackHours: 6,
      batchSize: 1000,
      offloadDefectsToSeaweedFs: true,
      defectTables: [createStandardDefectTable('mcl_defect', 'fk_prod_id')],
      imageCrawlerApiUrl: defaultConn?.apiBaseUrl || '',
      autoCrawlImages: true,
      imageCrawlerVpMode: defaultConn?.imageCrawlerVpMode || '0',
      bypassRuleEnabled: false,
      bypassConjunction: 'AND',
      bypassNoDefectField: 'Judge',
      bypassNoDefectOperator: 'equals',
      bypassNoDefectValue: 'G',
      bypassNameConditionEnabled: false,
      bypassNameField: 'DefectName',
      bypassNameOperator: 'NOT IN',
      bypassNameValuesStr: 'FD, filter',
      bypassCaseInsensitive: true,
      bypassNullPolicy: 'PreserveRow',
    }
  }
  const schedOption =
    p.scheduleIntervalMinutes === 60
      ? '60'
      : p.scheduleIntervalMinutes === 120
        ? '120'
        : p.scheduleIntervalMinutes === 180
          ? '180'
          : 'custom'

  const conn = connections.find((c) => c.id === p.databaseConnectionId)
  return {
    id: p.id,
    name: p.name,
    databaseConnectionId: p.databaseConnectionId,
    machineCode: p.machineCode?.trim() || conn?.machineCode?.trim() || 'MACHINE-A',
    lineCode: p.lineCode?.trim() || conn?.lineCode?.trim() || 'LINE-1',
    modelCode: p.modelCode?.trim() || conn?.modelCode?.trim() || 'UNASSIGNED',
    enabled: p.enabled,
    scheduleOption: schedOption,
    scheduleIntervalMinutes: p.scheduleIntervalMinutes,
    productTableName: p.productTableName,
    idColumnName: p.idColumnName,
    fkIdColumnName: p.fkIdColumnName,
    judgeColumnName: p.judgeColumnName,
    judgeFilterStr: (p.judgeFilterValues && p.judgeFilterValues.length > 0) ? p.judgeFilterValues.join(', ') : 'N, NG, G, OK',
    includeOkProducts: p.includeOkProducts ?? true,
    timestampColumnName: p.dateTimeInspectionColumnName || p.inspectedAtColumnName || p.timestampColumnName || 'datetime_inspection',
    dateTimeInspectionColumnName: p.dateTimeInspectionColumnName || p.inspectedAtColumnName || p.timestampColumnName || 'datetime_inspection',
    zoneColumnName: p.zoneColumnName ?? 'zone',
    defectNameColumnName: p.defectNameColumnName ?? 'defect_name',
    lookbackHours: p.lookbackHours,
    batchSize: p.batchSize,
    offloadDefectsToSeaweedFs: p.offloadDefectsToSeaweedFs ?? true,
    defectTables: p.defectTables && p.defectTables.length > 0 ? [...p.defectTables] : [createStandardDefectTable('mcl_defect', p.fkIdColumnName || 'fk_prod_id')],
    imageCrawlerApiUrl: p.imageCrawlerApiUrl ?? '',
    autoCrawlImages: p.autoCrawlImages ?? true,
    imageCrawlerVpMode: p.imageCrawlerVpMode ?? '0',
    bypassRuleEnabled: (p.bypassRule ?? p.defectBypassRule)?.enabled ?? false,
    bypassConjunction: (p.bypassRule ?? p.defectBypassRule)?.conjunction ?? 'AND',
    bypassNoDefectField: (p.bypassRule ?? p.defectBypassRule)?.noDefectCondition?.field ?? 'Judge',
    bypassNoDefectOperator: (p.bypassRule ?? p.defectBypassRule)?.noDefectCondition?.operator ?? 'equals',
    bypassNoDefectValue: ((p.bypassRule ?? p.defectBypassRule)?.noDefectCondition?.value ?? ((p.bypassRule ?? p.defectBypassRule)?.noDefectCondition?.values ?? []).join(', ')) || 'G',
    bypassNameConditionEnabled: Boolean((p.bypassRule ?? p.defectBypassRule)?.nameCondition && ((p.bypassRule ?? p.defectBypassRule)?.nameCondition?.values?.length ?? 0) > 0),
    bypassNameField: (p.bypassRule ?? p.defectBypassRule)?.nameCondition?.field ?? 'DefectName',
    bypassNameOperator: (p.bypassRule ?? p.defectBypassRule)?.nameCondition?.operator ?? 'NOT IN',
    bypassNameValuesStr: ((p.bypassRule ?? p.defectBypassRule)?.nameCondition?.values ?? ['FD', 'filter']).join(', '),
    bypassCaseInsensitive: (p.bypassRule ?? p.defectBypassRule)?.caseInsensitive ?? true,
    bypassNullPolicy: (p.bypassRule ?? p.defectBypassRule)?.nullPolicy ?? 'PreserveRow',
  }
}

/**
 * Create / edit an Overkill pipeline. The form keeps its own state, so typing in any of its fields re-renders
 * only this dialog, not the pipeline list, execution history and query profiles behind it.
 */
export function PipelineFormModal({
  pipeline,
  connections,
  onClose,
  onSaved,
  onNotice,
}: {
  /** null opens an empty form for a new pipeline */
  pipeline: OverkillPipelineConfig | null
  connections: DatabaseConnection[]
  onClose: () => void
  /** called after a successful save; the caller closes the dialog and reloads the list */
  onSaved: () => void
  onNotice: (notice: Notice) => void
}) {
  const [pipelineForm, setPipelineForm] = useState<PipelineFormState>(() => initialPipelineForm(pipeline, connections))
  const [showAdvancedBypass, setShowAdvancedBypass] = useState(() => {
    const rule = pipeline?.bypassRule ?? pipeline?.defectBypassRule
    return Boolean(rule?.nameCondition && (rule.nameCondition.values?.length ?? 0) > 0)
  })
  const [showBatchAdd, setShowBatchAdd] = useState(false)
  const [batchInput, setBatchInput] = useState('')
  const [expandedRow, setExpandedRow] = useState<number | null>(null)
  const [testResult, setTestResult] = useState<TestPipelineMappingResponse | null>(null)
  const [testing, setTesting] = useState(false)
  const [busy, setBusy] = useState(false)

  const parsedBatchDefectTables = useMemo(() => {
    if (!batchInput.trim()) return []
    const raw = batchInput.split(/[\n,\t]+/).map((s) => s.trim()).filter(Boolean)
    const unique = [...new Set(raw)]
    return unique.map((n) => createStandardDefectTable(n, pipelineForm.fkIdColumnName || 'fk_prod_id'))
  }, [batchInput, pipelineForm.fkIdColumnName])
  const selectedPipelineConnection = useMemo(
    () => connections.find((c) => c.id === pipelineForm.databaseConnectionId) ?? null,
    [connections, pipelineForm.databaseConnectionId],
  )

  function onPipelineConnectionChange(connId: string) {
    const conn = connections.find((c) => c.id === connId)
    setPipelineForm((prev) => ({
      ...prev,
      databaseConnectionId: connId,
      machineCode: (conn?.machineCode && conn.machineCode.trim()) ? conn.machineCode.trim() : (prev.machineCode || 'MACHINE-A'),
      lineCode: (conn?.lineCode && conn.lineCode.trim()) ? conn.lineCode.trim() : (prev.lineCode || 'LINE-1'),
      modelCode: (conn?.modelCode && conn.modelCode.trim()) ? conn.modelCode.trim() : (prev.modelCode || 'UNASSIGNED'),
    }))
  }

  function onScheduleOptionChange(val: string) {
    if (val === '60' || val === '120' || val === '180') {
      const mins = Number(val)
      setPipelineForm((prev) => ({ ...prev, scheduleOption: val as '60' | '120' | '180', scheduleIntervalMinutes: mins }))
    } else {
      setPipelineForm((prev) => ({ ...prev, scheduleOption: 'custom' }))
    }
  }

  function addDefectTable(tableName = '') {
    setPipelineForm((prev) => ({
      ...prev,
      defectTables: [
        ...prev.defectTables,
        createStandardDefectTable(tableName, prev.fkIdColumnName || 'fk_prod_id'),
      ],
    }))
  }

  function duplicateDefectTable(index: number) {
    setPipelineForm((prev) => {
      const source = prev.defectTables[index]
      if (!source) return prev
      const clone = { ...source, tableName: `${source.tableName}_copy`, displayName: `${source.displayName || source.tableName} (Copy)` }
      const nextTables = [...prev.defectTables]
      nextTables.splice(index + 1, 0, clone)
      return { ...prev, defectTables: nextTables }
    })
  }

  function handleBatchAdd() {
    if (!batchInput.trim()) return
    const rawNames = batchInput
      .split(/[\n,\t]+/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0)

    if (rawNames.length === 0) return

    setPipelineForm((prev) => {
      const existingNames = new Set(prev.defectTables.map((t) => t.tableName.toLowerCase()))
      const newTables: DefectTableMapping[] = []

      for (const name of rawNames) {
        if (!existingNames.has(name.toLowerCase())) {
          newTables.push(createStandardDefectTable(name, prev.fkIdColumnName || 'fk_prod_id'))
          existingNames.add(name.toLowerCase())
        }
      }

      if (newTables.length === 0) return prev
      return {
        ...prev,
        defectTables: [...prev.defectTables, ...newTables],
      }
    })

    setBatchInput('')
    setShowBatchAdd(false)
    onNotice({ tone: 'success', message: `Added ${rawNames.length} defect table mappings with standard schema.` })
  }

  function standardizeAllDefectTables() {
    setPipelineForm((prev) => ({
      ...prev,
      defectTables: prev.defectTables.map((t) => ({
        ...createStandardDefectTable(t.tableName, prev.fkIdColumnName || 'fk_prod_id'),
        displayName: t.displayName || t.tableName.replace(/_/g, ' ').toUpperCase(),
      })),
    }))
    onNotice({ tone: 'success', message: 'Standardized all defect tables with standard schema columns.' })
  }

  function removeDefectTable(index: number) {
    setPipelineForm((prev) => ({
      ...prev,
      defectTables: prev.defectTables.filter((_, i) => i !== index),
    }))
    setExpandedRow((prev) => (prev === index ? null : prev && prev > index ? prev - 1 : prev))
  }

  function updateDefectTable<K extends keyof DefectTableMapping>(index: number, field: K, value: DefectTableMapping[K]) {
    setPipelineForm((prev) => {
      const nextTables = [...prev.defectTables]
      const current = nextTables[index]
      if (!current) return prev

      const updated = { ...current, [field]: value }
      if (field === 'tableName' && typeof value === 'string' && (!current.displayName || current.displayName === current.tableName.replace(/_/g, ' ').toUpperCase())) {
        updated.displayName = value.replace(/_/g, ' ').toUpperCase()
      }
      nextTables[index] = updated
      return { ...prev, defectTables: nextTables }
    })
  }

  async function handleTestMapping() {
    if (!pipelineForm.databaseConnectionId) {
      onNotice({ tone: 'error', message: 'Please select a MariaDB connection first.' })
      return
    }
    if (!pipelineForm.productTableName.trim()) {
      onNotice({ tone: 'error', message: 'Product table name is required.' })
      return
    }

    setTesting(true)
    setTestResult(null)
    try {
      const filterVals = pipelineForm.judgeFilterStr
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean)

      const rawNoDefectVal = pipelineForm.bypassNoDefectValue.trim() || 'G'
      const hasComma = rawNoDefectVal.includes(',')
      const effectiveOperator = (pipelineForm.bypassNoDefectOperator === 'equals' && hasComma)
        ? 'in'
        : pipelineForm.bypassNoDefectOperator
      const isUnaryNoDefect = unaryBypassOperators.has(effectiveOperator.toLowerCase())
      const isListNoDefect = listBypassOperators.has(effectiveOperator.toLowerCase())

      if (pipelineForm.bypassRuleEnabled) {
        if (!isUnaryNoDefect && !rawNoDefectVal) {
          onNotice({ tone: 'error', message: 'Please specify the Judge value to bypass (e.g. G or OK).' })
          return
        }
      }

      const noDefectValues = isListNoDefect || hasComma
        ? rawNoDefectVal.split(',').map((s) => s.trim()).filter(Boolean)
        : null
      const noDefectVal = !isUnaryNoDefect && !isListNoDefect && !hasComma ? rawNoDefectVal : null
      const nameValues = pipelineForm.bypassNameConditionEnabled
        ? pipelineForm.bypassNameValuesStr.split(',').map((s) => s.trim()).filter(Boolean)
        : []

      const nameCondition = (pipelineForm.bypassNameConditionEnabled && nameValues.length > 0)
        ? {
            field: pipelineForm.bypassNameField.trim() || 'DefectName',
            operator: pipelineForm.bypassNameOperator.trim() || 'NOT IN',
            values: nameValues,
            caseInsensitive: pipelineForm.bypassCaseInsensitive,
          }
        : null

      const bypassRulePayload: DefectBypassRule = {
        enabled: pipelineForm.bypassRuleEnabled,
        conjunction: pipelineForm.bypassConjunction,
        noDefectCondition: {
          field: pipelineForm.bypassNoDefectField.trim() || 'Judge',
          operator: effectiveOperator,
          value: noDefectVal,
          values: noDefectValues,
          caseInsensitive: pipelineForm.bypassCaseInsensitive,
        },
        nameCondition: nameCondition,
        caseInsensitive: pipelineForm.bypassCaseInsensitive,
        nullPolicy: pipelineForm.bypassNullPolicy,
      }

      const payload: TestPipelineMappingRequest = {
        databaseConnectionId: pipelineForm.databaseConnectionId,
        productTableName: pipelineForm.productTableName.trim(),
        idColumnName: pipelineForm.idColumnName.trim() || 'ID',
        fkIdColumnName: pipelineForm.fkIdColumnName.trim() || 'Fk_ID',
        judgeColumnName: pipelineForm.judgeColumnName.trim() || 'Judge',
        judgeFilterValues: filterVals.length > 0 ? filterVals : ['N', 'NG'],
        zoneColumnName: pipelineForm.zoneColumnName.trim() || undefined,
        defectNameColumnName: pipelineForm.defectNameColumnName.trim() || undefined,
        dateTimeInspectionColumnName: pipelineForm.dateTimeInspectionColumnName.trim() || undefined,
        inspectedAtColumnName: pipelineForm.dateTimeInspectionColumnName.trim() || undefined,
        defectTables: pipelineForm.defectTables.filter((t) => t.tableName.trim().length > 0),
        sampleLimit: 10,
        defectSampleLimit: 2000,
        bypassRule: bypassRulePayload,
      }

      const res = await testOverkillPipelineMapping(payload)
      setTestResult(res)
      if (res.success) {
        onNotice({
          tone: 'success',
          message: `Mapping test passed: Retrieved ${res.sampleProductsCount} product sample(s) in ${res.latencyMs}ms.`,
        })
      } else {
        onNotice({ tone: 'error', message: res.errorMessage || 'Mapping test returned errors.' })
      }
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Mapping test failed.' })
    } finally {
      setTesting(false)
    }
  }

  async function handleSavePipeline(e: FormEvent) {
    e.preventDefault()
    if (!pipelineForm.name.trim()) {
      onNotice({ tone: 'error', message: 'Pipeline name is required.' })
      return
    }
    if (!pipelineForm.databaseConnectionId) {
      onNotice({ tone: 'error', message: 'MariaDB database connection is required.' })
      return
    }

    setBusy(true)
    try {
      const filterVals = pipelineForm.judgeFilterStr
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean)

      const rawNoDefectVal = pipelineForm.bypassNoDefectValue.trim() || 'G'
      const hasComma = rawNoDefectVal.includes(',')
      const effectiveOperator = (pipelineForm.bypassNoDefectOperator === 'equals' && hasComma)
        ? 'in'
        : pipelineForm.bypassNoDefectOperator
      const isUnaryNoDefect = unaryBypassOperators.has(effectiveOperator.toLowerCase())
      const isListNoDefect = listBypassOperators.has(effectiveOperator.toLowerCase())

      if (pipelineForm.bypassRuleEnabled) {
        if (!isUnaryNoDefect && !rawNoDefectVal) {
          onNotice({ tone: 'error', message: 'Please specify the Judge value to bypass (e.g. G or OK).' })
          return
        }
      }

      const noDefectValues = isListNoDefect || hasComma
        ? rawNoDefectVal.split(',').map((s) => s.trim()).filter(Boolean)
        : null
      const noDefectVal = !isUnaryNoDefect && !isListNoDefect && !hasComma ? rawNoDefectVal : null
      const nameValues = pipelineForm.bypassNameConditionEnabled
        ? pipelineForm.bypassNameValuesStr.split(',').map((s) => s.trim()).filter(Boolean)
        : []

      const nameCondition = (pipelineForm.bypassNameConditionEnabled && nameValues.length > 0)
        ? {
            field: pipelineForm.bypassNameField.trim() || 'DefectName',
            operator: pipelineForm.bypassNameOperator.trim() || 'NOT IN',
            values: nameValues,
            caseInsensitive: pipelineForm.bypassCaseInsensitive,
          }
        : null

      const bypassRulePayload: DefectBypassRule = {
        enabled: pipelineForm.bypassRuleEnabled,
        conjunction: pipelineForm.bypassConjunction,
        noDefectCondition: {
          field: pipelineForm.bypassNoDefectField.trim() || 'Judge',
          operator: effectiveOperator,
          value: noDefectVal,
          values: noDefectValues,
          caseInsensitive: pipelineForm.bypassCaseInsensitive,
        },
        nameCondition: nameCondition,
        caseInsensitive: pipelineForm.bypassCaseInsensitive,
        nullPolicy: pipelineForm.bypassNullPolicy,
      }

      const payload: SaveOverkillPipelineConfigRequest = {
        id: pipelineForm.id,
        name: pipelineForm.name.trim(),
        databaseConnectionId: pipelineForm.databaseConnectionId,
        machineCode: pipelineForm.machineCode.trim() || selectedPipelineConnection?.machineCode || 'MACHINE-A',
        lineCode: pipelineForm.lineCode.trim() || selectedPipelineConnection?.lineCode || 'LINE-1',
        modelCode: selectedPipelineConnection?.modelCode || 'UNASSIGNED',
        enabled: pipelineForm.enabled,
        scheduleIntervalMinutes: Math.max(5, pipelineForm.scheduleIntervalMinutes),
        productTableName: pipelineForm.productTableName.trim() || 'Product',
        idColumnName: pipelineForm.idColumnName.trim() || 'ID',
        fkIdColumnName: pipelineForm.fkIdColumnName.trim() || 'Fk_ID',
        judgeColumnName: pipelineForm.judgeColumnName.trim() || 'Judge',
        judgeFilterValues: filterVals.length > 0 ? filterVals : ['N', 'NG'],
        timestampColumnName: pipelineForm.dateTimeInspectionColumnName.trim() || pipelineForm.timestampColumnName.trim() || null,
        dateTimeInspectionColumnName: pipelineForm.dateTimeInspectionColumnName.trim() || undefined,
        inspectedAtColumnName: pipelineForm.dateTimeInspectionColumnName.trim() || undefined,
        zoneColumnName: pipelineForm.zoneColumnName.trim() || undefined,
        defectNameColumnName: pipelineForm.defectNameColumnName.trim() || undefined,
        lookbackHours: Math.max(1, pipelineForm.lookbackHours),
        batchSize: Math.max(10, pipelineForm.batchSize),
        offloadDefectsToSeaweedFs: pipelineForm.offloadDefectsToSeaweedFs,
        includeOkProducts: pipelineForm.includeOkProducts,
        imageCrawlerApiUrl: pipelineForm.imageCrawlerApiUrl.trim() || undefined,
        autoCrawlImages: pipelineForm.autoCrawlImages,
        imageCrawlerVpMode: pipelineForm.imageCrawlerVpMode || '0',
        defectTables: pipelineForm.defectTables.filter((t) => t.tableName.trim().length > 0),
        bypassRule: bypassRulePayload,
      }

      await saveOverkillPipelineConfig(payload)
      onNotice({
        tone: 'success',
        message: pipelineForm.id ? `Pipeline '${payload.name}' updated.` : `Pipeline '${payload.name}' saved and scheduled.`,
      })
      onSaved()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to save pipeline.' })
    } finally {
      setBusy(false)
    }
  }


  return (
    <Modal
      title={pipelineForm.id ? `Edit Pipeline · ${pipelineForm.name}` : 'New Overkill MariaDB Pipeline'}
      description={pipelineForm.id ? 'PUT /api/v1/overkill-pipeline/configs/{id}' : 'POST /api/v1/overkill-pipeline/configs'}
      open
      onClose={onClose}
      wide
    >
      <form className="modal-form" onSubmit={(e) => void handleSavePipeline(e)}>
        <div className="query-form-grid">
          <Field label="Pipeline Name" required hint="Identifier for line operators">
            <input
              value={pipelineForm.name}
              onChange={(e) => setPipelineForm((prev) => ({ ...prev, name: e.target.value }))}
              placeholder="e.g. SMT Line 1 Solder & Optical Scan"
              required
            />
          </Field>
          <Field label="Target MariaDB Connection" required hint="Database credential source (syncs Machine & Line)">
            <select
              value={pipelineForm.databaseConnectionId}
              onChange={(e) => onPipelineConnectionChange(e.target.value)}
              required
            >
              <option value="">Select MariaDB connection</option>
              {connections.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.machineCode || '—'}/{c.lineCode || '—'}) · {c.host}:{c.port}/{c.databaseName}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Schedule Interval" required hint="Automatic query period">
            <select
              value={pipelineForm.scheduleOption}
              onChange={(e) => onScheduleOptionChange(e.target.value)}
            >
              <option value="60">Every 1 hour (60m)</option>
              <option value="120">Every 2 hours (120m) [Recommended]</option>
              <option value="180">Every 3 hours (180m)</option>
              <option value="custom">Custom interval (minutes)...</option>
            </select>
          </Field>
        </div>

        {pipelineForm.scheduleOption === 'custom' && (
          <div style={{ maxWidth: '280px' }}>
            <Field label="Custom Interval (Minutes)" required hint="Minimum 5 minutes">
              <input
                type="number"
                min={5}
                max={10080}
                value={pipelineForm.scheduleIntervalMinutes}
                onChange={(e) =>
                  setPipelineForm((prev) => ({ ...prev, scheduleIntervalMinutes: Number(e.target.value) || 60 }))
                }
                required
              />
            </Field>
          </div>
        )}

        <div className="query-form-grid">
          <Field
            label="Machine Code"
            required
            hint={
              selectedPipelineConnection
                ? pipelineForm.machineCode === selectedPipelineConnection.machineCode
                  ? `Inherited from '${selectedPipelineConnection.name}'`
                  : `Custom override (Connection: ${selectedPipelineConnection.machineCode || '—'})`
                : 'Target machine code'
            }
          >
            <input
              value={pipelineForm.machineCode}
              onChange={(e) => setPipelineForm((prev) => ({ ...prev, machineCode: e.target.value }))}
              placeholder={selectedPipelineConnection?.machineCode || 'e.g. MACHINE-A'}
              required
            />
          </Field>
          <Field
            label="Line Code"
            required
            hint={
              selectedPipelineConnection
                ? pipelineForm.lineCode === selectedPipelineConnection.lineCode
                  ? `Inherited from '${selectedPipelineConnection.name}'`
                  : `Custom override (Connection: ${selectedPipelineConnection.lineCode || '—'})`
                : 'Target line code'
            }
          >
            <input
              value={pipelineForm.lineCode}
              onChange={(e) => setPipelineForm((prev) => ({ ...prev, lineCode: e.target.value }))}
              placeholder={selectedPipelineConnection?.lineCode || 'e.g. LINE-1'}
              required
            />
          </Field>
          <Field
            label="Model"
            hint="Read-only: mapped directly from the selected Database Connection"
          >
            <input
              value={selectedPipelineConnection?.modelCode || 'UNASSIGNED'}
              readOnly
              disabled
            />
          </Field>
          <Field label="Automated Schedule Enabled">
            <label className="check-row mt-2">
              <input
                type="checkbox"
                checked={pipelineForm.enabled}
                onChange={(e) => setPipelineForm((prev) => ({ ...prev, enabled: e.target.checked }))}
              />
              <span>Run automatically on scheduler</span>
            </label>
          </Field>
        </div>

        {/* Product Table Settings */}
        <section className="pipeline-config-section">
          <div className="pipeline-section-heading">
            <div>
              <h3>1. Product Table Query Settings</h3>
              <p>Map the product table and query limits in one editable row.</p>
            </div>
            <span className="pipeline-section-badge">Product source</span>
          </div>

          <div className="product-query-table-wrap">
            <table className="product-query-table">
              <thead>
                <tr>
                  <th>Product table *</th>
                  <th>Product ID *</th>
                  <th>Defect FK *</th>
                  <th>Judge column *</th>
                  <th>Judge values *</th>
                  <th>Inspection time</th>
                  <th>Zone</th>
                  <th>Defect name</th>
                  <th>Lookback (h)</th>
                  <th>Batch size</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>
                    <input
                      aria-label="Product table name"
                      value={pipelineForm.productTableName}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, productTableName: e.target.value }))}
                      required
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Product ID column"
                      title="Primary key or serial column"
                      value={pipelineForm.idColumnName}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, idColumnName: e.target.value }))}
                      required
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Defect foreign key column"
                      title="Foreign key used by defect tables"
                      value={pipelineForm.fkIdColumnName}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, fkIdColumnName: e.target.value }))}
                      required
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Judge column"
                      value={pipelineForm.judgeColumnName}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, judgeColumnName: e.target.value }))}
                      required
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Judge filter values"
                      title="Comma-separated values, for example N, NG"
                      value={pipelineForm.judgeFilterStr}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, judgeFilterStr: e.target.value }))}
                      required
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Inspection datetime column"
                      value={pipelineForm.dateTimeInspectionColumnName}
                      placeholder="datetime_inspection"
                      onChange={(e) => setPipelineForm((prev) => ({
                        ...prev,
                        dateTimeInspectionColumnName: e.target.value,
                        timestampColumnName: e.target.value,
                      }))}
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Zone column"
                      value={pipelineForm.zoneColumnName}
                      placeholder="zone"
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, zoneColumnName: e.target.value }))}
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Defect name column"
                      value={pipelineForm.defectNameColumnName}
                      placeholder="defect_name"
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, defectNameColumnName: e.target.value }))}
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Lookback window in hours"
                      type="number"
                      min={1}
                      max={168}
                      value={pipelineForm.lookbackHours}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, lookbackHours: Number(e.target.value) || 6 }))}
                    />
                  </td>
                  <td>
                    <input
                      aria-label="Batch size limit"
                      type="number"
                      min={10}
                      max={10000}
                      value={pipelineForm.batchSize}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, batchSize: Number(e.target.value) || 1000 }))}
                    />
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <label className="storage-toggle-row">
            <input
              type="checkbox"
              checked={pipelineForm.offloadDefectsToSeaweedFs}
              onChange={(e) => setPipelineForm((prev) => ({ ...prev, offloadDefectsToSeaweedFs: e.target.checked }))}
            />
            <span>
              <strong>Offload feature JSON and images to SeaweedFS</strong>
              <small>Recommended for smaller MariaDB rows and faster queries.</small>
            </span>
          </label>

          <label className="storage-toggle-row mt-2" style={{ border: '1px solid var(--status-info-border)', background: 'var(--status-info-bg)' }}>
            <input
              type="checkbox"
              checked={pipelineForm.includeOkProducts}
              onChange={(e) => {
                const checked = e.target.checked
                setPipelineForm((prev) => {
                  const vals = prev.judgeFilterStr.split(',').map((s) => s.trim()).filter(Boolean)
                  if (checked) {
                    for (const v of ['G', 'OK']) {
                      if (!vals.includes(v)) vals.push(v)
                    }
                  }
                  return {
                    ...prev,
                    includeOkProducts: checked,
                    judgeFilterStr: vals.join(', '),
                  }
                })
              }}
            />
            <span>
              <strong style={{ color: 'var(--blue-700)' }}>Include OK Product IDs in inspection window (Throughput Tracking)</strong>
              <small>Collects both OK and NG units to benchmark throughput, station NG rates, and overkill across lines.</small>
            </span>
          </label>

          {/* PC Image Crawler Configuration */}
          <div style={{ marginTop: '10px', padding: '10px 12px', background: 'var(--status-real-bg)', border: '1px solid var(--status-real-border)', borderRadius: '6px' }}>
            <div className="hstack justify-between" style={{ marginBottom: '6px' }}>
              <strong className="text-xs text-real"><Icon name="image" className="icon inline-icon" />PC Defect Image Crawler API</strong>
              <span className="text-xs text-muted">Crawls images using paths from <code>image_path</code> column</span>
            </div>
            <div className="stack-sm" style={{ display: 'grid', gridTemplateColumns: '1fr 180px auto', gap: '10px', alignItems: 'center' }}>
              <input
                className="font-mono text-xs"
                aria-label="Image Crawler API URL"
                placeholder="http://10.121.45.43:336/api/image/get"
                value={pipelineForm.imageCrawlerApiUrl}
                onChange={(e) => setPipelineForm((prev) => ({ ...prev, imageCrawlerApiUrl: e.target.value }))}
              />
              <select
                className="text-xs font-semibold"
                aria-label="VP Mode"
                value={pipelineForm.imageCrawlerVpMode}
                onChange={(e) => setPipelineForm((prev) => ({ ...prev, imageCrawlerVpMode: e.target.value }))}
              >
                <option value="all">All VPs (Auto-try VP 0 & other VPs)</option>
                <option value="0">VP = 0 (Prefer Thumbnail)</option>
                <option value="auto">Auto VP (Feature/Path)</option>
                <option value="1">VP = 1</option>
                <option value="2">VP = 2</option>
                <option value="3">VP = 3</option>
                <option value="4">VP = 4</option>
              </select>
              <label className="gap-sm text-xs" style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer' }}>
                <input
                  type="checkbox"
                  checked={pipelineForm.autoCrawlImages}
                  onChange={(e) => setPipelineForm((prev) => ({ ...prev, autoCrawlImages: e.target.checked }))}
                />
                <span>Auto-crawl thumbnails on scan</span>
              </label>
            </div>
          </div>
        </section>

        {/* Defect Tables High-Density Grid */}
        <div className="mt-1" style={{ borderTop: '1px solid var(--slate-200)', paddingTop: '12px' }}>
          <div className="hstack justify-between flex-wrap gap-md" style={{ marginBottom: '8px' }}>
            <div>
              <h3 className="text-sm font-bold m-0" style={{ color: 'var(--slate-800)' }}>
                2. Defect Tables Grid Mapping ({pipelineForm.defectTables.length})
              </h3>
              <p className="muted text-xs" style={{ margin: '2px 0 0' }}>
                All defect tables follow the standard MariaDB schema (<code>fk_prod_id</code>, <code>name</code>, <code>judge</code>, <code>feature</code>, <code>reason</code>, <code>image_path</code>, etc.). Only the table name varies.
              </p>
            </div>
            <div className="hstack gap-sm flex-wrap">
              <button
                type="button"
                className="secondary compact-button"
                onClick={() => addDefectTable()}
                title="Add a new defect table with standard schema defaults"
              >
                + Add Row
              </button>
              <button
                type="button"
                className={`compact-button ${showBatchAdd ? 'primary' : 'secondary'}`}
                onClick={() => setShowBatchAdd(!showBatchAdd)}
                title="Quick paste multiple table names (mcl_defect, aoi_defect, spi_defect...)"
              >
                <Icon name="clipboard" className="icon inline-icon" />Batch Paste Tables
              </button>
              <button
                type="button"
                className="secondary compact-button"
                onClick={standardizeAllDefectTables}
                title="Reset all rows to standard schema column names"
              >
                <Icon name="operations" className="icon inline-icon" />Standardize All Schemas
              </button>
            </div>
          </div>

          {/* Batch Add Drawer / Panel */}
          {showBatchAdd && (
            <div className="batch-add-panel">
              <div className="hstack justify-between" style={{ marginBottom: '6px' }}>
                <strong className="text-sm text-strong">
                  Batch Import Defect Tables (Multi-Table Paste & Live Preview)
                </strong>
                <span className="tag-badge text-xs font-bold" style={{ background: 'var(--status-ok)', color: '#fff' }}>
                  {parsedBatchDefectTables.length} Tables Detected
                </span>
              </div>

              {/* Detailed Guidance */}
              <div
                className="text-xs text-info"
                style={{
                  background: 'var(--status-info-bg)',
                  border: '1px solid var(--status-info-border)',
                  borderRadius: '6px',
                  padding: '8px 12px',
                  marginBottom: '8px',
                  lineHeight: 1.5,
                }}
              >
                <strong><Icon name="info" className="icon inline-icon" />Multi-Table AOI Guide:</strong> Paste defect table names separated by newlines or commas.
                The pipeline will automatically bind them to the <code>Product</code> table using Foreign Key <code>{pipelineForm.fkIdColumnName || 'fk_prod_id'}</code> and standard column definitions (<code>judge</code>, <code>name</code>, <code>feature</code>, <code>image_path</code>).
              </div>

              <textarea
                className="batch-add-textarea"
                placeholder="e.g.&#10;mcl_defect&#10;aoi_defect&#10;spi_defect&#10;xray_defect"
                value={batchInput}
                onChange={(e) => setBatchInput(e.target.value)}
              />

              {/* Live Preview of Parsed Tables */}
              {parsedBatchDefectTables.length > 0 && (
                <div className="mt-2" style={{ border: '1px solid var(--slate-300)', borderRadius: '4px', overflow: 'hidden' }}>
                  <div className="text-xs font-bold" style={{ background: 'var(--slate-100)', padding: '4px 8px', color: 'var(--slate-700)' }}>
                    Live Preview: {parsedBatchDefectTables.length} Defect Tables to be Generated
                  </div>
                  <div style={{ maxHeight: '120px', overflowY: 'auto' }}>
                    <table className="analytics-data-table m-0" role="table" >
                      <thead>
                        <tr>
                          <th style={{ width: '30px' }}>#</th>
                          <th>Table Name</th>
                          <th>Display Label</th>
                          <th>FK Column</th>
                          <th>Judge Col</th>
                          <th>Defect Name Col</th>
                          <th>Feature Col</th>
                        </tr>
                      </thead>
                      <tbody>
                        {parsedBatchDefectTables.map((t, idx) => (
                          <tr key={idx}>
                            <td className="font-mono text-muted text-xs">{idx + 1}</td>
                            <td><strong className="mono-code text-info">{t.tableName}</strong></td>
                            <td className="text-xs">{t.displayName}</td>
                            <td className="font-mono text-xs text-muted">{t.fkColumnName}</td>
                            <td className="font-mono text-xs text-muted">{t.judgeColumn}</td>
                            <td className="font-mono text-xs text-muted">{t.defectNameColumn}</td>
                            <td className="font-mono text-xs text-muted">{t.featureColumn}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              <div className="batch-add-actions mt-2">
                <button
                  type="button"
                  className="primary compact-button"
                  onClick={handleBatchAdd}
                  disabled={!batchInput.trim()}
                >
                  <Icon name="operations" className="icon inline-icon" />Generate {parsedBatchDefectTables.length} Table Rows
                </button>
                <button
                  type="button"
                  className="secondary compact-button"
                  onClick={() => setBatchInput('mcl_defect\naoi_defect\nspi_defect')}
                >
                  Load Example Set
                </button>
                <button
                  type="button"
                  className="secondary compact-button"
                  onClick={() => {
                    setBatchInput('')
                    setShowBatchAdd(false)
                  }}
                >
                  Cancel
                </button>
              </div>
            </div>
          )}

          {/* High Density Editable Grid Container */}
          <div className="defect-grid-container">
            <table className="defect-grid-table">
              <thead>
                <tr>
                  <th style={{ width: '32px', textAlign: 'center' }}>#</th>
                  <th style={{ minWidth: '135px' }}>Table Name *</th>
                  <th style={{ minWidth: '120px' }}>Display Label</th>
                  <th style={{ width: '90px' }}>FK Column *</th>
                  <th style={{ width: '90px' }}>Defect Name *</th>
                  <th style={{ width: '75px' }}>Judge</th>
                  <th style={{ width: '90px' }}>Image Path</th>
                  <th style={{ width: '80px' }}>Feature</th>
                  <th style={{ width: '80px' }}>Reason</th>
                  <th style={{ width: '80px' }}>Eqp Name</th>
                  <th style={{ width: '70px' }}>Emno</th>
                  <th style={{ width: '85px' }}>Spec Name</th>
                  <th style={{ width: '65px' }}>Priority</th>
                  <th style={{ width: '80px', textAlign: 'center' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {pipelineForm.defectTables.length === 0 ? (
                  <tr>
                    <td className="text-muted" colSpan={14} style={{ textAlign: 'center', padding: '18px' }}>
                      No defect tables configured. Click <strong>+ Add Row</strong> or <strong><Icon name="clipboard" className="icon inline-icon" />Batch Paste Tables</strong> above.
                    </td>
                  </tr>
                ) : (
                  pipelineForm.defectTables.map((defectTable, index) => (
                    <Fragment key={index}>
                      <tr>
                        <td className="font-semibold text-muted" style={{ textAlign: 'center' }}>
                          {index + 1}
                        </td>
                        <td>
                          <input
                            className="grid-cell-input font-semibold text-strong"
                            value={defectTable.tableName}
                            placeholder="e.g. mcl_defect"
                            onChange={(e) => updateDefectTable(index, 'tableName', e.target.value)}
                            required
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.displayName ?? ''}
                            placeholder="MCL DEFECT"
                            onChange={(e) => updateDefectTable(index, 'displayName', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.fkColumnName}
                            placeholder="fk_prod_id"
                            onChange={(e) => updateDefectTable(index, 'fkColumnName', e.target.value)}
                            required
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.defectNameColumn}
                            placeholder="name"
                            onChange={(e) => updateDefectTable(index, 'defectNameColumn', e.target.value)}
                            required
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.judgeColumn ?? ''}
                            placeholder="judge"
                            onChange={(e) => updateDefectTable(index, 'judgeColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.imageUriColumn ?? ''}
                            placeholder="image_path"
                            onChange={(e) => updateDefectTable(index, 'imageUriColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.featureColumn ?? ''}
                            placeholder="feature"
                            onChange={(e) => updateDefectTable(index, 'featureColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.reasonColumn ?? ''}
                            placeholder="reason"
                            onChange={(e) => updateDefectTable(index, 'reasonColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.eqpNameColumn ?? ''}
                            placeholder="eqpname"
                            onChange={(e) => updateDefectTable(index, 'eqpNameColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.emnoColumn ?? ''}
                            placeholder="emno"
                            onChange={(e) => updateDefectTable(index, 'emnoColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.specNameColumn ?? ''}
                            placeholder="spec_name"
                            onChange={(e) => updateDefectTable(index, 'specNameColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <input
                            className="grid-cell-input"
                            value={defectTable.priorityColumn ?? ''}
                            placeholder="priority"
                            onChange={(e) => updateDefectTable(index, 'priorityColumn', e.target.value)}
                          />
                        </td>
                        <td>
                          <div className="grid-actions">
                            <button
                              aria-label={expandedRow === index ? 'Collapse extra settings' : 'Expand extra columns (custom_feature, spec_judge, etc.)'} type="button"
                              className={`grid-icon-btn ${expandedRow === index ? 'active' : ''}`}
                              onClick={() => setExpandedRow(expandedRow === index ? null : index)}
                              title={expandedRow === index ? 'Collapse extra settings' : 'Expand extra columns (custom_feature, spec_judge, etc.)'}
                            >
                              <Icon name="settings" className="icon inline-icon" />
                            </button>
                            <button
                              aria-label="Duplicate table row" type="button"
                              className="grid-icon-btn"
                              onClick={() => duplicateDefectTable(index)}
                              title="Duplicate table row"
                            >
                              <Icon name="clipboard" className="icon inline-icon" />
                            </button>
                            <button
                              aria-label="Delete table row" type="button"
                              className="grid-icon-btn delete"
                              onClick={() => removeDefectTable(index)}
                              title="Delete table row"
                            >
                              <Icon name="close" className="icon inline-icon" />
                            </button>
                          </div>
                        </td>
                      </tr>

                      {/* Subrow for advanced/less-frequent columns */}
                      {expandedRow === index && (
                        <tr>
                          <td colSpan={14} style={{ padding: '0 4px 6px' }}>
                            <div className="subrow-card">
                              <div className="hstack justify-between" style={{ marginBottom: '8px' }}>
                                <strong className="text-xs" style={{ color: 'var(--slate-700)', textTransform: 'uppercase' }}>
                                  Extended Schema & Direct Coordinates for [{defectTable.tableName || `#${index + 1}`}]
                                </strong>
                                <button
                                  type="button"
                                  className="secondary compact-button text-xs"
                                  style={{ padding: '2px 8px' }}
                                  onClick={() => {
                                    const std = createStandardDefectTable(defectTable.tableName, pipelineForm.fkIdColumnName || 'fk_prod_id')
                                    setPipelineForm((prev) => {
                                      const nextTables = [...prev.defectTables]
                                      nextTables[index] = { ...std, displayName: defectTable.displayName }
                                      return { ...prev, defectTables: nextTables }
                                    })
                                  }}
                                >
                                  <Icon name="operations" className="icon inline-icon" />Re-apply Standard Preset
                                </button>
                              </div>
                              <div className="subrow-grid">
                                <Field label="Custom Feature Column" hint="custom_feature BLOB">
                                  <input
                                    value={defectTable.customFeatureColumn ?? ''}
                                    placeholder="custom_feature"
                                    onChange={(e) => updateDefectTable(index, 'customFeatureColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Spec Judge Column" hint="spec_judge">
                                  <input
                                    value={defectTable.specJudgeColumn ?? ''}
                                    placeholder="spec_judge"
                                    onChange={(e) => updateDefectTable(index, 'specJudgeColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Spec Reason Column" hint="spec_reason">
                                  <input
                                    value={defectTable.specReasonColumn ?? ''}
                                    placeholder="spec_reason"
                                    onChange={(e) => updateDefectTable(index, 'specReasonColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Defect Type Column" hint="Optional category">
                                  <input
                                    value={defectTable.defectTypeColumn ?? ''}
                                    placeholder="DefectType"
                                    onChange={(e) => updateDefectTable(index, 'defectTypeColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Severity Column" hint="Optional severity">
                                  <input
                                    value={defectTable.severityColumn ?? ''}
                                    placeholder="Severity"
                                    onChange={(e) => updateDefectTable(index, 'severityColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Component Column" hint="Optional component">
                                  <input
                                    value={defectTable.componentNameColumn ?? ''}
                                    placeholder="ComponentName"
                                    onChange={(e) => updateDefectTable(index, 'componentNameColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Coord X Column (Direct)" hint="If not in feature JSON">
                                  <input
                                    value={defectTable.coordXColumn ?? ''}
                                    placeholder="CoordinateX"
                                    onChange={(e) => updateDefectTable(index, 'coordXColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Coord Y Column (Direct)" hint="If not in feature JSON">
                                  <input
                                    value={defectTable.coordYColumn ?? ''}
                                    placeholder="CoordinateY"
                                    onChange={(e) => updateDefectTable(index, 'coordYColumn', e.target.value)}
                                  />
                                </Field>
                                <Field label="Offload to SeaweedFS" hint="Store blobs in SeaweedFS">
                                  <label className="gap-sm" style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer', marginTop: '6px' }}>
                                    <input
                                      type="checkbox"
                                      checked={defectTable.offloadToStorage !== false}
                                      onChange={(e) => updateDefectTable(index, 'offloadToStorage', e.target.checked)}
                                    />
                                    <span className="text-xs">Offload JSON to SeaweedFS</span>
                                  </label>
                                </Field>
                              </div>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Defect Query Bypass & Compound Exclusion Rules */}
        <section className={`bypass-rule-card ${pipelineForm.bypassRuleEnabled ? 'enabled' : ''}`}>
          <div className="bypass-rule-header">
            <div>
              <h3>Defect Bypass Rule (Filter by Judge)</h3>
              <p>Automatically filters out rows in defect tables that represent clean/pass items (e.g. Judge = 'G') to avoid false defect counts.</p>
            </div>
            <label className="compact-switch">
              <input
                type="checkbox"
                checked={pipelineForm.bypassRuleEnabled}
                onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassRuleEnabled: e.target.checked }))}
              />
              <span>{pipelineForm.bypassRuleEnabled ? 'Enabled' : 'Disabled'}</span>
            </label>
          </div>

          {pipelineForm.bypassRuleEnabled && (
            <div className="bypass-rule-body">
              {/* Core configuration row */}
              <div className="bypass-core-row">
                <div className="bypass-core-field">
                  <label>Inspection Column</label>
                  <select
                    aria-label="Inspection Column"
                    value={pipelineForm.bypassNoDefectField}
                    onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNoDefectField: e.target.value }))}
                  >
                    <option value="Judge">Judge (default)</option>
                    <option value="SpecJudge">SpecJudge</option>
                    <option value="DefectType">DefectType</option>
                    <option value="Severity">Severity</option>
                    <option value="Reason">Reason</option>
                    <option value="SpecName">SpecName</option>
                    <option value="SpecReason">SpecReason</option>
                    <option value="DefectName">DefectName</option>
                  </select>
                </div>

                <div className="bypass-core-field">
                  <label>Operator</label>
                  <select
                    aria-label="Operator"
                    value={pipelineForm.bypassNoDefectOperator}
                    onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNoDefectOperator: e.target.value }))}
                  >
                    <option value="equals">Equals (=)</option>
                    <option value="in">In List (IN)</option>
                    <option value="not_equals">Not Equals (!=)</option>
                    <option value="not_in">Not In List (NOT IN)</option>
                    <option value="is_null">IS NULL</option>
                    <option value="is_null_or_empty">IS NULL OR EMPTY</option>
                  </select>
                </div>

                <div className="bypass-core-field flex-grow">
                  <label>Bypass Values (e.g. G or G, OK)</label>
                  <div className="hstack gap-sm">
                    <input
                      aria-label="Bypass Values"
                      type="text"
                      value={pipelineForm.bypassNoDefectValue}
                      disabled={unaryBypassOperators.has(pipelineForm.bypassNoDefectOperator.toLowerCase())}
                      placeholder="e.g. G"
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNoDefectValue: e.target.value }))}
                    />
                    <div className="bypass-quick-presets">
                      {(['G', 'OK', 'PASS', 'G, OK'] as const).map((preset) => (
                        <button
                          key={preset}
                          type="button"
                          className={`bypass-preset-chip ${pipelineForm.bypassNoDefectValue === preset ? 'active' : ''}`}
                          onClick={() => setPipelineForm((prev) => ({ ...prev, bypassNoDefectValue: preset }))}
                          title={`Set '${preset}'`}
                        >
                          {preset}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              </div>

              {/* Rule Expression Preview */}
              <div className="rule-expression">
                <span>Applied Expression</span>
                <code>
                  Bypass defect row when:{' '}
                  {formatNoDefectCondition(
                    pipelineForm.bypassNoDefectField,
                    pipelineForm.bypassNoDefectOperator,
                    pipelineForm.bypassNoDefectValue,
                  )}
                  {pipelineForm.bypassNameConditionEnabled && pipelineForm.bypassNameValuesStr.trim() ? (
                    <>
                      {' '}{pipelineForm.bypassConjunction}{' '}
                      {formatNameCondition(
                        pipelineForm.bypassNameField,
                        pipelineForm.bypassNameOperator,
                        pipelineForm.bypassNameValuesStr,
                      )}
                    </>
                  ) : null}
                </code>
              </div>

              {/* Toggle Advanced Options */}
              <div>
                <button
                  type="button"
                  className="bypass-toggle-advanced-btn"
                  onClick={() => setShowAdvancedBypass((v) => !v)}
                  aria-expanded={showAdvancedBypass}
                >
                  <Icon name={showAdvancedBypass ? 'sort-asc' : 'sort-desc'} className="icon inline-icon" />
                  {showAdvancedBypass ? 'Collapse Advanced Options' : 'Advanced Options (Name filter, AND/OR, NULL policy)'}
                </button>
              </div>

              {/* Advanced Options Panel */}
              {showAdvancedBypass && (
                <div className="bypass-advanced-panel">
                  <div className="hstack" style={{ gap: '10px', marginBottom: '8px' }}>
                    <label className="check-row m-0 font-semibold text-xs">
                      <input
                        type="checkbox"
                        checked={pipelineForm.bypassNameConditionEnabled}
                        onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNameConditionEnabled: e.target.checked }))}
                      />
                      Compound Defect Name Filter
                    </label>

                    {pipelineForm.bypassNameConditionEnabled && (
                      <div className="rule-conjunction" role="group" aria-label="Conjunction Operator" style={{ marginLeft: 'auto' }}>
                        <span>Conjunction:</span>
                        <button
                          type="button"
                          className={pipelineForm.bypassConjunction === 'AND' ? 'active' : ''}
                          onClick={() => setPipelineForm((prev) => ({ ...prev, bypassConjunction: 'AND' }))}
                        >
                          AND
                        </button>
                        <button
                          type="button"
                          className={pipelineForm.bypassConjunction === 'OR' ? 'active warning' : ''}
                          onClick={() => setPipelineForm((prev) => ({ ...prev, bypassConjunction: 'OR' }))}
                        >
                          OR
                        </button>
                      </div>
                    )}
                  </div>

                  {pipelineForm.bypassNameConditionEnabled && (
                    <div className="bypass-core-row" style={{ marginTop: '6px' }}>
                      <div className="bypass-core-field">
                        <label>Name Column</label>
                        <select
                          value={pipelineForm.bypassNameField}
                          onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNameField: e.target.value }))}
                        >
                          <option value="DefectName">DefectName</option>
                          <option value="Name">Name</option>
                          <option value="DefectType">DefectType</option>
                          <option value="SpecName">SpecName</option>
                          <option value="SpecJudge">SpecJudge</option>
                          <option value="Severity">Severity</option>
                          <option value="Reason">Reason</option>
                        </select>
                      </div>

                      <div className="bypass-core-field">
                        <label>Operator</label>
                        <select
                          value={pipelineForm.bypassNameOperator}
                          onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNameOperator: e.target.value }))}
                        >
                          <option value="NOT IN">NOT IN (exclude)</option>
                          <option value="IN">IN</option>
                          <option value="NOT EQUALS">NOT EQUALS (!=)</option>
                          <option value="EQUALS">EQUALS (=)</option>
                        </select>
                      </div>

                      <div className="bypass-core-field flex-grow">
                        <label>Defect Names (comma-separated)</label>
                        <input
                          type="text"
                          value={pipelineForm.bypassNameValuesStr}
                          placeholder="e.g. FD, filter"
                          onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassNameValuesStr: e.target.value }))}
                        />
                      </div>
                    </div>
                  )}

                  <div className="rule-options-bar" style={{ marginTop: '10px', paddingTop: '8px', borderTop: '1px solid var(--slate-200)' }}>
                    <label>
                      <span>NULL Value Policy:</span>
                      <select
                        value={pipelineForm.bypassNullPolicy}
                        onChange={(e) => setPipelineForm((prev) => ({
                          ...prev,
                          bypassNullPolicy: e.target.value as 'PreserveRow' | 'MatchAsNoDefect' | 'Strict',
                        }))}
                      >
                        <option value="PreserveRow">PreserveRow (Safe: NULL is retained)</option>
                        <option value="MatchAsNoDefect">MatchAsNoDefect (NULL treated as clean)</option>
                        <option value="Strict">Strict (SQL Standard)</option>
                      </select>
                    </label>
                    <label className="case-toggle">
                      <input
                        type="checkbox"
                        checked={pipelineForm.bypassCaseInsensitive}
                        onChange={(e) => setPipelineForm((prev) => ({ ...prev, bypassCaseInsensitive: e.target.checked }))}
                      />
                      Case-insensitive matching
                    </label>
                  </div>
                </div>
              )}
            </div>
          )}
        </section>

        {/* Test Mapping Button & Live Preview Box */}
        <div style={{ marginTop: '14px', borderTop: '1px solid var(--slate-200)', paddingTop: '14px' }}>
          <div className="hstack gap-md">
            <Button type="button" onClick={() => void handleTestMapping()} variant="secondary" loading={testing} disabled={busy}>
              {testing ? 'Connecting & Testing...' : 'Test Mapping & Preview Data'}
            </Button>
            <span className="field-hint">
              Executes read-only sample query (LIMIT 5) against MariaDB without saving.
            </span>
          </div>

          {testResult && (
            <div className="test-preview-box">
              <div className="test-preview-header">
                <strong>
                  {testResult.success ? (
                    <span className="text-ok">
                      <Icon name="check" className="icon inline-icon" />Mapping Test Succeeded · Latency: {testResult.latencyMs}ms · {testResult.sampleProductsCount} sample product(s) found
                    </span>
                  ) : (
                    <span className="text-ng"><Icon name="warning" className="icon inline-icon" />Test Failed · {testResult.errorMessage}</span>
                  )}
                </strong>
              </div>

              <div className="gap-md" style={{ display: 'flex', flexWrap: 'wrap', marginBottom: '8px' }}>
                {testResult.defectCountsByTable &&
                  Object.entries(testResult.defectCountsByTable).map(([tbl, count]) => (
                    <span key={tbl} className="tag-badge text-ok font-bold" style={{ background: 'var(--status-ok-bg)', borderColor: 'var(--status-ok-border)' }}>
                      <Icon name="clipboard" className="icon inline-icon" />Table {tbl}: {count.toLocaleString()} defect(s)
                    </span>
                  ))}
                {pipelineForm.bypassRuleEnabled ? (
                  <span className="tag-badge text-overkill" style={{ background: 'var(--status-overkill-bg)', borderColor: 'var(--status-overkill-border)' }}>
                    <Icon name="overkill" className="icon inline-icon" />Bypassed: {testResult.bypassedDefectsCount ?? 0} | Scanned: {testResult.scannedDefectsCount ?? ((testResult.acceptedDefectsCount ?? testResult.sampleDefects.length) + (testResult.bypassedDefectsCount ?? 0))} | Accepted: {testResult.acceptedDefectsCount ?? testResult.sampleDefects.length}
                  </span>
                ) : (
                  (testResult.bypassedDefectsCount ?? 0) > 0 && (
                    <span className="tag-badge text-overkill" style={{ background: 'var(--status-overkill-bg)', borderColor: 'var(--status-overkill-border)' }}>
                      <Icon name="overkill" className="icon inline-icon" />{testResult.bypassedDefectsCount} defect(s) bypassed (legacy)
                    </span>
                  )
                )}
                {testResult.bypassRuleSummary && (
                  <span className="tag-badge text-soft" style={{ background: 'var(--slate-100)' }} title={testResult.bypassRuleSummary}>
                    Rule: {testResult.bypassRuleSummary}
                  </span>
                )}
                {testResult.alreadySavedProductsCount !== undefined && testResult.alreadySavedProductsCount > 0 && (
                  <span className="tag-badge font-bold" style={{ background: 'var(--slate-50)', color: 'var(--slate-800)', borderColor: 'var(--slate-300)' }} title="Products already persisted in database are automatically locked/skipped to prevent duplicates">
                    <Icon name="lock" className="icon inline-icon" />{testResult.alreadySavedProductsCount}/{testResult.sampleProductsCount} already in DB (automatically locked/skipped on real runs)
                  </span>
                )}
              </div>

              {testResult.generatedDefectSql && (
                <div style={{ marginBottom: '10px' }}>
                  <span className="text-xs font-bold" style={{ color: 'var(--slate-700)' }}>
                    <Icon name="operations" className="icon inline-icon" />Generated SQL Query ({Object.keys(testResult.defectCountsByTable || {}).length} defect table(s) - pushdown bypass active):
                  </span>
                  <pre className="text-info text-xs font-mono" style={{
                    background: 'var(--slate-50)',
                    padding: '8px 12px',
                    borderRadius: '6px',
                    overflowX: 'auto',
                    margin: '4px 0 0',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-all',
                  }}>
                    {testResult.generatedDefectSql}
                  </pre>
                </div>
              )}

              {testResult.sampleProducts && testResult.sampleProducts.length > 0 && (() => {
                const allKeys = new Set<string>()
                const priority = [
                  'Lock Status',
                  pipelineForm.idColumnName || 'ID',
                  pipelineForm.judgeColumnName || 'Judge',
                  'Zone',
                  'Defect Name',
                  'DateTime Inspection',
                ]
                testResult.sampleProducts.forEach((row) => {
                  Object.keys(row).forEach((k) => allKeys.add(k))
                })
                allKeys.add('Zone')
                allKeys.add('Defect Name')
                allKeys.add('DateTime Inspection')

                const previewCols: string[] = []
                for (const p of priority) {
                  if (allKeys.has(p) && !previewCols.includes(p)) {
                    previewCols.push(p)
                    allKeys.delete(p)
                  }
                }
                previewCols.push(...Array.from(allKeys))

                return (
                  <div className="mini-preview-table-wrap">
                    <table className="mini-preview-table">
                      <thead>
                        <tr>
                          {previewCols.map((k) => (
                            <th key={k}>{k}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {testResult.sampleProducts.map((row, rIdx) => (
                          <tr key={rIdx}>
                            {previewCols.map((col, cIdx) => {
                              const val = row[col]
                              return (
                                <td key={cIdx}>
                                  {val === null || val === undefined || val === '' ? '—' : String(val)}
                                </td>
                              )
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )
              })()}

              {testResult.sampleDefects && testResult.sampleDefects.length > 0 && (() => {
                const allDefectCols = new Set<string>()
                testResult.sampleDefects.forEach((row) => {
                  Object.keys(row).forEach((k) => allDefectCols.add(k))
                })
                const defectCols = Array.from(allDefectCols)

                return (
                  <div className="mt-3">
                    <div className="text-xs font-bold" style={{ color: 'var(--slate-700)', marginBottom: '4px' }}>
                      <Icon name="search" className="icon inline-icon" />Sample Defect Rows (Displaying {testResult.sampleDefects.length} / total {testResult.acceptedDefectsCount ?? testResult.sampleDefects.length} defect(s) found, query limit 2000):
                    </div>
                    <div className="mini-preview-table-wrap">
                      <table className="mini-preview-table">
                        <thead>
                          <tr>
                            {defectCols.map((k) => (
                              <th key={k}>{k}</th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {testResult.sampleDefects.map((row, rIdx) => (
                            <tr key={rIdx}>
                              {defectCols.map((col, cIdx) => {
                                const val = row[col]
                                return (
                                  <td key={cIdx}>
                                    {val === null || val === undefined || val === '' ? '—' : String(val)}
                                  </td>
                                )
                              })}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )
              })()}
            </div>
          )}
        </div>

        <div className="actions mt-4">
          <button type="submit" disabled={busy}>
            {pipelineForm.id ? 'Save Changes' : 'Create Pipeline'}
          </button>
          <button
            type="button"
            className="ghost-button"
            onClick={onClose}
          >
            Cancel
          </button>
        </div>
      </form>
    </Modal>
  )
}
