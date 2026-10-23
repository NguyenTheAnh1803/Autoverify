import React, { useMemo, useState } from 'react'
import { imageCrawlerApi, overkillPipelineApi, storageApi } from '../api'
import { Field, Icon, Modal } from '../ui'
import type {
  BatchGeneratePipelinesForConnectionsRequest,
  BatchProvisionResponse,
  DatabaseConnection,
  DefectTableMapping,
  Notice,
  SampleDefectForTestDto,
} from '../types'

export type BatchPipelinesModalProps = {
  open: boolean
  onClose: () => void
  onNotice: (notice: Notice) => void
  onCompleted: () => void
  connections: DatabaseConnection[]
}

function createDefectTableMapping(tableName: string, fkColumnName = 'fk_prod_id'): DefectTableMapping {
  const clean = tableName.trim()
  return {
    tableName: clean,
    displayName: clean ? clean.replace(/_/g, ' ').toUpperCase() : '',
    fkColumnName: fkColumnName || 'fk_prod_id',
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

const PRODUCT_PRESETS = [
  {
    name: 'SMT Standard',
    tag: 'Recommended',
    desc: 'Product · ID · fk_prod_id · Judge · N, NG · datetime_inspection',
    values: {
      productTableName: 'Product',
      idColumnName: 'ID',
      fkColumnName: 'fk_prod_id',
      judgeColumnName: 'Judge',
      judgeFilterStr: 'N, NG',
      dateTimeInspectionColumnName: 'datetime_inspection',
      zoneColumnName: 'zone',
      defectNameColumnName: 'defect_name',
      batchSize: 1000,
    },
  },
  {
    name: 'AOI / SPI (Pascal Case)',
    tag: 'Industrial AOI',
    desc: 'Product · ID · Fk_ID · Judge · N, NG, G, OK · InspectedAt',
    values: {
      productTableName: 'Product',
      idColumnName: 'ID',
      fkColumnName: 'Fk_ID',
      judgeColumnName: 'Judge',
      judgeFilterStr: 'N, NG, G, OK',
      dateTimeInspectionColumnName: 'InspectedAt',
      zoneColumnName: 'zone',
      defectNameColumnName: 'defect_name',
      batchSize: 1000,
    },
  },
  {
    name: 'MES / Snake Case',
    tag: 'Legacy MariaDB',
    desc: 'tbl_product · id · product_id · judge_result · NG, REJECT · created_at',
    values: {
      productTableName: 'tbl_product',
      idColumnName: 'id',
      fkColumnName: 'product_id',
      judgeColumnName: 'judge_result',
      judgeFilterStr: 'NG, REJECT',
      dateTimeInspectionColumnName: 'created_at',
      zoneColumnName: 'area',
      defectNameColumnName: 'defect_type',
      batchSize: 1000,
    },
  },
  {
    name: 'SPI Solder Paste',
    tag: 'SPI Station',
    desc: 'spi_product · ID · fk_prod_id · judge · FAIL, NG · datetime_inspection',
    values: {
      productTableName: 'spi_product',
      idColumnName: 'ID',
      fkColumnName: 'fk_prod_id',
      judgeColumnName: 'judge',
      judgeFilterStr: 'FAIL, NG',
      dateTimeInspectionColumnName: 'datetime_inspection',
      zoneColumnName: 'block_name',
      defectNameColumnName: 'defect_code',
      batchSize: 1000,
    },
  },
]

export const BatchPipelinesModal: React.FC<BatchPipelinesModalProps> = ({
  open,
  onClose,
  onNotice,
  onCompleted,
  connections,
}) => {
  // 1. Comprehensive Product Table Configuration
  const [productTableName, setProductTableName] = useState('Product')
  const [idColumnName, setIdColumnName] = useState('ID')
  const [fkColumnName, setFkColumnName] = useState('fk_prod_id')
  const [judgeColumnName, setJudgeColumnName] = useState('Judge')
  const [judgeFilterStr, setJudgeFilterStr] = useState('N, NG')
  const [dateTimeInspectionColumnName, setDateTimeInspectionColumnName] = useState('datetime_inspection')
  const [zoneColumnName, setZoneColumnName] = useState('zone')
  const [defectNameColumnName, setDefectNameColumnName] = useState('defect_name')
  const [batchSize, setBatchSize] = useState(1000)

  // Quick-Paste / Auto-fill for Product state
  const [quickFillOpen, setQuickFillOpen] = useState(false)
  const [quickFillText, setQuickFillText] = useState('')

  // 2. Defect Tables Multi-Paste state
  const [defectTablesText, setDefectTablesText] = useState('mcl_defect\naoi_defect\nspi_defect')

  // 3. PC Image Crawler API Configuration (Individual per PC / Machine)
  const [imageCrawlerApiUrl, setImageCrawlerApiUrl] = useState('http://10.121.45.43:336/api/image/get')
  const [imageCrawlerVpMode, setImageCrawlerVpMode] = useState<string>('all')
  const [autoCrawlImages, setAutoCrawlImages] = useState(true)
  const [testMode, setTestMode] = useState<'defectId' | 'rawPath'>('defectId')
  const [testDefectId, setTestDefectId] = useState('')
  const [testRawPath, setTestRawPath] = useState(
    '2026/07/25/CP-AOI #301-A/P1EF650120BA27311SDA/VP02/P1EF650120BA27311SDA_164426_A_2/VP02_P1EF650120BA27311SDA_164426_A_2_2183_.jpg'
  )
  const [testRawVp, setTestRawVp] = useState('2')
  const [testPayloadPreview, setTestPayloadPreview] = useState<any>(null)
  const [sampleDefects, setSampleDefects] = useState<SampleDefectForTestDto[]>([])
  const [loadingSamples, setLoadingSamples] = useState(false)
  const [testingImage, setTestingImage] = useState(false)
  const [testImageResult, setTestImageResult] = useState<{
    success: boolean
    imagePath?: string | null
    imageUrl?: string | null
    contentType?: string | null
    sizeBytes: number
    errorMessage?: string | null
    latencyMs: number
  } | null>(null)
  const [testImagePreviewUrl, setTestImagePreviewUrl] = useState('')

  // 4. Overkill Pipeline schedule & options
  const [scheduleIntervalMinutes, setScheduleIntervalMinutes] = useState(60)
  const [lookbackHours, setLookbackHours] = useState(6)
  const [includeOkProducts, setIncludeOkProducts] = useState(true)
  const [offloadDefectsToSeaweedFs, setOffloadDefectsToSeaweedFs] = useState(true)

  // Selection filter: all or selected
  const [selectAll, setSelectAll] = useState(true)
  const [selectedConnectionIds] = useState<string[]>([])

  // Submitting state
  const [submitting, setSubmitting] = useState(false)
  const [resultSummary, setResultSummary] = useState<BatchProvisionResponse | null>(null)

  // Parse Judge Filters into clean array
  const parsedJudgeFilters = useMemo(() => {
    return judgeFilterStr
      .split(/[,;\s]+/)
      .map((s) => s.trim())
      .filter(Boolean)
  }, [judgeFilterStr])

  // Real-time parsed defect tables
  const parsedDefectTables = useMemo(() => {
    if (!defectTablesText.trim()) return []
    const names = defectTablesText.split(/[\n,\t]+/).map((s) => s.trim()).filter(Boolean)
    const unique = [...new Set(names)]
    return unique.map((name) => createDefectTableMapping(name, fkColumnName))
  }, [defectTablesText, fkColumnName])

  const targetConnections = useMemo(() => {
    if (selectAll) return connections
    return connections.filter((c) => selectedConnectionIds.includes(c.id))
  }, [selectAll, connections, selectedConnectionIds])

  // Generated live SQL preview for Product querying
  const generatedSqlPreview = useMemo(() => {
    const table = productTableName.trim() || 'Product'
    const id = idColumnName.trim() || 'ID'
    const fk = fkColumnName.trim() || 'fk_prod_id'
    const judge = judgeColumnName.trim() || 'Judge'
    const time = dateTimeInspectionColumnName.trim() || 'datetime_inspection'
    const zone = zoneColumnName.trim() || 'zone'
    const defect = defectNameColumnName.trim() || 'defect_name'
    const inClause = parsedJudgeFilters.length > 0
      ? parsedJudgeFilters.map((v) => `'${v}'`).join(', ')
      : "'N', 'NG'"

    return `SELECT \`${id}\`, \`${fk}\`, \`${judge}\`, \`${zone}\`, \`${defect}\`, \`${time}\`\nFROM \`${table}\`\nWHERE \`${judge}\` IN (${inClause})\n  AND \`${time}\` >= DATE_SUB(NOW(), INTERVAL ${lookbackHours} HOUR)\n  AND \`${time}\` <= DATE_SUB(NOW(), INTERVAL 60 SECOND)\nORDER BY \`${time}\` DESC\nLIMIT ${batchSize};`
  }, [
    productTableName,
    idColumnName,
    fkColumnName,
    judgeColumnName,
    dateTimeInspectionColumnName,
    zoneColumnName,
    defectNameColumnName,
    parsedJudgeFilters,
    lookbackHours,
    batchSize,
  ])

  function applyProductPreset(preset: (typeof PRODUCT_PRESETS)[0]) {
    setProductTableName(preset.values.productTableName)
    setIdColumnName(preset.values.idColumnName)
    setFkColumnName(preset.values.fkColumnName)
    setJudgeColumnName(preset.values.judgeColumnName)
    setJudgeFilterStr(preset.values.judgeFilterStr)
    setDateTimeInspectionColumnName(preset.values.dateTimeInspectionColumnName)
    setZoneColumnName(preset.values.zoneColumnName)
    setDefectNameColumnName(preset.values.defectNameColumnName)
    setBatchSize(preset.values.batchSize)
    onNotice({
      tone: 'info',
      message: `Applied Product schema preset: ${preset.name}`,
    })
  }

  function handleQuickFillProduct() {
    const raw = quickFillText.trim()
    if (!raw) return

    // Check if key=value format (e.g. table=Product; id=ID; fk=fk_prod_id; judge=judge; values=N,NG)
    if (raw.includes('=')) {
      const parts = raw.split(/[;\n&]+/)
      for (const part of parts) {
        const [k, v] = part.split('=').map((s) => s.trim())
        if (!k || !v) continue
        const lk = k.toLowerCase()
        if (lk.includes('table')) setProductTableName(v)
        else if (lk === 'id' || lk.includes('primary')) setIdColumnName(v)
        else if (lk.includes('fk') || lk.includes('foreign')) setFkColumnName(v)
        else if (lk.includes('judge_val') || lk.includes('filter') || lk.includes('values')) setJudgeFilterStr(v)
        else if (lk.includes('judge')) setJudgeColumnName(v)
        else if (lk.includes('time') || lk.includes('date') || lk.includes('inspect')) setDateTimeInspectionColumnName(v)
        else if (lk.includes('zone') || lk.includes('area')) setZoneColumnName(v)
        else if (lk.includes('defect')) setDefectNameColumnName(v)
        else if (lk.includes('batch') || lk.includes('limit')) setBatchSize(parseInt(v, 10) || 1000)
      }
      onNotice({ tone: 'success', message: 'Parsed and populated Product schema parameters from key-value pairs.' })
      setQuickFillOpen(false)
      return
    }

    // Delimited row format (e.g. Excel tab, comma, or semicolon)
    const tokens = raw.split(/[\t,;]+/).map((s) => s.trim()).filter(Boolean)
    if (tokens.length >= 1) setProductTableName(tokens[0])
    if (tokens.length >= 2) setIdColumnName(tokens[1])
    if (tokens.length >= 3) setFkColumnName(tokens[2])
    if (tokens.length >= 4) setJudgeColumnName(tokens[3])
    if (tokens.length >= 5) setJudgeFilterStr(tokens[4])
    if (tokens.length >= 6) setDateTimeInspectionColumnName(tokens[5])
    if (tokens.length >= 7) setZoneColumnName(tokens[6])
    if (tokens.length >= 8) setDefectNameColumnName(tokens[7])
    if (tokens.length >= 9) setBatchSize(parseInt(tokens[8], 10) || 1000)

    onNotice({
      tone: 'success',
      message: `Parsed and populated ${Math.min(tokens.length, 9)} Product parameters from text tokens.`,
    })
    setQuickFillOpen(false)
  }

  async function handleLoadSamples() {
    setLoadingSamples(true)
    try {
      const samples = await imageCrawlerApi.getSamples(5)
      setSampleDefects(samples)
      if (samples.length > 0 && !testDefectId) {
        setTestDefectId(samples[0].id)
      }
      onNotice({ tone: 'info', message: `Loaded ${samples.length} sample defect(s) from database.` })
    } catch {
      onNotice({ tone: 'error', message: 'Failed to load sample defects from database.' })
    } finally {
      setLoadingSamples(false)
    }
  }

  async function handleTestDefectImage() {
    const defectId = testDefectId.trim()
    if (!defectId) {
      onNotice({ tone: 'error', message: 'Please enter or select a Defect ID to test image crawling.' })
      return
    }

    setTestingImage(true)
    setTestImageResult(null)
    setTestImagePreviewUrl('')
    setTestPayloadPreview(null)
    try {
      const result = await imageCrawlerApi.testDefect({
        defectId,
        customApiUrl: imageCrawlerApiUrl.trim() || undefined,
        customVp: imageCrawlerVpMode === 'auto' ? undefined : imageCrawlerVpMode,
      })
      setTestImageResult({
        success: result.success,
        imagePath: result.imagePath,
        imageUrl: result.success ? `${storageApi.defectImagesUrl(defectId)}` : null,
        contentType: result.contentType,
        sizeBytes: result.sizeBytes,
        errorMessage: result.errorMessage,
        latencyMs: result.latencyMs,
      })
      setTestPayloadPreview(result.requestPayload)
      if (result.success && result.base64Preview) {
        setTestImagePreviewUrl(result.base64Preview)
        onNotice({
          tone: 'success',
          message: `✓ Test successful! Received ${result.sizeBytes.toLocaleString()} bytes from PC API (${result.latencyMs}ms).`,
        })
      } else if (!result.success) {
        onNotice({
          tone: 'error',
          message: `✗ Crawl error: ${result.errorMessage || 'Image file not found on PC'}`,
        })
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Connection error to Image API'
      setTestImageResult({ success: false, sizeBytes: 0, latencyMs: 0, errorMessage: message })
      onNotice({ tone: 'error', message })
    } finally {
      setTestingImage(false)
    }
  }

  async function handleTestRawPath() {
    const path = testRawPath.trim()
    if (!path) {
      onNotice({ tone: 'error', message: 'Please enter an image_path to test.' })
      return
    }

    setTestingImage(true)
    setTestImageResult(null)
    setTestImagePreviewUrl('')
    setTestPayloadPreview(null)
    try {
      const result = await imageCrawlerApi.testPath({
        imagePath: path,
        apiUrl: imageCrawlerApiUrl.trim() || undefined,
        vp: testRawVp.trim() || undefined,
      })
      setTestImageResult({
        success: result.success,
        imagePath: result.imagePath,
        imageUrl: null,
        contentType: result.contentType,
        sizeBytes: result.sizeBytes,
        errorMessage: result.errorMessage,
        latencyMs: result.latencyMs,
      })
      setTestPayloadPreview(result.requestPayload)
      if (result.success && result.base64Preview) {
        setTestImagePreviewUrl(result.base64Preview)
        onNotice({
          tone: 'success',
          message: `✓ Test successful! Received ${result.sizeBytes.toLocaleString()} bytes from PC API (${result.latencyMs}ms).`,
        })
      } else if (!result.success) {
        onNotice({
          tone: 'error',
          message: `✗ Crawl error: ${result.errorMessage || 'Image file not found on PC'}`,
        })
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Connection error to Image API'
      setTestImageResult({ success: false, sizeBytes: 0, latencyMs: 0, errorMessage: message })
      onNotice({ tone: 'error', message })
    } finally {
      setTestingImage(false)
    }
  }

  async function handleExecute() {
    if (!productTableName.trim()) {
      onNotice({ tone: 'error', message: 'Please specify the Product table name.' })
      return
    }

    if (parsedDefectTables.length === 0) {
      onNotice({ tone: 'error', message: 'Please specify at least one defect table name.' })
      return
    }

    if (targetConnections.length === 0) {
      onNotice({ tone: 'error', message: 'No connections selected to generate pipelines for.' })
      return
    }

    setSubmitting(true)
    setResultSummary(null)
    try {
      const req: BatchGeneratePipelinesForConnectionsRequest = {
        connectionIds: selectAll ? null : targetConnections.map((c) => c.id),
        scheduleIntervalMinutes,
        lookbackHours,
        batchSize: Math.max(10, batchSize),
        productTableName: productTableName.trim() || 'Product',
        idColumnName: idColumnName.trim() || 'ID',
        fkIdColumnName: fkColumnName.trim() || 'fk_prod_id',
        judgeColumnName: judgeColumnName.trim() || 'Judge',
        judgeFilterValues: parsedJudgeFilters.length > 0 ? parsedJudgeFilters : ['N', 'NG'],
        dateTimeInspectionColumnName: dateTimeInspectionColumnName.trim() || 'datetime_inspection',
        timestampColumnName: dateTimeInspectionColumnName.trim() || 'datetime_inspection',
        zoneColumnName: zoneColumnName.trim() || 'zone',
        defectNameColumnName: defectNameColumnName.trim() || 'defect_name',
        includeOkProducts,
        offloadDefectsToSeaweedFs,
        imageCrawlerApiUrl: imageCrawlerApiUrl.trim() || undefined,
        autoCrawlImages,
        imageCrawlerVpMode,
        defectTables: parsedDefectTables,
      }

      const res = await overkillPipelineApi.batchProvisionFromConnections(req)
      setResultSummary(res)
      onNotice({
        tone: 'success',
        message: `✓ Successfully configured pipelines for ${res.totalItemsProcessed} connections (${res.createdPipelinesCount} created, ${res.updatedPipelinesCount} updated)!`,
      })
      onCompleted()
    } catch (err) {
      onNotice({ tone: 'error', message: err instanceof Error ? err.message : 'Failed to generate pipelines.' })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title="Batch Configure Overkill Pipelines for Lines & Machines"
      description="Configure master Product query parameters and automated multi-defect table mappings for all your registered line connections in one click."
      open={open}
      onClose={onClose}
      wide
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        {/* 1. Connection Target Banner */}
        <div
          style={{
            background: 'rgba(59, 130, 246, 0.08)',
            border: '1px solid rgba(59, 130, 246, 0.25)',
            borderRadius: '6px',
            padding: '10px 14px',
            fontSize: '13px',
            color: '#1d4ed8',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
          }}
        >
          <div>
            <strong>Target Lines & Connections:</strong> Generating Overkill background pipelines for{' '}
            <strong style={{ color: '#60a5fa' }}>{targetConnections.length}</strong> / <strong>{connections.length}</strong> registered connections.
          </div>
          <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', cursor: 'pointer', fontSize: '12px' }}>
            <input
              type="checkbox"
              checked={selectAll}
              onChange={(e) => setSelectAll(e.target.checked)}
            />
            <span>All {connections.length} Connections</span>
          </label>
        </div>

        {/* ========================================================================= */}
        {/* 2. COMPREHENSIVE PRODUCT TABLE & SCAN SCHEMA CONFIGURATION */}
        {/* ========================================================================= */}
        <div
          style={{
            background: 'rgba(255, 255, 255, 0.02)',
            border: '1px solid rgba(99, 102, 241, 0.25)',
            borderRadius: '8px',
            padding: '14px',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '8px', marginBottom: '10px' }}>
            <div>
              <span className="eyebrow" style={{ color: '#4f46e5', fontWeight: 700 }}>
                <Icon name="package" className="icon inline-icon" />PRIMARY PRODUCT TABLE CONFIGURATION
              </span>
              <h3 style={{ margin: '2px 0 0', fontSize: '14px', color: '#475569' }}>
                Master Product Schema & Inspection Parameters
              </h3>
            </div>
            <div style={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
              <button
                type="button"
                className="secondary compact"
                style={{ fontSize: '12px', padding: '3px 10px', background: 'rgba(99, 102, 241, 0.15)', borderColor: 'rgba(99, 102, 241, 0.3)' }}
                onClick={() => setQuickFillOpen(!quickFillOpen)}
              >
                {quickFillOpen ? '▲ Hide Quick-Fill' : 'Quick-Fill / Paste Row'}
              </button>
            </div>
          </div>

          {/* Quick Preset Selector Buttons */}
          <div style={{ marginBottom: '12px', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '12px', color: '#64748b', fontWeight: 600 }}>Schema Presets:</span>
            {PRODUCT_PRESETS.map((preset) => (
              <button
                key={preset.name}
                type="button"
                className="secondary compact"
                style={{
                  fontSize: '12px',
                  padding: '3px 8px',
                  borderColor: productTableName === preset.values.productTableName ? '#4f46e5' : undefined,
                }}
                onClick={() => applyProductPreset(preset)}
                title={preset.desc}
              >
                <span style={{ color: '#4f46e5', marginRight: '4px' }}>●</span>
                {preset.name}
              </button>
            ))}
          </div>

          {/* Collapsible Quick-Fill Box */}
          {quickFillOpen && (
            <div
              style={{
                background: 'rgba(15, 23, 42, 0.6)',
                border: '1px dashed rgba(99, 102, 241, 0.4)',
                borderRadius: '6px',
                padding: '10px 12px',
                marginBottom: '12px',
              }}
            >
              <div style={{ fontSize: '12px', color: '#c7d2fe', marginBottom: '6px' }}>
                <strong><Icon name="operations" className="icon inline-icon" />Quick-Fill Text Shorthand or Excel Paste:</strong> Paste a tab/comma-separated row or key-value pairs (e.g. <code>table=Product; id=ID; fk=fk_prod_id; judge=Judge; values=N,NG; time=datetime_inspection</code>).
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <input
                  value={quickFillText}
                  onChange={(e) => setQuickFillText(e.target.value)}
                  placeholder="Product, ID, fk_prod_id, Judge, N, NG, datetime_inspection, zone, defect_name"
                  style={{ flex: 1, fontFamily: 'monospace', fontSize: '12px', background: '#090d16', color: '#e2e8f0', padding: '6px 10px' }}
                />
                <button
                  type="button"
                  className="primary compact"
                  style={{ padding: '6px 14px', fontSize: '12px', background: '#4f46e5' }}
                  onClick={handleQuickFillProduct}
                >
                  Apply
                </button>
              </div>
            </div>
          )}

          {/* Full Grid of Product Table Parameters */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '10px' }}>
            <Field label="Product Table Name *" hint="Master table in MariaDB">
              <input
                value={productTableName}
                onChange={(e) => setProductTableName(e.target.value)}
                placeholder="Product"
                style={{ fontFamily: 'monospace', fontWeight: 600, color: '#4f46e5' }}
                required
              />
            </Field>

            <Field label="Primary Key ID Column *" hint="Unique product serial ID">
              <input
                value={idColumnName}
                onChange={(e) => setIdColumnName(e.target.value)}
                placeholder="ID"
                style={{ fontFamily: 'monospace' }}
                required
              />
            </Field>

            <Field label="Defect Table FK Column *" hint="FK in defect tables matching Product ID">
              <input
                value={fkColumnName}
                onChange={(e) => setFkColumnName(e.target.value)}
                placeholder="fk_prod_id or Fk_ID"
                style={{ fontFamily: 'monospace', color: '#047857' }}
                required
              />
            </Field>

            <Field label="Judge Column Name *" hint="Inspection judgment column">
              <input
                value={judgeColumnName}
                onChange={(e) => setJudgeColumnName(e.target.value)}
                placeholder="Judge"
                style={{ fontFamily: 'monospace' }}
                required
              />
            </Field>

            <Field label="Inspection Time Column" hint="Datetime of board inspection">
              <input
                value={dateTimeInspectionColumnName}
                onChange={(e) => setDateTimeInspectionColumnName(e.target.value)}
                placeholder="datetime_inspection"
                style={{ fontFamily: 'monospace' }}
              />
            </Field>

            <Field label="Zone / Area Column" hint="Spatial layout or board zone">
              <input
                value={zoneColumnName}
                onChange={(e) => setZoneColumnName(e.target.value)}
                placeholder="zone"
                style={{ fontFamily: 'monospace' }}
              />
            </Field>

            <Field label="Product Defect Name Col" hint="Defect label if present in Product">
              <input
                value={defectNameColumnName}
                onChange={(e) => setDefectNameColumnName(e.target.value)}
                placeholder="defect_name"
                style={{ fontFamily: 'monospace' }}
              />
            </Field>

            <Field label="Batch Query Limit" hint="Max products per scan cycle">
              <input
                type="number"
                min={10}
                max={10000}
                value={batchSize}
                onChange={(e) => setBatchSize(parseInt(e.target.value, 10) || 1000)}
                style={{ fontFamily: 'monospace' }}
              />
            </Field>
          </div>

          {/* Judge Filter Values with Quick-Add Chips */}
          <div style={{ marginTop: '10px', background: 'rgba(0, 0, 0, 0.2)', padding: '10px', borderRadius: '6px', border: '1px solid rgba(255,255,255,0.05)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px', flexWrap: 'wrap', gap: '6px' }}>
              <label style={{ fontSize: '12px', fontWeight: 600, color: '#475569' }}>
                Judge Filter Values (Target products to scan):
              </label>
              <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                <span style={{ fontSize: '12px', color: '#475569' }}>Quick values:</span>
                <button
                  type="button"
                  className="secondary compact"
                  style={{ fontSize: '12px', padding: '1px 6px' }}
                  onClick={() => setJudgeFilterStr('N, NG')}
                >
                  N, NG (Suspects)
                </button>
                <button
                  type="button"
                  className="secondary compact"
                  style={{ fontSize: '12px', padding: '1px 6px' }}
                  onClick={() => setJudgeFilterStr('N, NG, G, OK')}
                >
                  N, NG, G, OK (All)
                </button>
                <button
                  type="button"
                  className="secondary compact"
                  style={{ fontSize: '12px', padding: '1px 6px' }}
                  onClick={() => setJudgeFilterStr('NG, REJECT')}
                >
                  NG, REJECT
                </button>
                <button
                  type="button"
                  className="secondary compact"
                  style={{ fontSize: '12px', padding: '1px 6px' }}
                  onClick={() => setJudgeFilterStr('FAIL')}
                >
                  FAIL
                </button>
              </div>
            </div>
            <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
              <input
                value={judgeFilterStr}
                onChange={(e) => setJudgeFilterStr(e.target.value)}
                placeholder="N, NG"
                style={{ fontFamily: 'monospace', fontSize: '12px', flex: 1, background: '#090d16' }}
              />
              <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                {parsedJudgeFilters.map((val) => (
                  <span
                    key={val}
                    style={{
                      background: val.toUpperCase().includes('OK') || val.toUpperCase() === 'G' ? '#047857' : '#dc2626',
                      color: '#fff',
                      fontSize: '12px',
                      fontWeight: 700,
                      padding: '2px 6px',
                      borderRadius: '4px',
                      fontFamily: 'monospace',
                    }}
                  >
                    {val}
                  </span>
                ))}
              </div>
            </div>
          </div>

          {/* Live MariaDB Query Preview */}
          <div style={{ marginTop: '10px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
              <span style={{ fontSize: '12px', fontWeight: 600, color: '#64748b' }}>
                <Icon name="search" className="icon inline-icon" />MariaDB Product Query Preview:
              </span>
              <span style={{ fontSize: '12px', color: '#64748b' }}>
                Executed automatically on each connection during scheduled scan
              </span>
            </div>
            <pre
              style={{
                margin: 0,
                background: '#090d16',
                border: '1px solid rgba(255,255,255,0.08)',
                borderRadius: '6px',
                padding: '8px 12px',
                fontSize: '12px',
                fontFamily: 'monospace',
                color: '#a5b4fc',
                whiteSpace: 'pre-wrap',
                lineHeight: 1.4,
              }}
            >
              {generatedSqlPreview}
            </pre>
          </div>
        </div>

        {/* ========================================================================= */}
        {/* 3. DEFECT TABLES MULTI-PASTE & PREVIEW */}
        {/* ========================================================================= */}
        <div style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(16, 185, 129, 0.25)', borderRadius: '8px', padding: '14px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '8px' }}>
            <div>
              <span className="eyebrow" style={{ color: '#047857', fontWeight: 700 }}><Icon name="link" className="icon inline-icon" />DEFECT DETAIL TABLES MAPPING</span>
              <h3 style={{ margin: '2px 0 0', fontSize: '14px', color: '#475569' }}>
                Paste Defect Tables List (Joined via {fkColumnName || 'fk_prod_id'} = {productTableName || 'Product'}.{idColumnName || 'ID'})
              </h3>
            </div>
            <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
              <button
                type="button"
                className="secondary compact"
                style={{ fontSize: '12px', padding: '2px 8px' }}
                onClick={() => setDefectTablesText('mcl_defect\naoi_defect\nspi_defect')}
              >
                Preset: SMT Multi-Station
              </button>
              <button
                type="button"
                className="secondary compact"
                style={{ fontSize: '12px', padding: '2px 8px' }}
                onClick={() => setDefectTablesText('AOI_DEFECTS\nSPI_DEFECTS')}
              >
                Preset: Uppercase Tables
              </button>
              <span className="tag-badge" style={{ background: '#047857', color: '#fff', fontWeight: 700, fontSize: '12px' }}>
                {parsedDefectTables.length} Tables Detected
              </span>
            </div>
          </div>

          <textarea
            value={defectTablesText}
            onChange={(e) => setDefectTablesText(e.target.value)}
            rows={3}
            placeholder="mcl_defect&#10;aoi_defect&#10;spi_defect"
            style={{
              fontFamily: 'monospace',
              fontSize: '12px',
              width: '100%',
              background: '#090d16',
              color: '#e2e8f0',
              padding: '10px',
              borderRadius: '6px',
              border: '1px solid rgba(255,255,255,0.12)',
              whiteSpace: 'pre',
            }}
          />

          {/* Live Preview Grid */}
          {parsedDefectTables.length > 0 && (
            <div style={{ marginTop: '10px', border: '1px solid rgba(255,255,255,0.08)', borderRadius: '6px', overflow: 'hidden' }}>
              <div style={{ background: 'rgba(255,255,255,0.04)', padding: '6px 12px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span className="text-xs font-bold" style={{ color: '#047857' }}>
                  Live Preview: {parsedDefectTables.length} Defect Tables Schema Mapping
                </span>
                <span className="text-xs text-muted">Joined with {productTableName}.{idColumnName} via FK {fkColumnName}</span>
              </div>
              <div tabIndex={0} aria-label="Selected lines" role="group" style={{ maxHeight: '130px', overflowY: 'auto' }}>
                <table className="analytics-data-table" role="table" style={{ margin: 0 }}>
                  <thead>
                    <tr>
                      <th style={{ width: '40px' }}>#</th>
                      <th>Table Name</th>
                      <th>Display Label</th>
                      <th>FK Column</th>
                      <th>Judge Col</th>
                      <th>Defect Name Col</th>
                      <th>Feature Col</th>
                      <th>Image Path Col</th>
                    </tr>
                  </thead>
                  <tbody>
                    {parsedDefectTables.map((t, idx) => (
                      <tr key={idx}>
                        <td className="font-mono text-muted text-xs">{idx + 1}</td>
                        <td><strong className="mono-code" style={{ color: '#047857' }}>{t.tableName}</strong></td>
                        <td className="text-xs">{t.displayName}</td>
                        <td className="font-mono text-xs" style={{ color: '#60a5fa' }}>{t.fkColumnName}</td>
                        <td className="font-mono text-xs text-muted">{t.judgeColumn}</td>
                        <td className="font-mono text-xs text-muted">{t.defectNameColumn}</td>
                        <td className="font-mono text-xs text-muted">{t.featureColumn}</td>
                        <td className="font-mono text-xs text-muted">{t.imageUriColumn}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>

        {/* ========================================================================= */}
        {/* 4. PC IMAGE CRAWLER API CONFIGURATION (Per-PC Image Fetching) */}
        {/* ========================================================================= */}
        <div style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(168, 85, 247, 0.25)', borderRadius: '8px', padding: '14px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '8px', marginBottom: '8px' }}>
            <div>
              <span className="eyebrow" style={{ color: '#c084fc', fontWeight: 700 }}><Icon name="image" className="icon inline-icon" />PC IMAGE CRAWLER API CONFIGURATION</span>
              <h3 style={{ margin: '2px 0 0', fontSize: '14px', color: '#475569' }}>
                Per-PC Defect Thumbnail Image Crawler API
              </h3>
            </div>
            <div style={{ display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
              <button
                type="button"
                className="secondary compact"
                style={{ fontSize: '12px', padding: '2px 8px' }}
                onClick={() => setImageCrawlerApiUrl('http://10.121.45.43:336/api/image/get')}
              >
                AnywherePix API
              </button>
              <button
                type="button"
                className="secondary compact"
                style={{ fontSize: '12px', padding: '2px 8px' }}
                onClick={() => setImageCrawlerApiUrl('http://{host}:8080/crawl')}
              >
                Template: http://{'{host}'}:8080/crawl
              </button>
              <button
                type="button"
                className="secondary compact"
                style={{ fontSize: '12px', padding: '2px 8px' }}
                onClick={() => setImageCrawlerApiUrl('')}
              >
                Disable Crawler
              </button>
            </div>
          </div>

          <div
            style={{
              background: 'rgba(168, 85, 247, 0.08)',
              border: '1px solid rgba(168, 85, 247, 0.25)',
              borderRadius: '6px',
              padding: '10px 12px',
              fontSize: '12px',
              color: '#e9d5ff',
              marginBottom: '10px',
              lineHeight: 1.5,
            }}
          >
            <strong><Icon name="info" className="icon inline-icon" />How Image Crawling Works for Engineers:</strong>
            <ul style={{ margin: '4px 0 0 16px', padding: 0 }}>
              <li><strong>Local File Path:</strong> MariaDB defect tables store local PC paths in column <code>image_path</code> (e.g. <code>D:\AOI_Data\20260916\img_01.jpg</code>).</li>
              <li><strong>Per-PC API:</strong> Because images reside on industrial PCs, AutoVerify calls each PC's Image API separately. Using <code>{'{host}'}</code> automatically substitutes that connection's PC IP/Host.</li>
              <li><strong>Verification Thumbnail:</strong> When an engineer inspects defects in the Anomaly Gallery or Feature Inspector, the thumbnail is retrieved from the PC API and cached in SeaweedFS.</li>
            </ul>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 200px auto', gap: '12px', alignItems: 'flex-end' }}>
            <Field
              label="PC Image Crawler Endpoint / Template"
              hint="Use {host} for automatic PC IP substitution, or enter dedicated PC URL"
            >
              <input
                value={imageCrawlerApiUrl}
                onChange={(e) => setImageCrawlerApiUrl(e.target.value)}
                placeholder="http://10.121.45.43:336/api/image/get"
                style={{ fontFamily: 'monospace', fontSize: '12px' }}
              />
            </Field>

            <Field
              label="VP Setting (Camera / Viewpoint)"
              hint="Configure crawler VP mode sent to server"
            >
              <select
                value={imageCrawlerVpMode}
                onChange={(e) => {
                  const val = e.target.value
                  setImageCrawlerVpMode(val)
                  if (val !== 'auto' && val !== 'all') setTestRawVp(val)
                }}
                style={{ fontSize: '12px', fontWeight: 600 }}
              >
                <option value="all">All VPs (VP 0 & other VPs - Default)</option>
                <option value="0">VP = 0 (Thumbnail defectimage)</option>
                <option value="auto">Auto VP (Feature G010_VpID / Path)</option>
                <option value="1">VP = 1</option>
                <option value="2">VP = 2</option>
                <option value="3">VP = 3</option>
                <option value="4">VP = 4</option>
              </select>
            </Field>

            <Field label="Model Group" hint="Read-only: each pipeline inherits Model directly from its Database Connection">
              <input value="Mapped from Database Connections" readOnly disabled style={{ fontFamily: 'monospace', fontSize: '12px' }} />
            </Field>

            <div style={{ marginBottom: '10px' }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', cursor: 'pointer' }}>
                <input
                  type="checkbox"
                  checked={autoCrawlImages}
                  onChange={(e) => setAutoCrawlImages(e.target.checked)}
                />
                <span style={{ fontSize: '13px', fontWeight: 600 }}>
                  <Icon name="operations" className="icon inline-icon" />Proactively crawl defect images during scan
                </span>
              </label>
            </div>
          </div>

          <div style={{ marginTop: '12px', padding: '12px', border: '1px solid rgba(56, 189, 248, 0.3)', borderRadius: '8px', background: 'rgba(14, 165, 233, 0.06)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '8px', alignItems: 'center', flexWrap: 'wrap', marginBottom: '8px' }}>
              <div>
                <strong style={{ color: '#7dd3fc', fontSize: '13px' }}><Icon name="flask" className="icon inline-icon" />Image Crawl Test Tool (Verify Image Retrieval)</strong>
                <div style={{ color: '#64748b', fontSize: '12px', marginTop: '2px' }}>
                  Send AnywherePix payload to API <code>{imageCrawlerApiUrl || 'http://10.121.45.43:336/api/image/get'}</code> and preview image directly.
                </div>
              </div>
              <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                <button
                  type="button"
                  className={`ghost-button compact ${testMode === 'defectId' ? 'active' : ''}`}
                  style={{ fontSize: '12px', fontWeight: testMode === 'defectId' ? 700 : 400 }}
                  onClick={() => setTestMode('defectId')}
                >
                  <Icon name="target" className="icon inline-icon" />Test by Defect ID
                </button>
                <button
                  type="button"
                  className={`ghost-button compact ${testMode === 'rawPath' ? 'active' : ''}`}
                  style={{ fontSize: '12px', fontWeight: testMode === 'rawPath' ? 700 : 400 }}
                  onClick={() => setTestMode('rawPath')}
                >
                  <Icon name="link" className="icon inline-icon" />Test by image_path
                </button>
                {testImageResult && (
                  <span
                    className="tag-badge"
                    style={{
                      background: testImageResult.success ? '#047857' : '#dc2626',
                      borderColor: 'transparent',
                      color: '#fff',
                      fontWeight: 700,
                    }}
                  >
                    {testImageResult.success ? '✓ Valid Image / Success' : '✗ Crawl Error'}
                  </span>
                )}
              </div>
            </div>

            {/* Mode 1: Test by Defect ID */}
            {testMode === 'defectId' && (
              <div>
                <div style={{ display: 'grid', gridTemplateColumns: 'minmax(240px, 1fr) auto auto', gap: '8px', alignItems: 'center' }}>
                  <input
                    value={testDefectId}
                    onChange={(e) => setTestDefectId(e.target.value)}
                    placeholder="Paste Defect ID (GUID) from DB"
                    style={{ fontFamily: 'monospace', fontSize: '12px' }}
                  />
                  <button
                    type="button"
                    className="secondary compact"
                    style={{ fontSize: '12px', padding: '6px 10px' }}
                    onClick={() => void handleLoadSamples()}
                    disabled={loadingSamples}
                    title="Fetch 5 latest defects with image_path from DB"
                  >
                    {loadingSamples ? 'Fetching...' : 'Select Sample from DB'}
                  </button>
                  <button
                    type="button"
                    className="primary compact"
                    disabled={testingImage || !testDefectId.trim()}
                    onClick={() => void handleTestDefectImage()}
                    style={{ background: '#0369a1', padding: '6px 14px', fontWeight: 700 }}
                  >
                    {testingImage ? 'Crawling...' : 'Test Crawl & View'}
                  </button>
                </div>

                {/* Sample Defect Chips */}
                {sampleDefects.length > 0 && (
                  <div style={{ marginTop: '8px', display: 'flex', gap: '6px', alignItems: 'center', flexWrap: 'wrap' }}>
                    <span style={{ fontSize: '12px', color: '#64748b' }}>DB Samples:</span>
                    {sampleDefects.map((s) => (
                      <button
                        key={s.id}
                        type="button"
                        className="secondary compact"
                        style={{ fontSize: '12px', padding: '2px 8px', borderColor: testDefectId === s.id ? '#38bdf8' : undefined }}
                        onClick={() => {
                          setTestDefectId(s.id)
                          if (s.imagePath) setTestRawPath(s.imagePath)
                        }}
                        title={`KeyId: ${s.keyId} · Table: ${s.defectTable} · Path: ${s.imagePath}`}
                      >
                        {s.defectName} ({s.keyId.length > 12 ? s.keyId.slice(0, 10) + '...' : s.keyId})
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* Mode 2: Test by raw image_path string */}
            {testMode === 'rawPath' && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 90px auto', gap: '8px', alignItems: 'center' }}>
                  <input
                    value={testRawPath}
                    onChange={(e) => setTestRawPath(e.target.value)}
                    placeholder="Paste image_path (e.g. 2026/07/25/CP-AOI #301-A/.../VP02_...jpg)"
                    style={{ fontFamily: 'monospace', fontSize: '12px' }}
                  />
                  <input
                    value={testRawVp}
                    onChange={(e) => setTestRawVp(e.target.value)}
                    placeholder="VP (e.g. 2)"
                    title="G010_VpID / Camera VP"
                    style={{ fontFamily: 'monospace', fontSize: '12px', textAlign: 'center' }}
                  />
                  <button
                    type="button"
                    className="primary compact"
                    disabled={testingImage || !testRawPath.trim()}
                    onClick={() => void handleTestRawPath()}
                    style={{ background: '#0369a1', padding: '6px 14px', fontWeight: 700 }}
                  >
                    {testingImage ? 'Crawling...' : 'Test Crawl & View'}
                  </button>
                </div>
                <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                  <span style={{ fontSize: '12px', color: '#64748b' }}>Quick sample:</span>
                  <button
                    type="button"
                    className="secondary compact"
                    style={{ fontSize: '12px', padding: '1px 6px' }}
                    onClick={() => {
                      setTestRawPath('2026/07/25/CP-AOI #301-A/P1EF650120BA27311SDA/VP02/P1EF650120BA27311SDA_164426_A_2/VP02_P1EF650120BA27311SDA_164426_A_2_2183_.jpg')
                      setTestRawVp('2')
                    }}
                  >
                    Line 301 #301-A (VP2)
                  </button>
                </div>
              </div>
            )}

            {/* Test Results & Real-time Image Render */}
            {testImageResult && (
              <div style={{ display: 'grid', gridTemplateColumns: testImagePreviewUrl ? '200px 1fr' : '1fr', gap: '14px', marginTop: '12px', padding: '10px', background: 'rgba(0, 0, 0, 0.3)', borderRadius: '6px', border: '1px solid rgba(255,255,255,0.08)', alignItems: 'start' }}>
                {testImagePreviewUrl && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', alignItems: 'center' }}>
                    <a href={testImagePreviewUrl} target="_blank" rel="noreferrer" title="Click to open full-size image">
                      <img
                        src={testImagePreviewUrl}
                        alt="Crawled defect preview"
                        style={{ width: '200px', height: '150px', objectFit: 'contain', background: '#020617', border: '1px solid #38bdf8', borderRadius: '6px', display: 'block' }}
                      />
                    </a>
                    <a href={testImagePreviewUrl} target="_blank" rel="noreferrer" style={{ color: '#0369a1', fontSize: '12px', fontWeight: 700 }}>
                      <Icon name="search" className="icon inline-icon" />Open full-size image
                    </a>
                  </div>
                )}
                <div style={{ fontSize: '12px', color: '#475569', lineHeight: 1.6, overflow: 'hidden' }}>
                  <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap', marginBottom: '4px' }}>
                    <div><strong>Result:</strong> <span style={{ color: testImageResult.success ? '#34d399' : '#f87171', fontWeight: 700 }}>{testImageResult.success ? 'Success (200 OK)' : 'Failed'}</span></div>
                    <div><strong>Response Time:</strong> {testImageResult.latencyMs} ms</div>
                    <div><strong>Image Size:</strong> {testImageResult.sizeBytes.toLocaleString()} bytes</div>
                    <div><strong>Content-Type:</strong> <code>{testImageResult.contentType || 'image/jpeg'}</code></div>
                  </div>

                  {testImageResult.imagePath && (
                    <div style={{ marginBottom: '4px' }}>
                      <strong style={{ color: '#64748b' }}>Original Path (image_path):</strong>
                      <code style={{ display: 'block', fontSize: '12px', color: '#7dd3fc', background: '#090d16', padding: '3px 6px', borderRadius: '4px', wordBreak: 'break-all', marginTop: '2px' }}>
                        {testImageResult.imagePath}
                      </code>
                    </div>
                  )}

                  {testPayloadPreview && (
                    <div style={{ marginBottom: '4px' }}>
                      <strong style={{ color: '#64748b' }}>JSON Payload sent to AnywherePix API:</strong>
                      <pre style={{ margin: '2px 0 0', background: '#090d16', padding: '6px 8px', borderRadius: '4px', fontSize: '12px', color: '#a5b4fc', fontFamily: 'monospace', whiteSpace: 'pre-wrap', maxHeight: '100px', overflowY: 'auto' }}>
                        {JSON.stringify(testPayloadPreview, null, 2)}
                      </pre>
                    </div>
                  )}

                  {testImageResult.errorMessage && (
                    <div style={{ color: '#fca5a5', background: 'rgba(239, 68, 68, 0.1)', padding: '6px 8px', borderRadius: '4px', border: '1px solid rgba(239, 68, 68, 0.25)', marginTop: '4px' }}>
                      <strong>Server Message:</strong> {testImageResult.errorMessage}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>

        {/* ========================================================================= */}
        {/* 5. PIPELINE AUTOMATION & STORAGE OPTIONS */}
        {/* ========================================================================= */}
        <div style={{ background: 'rgba(255,255,255,0.02)', border: '1px solid rgba(245, 158, 11, 0.25)', borderRadius: '8px', padding: '14px' }}>
          <span className="eyebrow" style={{ color: '#b45309', fontWeight: 700 }}>⏱️ SCAN SCHEDULE & STORAGE OPTIONS</span>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '16px', alignItems: 'center', marginTop: '6px' }}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={includeOkProducts}
                onChange={(e) => setIncludeOkProducts(e.target.checked)}
              />
              <span style={{ fontSize: '13px' }}>
                Include OK Product IDs (Calculate total line throughput & yield)
              </span>
            </label>

            <label style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={offloadDefectsToSeaweedFs}
                onChange={(e) => setOffloadDefectsToSeaweedFs(e.target.checked)}
              />
              <span style={{ fontSize: '13px' }}>
                Offload defect feature JSON & images to SeaweedFS
              </span>
            </label>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginTop: '10px' }}>
            <Field label="Schedule Interval (Minutes)" hint="Background polling frequency">
              <input
                type="number"
                min={5}
                value={scheduleIntervalMinutes}
                onChange={(e) => setScheduleIntervalMinutes(parseInt(e.target.value, 10) || 60)}
              />
            </Field>

            <Field label="Lookback Hours (Query history window)" hint="Search products within last N hours">
              <input
                type="number"
                min={1}
                value={lookbackHours}
                onChange={(e) => setLookbackHours(parseInt(e.target.value, 10) || 6)}
              />
            </Field>
          </div>
        </div>

        {/* Results Banner */}
        {resultSummary && (
          <div
            style={{
              padding: '12px',
              borderRadius: '6px',
              background: 'rgba(16, 185, 129, 0.15)',
              border: '1px solid #10b981',
              color: '#10b981',
              fontSize: '13px',
            }}
          >
            <strong>{resultSummary.message}</strong>
          </div>
        )}

        {/* Bottom Actions */}
        <div className="actions" style={{ justifyContent: 'flex-end', gap: '8px', borderTop: '1px solid rgba(255,255,255,0.08)', paddingTop: '12px' }}>
          <button type="button" className="ghost-button" onClick={onClose}>
            {resultSummary ? 'Close' : 'Cancel'}
          </button>
          <button
            type="button"
            className="primary"
            style={{ background: '#2563eb', padding: '8px 24px', fontWeight: 600 }}
            onClick={() => void handleExecute()}
            disabled={submitting}
          >
            {submitting
              ? 'Configuring Pipelines...'
              : `Create/Update Pipelines for ${targetConnections.length} Lines Now`}
          </button>
        </div>
      </div>
    </Modal>
  )
}
