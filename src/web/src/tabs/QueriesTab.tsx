import { Fragment, useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import {
  connectionsApi,
  deleteOverkillPipelineConfig,
  getOverkillPipelineConfigs,
  getOverkillPipelineExecutions,
  queryProfilesApi,
  runOverkillPipelineConfig,
  saveOverkillPipelineConfig,
  testOverkillPipelineMapping,
  toggleOverkillPipelineConfig,
} from '../api'
import { formatDate, formNumber, formString, paginate } from '../format'
import { BatchPipelinesModal } from '../components/BatchPipelinesModal'
import { DataTable, Field, Modal, Pagination, StatusPill } from '../ui'
import type {
  CreateQueryProfileRequest,
  DatabaseConnection,
  DefectBypassRule,
  DefectTableMapping,
  Notice,
  OverkillPipelineConfig,
  OverkillPipelineExecution,
  QueryProfile,
  QueryTestResponse,
  RenderedQuery,
  SaveOverkillPipelineConfigRequest,
  TestPipelineMappingRequest,
  TestPipelineMappingResponse,
} from '../types'

function schemaKeys(json: string) {
  try {
    return Object.keys(JSON.parse(json) as Record<string, unknown>)
  } catch {
    return []
  }
}

type SubView = 'profiles' | 'pipelines'

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

export function QueriesTab({ onNotice }: { onNotice: (notice: Notice) => void }) {
  const [subView, setSubView] = useState<SubView>('pipelines')

  // Standard Profiles state
  const [profiles, setProfiles] = useState<QueryProfile[]>([])
  const [connections, setConnections] = useState<DatabaseConnection[]>([])
  const [selectedConnectionId, setSelectedConnectionId] = useState('')
  const [profileOpen, setProfileOpen] = useState(false)
  const [editing, setEditing] = useState<QueryProfile | null>(null)
  const [runOpen, setRunOpen] = useState<QueryProfile | null>(null)
  const [rendered, setRendered] = useState<RenderedQuery | null>(null)
  const [tested, setTested] = useState<QueryTestResponse | null>(null)
  const [busy, setBusy] = useState(false)
  const [draftConnectionId, setDraftConnectionId] = useState('')
  const [profilePage, setProfilePage] = useState(1)
  const [filterMachine, setFilterMachine] = useState('All')
  const [filterLine, setFilterLine] = useState('All')
  const [filterCategory, setFilterCategory] = useState('All')

  // Overkill Automated Pipelines state
  const [pipelines, setPipelines] = useState<OverkillPipelineConfig[]>([])
  const [pipelineModalOpen, setPipelineModalOpen] = useState(false)
  const [pipelineForm, setPipelineForm] = useState<PipelineFormState>({
    name: '',
    databaseConnectionId: '',
    machineCode: 'MACHINE-A',
    lineCode: 'LINE-1',
    modelCode: 'UNASSIGNED',
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
    imageCrawlerApiUrl: '',
    autoCrawlImages: true,
    imageCrawlerVpMode: '0',
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
  })
  const [showAdvancedBypass, setShowAdvancedBypass] = useState(false)
  const [showBatchAdd, setShowBatchAdd] = useState(false)
  const [batchInput, setBatchInput] = useState('')

  const parsedBatchDefectTables = useMemo(() => {
    if (!batchInput.trim()) return []
    const raw = batchInput.split(/[\n,\t]+/).map((s) => s.trim()).filter(Boolean)
    const unique = [...new Set(raw)]
    return unique.map((n) => createStandardDefectTable(n, pipelineForm.fkIdColumnName || 'fk_prod_id'))
  }, [batchInput, pipelineForm.fkIdColumnName])
  const [expandedRow, setExpandedRow] = useState<number | null>(null)
  const [pipelinePage, setPipelinePage] = useState(1)
  const [executions, setExecutions] = useState<OverkillPipelineExecution[]>([])
  const [executionFilterConfigId, setExecutionFilterConfigId] = useState<string>('All')
  const [executionPage, setExecutionPage] = useState(1)
  const [testResult, setTestResult] = useState<TestPipelineMappingResponse | null>(null)
  const [testing, setTesting] = useState(false)
  const [runningPipelineId, setRunningPipelineId] = useState<string | null>(null)
  const [batchProvisionModalOpen, setBatchProvisionModalOpen] = useState(false)

  const mariaDbConnections = useMemo(() => {
    return connections.filter((c) => !c.engine || c.engine.toLowerCase().includes('maria'))
  }, [connections])

  const machines = useMemo(() => {
    const list = new Set<string>()
    connections.forEach((c) => {
      if (c.machineCode) list.add(c.machineCode)
    })
    profiles.forEach((p) => {
      if (p.machineCode) list.add(p.machineCode)
    })
    return [...list].sort()
  }, [connections, profiles])

  const lines = useMemo(() => {
    const list = new Set<string>()
    connections
      .filter((c) => filterMachine === 'All' || c.machineCode === filterMachine)
      .forEach((c) => {
        if (c.lineCode) list.add(c.lineCode)
      })
    profiles
      .filter((p) => filterMachine === 'All' || p.machineCode === filterMachine)
      .forEach((p) => {
        if (p.lineCode) list.add(p.lineCode)
      })
    return [...list].sort()
  }, [connections, profiles, filterMachine])

  const compatibleConnections = useMemo(() => {
    return connections.filter((c) => {
      if (filterMachine !== 'All' && c.machineCode !== filterMachine) return false
      if (filterLine !== 'All' && c.lineCode !== filterLine) return false
      return true
    })
  }, [connections, filterMachine, filterLine])

  const selectedConnection = useMemo(
    () => connections.find((connection) => connection.id === selectedConnectionId) ?? null,
    [connections, selectedConnectionId],
  )
  const draftConnection = useMemo(
    () => connections.find((connection) => connection.id === draftConnectionId) ?? selectedConnection,
    [connections, draftConnectionId, selectedConnection],
  )
  const selectedPipelineConnection = useMemo(
    () => connections.find((c) => c.id === pipelineForm.databaseConnectionId) ?? null,
    [connections, pipelineForm.databaseConnectionId],
  )
  const runKeys = useMemo(() => (runOpen ? schemaKeys(runOpen.parameterSchemaJson) : []), [runOpen])

  const filteredProfiles = useMemo(() => {
    return profiles.filter((profile) => {
      if (filterCategory !== 'All' && profile.verificationCategory !== filterCategory) return false
      if (filterMachine !== 'All' && profile.machineCode !== filterMachine) return false
      if (filterLine !== 'All' && profile.lineCode !== filterLine) return false
      if (selectedConnectionId && profile.dataSourceId !== selectedConnectionId) return false
      return true
    })
  }, [profiles, filterCategory, filterMachine, filterLine, selectedConnectionId])

  const pagedProfiles = paginate(filteredProfiles, profilePage)
  const pagedPipelines = paginate(pipelines, pipelinePage, 8)

  const filteredExecutions = useMemo(() => {
    if (executionFilterConfigId === 'All') return executions
    return executions.filter((e) => e.configId === executionFilterConfigId)
  }, [executions, executionFilterConfigId])

  const pagedExecutions = paginate(filteredExecutions, executionPage, 8)

  const loadProfilesAndConnections = useCallback(async () => {
    try {
      const [nextProfiles, connectionPage] = await Promise.all([
        queryProfilesApi.list(),
        connectionsApi.list({ engine: 'MariaDB', page: 1, pageSize: 100 }),
      ])
      setProfiles(nextProfiles)
      setConnections(connectionPage.items)
      setSelectedConnectionId((current) => current || connectionPage.items[0]?.id || '')
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load MariaDB queries.' })
    }
  }, [onNotice])

  const loadPipelines = useCallback(async () => {
    try {
      const data = await getOverkillPipelineConfigs()
      setPipelines(data)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load Overkill pipelines.' })
    }
  }, [onNotice])

  const loadExecutions = useCallback(async () => {
    try {
      const data = await getOverkillPipelineExecutions()
      setExecutions(data)
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to load pipeline executions.' })
    }
  }, [onNotice])

  useEffect(() => {
    void loadProfilesAndConnections()
    void loadPipelines()
    void loadExecutions()
  }, [loadProfilesAndConnections, loadPipelines, loadExecutions])

  // Profile Form Handling
  function profilePayload(event: FormEvent<HTMLFormElement>): CreateQueryProfileRequest {
    const connectionId = formString(event, 'dataSourceId') || selectedConnectionId
    const connection = connections.find((item) => item.id === connectionId)
    return {
      name: formString(event, 'name'),
      dataSourceId: connectionId || null,
      machineCode: connection?.machineCode ?? formString(event, 'machineCode'),
      lineCode: connection?.lineCode ?? formString(event, 'lineCode'),
      concept: formString(event, 'concept') || 'PRODUCT-STATUS',
      sourceSystem: 'MARIADB',
      sqlTemplate: formString(event, 'sqlTemplate'),
      parameterSchemaJson: formString(event, 'parameterSchemaJson') || '{"keyId":"string","lineCode":"string"}',
      timeoutSeconds: formNumber(event, 'timeoutSeconds', 30),
      maxConcurrency: formNumber(event, 'maxConcurrency', 2),
      notes: formString(event, 'notes') || null,
      verificationCategory: formString(event, 'verificationCategory') || 'Overkill',
    }
  }

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    try {
      const body = profilePayload(event)
      if (!body.dataSourceId) throw new Error('Select a MariaDB connection first.')
      if (editing) await queryProfilesApi.update(editing.id, body)
      else await queryProfilesApi.create(body)
      setProfileOpen(false)
      setEditing(null)
      onNotice({
        tone: 'success',
        message: editing ? 'Query updated and bound to connection.' : 'Query saved to MariaDB connection.',
      })
      await loadProfilesAndConnections()
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Save query failed.' })
    } finally {
      setBusy(false)
    }
  }

  function collectParameters(event: FormEvent<HTMLFormElement>) {
    const data = new FormData(event.currentTarget)
    const parameters: Record<string, string> = {}
    for (const key of runKeys) parameters[key] = String(data.get(key) ?? '')
    return parameters
  }

  async function renderProfile(event: FormEvent<HTMLFormElement>) {
    if (!runOpen) return
    setBusy(true)
    try {
      setRendered(await queryProfilesApi.render(runOpen.id, collectParameters(event)))
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Render failed.' })
    } finally {
      setBusy(false)
    }
  }

  async function testProfile(event: FormEvent<HTMLFormElement>) {
    if (!runOpen) return
    setBusy(true)
    try {
      const result = await queryProfilesApi.test(runOpen.id, collectParameters(event))
      setTested(result)
      onNotice({ tone: result.success ? 'success' : 'error', message: result.message })
    } catch (error) {
      onNotice({ tone: 'error', message: error instanceof Error ? error.message : 'Test failed.' })
    } finally {
      setBusy(false)
    }
  }

  // Pipeline Handlers
  function openCreatePipeline() {
    const defaultConn = mariaDbConnections[0] || connections[0]
    setPipelineForm({
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
    })
    setShowAdvancedBypass(false)
    setTestResult(null)
    setExpandedRow(null)
    setShowBatchAdd(false)
    setBatchInput('')
    setPipelineModalOpen(true)
  }

  function openEditPipeline(p: OverkillPipelineConfig) {
    const schedOption =
      p.scheduleIntervalMinutes === 60
        ? '60'
        : p.scheduleIntervalMinutes === 120
          ? '120'
          : p.scheduleIntervalMinutes === 180
            ? '180'
            : 'custom'

    const conn = connections.find((c) => c.id === p.databaseConnectionId)
    setPipelineForm({
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
    })
    setShowAdvancedBypass(Boolean((p.bypassRule ?? p.defectBypassRule)?.nameCondition && ((p.bypassRule ?? p.defectBypassRule)?.nameCondition?.values?.length ?? 0) > 0))
    setTestResult(null)
    setExpandedRow(null)
    setShowBatchAdd(false)
    setBatchInput('')
    setPipelineModalOpen(true)
  }

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
      setPipelineModalOpen(false)
      await loadPipelines()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to save pipeline.' })
    } finally {
      setBusy(false)
    }
  }

  async function handleTogglePipeline(id: string) {
    try {
      const updated = await toggleOverkillPipelineConfig(id)
      onNotice({
        tone: 'info',
        message: `Pipeline '${updated.name}' is now ${updated.enabled ? 'Enabled' : 'Disabled'}.`,
      })
      await loadPipelines()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to toggle pipeline.' })
    }
  }

  async function handleRunPipeline(id: string) {
    setRunningPipelineId(id)
    try {
      const result = await runOverkillPipelineConfig(id)
      onNotice({
        tone: result.status === 'Success' ? 'success' : 'error',
        message: `${result.message} (Scanned: ${result.productsScanned}, Defects: ${result.defectsExtracted}, Overkill Suspects: ${result.overkillCandidatesCount})`,
      })
      await Promise.all([loadPipelines(), loadExecutions()])
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Execution failed.' })
    } finally {
      setRunningPipelineId(null)
    }
  }

  async function handleDeletePipeline(id: string, name: string) {
    if (!window.confirm(`Are you sure you want to delete pipeline '${name}'?`)) return
    try {
      await deleteOverkillPipelineConfig(id)
      onNotice({ tone: 'info', message: `Pipeline '${name}' deleted.` })
      await Promise.all([loadPipelines(), loadExecutions()])
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to delete pipeline.' })
    }
  }

  return (
    <div className="tab-stack">
      {/* View Switcher Bar */}
      <div style={{ display: 'flex', gap: '8px', alignItems: 'center', marginBottom: '4px' }}>
        <button
          type="button"
          className={`filter-chip ${subView === 'pipelines' ? 'active' : ''}`}
          onClick={() => setSubView('pipelines')}
        >
          ⚡ Overkill Automated Pipelines ({pipelines.length})
        </button>
        <button
          type="button"
          className={`filter-chip ${subView === 'profiles' ? 'active' : ''}`}
          onClick={() => setSubView('profiles')}
        >
          Standard SQL Query Profiles ({profiles.length})
        </button>
      </div>

      {subView === 'pipelines' ? (
        <>
          {/* Overkill Pipelines Section */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">GET /api/v1/overkill-pipeline/configs</p>
                <h2>Overkill Automated Pipelines (MariaDB)</h2>
              </div>
              <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                <button
                  type="button"
                  className="primary"
                  style={{ background: '#2563eb', borderColor: '#3b82f6', fontWeight: 600 }}
                  onClick={() => setBatchProvisionModalOpen(true)}
                  title="Batch configure Overkill pipelines and multi-defect tables for lines"
                >
                  🚀 Batch Setup Defect Pipelines
                </button>
                <button type="button" onClick={openCreatePipeline}>
                  + New Overkill Pipeline
                </button>
              </div>
            </div>
            <p className="muted" style={{ marginBottom: '14px' }}>
              Automated pipelines periodically query MariaDB product tables for NG/N judges, extract corresponding defect records
              from multiple linked inspection tables, and identify Overkill suspects.
            </p>

            <DataTable
              eyebrow="SCHEDULED PIPELINES"
              title="Configured Overkill Pipelines"
              columns={['Pipeline Name', 'Connection', 'Machine / Line', 'Schedule Interval', 'Status', 'Last Run / Scan', 'Actions']}
              rows={pagedPipelines.items.map((p) => {
                const intervalHours = (p.scheduleIntervalMinutes / 60).toFixed(1).replace('.0', '')
                const isRunning = runningPipelineId === p.id || p.lastRunStatus === 'Running'

                return {
                  id: p.id,
                  cells: [
                    <div key="name">
                      <strong>{p.name}</strong>
                      <div className="field-hint">
                        Table: <code>{p.productTableName}</code> · Zone: <code>{p.zoneColumnName || 'zone'}</code> · Defect: <code>{p.defectNameColumnName || 'defect_name'}</code> · Defect tables: {p.defectTables?.length ?? 0}
                      </div>
                      <div className="field-hint" style={{ marginTop: '2px' }}>
                        PC Image API: <code style={{ color: '#9333ea' }}>{p.imageCrawlerApiUrl || 'Default PC API'}</code> {p.autoCrawlImages ? '· ⚡ Auto-crawl' : ''}
                      </div>
                    </div>,
                    <div key="conn" style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                      <span>{p.connectionName}</span>
                      {p.offloadDefectsToSeaweedFs && (
                        <span className="tag-badge" style={{ background: '#e0f2fe', color: '#0369a1', borderColor: '#bae6fd', fontSize: '10px', width: 'fit-content' }}>
                          🌊 SeaweedFS
                        </span>
                      )}
                    </div>,
                    <span key="ml" className="line-pill">
                      {p.machineCode}/{p.lineCode}
                    </span>,
                    <span key="sched" className="tag-badge">
                      Every {p.scheduleIntervalMinutes}m ({intervalHours}h)
                    </span>,
                    <div key="status" className="status-badges">
                      <StatusPill value={p.enabled ? (isRunning ? 'Running' : p.lastRunStatus || 'Idle') : 'Disabled'} />
                    </div>,
                    <div key="run">
                      <div>{p.lastRunAtUtc ? formatDate(p.lastRunAtUtc) : 'Never'}</div>
                      <small className="field-hint">
                        {p.lastRunProductsCount} prods · {p.lastRunDefectsCount} defects
                      </small>
                    </div>,
                    <div key="actions" className="row-actions">
                      <button
                        type="button"
                        className="table-button"
                        disabled={isRunning}
                        onClick={() => void handleRunPipeline(p.id)}
                        title="Run this pipeline immediately"
                      >
                        {isRunning ? 'Running...' : 'Run Now'}
                      </button>
                      <button
                        type="button"
                        className="table-button"
                        onClick={() => void handleTogglePipeline(p.id)}
                      >
                        {p.enabled ? 'Pause' : 'Enable'}
                      </button>
                      <button
                        type="button"
                        className="table-button"
                        onClick={() => openEditPipeline(p)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="table-button"
                        style={{ color: '#ef4444' }}
                        onClick={() => void handleDeletePipeline(p.id, p.name)}
                      >
                        Delete
                      </button>
                    </div>,
                  ],
                }
              })}
            />
            <Pagination
              page={pagedPipelines.page}
              totalPages={pagedPipelines.totalPages}
              totalCount={pagedPipelines.totalCount}
              onPage={setPipelinePage}
            />
          </section>

          {/* Execution History Section */}
          <section className="panel" style={{ marginTop: '14px' }}>
            <div className="section-heading">
              <div>
                <p className="eyebrow">GET /api/v1/overkill-pipeline/executions</p>
                <h2>Pipeline Execution History</h2>
              </div>
              <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                <select
                  value={executionFilterConfigId}
                  onChange={(e) => {
                    setExecutionFilterConfigId(e.target.value)
                    setExecutionPage(1)
                  }}
                  style={{ width: 'auto', minWidth: '200px' }}
                >
                  <option value="All">All Pipelines</option>
                  {pipelines.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
                <button
                  type="button"
                  className="secondary"
                  onClick={() => void loadExecutions()}
                >
                  Refresh Logs
                </button>
              </div>
            </div>

            <DataTable
              eyebrow="AUDIT TRAIL"
              title="Recent Automated & Manual Ingestions"
              columns={['Pipeline', 'Trigger', 'Started', 'Duration', 'Status', 'Products', 'Defects', 'Overkill Suspects', 'Message']}
              rows={pagedExecutions.items.map((exec) => ({
                id: exec.id,
                cells: [
                  <strong key="p">{exec.pipelineName}</strong>,
                  <span key="trig" className="clean-pill">{exec.triggerType}</span>,
                  <span key="st">{formatDate(exec.startedAtUtc)}</span>,
                  <span key="lat">{exec.latencyMs}ms</span>,
                  <StatusPill key="stat" value={exec.status} />,
                  <strong key="prod">{exec.productsScanned}</strong>,
                  <span key="def">{exec.defectsExtracted}</span>,
                  <span key="ok" className={exec.overkillCandidatesCount > 0 ? 'warning-stat-badge' : 'clean-pill'}>
                    {exec.overkillCandidatesCount}
                  </span>,
                  <small key="msg" className="field-hint" style={{ maxWidth: '240px', overflow: 'hidden', textOverflow: 'ellipsis', display: 'block' }}>
                    {exec.errorMessage || 'Completed successfully'}
                  </small>,
                ],
              }))}
            />
            <Pagination
              page={pagedExecutions.page}
              totalPages={pagedExecutions.totalPages}
              totalCount={pagedExecutions.totalCount}
              onPage={setExecutionPage}
            />
          </section>

          {/* Create / Edit Pipeline Modal */}
          <Modal
            title={pipelineForm.id ? `Edit Pipeline · ${pipelineForm.name}` : 'New Overkill MariaDB Pipeline'}
            description={pipelineForm.id ? 'PUT /api/v1/overkill-pipeline/configs/{id}' : 'POST /api/v1/overkill-pipeline/configs'}
            open={pipelineModalOpen}
            onClose={() => setPipelineModalOpen(false)}
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
                  <label className="check-row" style={{ marginTop: '8px' }}>
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

                <label className="storage-toggle-row" style={{ marginTop: '8px', border: '1px solid rgba(59, 130, 246, 0.3)', background: 'rgba(59, 130, 246, 0.08)' }}>
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
                    <strong style={{ color: '#93c5fd' }}>Include OK Product IDs in inspection window (Throughput Tracking)</strong>
                    <small>Collects both OK and NG units to benchmark throughput, station NG rates, and overkill across lines.</small>
                  </span>
                </label>

                {/* PC Image Crawler Configuration */}
                <div style={{ marginTop: '10px', padding: '10px 12px', background: 'rgba(168, 85, 247, 0.06)', border: '1px solid rgba(168, 85, 247, 0.3)', borderRadius: '6px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                    <strong style={{ fontSize: '12px', color: '#7e22ce' }}>🖼️ PC Defect Image Crawler API</strong>
                    <span style={{ fontSize: '11px', color: '#6b7280' }}>Crawls images using paths from <code>image_path</code> column</span>
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 180px auto', gap: '10px', alignItems: 'center' }}>
                    <input
                      aria-label="Image Crawler API URL"
                      placeholder="http://10.121.45.43:336/api/image/get"
                      value={pipelineForm.imageCrawlerApiUrl}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, imageCrawlerApiUrl: e.target.value }))}
                      style={{ fontFamily: 'monospace', fontSize: '12px' }}
                    />
                    <select
                      aria-label="VP Mode"
                      value={pipelineForm.imageCrawlerVpMode}
                      onChange={(e) => setPipelineForm((prev) => ({ ...prev, imageCrawlerVpMode: e.target.value }))}
                      style={{ fontSize: '12px', fontWeight: 600 }}
                    >
                      <option value="all">⚡ All VPs (Auto-try VP 0 & other VPs)</option>
                      <option value="0">VP = 0 (Prefer Thumbnail)</option>
                      <option value="auto">Auto VP (Feature/Path)</option>
                      <option value="1">VP = 1</option>
                      <option value="2">VP = 2</option>
                      <option value="3">VP = 3</option>
                      <option value="4">VP = 4</option>
                    </select>
                    <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', cursor: 'pointer', fontSize: '12px' }}>
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
              <div style={{ borderTop: '1px solid #e2e8f0', paddingTop: '12px', marginTop: '4px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px', flexWrap: 'wrap', gap: '8px' }}>
                  <div>
                    <h3 style={{ fontSize: '13px', fontWeight: 700, margin: 0, color: '#1e293b' }}>
                      2. Defect Tables Grid Mapping ({pipelineForm.defectTables.length})
                    </h3>
                    <p className="muted" style={{ margin: '2px 0 0', fontSize: '11px' }}>
                      All defect tables follow the standard MariaDB schema (<code>fk_prod_id</code>, <code>name</code>, <code>judge</code>, <code>feature</code>, <code>reason</code>, <code>image_path</code>, etc.). Only the table name varies.
                    </p>
                  </div>
                  <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' }}>
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
                      📋 Batch Paste Tables
                    </button>
                    <button
                      type="button"
                      className="secondary compact-button"
                      onClick={standardizeAllDefectTables}
                      title="Reset all rows to standard schema column names"
                    >
                      ⚡ Standardize All Schemas
                    </button>
                  </div>
                </div>

                {/* Batch Add Drawer / Panel */}
                {showBatchAdd && (
                  <div className="batch-add-panel">
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                      <strong style={{ fontSize: '13px', color: '#0f172a' }}>
                        Batch Import Defect Tables (Multi-Table Paste & Live Preview)
                      </strong>
                      <span className="tag-badge" style={{ background: '#059669', color: '#fff', fontSize: '11px', fontWeight: 700 }}>
                        {parsedBatchDefectTables.length} Tables Detected
                      </span>
                    </div>

                    {/* Detailed Guidance */}
                    <div
                      style={{
                        background: 'rgba(59, 130, 246, 0.08)',
                        border: '1px solid rgba(59, 130, 246, 0.25)',
                        borderRadius: '6px',
                        padding: '8px 12px',
                        fontSize: '11px',
                        color: '#1e40af',
                        marginBottom: '8px',
                        lineHeight: 1.5,
                      }}
                    >
                      <strong>💡 Multi-Table AOI Guide:</strong> Paste defect table names separated by newlines or commas.
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
                      <div style={{ marginTop: '8px', border: '1px solid #cbd5e1', borderRadius: '4px', overflow: 'hidden' }}>
                        <div style={{ background: '#f1f5f9', padding: '4px 8px', fontSize: '11px', fontWeight: 700, color: '#334155' }}>
                          Live Preview: {parsedBatchDefectTables.length} Defect Tables to be Generated
                        </div>
                        <div style={{ maxHeight: '120px', overflowY: 'auto' }}>
                          <table className="analytics-data-table" role="table" style={{ margin: 0 }}>
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
                                  <td><strong className="mono-code" style={{ color: '#0284c7' }}>{t.tableName}</strong></td>
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

                    <div className="batch-add-actions" style={{ marginTop: '8px' }}>
                      <button
                        type="button"
                        className="primary compact-button"
                        onClick={handleBatchAdd}
                        disabled={!batchInput.trim()}
                      >
                        ⚡ Generate {parsedBatchDefectTables.length} Table Rows
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
                          <td colSpan={14} style={{ textAlign: 'center', padding: '18px', color: '#64748b' }}>
                            No defect tables configured. Click <strong>+ Add Row</strong> or <strong>📋 Batch Paste Tables</strong> above.
                          </td>
                        </tr>
                      ) : (
                        pipelineForm.defectTables.map((defectTable, index) => (
                          <Fragment key={index}>
                            <tr>
                              <td style={{ textAlign: 'center', fontWeight: 600, color: '#64748b' }}>
                                {index + 1}
                              </td>
                              <td>
                                <input
                                  className="grid-cell-input"
                                  value={defectTable.tableName}
                                  placeholder="e.g. mcl_defect"
                                  onChange={(e) => updateDefectTable(index, 'tableName', e.target.value)}
                                  required
                                  style={{ fontWeight: 600, color: '#0f172a' }}
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
                                    type="button"
                                    className={`grid-icon-btn ${expandedRow === index ? 'active' : ''}`}
                                    onClick={() => setExpandedRow(expandedRow === index ? null : index)}
                                    title={expandedRow === index ? 'Collapse extra settings' : 'Expand extra columns (custom_feature, spec_judge, etc.)'}
                                  >
                                    ⚙️
                                  </button>
                                  <button
                                    type="button"
                                    className="grid-icon-btn"
                                    onClick={() => duplicateDefectTable(index)}
                                    title="Duplicate table row"
                                  >
                                    📋
                                  </button>
                                  <button
                                    type="button"
                                    className="grid-icon-btn delete"
                                    onClick={() => removeDefectTable(index)}
                                    title="Delete table row"
                                  >
                                    ✕
                                  </button>
                                </div>
                              </td>
                            </tr>

                            {/* Subrow for advanced/less-frequent columns */}
                            {expandedRow === index && (
                              <tr>
                                <td colSpan={14} style={{ padding: '0 4px 6px' }}>
                                  <div className="subrow-card">
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
                                      <strong style={{ fontSize: '11px', color: '#334155', textTransform: 'uppercase' }}>
                                        Extended Schema & Direct Coordinates for [{defectTable.tableName || `#${index + 1}`}]
                                      </strong>
                                      <button
                                        type="button"
                                        className="secondary compact-button"
                                        style={{ fontSize: '11px', padding: '2px 8px' }}
                                        onClick={() => {
                                          const std = createStandardDefectTable(defectTable.tableName, pipelineForm.fkIdColumnName || 'fk_prod_id')
                                          setPipelineForm((prev) => {
                                            const nextTables = [...prev.defectTables]
                                            nextTables[index] = { ...std, displayName: defectTable.displayName }
                                            return { ...prev, defectTables: nextTables }
                                          })
                                        }}
                                      >
                                        ⚡ Re-apply Standard Preset
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
                                        <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', cursor: 'pointer', marginTop: '6px' }}>
                                          <input
                                            type="checkbox"
                                            checked={defectTable.offloadToStorage !== false}
                                            onChange={(e) => updateDefectTable(index, 'offloadToStorage', e.target.checked)}
                                          />
                                          <span style={{ fontSize: '12px' }}>Offload JSON to SeaweedFS</span>
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
                        <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
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
                      >
                        {showAdvancedBypass ? '▲ Collapse Advanced Options' : '▼ Advanced Options (Name filter, AND/OR, NULL policy)'}
                      </button>
                    </div>

                    {/* Advanced Options Panel */}
                    {showAdvancedBypass && (
                      <div className="bypass-advanced-panel">
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '8px' }}>
                          <label className="check-row" style={{ margin: 0, fontWeight: 600, fontSize: '12px' }}>
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

                        <div className="rule-options-bar" style={{ marginTop: '10px', paddingTop: '8px', borderTop: '1px solid #e2e8f0' }}>
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
              <div style={{ marginTop: '14px', borderTop: '1px solid #e2e8f0', paddingTop: '14px' }}>
                <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                  <button
                    type="button"
                    className="secondary"
                    disabled={testing || busy}
                    onClick={() => void handleTestMapping()}
                  >
                    {testing ? 'Connecting & Testing...' : '🔍 Test Mapping & Preview Data'}
                  </button>
                  <span className="field-hint">
                    Executes read-only sample query (LIMIT 5) against MariaDB without saving.
                  </span>
                </div>

                {testResult && (
                  <div className="test-preview-box">
                    <div className="test-preview-header">
                      <strong>
                        {testResult.success ? (
                          <span style={{ color: '#16a34a' }}>
                            ✓ Mapping Test Succeeded · Latency: {testResult.latencyMs}ms · {testResult.sampleProductsCount} sample product(s) found
                          </span>
                        ) : (
                          <span style={{ color: '#dc2626' }}>✗ Test Failed · {testResult.errorMessage}</span>
                        )}
                      </strong>
                    </div>

                    <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginBottom: '8px' }}>
                      {testResult.defectCountsByTable &&
                        Object.entries(testResult.defectCountsByTable).map(([tbl, count]) => (
                          <span key={tbl} className="tag-badge" style={{ background: '#ecfdf5', color: '#166534', borderColor: '#86efac', fontWeight: 700 }}>
                            📋 Table {tbl}: {count.toLocaleString()} defect(s)
                          </span>
                        ))}
                      {pipelineForm.bypassRuleEnabled ? (
                        <span className="tag-badge" style={{ background: '#fef3c7', color: '#92400e', borderColor: '#fcd34d' }}>
                          🛡️ Bypassed: {testResult.bypassedDefectsCount ?? 0} | Scanned: {testResult.scannedDefectsCount ?? ((testResult.acceptedDefectsCount ?? testResult.sampleDefects.length) + (testResult.bypassedDefectsCount ?? 0))} | Accepted: {testResult.acceptedDefectsCount ?? testResult.sampleDefects.length}
                        </span>
                      ) : (
                        (testResult.bypassedDefectsCount ?? 0) > 0 && (
                          <span className="tag-badge" style={{ background: '#fef3c7', color: '#92400e', borderColor: '#fcd34d' }}>
                            🛡️ {testResult.bypassedDefectsCount} defect(s) bypassed (legacy)
                          </span>
                        )
                      )}
                      {testResult.bypassRuleSummary && (
                        <span className="tag-badge" style={{ background: '#f1f5f9', color: '#475569' }} title={testResult.bypassRuleSummary}>
                          Rule: {testResult.bypassRuleSummary}
                        </span>
                      )}
                      {testResult.alreadySavedProductsCount !== undefined && testResult.alreadySavedProductsCount > 0 && (
                        <span className="tag-badge" style={{ background: '#f8fafc', color: '#1e293b', borderColor: '#94a3b8', fontWeight: 700 }} title="Products already persisted in database are automatically locked/skipped to prevent duplicates">
                          🔒 {testResult.alreadySavedProductsCount}/{testResult.sampleProductsCount} already in DB (automatically locked/skipped on real runs)
                        </span>
                      )}
                    </div>

                    {testResult.generatedDefectSql && (
                      <div style={{ marginBottom: '10px' }}>
                        <span style={{ fontSize: '11px', fontWeight: 700, color: '#334155' }}>
                          ⚡ Generated SQL Query ({Object.keys(testResult.defectCountsByTable || {}).length} defect table(s) - pushdown bypass active):
                        </span>
                        <pre style={{
                          background: '#0f172a',
                          color: '#38bdf8',
                          padding: '8px 12px',
                          borderRadius: '6px',
                          fontSize: '11px',
                          overflowX: 'auto',
                          margin: '4px 0 0',
                          fontFamily: 'monospace',
                          whiteSpace: 'pre-wrap',
                          wordBreak: 'break-all'
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
                        <div style={{ marginTop: '12px' }}>
                          <div style={{ fontSize: '11px', fontWeight: 700, color: '#334155', marginBottom: '4px' }}>
                            🔍 Sample Defect Rows (Displaying {testResult.sampleDefects.length} / total {testResult.acceptedDefectsCount ?? testResult.sampleDefects.length} defect(s) found, query limit 2000):
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

              <div className="actions" style={{ marginTop: '16px' }}>
                <button type="submit" disabled={busy}>
                  {pipelineForm.id ? 'Save Changes' : 'Create Pipeline'}
                </button>
                <button
                  type="button"
                  className="ghost-button"
                  onClick={() => setPipelineModalOpen(false)}
                >
                  Cancel
                </button>
              </div>
            </form>
          </Modal>
        </>
      ) : (
        <>
          {/* Standard SQL Query Profiles */}
          <section className="panel">
            <div className="section-heading">
              <div>
                <p className="eyebrow">GET /database-connections</p>
                <h2>MariaDB connection</h2>
              </div>
              <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                <button
                  type="button"
                  className="primary"
                  style={{ background: '#2563eb', borderColor: '#3b82f6', fontWeight: 600 }}
                  onClick={() => setBatchProvisionModalOpen(true)}
                  title="Batch configure Overkill pipelines and multi-defect tables for lines"
                >
                  🚀 Batch Setup Defect Pipelines
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setEditing(null)
                    setDraftConnectionId(selectedConnectionId)
                    setProfileOpen(true)
                  }}
                  disabled={!selectedConnectionId}
                >
                  New SQL query
                </button>
              </div>
            </div>
            <div className="compact-filter-bar">
              <Field label="Machine filter" hint="Standard machine reference">
                <select
                  value={filterMachine}
                  onChange={(event) => {
                    setFilterMachine(event.target.value)
                    setFilterLine('All')
                    setProfilePage(1)
                  }}
                >
                  <option value="All">All machines</option>
                  {machines.map((m) => (
                    <option key={m} value={m}>
                      {m}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Line filter" hint="Filtered by machine">
                <select
                  value={filterLine}
                  onChange={(event) => {
                    setFilterLine(event.target.value)
                    setProfilePage(1)
                  }}
                >
                  <option value="All">All lines</option>
                  {lines.map((l) => (
                    <option key={l} value={l}>
                      {l}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Category" hint="Leakage or Overkill verification">
                <select
                  value={filterCategory}
                  onChange={(event) => {
                    setFilterCategory(event.target.value)
                    setProfilePage(1)
                  }}
                >
                  <option value="All">All categories</option>
                  <option value="Leakage">Leakage</option>
                  <option value="Overkill">Overkill</option>
                </select>
              </Field>
              <Field label="MariaDB connection" hint="Bound connection">
                <select
                  value={selectedConnectionId}
                  onChange={(event) => {
                    setSelectedConnectionId(event.target.value)
                    setProfilePage(1)
                  }}
                >
                  <option value="">All connections</option>
                  {compatibleConnections.map((connection) => (
                    <option key={connection.id} value={connection.id}>
                      {connection.name} · {connection.machineCode}/{connection.lineCode}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            {selectedConnection ? (
              <p className="muted">
                {selectedConnection.engine} {selectedConnection.host}:{selectedConnection.port}/{selectedConnection.databaseName} ·{' '}
                <StatusPill value={selectedConnection.health} />
              </p>
            ) : (
              <p className="muted">Create a MariaDB connection on the Sources tab first. Queries inherit its machine and line.</p>
            )}
          </section>

          <DataTable
            eyebrow="GET /query-profiles"
            title="Saved query content"
            columns={['Name', 'Category', 'Machine', 'Line', 'Connection', 'Updated', '']}
            rows={pagedProfiles.items.map((profile) => ({
              id: profile.id,
              cells: [
                profile.name,
                <StatusPill key="c" value={profile.verificationCategory} />,
                profile.machineCode,
                profile.lineCode,
                profile.connectionName || 'Unbound',
                formatDate(profile.updatedAtUtc),
                <div key="a" className="row-actions">
                  <button
                    type="button"
                    className="table-button"
                    onClick={() => {
                      setEditing(profile)
                      setDraftConnectionId(profile.dataSourceId ?? selectedConnectionId)
                      setProfileOpen(true)
                    }}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className="table-button"
                    onClick={() => {
                      setRunOpen(profile)
                      setRendered(null)
                      setTested(null)
                    }}
                  >
                    Render / test
                  </button>
                </div>,
              ],
            }))}
          />
          <Pagination
            page={pagedProfiles.page}
            totalPages={pagedProfiles.totalPages}
            totalCount={pagedProfiles.totalCount}
            onPage={setProfilePage}
          />

          <Modal
            title={editing ? 'Edit MariaDB query' : 'Save MariaDB query'}
            description={editing ? 'PUT /query-profiles/{id}' : 'POST /query-profiles'}
            open={profileOpen}
            onClose={() => setProfileOpen(false)}
            wide
          >
            <form className="modal-form" onSubmit={(event) => void saveProfile(event)}>
              <div className="query-form-grid">
                <Field label="Query name" required>
                  <input name="name" defaultValue={editing?.name ?? ''} required />
                </Field>
                <Field label="MariaDB connection" required hint="Machine/Line auto-assigned">
                  <select
                    name="dataSourceId"
                    value={draftConnectionId || selectedConnectionId}
                    onChange={(event) => setDraftConnectionId(event.target.value)}
                    required
                  >
                    {connections.map((connection) => (
                      <option key={connection.id} value={connection.id}>
                        {connection.name}
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Verification category" required>
                  <select name="verificationCategory" defaultValue={editing?.verificationCategory ?? 'Overkill'}>
                    <option>Overkill</option>
                    <option>Leakage</option>
                  </select>
                </Field>
                <Field label="Concept" required>
                  <input name="concept" defaultValue={editing?.concept ?? 'PRODUCT-STATUS'} required />
                </Field>
                <Field label="Machine">
                  <input readOnly value={draftConnection?.machineCode ?? '—'} />
                </Field>
                <Field label="Line">
                  <input readOnly value={draftConnection?.lineCode ?? '—'} />
                </Field>
                <Field label="Timeout (s)">
                  <input name="timeoutSeconds" type="number" defaultValue={editing?.timeoutSeconds ?? 30} />
                </Field>
                <Field label="Max concurrency">
                  <input name="maxConcurrency" type="number" defaultValue={editing?.maxConcurrency ?? 2} />
                </Field>
              </div>
              <Field label="SQL template" required hint="SELECT/WITH only. Use {{keyId}} {{lineCode}} placeholders.">
                <textarea
                  name="sqlTemplate"
                  className="code-textarea"
                  defaultValue={
                    editing?.sqlTemplate ??
                    'SELECT key_id, judge, line_code FROM products WHERE key_id = {{keyId}} AND line_code = {{lineCode}} LIMIT 1'
                  }
                  required
                />
              </Field>
              <Field label="Parameter schema JSON" required>
                <textarea
                  name="parameterSchemaJson"
                  className="code-textarea"
                  defaultValue={editing?.parameterSchemaJson ?? '{"keyId":"string","lineCode":"string"}'}
                  required
                />
              </Field>
              <Field label="Notes">
                <textarea name="notes" defaultValue={editing?.notes ?? ''} />
              </Field>
              <div className="actions">
                <button type="submit" disabled={busy}>
                  Save query
                </button>
                <button type="button" className="ghost-button" onClick={() => setProfileOpen(false)}>
                  Cancel
                </button>
              </div>
            </form>
          </Modal>

          <Modal
            title={runOpen ? `Render / test · ${runOpen.name}` : 'Render / test'}
            description="POST /query-profiles/{id}/render · /test"
            open={Boolean(runOpen)}
            onClose={() => setRunOpen(null)}
            wide
          >
            {runOpen ? (
              <form
                className="modal-form"
                onSubmit={(event) => {
                  event.preventDefault()
                  const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null
                  if (submitter?.value === 'test') void testProfile(event)
                  else void renderProfile(event)
                }}
              >
                <p className="muted">
                  Connection {runOpen.connectionName || 'unbound'} · {runOpen.machineCode}/{runOpen.lineCode}
                </p>
                <div className="query-form-grid">
                  {runKeys.length === 0 ? <p className="muted">No parameters in schema.</p> : null}
                  {runKeys.map((key) => (
                    <Field key={key} label={key} required>
                      <input name={key} defaultValue={key === 'lineCode' ? runOpen.lineCode : ''} required />
                    </Field>
                  ))}
                </div>
                <div className="actions">
                  <button type="submit" value="render" disabled={busy}>
                    Render SQL
                  </button>
                  <button type="submit" value="test" className="secondary" disabled={busy}>
                    Test on MariaDB
                  </button>
                </div>
                {rendered ? (
                  <div className="rendered-query">
                    <strong>Rendered SQL</strong>
                    {rendered.missingParameters.length ? <p>Missing: {rendered.missingParameters.join(', ')}</p> : null}
                    {rendered.warnings.length ? <p>Warnings: {rendered.warnings.join(', ')}</p> : null}
                    <pre>{rendered.renderedSql}</pre>
                  </div>
                ) : null}
                {tested ? (
                  <div className="rendered-query">
                    <strong>
                      {tested.success ? 'Success' : 'Failed'} · {tested.rowCount} rows · {tested.latencyMs} ms
                    </strong>
                    <pre>{JSON.stringify(tested.rows.slice(0, 8), null, 2)}</pre>
                  </div>
                ) : null}
              </form>
            ) : null}
          </Modal>
        </>
      )}

      {/* Batch Pipelines Modal - Rendered outside subView ternary so it opens in both Pipelines and Profiles */}
      <BatchPipelinesModal
        open={batchProvisionModalOpen}
        onClose={() => setBatchProvisionModalOpen(false)}
        onNotice={onNotice}
        onCompleted={() => {
          void loadPipelines()
          void loadExecutions()
        }}
        connections={connections}
      />
    </div>
  )
}
