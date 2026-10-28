export type UserProfile = {
  username: string
  displayName: string
  roles: string[]
}

export type LoginResponse = {
  accessToken: string
  user: UserProfile
}

export type DashboardRateTrend = {
  date: string
  productionItems: number
  evaluatedItems: number
  anomalyItems: number
  anomalyRate: number
}

export type DashboardDefectName = {
  defectName: string
  count: number
  share: number
}

export type DashboardSectionReport = {
  productionItems: number
  evaluatedItems: number
  anomalyItems: number
  anomalyRate: number
  defectRecords: number
  lastUpdatedAtUtc: string | null
  trend: DashboardRateTrend[]
  defectsByName: DashboardDefectName[]
}

export type DashboardProductionReport = {
  days: number
  generatedAtUtc: string
  overkill: DashboardSectionReport
  leakage: DashboardSectionReport
}

export type ImportAcceptedRow = {
  rowNumber: number
  entityId: string
  lineCode?: string | null
  mappingInt?: number | null
  mapping?: string | null
  warning?: string | null
}

export type ImportRejectedRow = {
  rowNumber: number
  rawValue: string
  reason: string
}

export type ImportPreview = {
  acceptedCount: number
  rejectedCount: number
  duplicateCount: number
  checksum: string
  verificationCategory: string
  machineCode: string
  lineCode: string | null
  hasLineWarning: boolean
  acceptedRows: ImportAcceptedRow[]
  rejectedRows: ImportRejectedRow[]
}

export type ImportPreviewRequest = {
  rawText: string
  mappingName?: string
  machineCode?: string | null
  lineCode?: string | null
  verificationCategory?: string
}

export type ScanList = {
  id: string
  name: string
  mappingName: string
  machineCode: string
  lineCode: string
  modelCode: string
  verificationCategory: string
  acceptedCount: number
  rejectedCount: number
  duplicateCount: number
  checksum: string
  evidenceUri: string | null
  createdAtUtc: string
  createdBy: string
}

export type CreateScanListRequest = {
  name: string
  mappingName: string
  rawText: string
  machineCode?: string | null
  lineCode?: string | null
  verificationCategory?: string
}

export type DataSource = {
  id: string
  name: string
  type: string
  machineCode: string
  verificationCategory: string
  schedulePeriod: string
  environment: string
  health: string
  averageLatencyMs: number
  enabled: boolean
  host: string | null
  port: number | null
  databaseName: string | null
  apiBaseUrl: string | null
  hasSecret: boolean
}

export type CreateDataSourceRequest = {
  name: string
  type: string
  machineCode: string
  verificationCategory: string
  schedulePeriod: string
  environment: string
  enabled: boolean
}

export type DatabaseConnection = {
  id: string
  name: string
  engine: string
  machineCode: string
  lineCode: string
  modelCode?: string | null
  host: string
  port: number
  databaseName: string
  username: string
  apiBaseUrl: string | null
  imageCrawlerApiUrl?: string | null
  imageCrawlerVpMode?: string | null
  health: string
  averageLatencyMs: number
  hasSecret: boolean
  credentialStatus: 'Protected' | 'Missing' | 'Unreadable' | string
  updatedAtUtc: string
}

export type CreateDatabaseConnectionRequest = {
  name: string
  engine: string
  machineCode: string
  lineCode: string
  modelCode?: string | null
  host?: string | null
  port?: number
  databaseName?: string | null
  username?: string | null
  password?: string | null
  apiBaseUrl?: string | null
  imageCrawlerApiUrl?: string | null
  imageCrawlerVpMode?: string | null
  apiKey?: string | null
}

export type BatchProvisionLineItem = {
  lineCode: string
  machineCode: string
  modelCode?: string | null
  host?: string | null
  databaseName?: string | null
  username?: string | null
  password?: string | null
}

export type BatchProvisionRequest = {
  engine?: string
  host?: string
  port?: number
  databaseName?: string
  username?: string | null
  password?: string | null
  lines?: BatchProvisionLineItem[] | null
  linePrefix?: string | null
  lineStart?: number | null
  lineEnd?: number | null
  linePad?: number
  machinePrefix?: string | null
  machineStart?: number | null
  machineEnd?: number | null
  machinePad?: number
  createPipelines?: boolean
  scheduleIntervalMinutes?: number
  lookbackHours?: number
  batchSize?: number
  productTableName?: string
  idColumnName?: string
  fkIdColumnName?: string
  judgeColumnName?: string
  judgeFilterValues?: string[] | null
  includeOkProducts?: boolean
  timestampColumnName?: string | null
  dateTimeInspectionColumnName?: string | null
  zoneColumnName?: string | null
  defectNameColumnName?: string | null
  defectTables?: DefectTableMapping[] | null
  offloadDefectsToSeaweedFs?: boolean
  bypassRule?: DefectBypassRule | null
  imageCrawlerApiUrl?: string | null
  autoCrawlImages?: boolean
  imageCrawlerVpMode?: string | null
  modelCode?: string | null
}

export type BatchProvisionResponse = {
  success: boolean
  createdConnectionsCount: number
  updatedConnectionsCount: number
  createdPipelinesCount: number
  updatedPipelinesCount: number
  totalItemsProcessed: number
  processedItems: string[]
  message: string
}

export type BatchGeneratePipelinesForConnectionsRequest = {
  connectionIds?: string[] | null
  scheduleIntervalMinutes?: number
  lookbackHours?: number
  batchSize?: number
  productTableName?: string
  idColumnName?: string
  fkIdColumnName?: string
  judgeColumnName?: string
  judgeFilterValues?: string[] | null
  includeOkProducts?: boolean
  timestampColumnName?: string | null
  dateTimeInspectionColumnName?: string | null
  zoneColumnName?: string | null
  defectNameColumnName?: string | null
  defectTables?: DefectTableMapping[] | null
  offloadDefectsToSeaweedFs?: boolean
  bypassRule?: DefectBypassRule | null
  imageCrawlerApiUrl?: string | null
  autoCrawlImages?: boolean
  imageCrawlerVpMode?: string | null
  modelCode?: string | null
}

// -------------------------------------------------------------
// Image Crawler Types (Per-PC / Per-Machine Image Fetching)
// -------------------------------------------------------------
export type CrawlDefectImageRequest = {
  defectId: string
  forceRefresh?: boolean
  customApiUrl?: string | null
  customVp?: string | null
}

export type CrawlDefectImageResponse = {
  success: boolean
  defectId: string
  imagePath?: string | null
  imageUrl?: string | null
  contentType?: string | null
  sizeBytes: number
  errorMessage?: string | null
  latencyMs: number
}

export type TestImageCrawlerApiRequest = {
  apiUrl: string
  imagePath: string
  method?: string
}

export type TestImageCrawlerApiResponse = {
  success: boolean
  statusCode: number
  contentType?: string | null
  sizeBytes: number
  base64Preview?: string | null
  errorMessage?: string | null
  latencyMs: number
}

export type BatchImageCrawlRequest = {
  defectIds?: string[] | null
  keyId?: string | null
  lineCode?: string | null
  machineCode?: string | null
  limit?: number
  forceRefresh?: boolean
}

export type BatchImageCrawlResponse = {
  success: boolean
  totalRequested: number
  crawledCount: number
  failedCount: number
  messages: string[]
}

export type TestDefectCrawlRequest = {
  defectId: string
  customApiUrl?: string | null
  customVp?: string | null
}

export type TestDefectCrawlResponse = {
  success: boolean
  defectId: string
  defectName: string
  keyId: string
  imagePath?: string | null
  apiUrlUsed: string
  requestPayload?: any
  statusCode: number
  contentType?: string | null
  sizeBytes: number
  base64Preview?: string | null
  errorMessage?: string | null
  latencyMs: number
}

export type TestPathCrawlRequest = {
  imagePath: string
  apiUrl?: string | null
  featureJson?: string | null
  vp?: string | null
}

export type TestPathCrawlResponse = {
  success: boolean
  imagePath: string
  apiUrlUsed: string
  requestPayload: any
  statusCode: number
  contentType?: string | null
  sizeBytes: number
  base64Preview?: string | null
  errorMessage?: string | null
  latencyMs: number
}

export type SampleDefectForTestDto = {
  id: string
  keyId: string
  defectName: string
  defectTable: string
  imagePath?: string | null
  createdAtUtc: string
}

export type ScanDirectoryPathsRequest = {
  rootPath: string
  searchPattern?: string | null
  maxDepth?: number
  defaultEngine?: string
  defaultHost?: string
  defaultPort?: number
  defaultDbPattern?: string
  defaultUsername?: string | null
  defaultPassword?: string | null
}

export type ScannedPathItem = {
  fullPath: string
  lineCode: string
  machineCode: string
  host: string
  databaseName: string
}

export type ScanDirectoryPathsResponse = {
  success: boolean
  rootPath: string
  totalFound: number
  items: ScannedPathItem[]
  errorMessage?: string | null
}

export type ConnectionTestResult = {
  sourceId: string
  sourceName: string
  success: boolean
  latencyMs: number
  message: string
}

export type ScanExecution = {
  id: string
  name: string
  scanListId: string
  scanListName: string
  status: string
  sourceNames: string[]
  totalItems: number
  passedItems: number
  warningItems: number
  failedItems: number
  technicalErrorItems: number
  anomalyItems: number
  evidenceUri: string | null
  startedAtUtc: string
  completedAtUtc: string
}

export type CreateScanExecutionRequest = {
  scanListId: string
  name: string
  sourceIds: string[]
}

export type VerificationResult = {
  id: string
  executionId: string
  entityId: string
  sourceName: string
  status: string
  severity: string
  ruleCode: string
  message: string
  anomalyScore: number
  completedAtUtc: string
}

export type ProductRecord = {
  id: string
  keyId: string
  judge: string
  panelId: string
  panelJudge: string
  nameDefect: string
  machineCode: string
  lineCode: string
  inspectedAtUtc: string
  metadataJson: string | null
  zone?: string | null
  defectName?: string | null
  defectPackUrl?: string | null
  opReviewMark?: string | null
  opReviewJudge?: string | null
  opReviewDefectName?: string | null
  opReviewMappedDefect?: string | null
  opReviewNote?: string | null
  opReviewedAtUtc?: string | null
}

export type ProductFilterOptions = {
  judges: string[]
  zones: string[]
  defectNames: string[]
  machineCodes: string[]
  lineCodes: string[]
}

export type DefectRecord = {
  id: string
  keyId: string
  defectTable: string
  defectName: string
  defectType: string
  componentName: string | null
  coordinateX: number | null
  coordinateY: number | null
  severity: string
  judge: string
  confidenceScore: number
  imageUri: string | null
  feature1StorageUri: string | null
  feature2StorageUri: string | null
  createdAtUtc: string
  featureJson?: string | null
  customFeatureJson?: string | null
  reason?: string | null
  eqpName?: string | null
  emNo?: string | null
  specName?: string | null
  specJudge?: string | null
  specReason?: string | null
  priority?: number | null
  featureUrl?: string | null
  customFeatureUrl?: string | null
  imageUrl?: string | null
  modelCode?: string | null
  connectionId?: string | null
  machineCode?: string | null
  lineCode?: string | null
}

export type ProductScanItem = {
  product: ProductRecord
  defects: DefectRecord[]
  totalDefects: number
  anomalyStatus: string
  anomalyScore: number
  analysisNotes: string
}

export type MultiTableScanRequest = {
  connectionId?: string | null
  keyIds?: string[] | null
  panelId?: string | null
  machineCode?: string | null
  lineCode?: string | null
  includeDefects?: boolean
}

export type MultiTableScanResponse = {
  scannedProductsCount: number
  totalDefectsFound: number
  overkillCount: number
  leakageCount: number
  items: ProductScanItem[]
  executionLatencyMs: number
}

export type OverkillCandidate = {
  keyId: string
  judge: string
  panelId: string
  panelJudge: string
  nameDefect: string
  machineCode: string
  lineCode: string
  inspectedAtUtc: string
  zone?: string | null
  defectName?: string | null
}

export type CreateProductRecordRequest = {
  keyId: string
  judge: string
  panelId: string
  panelJudge: string
  nameDefect: string
  machineCode: string
  lineCode: string
  zone?: string | null
  defectName?: string | null
}

export type CreateDefectRecordRequest = {
  keyId: string
  defectTable: string
  defectName: string
  defectType: string
  componentName?: string | null
  coordinateX?: number | null
  coordinateY?: number | null
  severity: string
  judge: string
  confidenceScore: number
  imageUri?: string | null
  featureJson?: string | null
  customFeatureJson?: string | null
  modelCode?: string | null
  connectionId?: string | null
  machineCode?: string | null
  lineCode?: string | null
}

export type ProductQuery = {
  id: string
  queryKey: string
  productId: string
  sourceSystem: string
  status: string
  payloadJson: string
  judgeResult: string | null
  message: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export type CreateProductQueryRequest = {
  queryKey: string
  productId: string
  payloadJson: string
  sourceSystem?: string | null
}

export type UpdateProductQueryStatusRequest = {
  status: string
  judgeResult?: string | null
  message?: string | null
}

export type QueryProfile = {
  id: string
  name: string
  dataSourceId: string | null
  connectionName: string | null
  machineCode: string
  lineCode: string
  verificationCategory: string
  concept: string
  sourceSystem: string
  sqlTemplate: string
  parameterSchemaJson: string
  timeoutSeconds: number
  maxConcurrency: number
  enabled: boolean
  notes: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export type CreateQueryProfileRequest = {
  name: string
  dataSourceId?: string | null
  machineCode?: string | null
  lineCode?: string | null
  concept: string
  sourceSystem: string
  sqlTemplate: string
  parameterSchemaJson: string
  timeoutSeconds?: number
  maxConcurrency?: number
  notes?: string | null
  verificationCategory?: string
}

export type RenderedQuery = {
  queryProfileId: string
  renderedSql: string
  missingParameters: string[]
  warnings: string[]
}

export type QueryTestResponse = {
  queryProfileId: string
  success: boolean
  rowCount: number
  rows: Record<string, unknown>[]
  message: string
  latencyMs: number
}

export type StoredObject = {
  id: string
  backend: string
  bucket: string
  objectName: string
  contentType: string
  sizeBytes: number
  uri: string
  createdAtUtc: string
}

export type UploadJsonRequest = {
  bucket: string
  objectName: string
  payload: unknown
}

export type SeaweedFsConfig = {
  masterUrl: string
  filerUrl: string
  publicBaseUrl: string
  defaultBucket: string
  offloadDefects: boolean
  localFallbackPath?: string
}

export type StorageComponentCheck = {
  name: string
  reachable: boolean
  responseTimeMs: number
  details: string
  statusCode?: number | null
}

export type StorageTestConnectionResult = {
  success: boolean
  statusMessage: string
  master: StorageComponentCheck
  filer: StorageComponentCheck
  writeReadDeleteTest: StorageComponentCheck
  diagnostics: Record<string, string>
}

export type DefectStoragePreviewItem = {
  id: string
  keyId: string
  productId?: string | null
  productName?: string | null
  defectName: string
  defectTable: string
  judge: string
  severity: string
  hasInlineFeatureJson: boolean
  featureJsonBytes: number
  featureUrl?: string | null
  customFeatureUrl?: string | null
  createdAtUtc: string
}

export type MigrateSingleDefectResult = {
  defectId: string
  keyId: string
  success: boolean
  featureUrl?: string | null
  customFeatureUrl?: string | null
  bytesOffloaded: number
  message: string
}

export type MigrateBatchDefectsResult = {
  totalProcessed: number
  successCount: number
  failedCount: number
  bytesOffloaded: number
  details: MigrateSingleDefectResult[]
}

export type StorageShardingInfo = {
  shardingStrategy: string
  samplePath: string
  bundleSamplePath: string
  tiers: number
  bitsPerTier: number
  maxFoldersPerTier: number
}

export type MigrateBundleItem = {
  keyId: string
  success: boolean
  defectsCount: number
  bundlePath?: string | null
  bundleUri?: string | null
  originalBytes: number
  compressedBytes: number
  compressionRatio: number
  errorMessage?: string | null
}

export type MigrateBundlesResult = {
  totalProductsProcessed: number
  totalDefectsOffloaded: number
  successfulBundlesCount: number
  failedBundlesCount: number
  totalOriginalBytes: number
  totalCompressedBytes: number
  overallCompressionRatio: number
  bundles: MigrateBundleItem[]
}

export type MigratePackItem = {
  keyId: string
  defectsCount: number
  success: boolean
  packUrl?: string | null
  message: string
}

export type MigratePacksResult = {
  totalKeyIdsProcessed: number
  totalDefectsProcessed: number
  successCount: number
  failedCount: number
  packs: MigratePackItem[]
}

export type CreateDefectPreviewSessionRequest = {
  storageUri?: string
  sourceUri?: string
  keyId?: string | null
  ttlMinutes?: number
  format?: string | null
  maxRows?: number | null
}

export type DefectPreviewSession = {
  sessionId: string
  status: 'Pending' | 'Ingesting' | 'Ready' | 'Failed' | 'Expired' | string
  sourceUri: string
  createdAtUtc: string
  expiresAtUtc: string
  keyId?: string | null
  processedRecords: number
  totalRecords: number
  detectedFormat?: string | null
  processingStage?: string | null
  warningMessage?: string | null
}

export type DefectPreviewFilterSummary = {
  total: number
  ngCount: number
  okCount: number
  severityBreakdown: Record<string, number>
}

export type DefectPreviewProgress = {
  sessionId: string
  status: 'Pending' | 'Ingesting' | 'Ready' | 'Failed' | 'Expired' | string
  processedRecords: number
  totalRecords: number
  percentComplete: number
  filterSummary?: DefectPreviewFilterSummary | null
  errorMessage?: string | null
  detectedFormat?: string | null
  processingStage?: string | null
  warningMessage?: string | null
}

export type DefectPreviewRecord = {
  id: number
  rowIndex: number
  defectId?: string | null
  keyId?: string | null
  defectName?: string | null
  defectTable?: string | null
  defectType?: string | null
  componentName?: string | null
  coordinateX?: number | null
  coordinateY?: number | null
  severity?: string | null
  judge?: string | null
  confidenceScore?: number | null
  hasFeatureJson: boolean
  featureJsonSnippet?: string | null
  rawFeatureUri?: string | null
  rawImageUri?: string | null
  inspectedAtUtc?: string | null
}

export type DefectPreviewPagedResponse = {
  items: DefectPreviewRecord[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export type DefectPreviewQueryParams = {
  page?: number
  pageSize?: number
  sortBy?: string
  sortDir?: 'asc' | 'desc'
  sortDirection?: 'asc' | 'desc'
  keyword?: string
  search?: string
  judge?: string
  severity?: string
  defectTable?: string
}

export type AuditEvent = {
  id: string
  occurredAtUtc: string
  actor: string
  action: string
  targetType: string
  targetId: string
  message: string
}

export type SystemHealth = {
  status: string
  checkedAtUtc: string
  service: string
  version: string
  storageMode: string
}

export type Paged<T> = {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export type ScanListItem = {
  rowNumber: number
  entityId: string
  lineCode: string
  mappingInt: number | null
  mapping: string | null
}

export type VerifyBatchRequest = {
  scanListId?: string | null
  connectionId?: string | null
  queryProfileId?: string | null
  rawText?: string | null
  machineCode?: string | null
  lineCode?: string | null
  mappingName?: string
}

export type VerificationHistory = {
  id: string
  category: string
  entityId: string
  lineCode: string
  mapping: string | null
  machineCode: string
  judge: string
  panelJudge: string
  nameDefect: string
  anomalyStatus: string
  anomalyScore: number
  analysisNotes: string
  totalDefects: number
  eligible: boolean
  scanListId: string | null
  queryProfileId: string | null
  connectionId: string | null
  connectionName: string | null
  queryName: string | null
  createdAtUtc: string
}

export type OverkillSystemShare = {
  systemName: string
  defectType: string
  totalDefects: number
  ngDefects: number
  ngShare: number
  linkedNgProducts: number
  overkillSuspects: number
  guidance: string
  sampleKeyIds: string[]
}

export type OverkillAnalysis = {
  machineCode: string
  lineCode: string
  eligibleNgProducts: number
  totalDefects: number
  systems: OverkillSystemShare[]
  recommendations: string[]
}

// -------------------------------------------------------------
// Classifier-Driven Overkill, Real NG, Leakage & Thumbnail Types
// -------------------------------------------------------------

export type OverkillTrendSummary = {
  totalInspected: number
  stationNgCount: number
  realNgCount: number
  overkillCount: number
  naCount: number
  leakageCount: number
  consistentOkCount: number
  opReviewedCount: number
  opRealDefectCount: number
  opOverkillCount: number
  stationNgRate: number
  realNgRate: number
  overkillRate: number
  overkillShare: number
  leakageRate: number
}

export type OverkillTrendPoint = {
  timestamp: string
  dateLabel: string
  totalInspected: number
  stationNgCount: number
  realNgCount: number
  overkillCount: number
  naCount: number
  leakageCount: number
  stationNgRate: number
  realNgRate: number
  overkillRate: number
  overkillShare: number
}

export type DefectFamilyOverkill = {
  defectFamily: string
  totalDefects: number
  realNgCount: number
  overkillCount: number
  overkillShare: number
  activeRuleCode?: string | null
  topDefectNames: string[]
  meanConfidence?: number | null
  meanArea?: number | null
}

export type DefectClassOverkill = {
  defectName: string
  defectFamily: string
  totalDefects: number
  realNgCount: number
  overkillCount: number
  overkillShare: number
  ruleCode?: string | null
  evaluationGuidance: string
}

export type LineOverkillComparison = {
  lineCode: string
  machineCode: string
  totalInspected: number
  okCount: number
  stationNgCount: number
  stationNgRate: number
  realNgCount: number
  realNgRate: number
  overkillCount: number
  naCount: number
  overkillRate: number
  overkillShare: number
  leakageCount: number
  opMappedCount: number
  opMappedRate: number
  riskLevel: string
  statusAssessment: string
}

export type OverkillTrendResponse = {
  machineCode: string
  lineCode: string
  defectFamily: string
  summary: OverkillTrendSummary
  trendPoints: OverkillTrendPoint[]
  familyBreakdown: DefectFamilyOverkill[]
  classBreakdown: DefectClassOverkill[]
  lineComparison: LineOverkillComparison[]
  recommendations: string[]
}

export type AnomalyThumbnailItem = {
  keyId: string
  panelId?: string | null
  defectId?: string | null
  productId: string
  stationJudge: string
  anomalyCategory: string // "Overkill", "Leakage", "RealNG", "ConsistentOK"
  anomalyScore: number
  machineCode: string
  lineCode: string
  modelCode: string
  stationCode?: string | null
  inspectedAtUtc: string
  defectName?: string | null
  defectFamily?: string | null
  defectType?: string | null
  severity?: string | null
  componentName?: string | null
  coordinates?: string | null
  confidenceScore?: number | null
  thumbnailUrl?: string | null
  imageUri?: string | null
  ruleCode?: string | null
  ruleVersion?: string | null
  ruleVerdict?: string | null
  keyFeatures?: Record<string, number> | null
  decisionReason: string
  originalVp?: string | null
  opReviewMark?: string | null
  productNameDefect?: string | null
  productDefectName?: string | null
  aiVerdict?: string | null
  aiStatus?: string | null
  aiAnomalyScore?: number | null
  decisionRoute?: string | null
}

export type AnomalyThumbnailQueryRequest = {
  category?: string
  defectFamily?: string
  machineCode?: string
  lineCode?: string
  modelCode?: string
  defectCluster?: string
  search?: string
  fromUtc?: string
  toUtc?: string
  page?: number
  pageSize?: number
}

// -------------------------------------------------------------
// OP Defect Mapping & Excel Reconciliation Types
// -------------------------------------------------------------

export type OpDefectMappingDto = {
  id: string
  machineCode: string
  packageName: string
  opDefectName: string
  machineDefectName: string
  defectFamily: string
  componentGroup: string
  isConfirmedDefect: boolean
  description?: string | null
  createdAtUtc: string
  updatedAtUtc?: string | null
}

export type SaveOpDefectMappingRequest = {
  machineCode?: string
  packageName?: string
  opDefectName: string
  machineDefectName: string
  defectFamily?: string
  componentGroup?: string
  isConfirmedDefect: boolean
  description?: string | null
}

export type ClsfRuleOccurrence = {
  priority: number
  ruleName: string
  defectTypeId?: string | null
  isOccurring: boolean
  occurrenceCount: number
  occurrencePercent: number
  vpScope?: string | null
  script?: string | null
}

export type ClsfPackageAnalysisResponse = {
  success: boolean
  fileName: string
  packageName: string
  machineCode: string
  modelCode: string
  totalRules: number
  activeRuleCount: number
  totalDefectOccurrences: number
  rules: ClsfRuleOccurrence[]
  message: string
}

export type UploadOpExcelResponse = {
  success: boolean
  batchId: string
  totalRecordsParsed: number
  totalRecordsSaved: number
  createdCount: number
  updatedCount: number
  rejectedCount: number
  mappedCount: number
  unmappedCount: number
  mappingRulesImported: number
  sheetNames: string[]
  message: string
}

export type PasteOpRecordsRequest = {
  text: string
  machineCode?: string
  lineCode?: string
  defaultJudge?: string
}

export type OpReconciliationSummary = {
  totalReconciled: number
  totalMachineNg: number
  realDefectCount: number
  overkillCount: number
  leakageCount: number
  consistentOkCount: number
  noDetectCount: number
  realDefectRate: number
  overkillRate: number
  leakageRate: number
}

export type OpDefectReconciliationItem = {
  machineDefectName: string
  defectFamily: string
  totalMachineNg: number
  realDefectCount: number
  overkillCount: number
  realDefectRate: number
  overkillRate: number
  associatedOpDefectNames: string[]
  overkillSeverityAssessment: string
}

export type OverkillFeatureDistribution = {
  featureName: string
  overkillMean: number
  overkillMin: number
  overkillMax: number
  realDefectMean: number
  realDefectMin: number
  realDefectMax: number
  separationAssessment: string
}

export type OverkillSample = {
  keyId: string
  machineDefectName: string
  defectFamily: string
  opDefectName?: string | null
  opJudge: string
  inspectedAtUtc: string
  lineCode?: string | null
  machineCode?: string | null
  keyFeatures?: Record<string, number> | null
  thumbnailUrl?: string | null
  reason: string
}

export type OverkillDeepDiveAnalysis = {
  targetDefectName: string
  overkillCount: number
  realDefectCount: number
  overkillRate: number
  featureDistributions: OverkillFeatureDistribution[]
  thresholdTuningRecommendations: string[]
  samples: OverkillSample[]
}

export type OpReconciliationAnalysisResponse = {
  summary: OpReconciliationSummary
  defectBreakdown: OpDefectReconciliationItem[]
  deepDive?: OverkillDeepDiveAnalysis | null
  availableDefectTypes: string[]
  recommendations: string[]
}

export type OpReconciliationRecord = {
  id: string
  batchId: string
  keyId: string
  opJudge: string
  opDefectName?: string | null
  mappedMachineDefect?: string | null
  mappedDefectFamily?: string | null
  machineCode?: string | null
  lineCode?: string | null
  opInspectedAtUtc: string
  productFound: boolean
  productJudge?: string | null
  productNameDefect?: string | null
  productDefectName?: string | null
  defects: OpReconciliationDefect[]
  productMachineCode?: string | null
  productLineCode?: string | null
  productInspectedAtUtc?: string | null
  classification: 'Real Defect' | 'Overkill' | 'Leakage' | 'Consistent OK' | 'No Detect' | 'Not Found'
}

export type OpReconciliationDefect = {
  id: string
  defectName: string
  defectType: string
  defectTable: string
  severity?: string | null
}

export type OpReconciliationRecordsResponse = {
  items: OpReconciliationRecord[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  matchedCount: number
  notFoundCount: number
  realDefectCount: number
  overkillCount: number
  leakageCount: number
  consistentOkCount: number
  noDetectCount: number
  realDefectRate: number
  overkillRate: number
}

export type VerifyBatchResponse = {
  category: string
  totalItems: number
  eligibleItems: number
  skippedItems: number
  overkillCount: number
  leakageCount: number
  consistentNgCount: number
  scanListId: string | null
  connectionId: string | null
  connectionName: string | null
  items: VerificationHistory[]
}

export type DefectTableMapping = {
  tableName: string
  displayName?: string | null
  fkColumnName: string
  defectNameColumn: string
  defectTypeColumn?: string | null
  severityColumn?: string | null
  judgeColumn?: string | null
  componentNameColumn?: string | null
  coordXColumn?: string | null
  coordYColumn?: string | null
  imageUriColumn?: string | null
  feature1JsonColumn?: string | null
  feature2JsonColumn?: string | null
  featureColumn?: string
  customFeatureColumn?: string
  reasonColumn?: string
  eqpNameColumn?: string
  emnoColumn?: string
  specNameColumn?: string
  specJudgeColumn?: string
  specReasonColumn?: string
  priorityColumn?: string
  offloadToStorage?: boolean
}

export type DefectBypassCondition = {
  field: string
  operator: string
  value?: string | null
  values?: string[] | null
  caseInsensitive?: boolean
}

export type DefectBypassNameCondition = {
  field: string
  operator: string
  values: string[]
  caseInsensitive?: boolean
}

export type DefectBypassRule = {
  enabled: boolean
  conjunction: 'AND' | 'OR'
  noDefectCondition?: DefectBypassCondition | null
  nameCondition?: DefectBypassNameCondition | null
  caseInsensitive?: boolean
  nullPolicy?: 'PreserveRow' | 'MatchAsNoDefect' | 'Strict'
}

export type OverkillPipelineConfig = {
  id: string
  name: string
  databaseConnectionId: string
  connectionName: string
  machineCode: string
  lineCode: string
  enabled: boolean
  scheduleIntervalMinutes: number
  lastRunAtUtc: string | null
  nextRunAtUtc: string | null
  lastRunStatus: string
  lastRunMessage: string | null
  lastRunProductsCount: number
  lastRunDefectsCount: number
  lastRunDefectsBypassedCount?: number
  productTableName: string
  idColumnName: string
  fkIdColumnName: string
  judgeColumnName: string
  judgeFilterValues: string[]
  includeOkProducts?: boolean
  timestampColumnName: string | null
  zoneColumnName?: string
  defectNameColumnName?: string
  dateTimeInspectionColumnName?: string
  inspectedAtColumnName?: string
  lookbackHours: number
  batchSize: number
  offloadDefectsToSeaweedFs?: boolean
  defectTables: DefectTableMapping[]
  bypassRule?: DefectBypassRule | null
  defectBypassRule?: DefectBypassRule | null
  imageCrawlerApiUrl?: string | null
  autoCrawlImages?: boolean
  imageCrawlerVpMode?: string | null
  modelCode?: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export type SaveOverkillPipelineConfigRequest = {
  id?: string
  name: string
  databaseConnectionId: string
  machineCode?: string
  lineCode?: string
  enabled: boolean
  scheduleIntervalMinutes: number
  productTableName: string
  idColumnName: string
  fkIdColumnName: string
  judgeColumnName: string
  judgeFilterValues: string[]
  includeOkProducts?: boolean
  timestampColumnName?: string | null
  zoneColumnName?: string
  defectNameColumnName?: string
  dateTimeInspectionColumnName?: string
  inspectedAtColumnName?: string
  lookbackHours: number
  batchSize: number
  offloadDefectsToSeaweedFs?: boolean
  defectTables: DefectTableMapping[]
  bypassRule?: DefectBypassRule | null
  defectBypassRule?: DefectBypassRule | null
  imageCrawlerApiUrl?: string | null
  autoCrawlImages?: boolean
  imageCrawlerVpMode?: string | null
  modelCode?: string | null
}

export type OverkillPipelineExecution = {
  id: string
  configId: string
  pipelineName: string
  triggerType: string
  startedAtUtc: string
  completedAtUtc: string | null
  status: string
  productsScanned: number
  defectsExtracted: number
  defectsBypassed?: number
  bypassRuleSummary?: string | null
  overkillCandidatesCount: number
  latencyMs: number
  errorMessage: string | null
  executionSummaryJson?: string | null
}

export type TestPipelineMappingRequest = {
  databaseConnectionId: string
  productTableName: string
  idColumnName: string
  fkIdColumnName: string
  judgeColumnName: string
  judgeFilterValues?: string[] | null
  defectTables: DefectTableMapping[]
  sampleLimit?: number
  defectSampleLimit?: number
  zoneColumnName?: string
  defectNameColumnName?: string
  dateTimeInspectionColumnName?: string
  inspectedAtColumnName?: string
  bypassRule?: DefectBypassRule | null
}

export type TestPipelineMappingResponse = {
  success: boolean
  sampleProductsCount: number
  sampleProducts: Record<string, unknown>[]
  defectCountsByTable: Record<string, number>
  sampleDefects: Record<string, unknown>[]
  latencyMs: number
  errorMessage: string | null
  bypassedDefectsCount?: number
  bypassRuleSummary?: string | null
  scannedDefectsCount?: number
  acceptedDefectsCount?: number
  generatedDefectSql?: string | null
  alreadySavedProductsCount?: number
}

export type OverkillRunResult = {
  executionId: string
  configId: string
  status: string
  message: string
  productsScanned: number
  defectsExtracted: number
  overkillCandidatesCount: number
}

export type LeakageQueueItem = {
  id: string
  batchId: string
  productId: string
  lineCode: string | null
  status: string
  resolvedFkId: string | null
  stationJudge: string | null
  anomalyScore: number
  isLeakageSuspect: boolean
  defectCount: number
  connectionId: string | null
  tableScanned: string | null
  errorMessage: string | null
  retryCount: number
  createdAtUtc: string
  processedAtUtc: string | null
}

export type LeakageBatch = {
  id: string
  name: string
  sourceType: string
  rawInputSummary: string | null
  totalCount: number
  pendingCount: number
  processingCount: number
  completedCount: number
  notFoundCount: number
  leakageCount: number
  consistentCount: number
  failedCount: number
  createdBy: string
  createdAtUtc: string
  processedAtUtc: string | null
}

export type LeakageQueueItemDetail = {
  queueItem: LeakageQueueItem
  product: ProductRecord | null
  defects: DefectRecord[]
}

export type EnqueuePasteRequest = {
  text: string
  batchName?: string | null
  defaultLineCode?: string | null
}

export type ProcessLeakageQueueRequest = {
  batchId?: string | null
  itemIds?: string[] | null
  maxDegreeOfParallelism?: number | null
}

export type LeakageQueueProgress = {
  isRunning: boolean
  currentBatchId: string | null
  processedCount: number
  totalCount: number
  pendingCount: number
  leakageCount: number
  notFoundCount: number
  failedCount: number
}

export type RetryQueueItemsRequest = {
  batchId?: string | null
  itemIds?: string[] | null
}

export type ClearQueueRequest = {
  batchId?: string | null
  itemIds?: string[] | null
  clearCompletedOnly?: boolean
}

export type TabId =
  | 'overview'
  | 'import'
  | 'overkill-ops'
  | 'op-input'
  | 'leakage-ops'
  | 'scans'
  | 'products'
  | 'overkill'
  | 'feature-trends'
  | 'queries'
  | 'history'
  | 'sources'
  | 'classifier-rules'
  | 'storage'
  | 'audit'
  | 'ai-verification'

export type ProductionFeaturePoint = {
  inspectedAtUtc: string
  value: number
  defectId: string
  keyId: string
  defectName: string
  judge: string
}

export type ProductionFeatureSeries = {
  featureName: string
  sampleCount: number
  min: number
  max: number
  mean: number
  points: ProductionFeaturePoint[]
}

export type ProductionFeatureJob = {
  jobId: string
  status: string
  period: string
  machineCode: string | null
  lineCode: string | null
  fromUtc: string
  toUtc: string
  totalDefects: number
  processedDefects: number
  loadedPacks: number
  featureValueCount: number
  featuresWithValues: number
  progress: number
  message: string
  machines: string[]
  lines: string[]
  defectNames: string[]
  judges: string[]
  series: ProductionFeatureSeries[]
}

export type ProductHandoff = {
  keyId: string
  sourceUri?: string
}

export type ActivePreviewSessionState = {
  sessionId: string
  sourceUri?: string
  keyId?: string | null
  initialSession?: DefectPreviewSession | null
}

// AI verification (Master Server / Deeplearning)
export type MasterConnectionStatus = { reachable: boolean; authorized: boolean; message: string }

export type AiMapping = {
  id: string
  machineCode: string
  lineCode: string
  modelCode: string
  stationId: string
  productId: string
  enabled: boolean
  autoSubmit: boolean
  updatedAtUtc: string
}

export type AiMappingInput = Omit<AiMapping, 'id' | 'updatedAtUtc'>

export type AiSubmitResult = {
  submitted: number
  alreadyJudged: number
  noMapping: number
  noImage: number
  message?: string | null
  deferred: number
  transient: boolean
}

export type AiFinalStatus =
  | 'OverkillConfirmed'
  | 'LeakageConfirmed'
  | 'ConsistentNg'
  | 'ConsistentOk'
  | 'AiUnavailable'
  | 'NoImages'
  | 'Pending'

export type AiProductSummary = {
  keyId: string
  aiStatus: AiFinalStatus
  done: number
  ng: number
  ok: number
  pending: number
  unavailable: number
  maxAnomalyScore?: number | null
}

export type AiDefectResult = {
  defectId: string
  status: string
  verdict?: string | null
  anomalyScore?: number | null
  defectClass?: string | null
  clientMode?: string | null
  error?: string | null
}

export type AiProductResult = { keyId: string; judge: string; aiStatus: AiFinalStatus; defects: AiDefectResult[] }

export type AiVerificationStatus = {
  configured: boolean
  enabled: boolean
  autoSubmit: boolean
  pollIntervalSeconds: number
  maxProductsPerTick: number
  lookbackHours: number
  dispatch: {
    lastRunAtUtc?: string | null
    lastSubmitAtUtc?: string | null
    lastPicked: number
    lastSubmitted: number
    lastError?: string | null
    pausedUntilUtc?: string | null
  }
  last24h: { submitted: number; done: number; rejected: number; unavailable: number }
}

export type NoticeTone = 'success' | 'error' | 'info'
export type Notice = { tone: NoticeTone; message: string }

// Defect Feature Analytics Contracts & Engines
export type AnalyticsJobType =
  | 'Spc'
  | 'Ewma'
  | 'Cusum'
  | 'Pca'
  | 'Msa'
  | 'Doe'
  | 'SPC'
  | 'EWMA'
  | 'CUSUM'
  | 'PCA'
  | 'MSA'
  | 'DOE'
  | 'FEATURE_RELATIONSHIP'
  | 'KMEANS'
  | 'GMM'
  | 'ISOLATION_FOREST'
  | 'SCRIPT_CONDITION'

export type AnalyticsJobStatus =
  | 'Queued'
  | 'Computing'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'

export type ObservationReference = {
  observationId: string
  rowIndex: number
  defectId?: string | null
  keyId?: string | null
  lineCode?: string | null
  machineCode?: string | null
  stationCode?: string | null
  classifierRuleCode?: string | null
  classifierRuleVersion?: string | null
  defectFamily?: string | null
  defectClass?: string | null
  judge?: string | null
  inspectedAtUtc?: string | null
}

export type FeatureSummary = {
  name: string
  count: number
  missingCount: number
  missingRatio: number
  min: number | null
  max: number | null
  mean: number | null
  stdDev: number | null
}

export type AnalysisReadiness = {
  analysisType: string
  isReady: boolean
  readinessScore: number
  recommendedFeatures: string[]
  blockers: string[]
  warnings: string[]
}

export type FeatureSchemaReadinessResponse = {
  sessionId: string
  totalRecords: number
  isNgOnlyDataset: boolean
  features: FeatureSummary[]
  readiness: Record<string, AnalysisReadiness>
  detectedRuleVersions?: string[] | null
  scopeWarnings?: string[] | null
}

export type FacetItem = {
  value: string
  count: number
}

export type SessionScopeFacetsResponse = {
  sessionId: string
  totalRecords: number
  lines: FacetItem[]
  machines: FacetItem[]
  stations: FacetItem[]
  classifierRules: FacetItem[]
  classifierRuleVersions: FacetItem[]
  defectFamilies: FacetItem[]
  defectTypes: FacetItem[]
  features: FacetItem[]
}

export type AnalyticsScopeFilter = {
  productKey?: string | null
  lineCode?: string | null
  machineCode?: string | null
  stationCode?: string | null
  classifierRuleCode?: string | null
  classifierRuleVersion?: string | null
  defectFamily?: string | null
  defectClass?: string | null
  defectType?: string | null
  fromUtc?: string | null
  toUtc?: string | null
  timeFromUtc?: string | null
  timeToUtc?: string | null
}

export type CreateAnalyticsJobRequest = {
  analysisType: AnalyticsJobType | string
  featureName?: string | null
  featureNames?: string[] | null
  partField?: string | null
  operatorField?: string | null
  responseFeature?: string | null
  factorFeatures?: string[] | null
  subgroupIdField?: string | null
  parameters?: Record<string, unknown> | null
  ttlMinutes?: number
  maxDataPoints?: number

  // Optional Scope Filters
  scopeFilter?: AnalyticsScopeFilter | null
  productKey?: string | null
  lineCode?: string | null
  machineCode?: string | null
  stationCode?: string | null
  classifierRuleCode?: string | null
  classifierRuleVersion?: string | null
  defectFamily?: string | null
  defectType?: string | null
  timeFromUtc?: string | null
  timeToUtc?: string | null

  // Preprocessing & Seed Options
  seed?: number | null
  missingValuePolicy?: string | null // CompleteCase, PairwiseComplete, MedianImpute, RejectMissing
  scalingPolicy?: string | null // None, Standard, Robust

  // Algorithm-specific options
  k?: number | null
  componentsCount?: number | null
  covarianceType?: string | null
  treesCount?: number | null
  subsampleSize?: number | null
  contamination?: number | null
  maxScatterPoints?: number | null
  allowRuleVersionMixing?: boolean | null

  // Script Condition Analysis
  scriptText?: string | null
  maxEvaluationRows?: number | null
}

export type AnalyticsJob<T = unknown> = {
  jobId: string
  id?: string
  sessionId: string
  analysisType: AnalyticsJobType
  status: AnalyticsJobStatus
  progressPercent: number
  requestHash: string
  createdAtUtc: string
  startedAtUtc?: string | null
  completedAtUtc?: string | null
  expiresAtUtc?: string | null
  errorMessage?: string | null
  warnings: string[]
  result?: T | null
}

// 1. SPC & Nelson Rules
export type NelsonRuleViolation = {
  index: number
  ruleId: number
  ruleName: string
  description: string
  severity: string
}

export type SpcPoint = {
  rowIndex: number
  value: number
  subgroupId: string
  movingRange?: number | null
  violatingRuleIds: number[]
}

export type SpcChartSeries = {
  name: string
  centerLine: number
  upperControlLimit: number
  lowerControlLimit: number
  sigma: number
  points: SpcPoint[]
}

export type ProcessCapabilityResult = {
  cp?: number | null
  cpk?: number | null
  pp?: number | null
  ppk?: number | null
  cpu?: number | null
  cpl?: number | null
  ppu?: number | null
  ppl?: number | null
  lsl?: number | null
  usl?: number | null
  target?: number | null
  sigmaWithin: number
  sigmaOverall: number
  isValid: boolean
  warningMessage?: string | null
}

export type SpcResult = {
  chartType: string
  featureName: string
  totalSamples: number
  subgroupCount: number
  primaryChart?: SpcChartSeries | null
  secondaryChart?: SpcChartSeries | null
  capability?: ProcessCapabilityResult | null
  violations: NelsonRuleViolation[]
  warnings: string[]
}

// 2. EWMA & CUSUM Time Series
export type EwmaPoint = {
  rowIndex: number
  rawValue: number
  ewmaValue: number
  ucl: number
  lcl: number
  isViolation: boolean
}

export type EwmaResult = {
  featureName: string
  lambda: number
  l: number
  target: number
  sigma: number
  asymptoticUcl: number
  asymptoticLcl: number
  totalSamples: number
  violationCount: number
  points: EwmaPoint[]
}

export type CusumPoint = {
  rowIndex: number
  rawValue: number
  cusumHigh: number
  cusumLow: number
  runLengthHigh: number
  runLengthLow: number
  isViolationHigh: boolean
  isViolationLow: boolean
}

export type CusumResult = {
  featureName: string
  target: number
  sigma: number
  k: number
  h: number
  totalSamples: number
  violationHighCount: number
  violationLowCount: number
  points: CusumPoint[]
}

// 3. PCA & Hotelling T2 / SPE Q
export type PcaPoint = {
  rowIndex: number
  pc1: number
  pc2: number
  hotellingT2: number
  speQ: number
  isHotellingOutlier: boolean
  isSpeOutlier: boolean
}

export type PcaComponentSummary = {
  componentIndex: number
  name: string
  eigenvalue: number
  explainedVarianceRatio: number
  cumulativeVarianceRatio: number
  featureLoadings: Record<string, number>
}

export type PcaResult = {
  featureNames: string[]
  totalSamples: number
  retainedComponents: number
  confidenceLevel: number
  hotellingT2Threshold: number
  speQThreshold: number
  components: PcaComponentSummary[]
  sampledPoints: PcaPoint[]
  totalHotellingOutliers: number
  totalSpeOutliers: number
  warnings: string[]
}

export type ProductionFeatureAnalysis = {
  jobId: string
  analysisType: string
  featureName: string
  featureNames: string[]
  spc?: SpcResult | null
  ewma?: EwmaResult | null
  cusum?: CusumResult | null
  pca?: PcaResult | null
}

// 4. MSA / Gage R&R
export type MsaAnovaRow = {
  source: string
  degreesOfFreedom: number
  sumOfSquares: number
  meanSquare: number
  fStatistic?: number | null
  pValue?: number | null
}

export type MsaVarianceComponent = {
  source: string
  variance: number
  standardDeviation: number
  studyVariation: number
  percentContribution: number
  percentStudyVar: number
  percentTolerance?: number | null
}

export type MsaResult = {
  featureName: string
  partCount: number
  operatorCount: number
  repeatCount: number
  totalMeasurements: number
  isBalanced: boolean
  interactionSignificant: boolean
  wasInteractionPooled: boolean
  numberOfDistinctCategories: number
  statusAssessment: string
  anovaTable: MsaAnovaRow[]
  varianceComponents: MsaVarianceComponent[]
  blockers: string[]
  warnings: string[]
}

// 5. DOE (Factorial Analysis & Pareto)
export type DoeFactorEffect = {
  term: string
  termType: string
  coefficient: number
  standardError: number
  tStatistic: number
  pValue: number
  standardizedEffectMagnitude: number
  isStatisticallySignificant: boolean
}

export type DoeModelSummary = {
  r2: number
  adjustedR2: number
  rootMeanSquareError: number
  sumOfSquaresModel: number
  sumOfSquaresResidual: number
  sumOfSquaresTotal: number
  dfModel: number
  dfResidual: number
  dfTotal: number
  fStatistic: number
  pValue: number
}

export type DoeResidualPoint = {
  rowIndex: number
  actual: number
  fitted: number
  residual: number
}

export type DoeResult = {
  responseName: string
  factorNames: string[]
  totalRuns: number
  isUnreplicatedSaturated: boolean
  significanceThreshold: number
  summary: DoeModelSummary
  effectsPareto: DoeFactorEffect[]
  residualDiagnostics: DoeResidualPoint[]
  warnings: string[]
}

// 6. Feature Relationship Contracts
export type FeatureRelationshipPair = {
  featureA: string
  featureB: string
  pearson?: number | null
  spearman?: number | null
  overlapCount: number
  pValue?: number | null
  strength: string
}

export type RelationshipScatterPoint = {
  observation: ObservationReference
  x: number
  y: number
}

export type FeatureRelationshipResult = {
  features: string[]
  pearsonMatrix: (number | null)[][]
  spearmanMatrix: (number | null)[][]
  overlapCounts: number[][]
  missingCounts: Record<string, number>
  missingRatios: Record<string, number>
  rankedPairs: FeatureRelationshipPair[]
  scatterPoints: RelationshipScatterPoint[]
  totalObservations: number
  includedObservations: number
}

// 7. K-Means Clustering Contracts
export type KMeansClusterSummary = {
  clusterIndex: number
  size: number
  percentage: number
  scaledCentroid: number[]
  originalCentroid: number[]
  meanDistance: number
}

export type KMeansAssignment = {
  observation: ObservationReference
  clusterIndex: number
  distanceToCentroid: number
}

export type KMeansResult = {
  k: number
  inertia: number
  iterations: number
  converged: boolean
  startsRun: number
  silhouetteScore?: number | null
  clusterSummaries: KMeansClusterSummary[]
  assignments: KMeansAssignment[]
  selectedFeatures: string[]
  centers: number[]
  scales: number[]
}

// 8. Gaussian Mixture Model (GMM) Contracts
export type GmmComponentSummary = {
  componentIndex: number
  weight: number
  size: number
  meanVector: number[]
  varianceVector: number[]
}

export type GmmAssignment = {
  observation: ObservationReference
  assignedComponent: number
  maxProbability: number
  probabilities: number[]
  logDensity: number
  entropy: number
}

export type GaussianMixtureResult = {
  componentsCount: number
  covarianceType: string
  logLikelihood: number
  aic: number
  bic: number
  converged: boolean
  iterations: number
  weights: number[]
  means: number[][]
  variances: number[][]
  fullCovariances?: number[][][] | null
  componentSummaries: GmmComponentSummary[]
  assignments: GmmAssignment[]
  selectedFeatures: string[]
}

// 9. Isolation Forest Contracts
export type IsolationForestAssignment = {
  observation: ObservationReference
  anomalyScore: number
  isAnomaly: boolean
  normalizedPathLength: number
}

export type TopAnomalyItem = {
  observation: ObservationReference
  anomalyScore: number
  rank: number
  featureContributions: Record<string, number>
}

export type IsolationForestResult = {
  treesCount: number
  subsampleSize: number
  contamination: number
  scoreThreshold: number
  totalObservations: number
  anomalyCount: number
  selectedFeatures: string[]
  featureContributions: Record<string, number>
  assignments: IsolationForestAssignment[]
  topAnomalies: TopAnomalyItem[]
}

// 10. Script Condition Analysis
export type ScriptGuardClause = {
  feature: string
  operator: string
  threshold: number
  rawExpression: string
  lineNumber: number
}

export type ScriptConditionDefinition = {
  name: string
  expression: string
  lineNumber: number
  requiredFeatures: string[]
}

export type ScriptReturnExpression = {
  rawExpression: string
  conditionNames: string[]
  lineNumber: number
}

export type ParsedScript = {
  guards: ScriptGuardClause[]
  conditions: ScriptConditionDefinition[]
  finalReturn?: ScriptReturnExpression | null
  allRequiredFeatures: string[]
  parseWarnings: string[]
}

export type ScriptObservationEvaluation = {
  observation: ObservationReference
  finalResult: boolean
  guardResults: Record<string, boolean>
  conditionResults: Record<string, boolean>
  featureValues: Record<string, number | null>
  guardFailed: boolean
  failedGuardFeature?: string | null
}

export type ScriptConditionStatistics = {
  conditionName: string
  expression: string
  totalEvaluated: number
  trueCount: number
  falseCount: number
  trueRatio: number
  falseRatio: number
  missingFeatureCount: number
}

export type ScriptGuardStatistics = {
  feature: string
  expression: string
  totalEvaluated: number
  passCount: number
  failCount: number
  failRatio: number
}

export type ScriptConditionResult = {
  script: ParsedScript
  totalObservations: number
  guardFailedCount: number
  finalTrueCount: number
  finalFalseCount: number
  finalTrueRatio: number
  guardStats: ScriptGuardStatistics[]
  conditionStats: ScriptConditionStatistics[]
  evaluations: ScriptObservationEvaluation[]
  warnings: string[]
}

// 11. Classifier Rule Contracts & DTOs
export type ClassifierRuleDto = {
  id: string
  ruleCode: string
  ruleName: string
  version: string
  productKey?: string | null
  lineCode?: string | null
  machineCode?: string | null
  stationCode?: string | null
  defectFamily?: string | null
  defectClass?: string | null
  thresholdsConfigJson: string
  modelMetadataJson?: string | null
  scriptContent?: string | null
  priority?: number
  defectTypeId?: string | null
  isActive: boolean
  effectiveFromUtc: string
  effectiveToUtc?: string | null
  createdAtUtc: string
  updatedAtUtc?: string | null
  createdBy: string
  description?: string | null
}

export type CreateClassifierRuleRequest = {
  ruleCode: string
  ruleName: string
  version: string
  productKey?: string | null
  lineCode?: string | null
  machineCode?: string | null
  stationCode?: string | null
  defectFamily?: string | null
  defectClass?: string | null
  thresholdsConfigJson?: string | null
  modelMetadataJson?: string | null
  scriptContent?: string | null
  priority?: number
  defectTypeId?: string | null
  isActive?: boolean
  effectiveFromUtc?: string | null
  effectiveToUtc?: string | null
  description?: string | null
}

export type UpdateClassifierRuleRequest = {
  ruleName?: string | null
  version?: string | null
  thresholdsConfigJson?: string | null
  modelMetadataJson?: string | null
  scriptContent?: string | null
  priority?: number | null
  defectTypeId?: string | null
  isActive?: boolean | null
  effectiveToUtc?: string | null
  description?: string | null
}

export type AoiScriptRuleItem = {
  priority: number
  name: string
  guid?: string | null
  defectTypeId?: string | null
  script?: string | null
}

export type ImportAoiRulesJsonRequest = {
  rules: AoiScriptRuleItem[]
  machineCode?: string | null
  lineCode?: string | null
}

export type ResolveClassifierRuleRequest = {
  productKey?: string | null
  lineCode?: string | null
  machineCode?: string | null
  stationCode?: string | null
  defectFamily?: string | null
  defectClass?: string | null
  ruleCode?: string | null
  effectiveTimeUtc?: string | null
}

export type ResolveClassifierRuleResponse = {
  isResolved: boolean
  rule?: ClassifierRuleDto | null
  matchResolutionKind?: string | null
  specificityScore: number
  message?: string | null
}

export type TestClassifierRuleRequest = {
  scriptContent?: string | null
  ruleId?: string | null
  features?: Record<string, number> | null
  featuresJson?: string | null
}

export type TestClassifierRuleResponse = {
  success: boolean
  finalResult: boolean
  verdict: string
  guardResults: Record<string, boolean>
  conditionResults: Record<string, boolean>
  featureValues: Record<string, number | null>
  guardFailed: boolean
  failedGuardFeature?: string | null
  requiredFeatures: string[]
  warnings: string[]
  errorMessage?: string | null
}

// 12. Rule Auto-Tuning & What-If Simulation Types
export type RuleTuningRecommendationDto = {
  featureName: string
  currentThresholdInScript?: number | null
  proposedThreshold: number
  comparisonOperator: string
  overkillMin: number
  overkillMax: number
  overkillMean: number
  realMin: number
  realMax: number
  realMean: number
  separationKind: 'CompletelySeparated' | 'SignificantShift' | 'Overlapped' | string
  projectedOverkillCutPercent: number
  recommendedChangeCode: string
  explanation: string
}

export type RuleTuningAnalysisResponse = {
  ruleId?: string | null
  ruleCode: string
  ruleName: string
  defectClass: string
  totalOverkillSamples: number
  totalRealDefectSamples: number
  currentOverkillRate: number
  recommendations: RuleTuningRecommendationDto[]
  originalScript: string
  autoTunedScript: string
  executiveSummary: string
}

export type SimulateWhatIfRequest = {
  ruleId?: string | null
  baselineScript?: string | null
  candidateScript?: string | null
  defectName?: string | null
  lineCode?: string | null
  machineCode?: string | null
  maxSamples?: number
}

export type WhatIfDivergenceDto = {
  keyId: string
  groundTruthIsDefect: boolean
  baselineVerdict: boolean
  candidateVerdict: boolean
  divergenceKind: 'OverkillEliminated' | 'DangerousLeakage' | string
  features: Record<string, number>
  explanation: string
}

export type SimulateWhatIfResponse = {
  totalEvaluated: number
  baselineRealDefectCount: number
  baselineOverkillCount: number
  baselineOverkillRate: number
  baselineLeakageCount: number
  candidateRealDefectCount: number
  candidateOverkillCount: number
  candidateOverkillRate: number
  candidateLeakageCount: number
  overkillReducedCount: number
  overkillReductionPercent: number
  newLeakageIntroducedCount: number
  isSafeToDeploy: boolean
  estimatedSavedOpHoursPerMonth: number
  detailedExplanation: string
  divergences: WhatIfDivergenceDto[]
}
