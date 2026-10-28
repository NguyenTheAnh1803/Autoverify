import type {
  AuditEvent,
  ClearQueueRequest,
  ConnectionTestResult,
  CreateDatabaseConnectionRequest,
  CreateDataSourceRequest,
  CreateDefectRecordRequest,
  CreateProductQueryRequest,
  CreateProductRecordRequest,
  CreateQueryProfileRequest,
  CreateScanExecutionRequest,
  CreateScanListRequest,
  DashboardProductionReport,
  ProductionFeatureAnalysis,
  ProductionFeatureJob,
  DatabaseConnection,
  DataSource,
  CreateDefectPreviewSessionRequest,
  DefectPreviewPagedResponse,
  DefectPreviewProgress,
  DefectPreviewQueryParams,
  DefectPreviewSession,
  DefectRecord,
  DefectStoragePreviewItem,
  EnqueuePasteRequest,
  ImportPreview,
  ImportPreviewRequest,
  LeakageBatch,
  LeakageQueueItem,
  LeakageQueueItemDetail,
  LeakageQueueProgress,
  LoginResponse,
  MultiTableScanRequest,
  MultiTableScanResponse,
  OverkillAnalysis,
  OverkillTrendResponse,
  AnomalyThumbnailItem,
  AnomalyThumbnailQueryRequest,
  OverkillCandidate,
  OverkillPipelineConfig,
  OverkillPipelineExecution,
  MigrateBatchDefectsResult,
  MigrateBundlesResult,
  MigratePacksResult,
  MigrateSingleDefectResult,
  OverkillRunResult,
  StorageShardingInfo,
  Paged,
  ProcessLeakageQueueRequest,
  ProductQuery,
  ProductFilterOptions,
  ProductRecord,
  QueryProfile,
  QueryTestResponse,
  RenderedQuery,
  RetryQueueItemsRequest,
  ScanExecution,
  ScanList,
  ScanListItem,
  SaveOverkillPipelineConfigRequest,
  SeaweedFsConfig,
  StorageTestConnectionResult,
  StoredObject,
  SystemHealth,
  TestPipelineMappingRequest,
  TestPipelineMappingResponse,
  UpdateProductQueryStatusRequest,
  UploadJsonRequest,
  UserProfile,
  VerificationHistory,
  VerificationResult,
  VerifyBatchRequest,
  VerifyBatchResponse,
  FeatureSchemaReadinessResponse,
  CreateAnalyticsJobRequest,
  AnalyticsJob,
  SessionScopeFacetsResponse,
  ClassifierRuleDto,
  CreateClassifierRuleRequest,
  UpdateClassifierRuleRequest,
  ResolveClassifierRuleRequest,
  ResolveClassifierRuleResponse,
  ImportAoiRulesJsonRequest,
  TestClassifierRuleRequest,
  TestClassifierRuleResponse,
  OpDefectMappingDto,
  SaveOpDefectMappingRequest,
  UploadOpExcelResponse,
  PasteOpRecordsRequest,
  OpReconciliationAnalysisResponse,
  OpReconciliationRecordsResponse,
  RuleTuningAnalysisResponse,
  SimulateWhatIfRequest,
  SimulateWhatIfResponse,
  BatchProvisionRequest,
  BatchProvisionResponse,
  BatchGeneratePipelinesForConnectionsRequest,
  ScanDirectoryPathsRequest,
  ScanDirectoryPathsResponse,
  CrawlDefectImageRequest,
  CrawlDefectImageResponse,
  TestImageCrawlerApiRequest,
  TestImageCrawlerApiResponse,
  BatchImageCrawlRequest,
  BatchImageCrawlResponse,
  TestDefectCrawlRequest,
  TestDefectCrawlResponse,
  TestPathCrawlRequest,
  TestPathCrawlResponse,
  SampleDefectForTestDto,
  AiMapping,
  AiMappingInput,
  AiProductResult,
  AiProductSummary,
  AiSubmitResult,
  AiVerificationStatus,
  MasterConnectionStatus,
} from './types'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

/** What the user can do about a failed request when the server sent no message of its own. `0` means the server was not reached. */
function describeStatus(status: number) {
  if (status === 0) return 'Cannot reach the AutoVerify server. Check the network connection or that the API service is running, then try again.'
  if (status === 401) return 'Your session has expired. Sign in again to continue.'
  if (status === 403) return 'Your account does not have permission for this action.'
  if (status === 404) return 'The requested item no longer exists. Refresh the list and try again.'
  if (status === 408 || status === 504) return 'The server took too long to answer. Try again, or narrow the filter so less data is loaded.'
  if (status === 413) return 'The file is too large for the server to accept.'
  if (status === 429) return 'Too many requests in a short time. Wait a few seconds, then try again.'
  if (status >= 500) return `The server hit an error (${status}). Try again; if it keeps failing, check the API service log.`
  return `Request failed (${status}).`
}

export class ApiError extends Error {
  status: number
  body: string

  constructor(status: number, body: string) {
    super(body || describeStatus(status))
    this.status = status
    this.body = body
  }
}

function getToken() {
  return localStorage.getItem('autoverify.token') ?? ''
}

/** Called once when any request comes back 401, so the app can return to the sign-in screen instead of every panel failing. */
let unauthorizedHandler: (() => void) | null = null
export function setUnauthorizedHandler(handler: (() => void) | null) {
  unauthorizedHandler = handler
}

/** fetch() that reports "server not reached" as an ApiError with status 0 instead of the browser's bare "Failed to fetch". */
async function send(url: string, init: RequestInit) {
  try {
    return await fetch(url, init)
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiError(0, '')
  }
}

async function failure(path: string, response: Response) {
  if (response.status === 401 && !path.startsWith('/auth/')) unauthorizedHandler?.()
  return new ApiError(response.status, await parseError(response))
}

/** Longest server message shown as-is; anything longer (a stack trace, a dump) is cut so the notice stays readable. */
const MAX_ERROR_LENGTH = 300

async function parseError(response: Response) {
  const text = (await response.text().catch(() => '')).trim()
  let message: string
  try {
    const json = JSON.parse(text) as { error?: string; title?: string; message?: string }
    message = json.error ?? json.title ?? json.message ?? ''
  } catch {
    // A proxy or IIS error page is HTML; showing its markup helps nobody, so fall back to the status explanation.
    message = /^<(!doctype|html)/i.test(text) ? '' : text
  }
  if (message.length > MAX_ERROR_LENGTH) {
    console.error(`API ${response.status} ${response.url}:`, message)
    message = `${message.slice(0, MAX_ERROR_LENGTH).trimEnd()}… (full message in the browser console)`
  }
  return message
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await send(`${API_BASE_URL}${path}`, { ...init, headers })
  if (response.status === 204) return undefined as T
  if (!response.ok) throw await failure(path, response)
  if (response.status === 201 || response.headers.get('content-type')?.includes('json')) {
    return (await response.json()) as T
  }
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

function qs(params: Record<string, string | number | boolean | null | undefined>) {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const encoded = search.toString()
  return encoded ? `?${encoded}` : ''
}

export function asList<T>(payload: T[] | Paged<T> | { items?: T[] }): T[] {
  if (Array.isArray(payload)) return payload
  return payload.items ?? []
}

export const authApi = {
  login: (username: string, password: string) =>
    api<LoginResponse>('/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
  me: () => api<UserProfile>('/auth/me'),
  logout: () => api<void>('/auth/logout', { method: 'POST' }),
}

export const dashboardApi = {
  productionReport: (days = 14) => api<DashboardProductionReport>(`/dashboard/production-report${qs({ days })}`),
}

export const productionFeatureApi = {
  facets: (signal?: AbortSignal) =>
    api<ProductionFeatureJob>('/analytics/production-features/facets', { signal }),
  startJob: (filters: { machineCode?: string; lineCode?: string; period?: string; fromUtc?: string; toUtc?: string; defectName?: string; judge?: string } = {}, signal?: AbortSignal) =>
    api<ProductionFeatureJob>(`/analytics/production-features/jobs${qs(filters)}`, { method: 'POST', signal }),
  getJob: (jobId: string, signal?: AbortSignal) =>
    api<ProductionFeatureJob>(`/analytics/production-features/jobs/${encodeURIComponent(jobId)}`, { signal }),
  cancelJob: (jobId: string) =>
    api<ProductionFeatureJob>(`/analytics/production-features/jobs/${encodeURIComponent(jobId)}/cancel`, { method: 'POST' }),
  analyze: (jobId: string, analysisType: string, featureName?: string, featureNames?: string[]) =>
    api<ProductionFeatureAnalysis>(`/analytics/production-features/jobs/${encodeURIComponent(jobId)}/analyze${qs({
      analysisType,
      featureName,
      featureNames: featureNames?.join(','),
    })}`, { method: 'POST' }),
}

export const importsApi = {
  preview: (body: ImportPreviewRequest) =>
    api<ImportPreview>('/imports/preview', { method: 'POST', body: JSON.stringify(body) }),
}

export const scanListsApi = {
  list: () => api<ScanList[]>('/scan-lists'),
  items: (id: string) => api<ScanListItem[]>(`/scan-lists/${id}/items`),
  create: (body: CreateScanListRequest) =>
    api<ScanList>('/scan-lists', { method: 'POST', body: JSON.stringify(body) }),
}

export const scanExecutionsApi = {
  list: (page = 1, pageSize = 25) =>
    api<Paged<ScanExecution>>(`/scan-executions${qs({ page, pageSize })}`),
  create: (body: CreateScanExecutionRequest) =>
    api<ScanExecution>('/scan-executions', { method: 'POST', body: JSON.stringify(body) }),
  results: (executionId: string, page = 1, pageSize = 25) =>
    api<Paged<VerificationResult>>(`/scan-executions/${executionId}/results${qs({ page, pageSize })}`),
}

export const dataSourcesApi = {
  list: (filters: { machineCode?: string; verificationCategory?: string; schedulePeriod?: string } = {}) =>
    api<DataSource[]>(`/data-sources${qs(filters)}`),
  create: (body: CreateDataSourceRequest) =>
    api<DataSource>('/data-sources', { method: 'POST', body: JSON.stringify(body) }),
  test: (sourceId: string) =>
    api<ConnectionTestResult>(`/data-sources/${sourceId}/test`, { method: 'POST' }),
}

export const connectionsApi = {
  list: (filters: { search?: string; engine?: string; page?: number; pageSize?: number } = {}) =>
    api<Paged<DatabaseConnection>>(`/database-connections${qs(filters)}`),
  create: (body: CreateDatabaseConnectionRequest) =>
    api<DatabaseConnection>('/database-connections', { method: 'POST', body: JSON.stringify(body) }),
  update: (id: string, body: CreateDatabaseConnectionRequest) =>
    api<DatabaseConnection>(`/database-connections/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  test: (id: string) =>
    api<ConnectionTestResult>(`/database-connections/${id}/test`, { method: 'POST' }),
  batchProvision: (body: BatchProvisionRequest) =>
    api<BatchProvisionResponse>('/database-connections/batch-provision', { method: 'POST', body: JSON.stringify(body) }),
  scanDirectoryPaths: (body: ScanDirectoryPathsRequest) =>
    api<ScanDirectoryPathsResponse>('/database-connections/scan-directory-paths', { method: 'POST', body: JSON.stringify(body) }),
  batchDelete: (connectionIds: string[], deleteLinkedPipelines = true) =>
    api<{ deletedCount: number; message: string }>('/database-connections/batch-delete', {
      method: 'POST',
      body: JSON.stringify({ connectionIds, deleteLinkedPipelines }),
    }),
}

export const productsApi = {
  list: (filters: {
    search?: string
    judge?: string
    panelId?: string
    machineCode?: string
    lineCode?: string
    zone?: string
    defectName?: string
    page?: number
    pageSize?: number
  } = {}) => api<Paged<ProductRecord>>(`/products${qs(filters)}`),
  filterOptions: (filters: { machineCode?: string; lineCode?: string } = {}) =>
    api<ProductFilterOptions>(`/products/filter-options${qs(filters)}`),
  create: (body: CreateProductRecordRequest) =>
    api<ProductRecord>('/products', { method: 'POST', body: JSON.stringify(body) }),
  scan: (body: MultiTableScanRequest) =>
    api<MultiTableScanResponse>('/products/scan', { method: 'POST', body: JSON.stringify(body) }),
  overkill: (filters: { machineCode?: string; lineCode?: string } = {}) =>
    api<OverkillCandidate[]>(`/products/overkill-candidates${qs(filters)}`),
  defects: (keyId: string) => api<DefectRecord[]>(`/products/${encodeURIComponent(keyId)}/defects`),
}

export const defectsApi = {
  create: (body: CreateDefectRecordRequest) =>
    api<DefectRecord>('/defects', { method: 'POST', body: JSON.stringify(body) }),
}

export const productQueriesApi = {
  list: (status?: string) => api<ProductQuery[]>(`/product-queries${qs({ status })}`),
  create: (body: CreateProductQueryRequest) =>
    api<ProductQuery>('/product-queries', { method: 'POST', body: JSON.stringify(body) }),
  updateStatus: (id: string, body: UpdateProductQueryStatusRequest) =>
    api<ProductQuery>(`/product-queries/${id}/status`, { method: 'POST', body: JSON.stringify(body) }),
}

export const queryProfilesApi = {
  list: (filters: { machineCode?: string; lineCode?: string; concept?: string; sourceSystem?: string; connectionId?: string; verificationCategory?: string } = {}) =>
    api<QueryProfile[]>(`/query-profiles${qs(filters)}`),
  create: (body: CreateQueryProfileRequest) =>
    api<QueryProfile>('/query-profiles', { method: 'POST', body: JSON.stringify(body) }),
  update: (id: string, body: CreateQueryProfileRequest) =>
    api<QueryProfile>(`/query-profiles/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  render: (id: string, parameters: Record<string, string>) =>
    api<RenderedQuery>(`/query-profiles/${id}/render`, { method: 'POST', body: JSON.stringify({ parameters }) }),
  test: (id: string, parameters: Record<string, string>) =>
    api<QueryTestResponse>(`/query-profiles/${id}/test`, { method: 'POST', body: JSON.stringify({ parameters }) }),
}

export const storageApi = {
  list: () => api<StoredObject[]>('/storage'),
  getConfig: () => api<SeaweedFsConfig>('/storage/config'),
  updateConfig: (body: Partial<SeaweedFsConfig>) =>
    api<SeaweedFsConfig>('/storage/config', { method: 'POST', body: JSON.stringify(body) }),
  testConnection: (body?: Partial<SeaweedFsConfig>) =>
    api<StorageTestConnectionResult>('/storage/test-connection', { method: 'POST', body: JSON.stringify(body ?? {}) }),
  previewDefects: (params: { keyword?: string; filter?: string; limit?: number } = {}) =>
    api<DefectStoragePreviewItem[]>(`/storage/defects/preview${qs(params)}`),
  migrateSingleDefect: (defectId: string) =>
    api<MigrateSingleDefectResult>(`/storage/defects/${defectId}/migrate-single`, { method: 'POST' }),
  migrateBatchDefects: (body: { defectIds?: string[]; allWithInlineJson?: boolean; limit?: number }) =>
    api<MigrateBatchDefectsResult>('/storage/defects/migrate-batch', { method: 'POST', body: JSON.stringify(body) }),
  getShardingInfo: () => api<StorageShardingInfo>('/storage/sharding-info'),
  migrateBundles: (body: { defectIds?: string[]; allWithInlineJson?: boolean; limit?: number }) =>
    api<MigrateBundlesResult>('/storage/defects/migrate-bundles', { method: 'POST', body: JSON.stringify(body) }),
  migratePacks: (body: { defectIds?: string[]; allWithInlineJson?: boolean; limit?: number }) =>
    api<MigratePacksResult>('/storage/defects/migrate-packs', { method: 'POST', body: JSON.stringify(body) }),
  productDefectPackUrl: (keyOrId: string) =>
    `/api/v1/storage/product-defect-pack/${encodeURIComponent(keyOrId)}`,
  getProductDefectPack: (keyOrId: string) =>
    api<any>(`/storage/product-defect-pack/${encodeURIComponent(keyOrId)}`),
  defectFileUrl: (defectId: string, fileType: 'feature' | 'custom' | 'image' = 'feature') =>
    `/api/v1/storage/defect-file/${defectId}?fileType=${fileType}`,
  defectImagesUrl: (defectId: string) =>
    `/api/v1/storage/defect-images/${defectId}`,
  uploadJson: (body: UploadJsonRequest) =>
    api<StoredObject>('/storage/json', { method: 'POST', body: JSON.stringify(body) }),
  uploadImage: (file: File, bucket?: string) => {
    const form = new FormData()
    form.append('file', file)
    if (bucket) form.append('bucket', bucket)
    return api<StoredObject>('/storage/images', { method: 'POST', body: form })
  },
  createPreviewSession: (body: CreateDefectPreviewSessionRequest, signal?: AbortSignal) =>
    api<DefectPreviewSession>('/storage/defect-preview/sessions', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),
  getPreviewProgress: (sessionId: string, signal?: AbortSignal) =>
    api<DefectPreviewProgress>(`/storage/defect-preview/sessions/${encodeURIComponent(sessionId)}/progress`, {
      signal,
    }),
  queryPreviewRows: (sessionId: string, params: DefectPreviewQueryParams = {}, signal?: AbortSignal) => {
    const searchParams: Record<string, string | number | boolean | null | undefined> = {
      page: params.page,
      pageSize: params.pageSize,
      sortBy: params.sortBy,
      sortDir: params.sortDir ?? params.sortDirection,
      sortDirection: params.sortDirection ?? params.sortDir,
      keyword: params.keyword ?? params.search,
      search: params.search ?? params.keyword,
      judge: params.judge,
      severity: params.severity,
      defectTable: params.defectTable,
    }
    return api<DefectPreviewPagedResponse>(
      `/storage/defect-preview/sessions/${encodeURIComponent(sessionId)}/rows${qs(searchParams)}`,
      { signal }
    )
  },
  deletePreviewSession: (sessionId: string, signal?: AbortSignal) =>
    api<{ success: boolean; message: string }>(`/storage/defect-preview/sessions/${encodeURIComponent(sessionId)}`, {
      method: 'DELETE',
      signal,
    }),
}

export const defectPreviewApi = {
  createSession: storageApi.createPreviewSession,
  getProgress: storageApi.getPreviewProgress,
  queryRows: storageApi.queryPreviewRows,
  deleteSession: storageApi.deletePreviewSession,
}

export const systemApi = {
  audit: () => api<AuditEvent[]>('/audit-events'),
  health: () => api<SystemHealth>('/system/health'),
}

export const verificationApi = {
  overkill: (body: VerifyBatchRequest) =>
    api<VerifyBatchResponse>('/verify/overkill', { method: 'POST', body: JSON.stringify(body) }),
  leakage: (body: VerifyBatchRequest) =>
    api<VerifyBatchResponse>('/verify/leakage', { method: 'POST', body: JSON.stringify(body) }),
  history: (filters: { category?: string; machineCode?: string; lineCode?: string; page?: number; pageSize?: number } = {}) =>
    api<Paged<VerificationHistory>>(`/verification-history${qs(filters)}`),
  overkillAnalysis: (filters: { machineCode?: string; lineCode?: string; minNgShare?: number } = {}) =>
    api<OverkillAnalysis>(`/overkill/analysis${qs(filters)}`),
  overkillTrend: (filters: { machineCode?: string; lineCode?: string; modelCode?: string; defectFamily?: string; fromUtc?: string; toUtc?: string } = {}) =>
    api<OverkillTrendResponse>(`/verification/overkill-trend${qs(filters)}`),
  anomalyThumbnails: (params: AnomalyThumbnailQueryRequest = {}) =>
    api<Paged<AnomalyThumbnailItem>>(`/verification/anomaly-thumbnails${qs(params)}`),
}

export async function getOverkillPipelineConfigs(): Promise<OverkillPipelineConfig[]> {
  return api<OverkillPipelineConfig[]>('/overkill-pipeline/configs')
}

export async function saveOverkillPipelineConfig(
  payload: SaveOverkillPipelineConfigRequest
): Promise<OverkillPipelineConfig> {
  if (payload.id) {
    return api<OverkillPipelineConfig>(`/overkill-pipeline/configs/${payload.id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    })
  }
  return api<OverkillPipelineConfig>('/overkill-pipeline/configs', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function deleteOverkillPipelineConfig(id: string): Promise<void> {
  return api<void>(`/overkill-pipeline/configs/${id}`, { method: 'DELETE' })
}

export async function toggleOverkillPipelineConfig(id: string): Promise<OverkillPipelineConfig> {
  return api<OverkillPipelineConfig>(`/overkill-pipeline/configs/${id}/toggle`, { method: 'POST' })
}

export async function runOverkillPipelineConfig(id: string): Promise<OverkillRunResult> {
  return api<OverkillRunResult>(`/overkill-pipeline/configs/${id}/run`, { method: 'POST' })
}

export async function testOverkillPipelineMapping(
  payload: TestPipelineMappingRequest
): Promise<TestPipelineMappingResponse> {
  return api<TestPipelineMappingResponse>('/overkill-pipeline/test-mapping', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export async function getOverkillPipelineExecutions(
  configId?: string
): Promise<OverkillPipelineExecution[]> {
  if (configId) {
    return api<OverkillPipelineExecution[]>(`/overkill-pipeline/configs/${configId}/executions`)
  }
  return api<OverkillPipelineExecution[]>('/overkill-pipeline/executions')
}

export const overkillPipelineApi = {
  getConfigs: getOverkillPipelineConfigs,
  saveConfig: saveOverkillPipelineConfig,
  deleteConfig: deleteOverkillPipelineConfig,
  toggleConfig: toggleOverkillPipelineConfig,
  runConfig: runOverkillPipelineConfig,
  testMapping: testOverkillPipelineMapping,
  getExecutions: getOverkillPipelineExecutions,
  batchProvisionFromConnections: (body: BatchGeneratePipelinesForConnectionsRequest) =>
    api<BatchProvisionResponse>('/overkill-pipeline/batch-provision-from-connections', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  batchDelete: (pipelineIds: string[]) =>
    api<{ deletedCount: number; message: string }>('/overkill-pipeline/batch-delete', {
      method: 'POST',
      body: JSON.stringify({ pipelineIds }),
    }),
}

export const aiVerificationApi = {
  status: () => api<AiVerificationStatus>('/ai-verification/status'),
  testConnection: () => api<MasterConnectionStatus>('/master/test-connection', { method: 'POST' }),
  mappings: () => api<AiMapping[]>('/ai-verification/mappings'),
  saveMapping: (body: AiMappingInput) =>
    api<AiMapping>('/ai-verification/mappings', { method: 'PUT', body: JSON.stringify(body) }),
  deleteMapping: (id: string) => api<void>(`/ai-verification/mappings/${id}`, { method: 'DELETE' }),
  submit: (keyIds: string[], resubmit = false) =>
    api<AiSubmitResult>('/ai-verification/submit', { method: 'POST', body: JSON.stringify({ keyIds, resubmit }) }),
  poll: () => api<{ completed: number }>('/ai-verification/poll', { method: 'POST' }),
  productStatuses: (keyIds: string[]) =>
    api<AiProductSummary[]>('/ai-verification/products/status', { method: 'POST', body: JSON.stringify({ keyIds }) }),
  product: (keyId: string) => api<AiProductResult>(`/ai-verification/products/${encodeURIComponent(keyId)}`),
}

export const imageCrawlerApi = {
  crawlDefect: (body: CrawlDefectImageRequest) =>
    api<CrawlDefectImageResponse>('/image-crawler/crawl', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  testApi: (body: TestImageCrawlerApiRequest) =>
    api<TestImageCrawlerApiResponse>('/image-crawler/test', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  testDefect: (body: TestDefectCrawlRequest) =>
    api<TestDefectCrawlResponse>('/image-crawler/test-defect', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  testPath: (body: TestPathCrawlRequest) =>
    api<TestPathCrawlResponse>('/image-crawler/test-path', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  getSamples: (limit = 10) =>
    api<SampleDefectForTestDto[]>(`/image-crawler/sample-defects?limit=${limit}`),
  batchCrawl: (body: BatchImageCrawlRequest) =>
    api<BatchImageCrawlResponse>('/image-crawler/batch-crawl', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  previewUrl: (defectId: string, refresh = false, vp = '0') =>
    `/api/v1/image-crawler/preview/${defectId}?vp=${vp}${refresh ? '&refresh=true' : ''}`,
}

export async function enqueueLeakagePaste(req: EnqueuePasteRequest): Promise<LeakageBatch> {
  return api<LeakageBatch>('/leakage-queue/paste', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

export async function uploadLeakageCsv(
  file: File,
  batchName?: string,
  defaultLineCode?: string
): Promise<LeakageBatch> {
  const form = new FormData()
  form.append('file', file)
  if (batchName) form.append('batchName', batchName)
  if (defaultLineCode) form.append('defaultLineCode', defaultLineCode)
  return api<LeakageBatch>('/leakage-queue/upload-csv', {
    method: 'POST',
    body: form,
  })
}

export async function getLeakageBatches(limit?: number): Promise<LeakageBatch[]> {
  return api<LeakageBatch[]>(`/leakage-queue/batches${qs({ limit })}`)
}

export async function getLeakageItems(params?: {
  batchId?: string
  status?: string
  search?: string
  page?: number
  pageSize?: number
}): Promise<Paged<LeakageQueueItem>> {
  return api<Paged<LeakageQueueItem>>(`/leakage-queue/items${qs(params ?? {})}`)
}

export async function getLeakageItemDetail(id: string): Promise<LeakageQueueItemDetail> {
  return api<LeakageQueueItemDetail>(`/leakage-queue/items/${id}/details`)
}

export async function processLeakageQueue(
  req: ProcessLeakageQueueRequest
): Promise<{ message: string; isRunning: boolean }> {
  return api<{ message: string; isRunning: boolean }>('/leakage-queue/process', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

export async function getLeakageProgress(): Promise<LeakageQueueProgress> {
  return api<LeakageQueueProgress>('/leakage-queue/progress')
}

export async function retryLeakageItems(
  req: RetryQueueItemsRequest
): Promise<{ resetCount: number; message: string }> {
  return api<{ resetCount: number; message: string }>('/leakage-queue/retry', {
    method: 'POST',
    body: JSON.stringify(req),
  })
}

export async function clearLeakageQueue(
  req: ClearQueueRequest
): Promise<{ removedCount: number; message: string }> {
  return api<{ removedCount: number; message: string }>('/leakage-queue/clear', {
    method: 'DELETE',
    body: JSON.stringify(req),
  })
}

export const leakageQueueApi = {
  enqueuePaste: enqueueLeakagePaste,
  uploadCsv: uploadLeakageCsv,
  getBatches: getLeakageBatches,
  getItems: getLeakageItems,
  getItemDetail: getLeakageItemDetail,
  processQueue: processLeakageQueue,
  getProgress: getLeakageProgress,
  retryItems: retryLeakageItems,
  clearQueue: clearLeakageQueue,
}

export async function fetchDefectFeatureContent(defectId: string, type: 'feature' | 'custom' = 'feature'): Promise<Record<string, unknown>> {
  return api<Record<string, unknown>>(`/defects/${defectId}/feature-content?type=${type}`)
}

// Defect Feature Analytics API
export async function getFeatureSchemaReadiness(
  sessionId: string,
  signal?: AbortSignal
): Promise<FeatureSchemaReadinessResponse> {
  return api<FeatureSchemaReadinessResponse>(
    `/storage/defect-preview/sessions/${sessionId}/analytics/schema-readiness`,
    { signal }
  )
}

export async function createAnalyticsJob(
  sessionId: string,
  request: CreateAnalyticsJobRequest,
  signal?: AbortSignal
): Promise<AnalyticsJob> {
  return api<AnalyticsJob>(
    `/storage/defect-preview/sessions/${sessionId}/analytics/jobs`,
    {
      method: 'POST',
      body: JSON.stringify(request),
      signal,
    }
  )
}

export async function listAnalyticsJobs(
  sessionId: string,
  signal?: AbortSignal
): Promise<AnalyticsJob[]> {
  return api<AnalyticsJob[]>(
    `/storage/defect-preview/sessions/${sessionId}/analytics/jobs`,
    { signal }
  )
}

export async function getAnalyticsJob<T = unknown>(
  sessionId: string,
  jobId: string,
  signal?: AbortSignal
): Promise<AnalyticsJob<T>> {
  return api<AnalyticsJob<T>>(
    `/storage/defect-preview/sessions/${sessionId}/analytics/jobs/${jobId}`,
    { signal }
  )
}

export async function cancelAnalyticsJob(
  sessionId: string,
  jobId: string,
  signal?: AbortSignal
): Promise<boolean> {
  return api<boolean>(
    `/storage/defect-preview/sessions/${sessionId}/analytics/jobs/${jobId}/cancel`,
    {
      method: 'POST',
      signal,
    }
  )
}

export async function deleteAnalyticsJob(
  sessionId: string,
  jobId: string,
  signal?: AbortSignal
): Promise<boolean> {
  return api<boolean>(
    `/storage/defect-preview/sessions/${sessionId}/analytics/jobs/${jobId}`,
    {
      method: 'DELETE',
      signal,
    }
  )
}

export async function getSessionScopeFacets(
  sessionId: string,
  signal?: AbortSignal
): Promise<SessionScopeFacetsResponse> {
  return api<SessionScopeFacetsResponse>(
    `/storage/defect-preview/sessions/${encodeURIComponent(sessionId)}/analytics/facets`,
    { signal }
  )
}

export function getScriptConditionCsvExportUrl(sessionId: string, jobId: string): string {
  const base = API_BASE_URL.replace(/\/$/, '')
  return `${base}/storage/defect-preview/sessions/${encodeURIComponent(sessionId)}/analytics/jobs/${encodeURIComponent(jobId)}/export-csv`
}

export const defectAnalyticsApi = {
  getSchemaReadiness: getFeatureSchemaReadiness,
  getScopeFacets: getSessionScopeFacets,
  createJob: createAnalyticsJob,
  listJobs: listAnalyticsJobs,
  getJob: getAnalyticsJob,
  cancelJob: cancelAnalyticsJob,
  deleteJob: deleteAnalyticsJob,
  getCsvExportUrl: getScriptConditionCsvExportUrl,
}

// Machine-Line Classifier Rules API
export async function listClassifierRules(
  filter?: {
    lineCode?: string
    machineCode?: string
    stationCode?: string
    ruleCode?: string
    defectFamily?: string
    isActive?: boolean
  },
  signal?: AbortSignal
): Promise<ClassifierRuleDto[]> {
  const query = new URLSearchParams()
  if (filter?.lineCode) query.set('lineCode', filter.lineCode)
  if (filter?.machineCode) query.set('machineCode', filter.machineCode)
  if (filter?.stationCode) query.set('stationCode', filter.stationCode)
  if (filter?.ruleCode) query.set('ruleCode', filter.ruleCode)
  if (filter?.defectFamily) query.set('defectFamily', filter.defectFamily)
  if (filter?.isActive !== undefined) query.set('isActive', String(filter.isActive))

  const queryString = query.toString()
  const path = `/classifier-rules${queryString ? `?${queryString}` : ''}`
  return api<ClassifierRuleDto[]>(path, { signal })
}

export async function getClassifierRuleById(
  id: string,
  signal?: AbortSignal
): Promise<ClassifierRuleDto> {
  return api<ClassifierRuleDto>(`/classifier-rules/${encodeURIComponent(id)}`, { signal })
}

export async function createClassifierRule(
  request: CreateClassifierRuleRequest,
  signal?: AbortSignal
): Promise<ClassifierRuleDto> {
  return api<ClassifierRuleDto>('/classifier-rules', {
    method: 'POST',
    body: JSON.stringify(request),
    signal,
  })
}

export async function updateClassifierRule(
  id: string,
  request: UpdateClassifierRuleRequest,
  signal?: AbortSignal
): Promise<ClassifierRuleDto> {
  return api<ClassifierRuleDto>(`/classifier-rules/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(request),
    signal,
  })
}

export async function resolveClassifierRule(
  request: ResolveClassifierRuleRequest,
  signal?: AbortSignal
): Promise<ResolveClassifierRuleResponse> {
  return api<ResolveClassifierRuleResponse>('/classifier-rules/resolve', {
    method: 'POST',
    body: JSON.stringify(request),
    signal,
  })
}

export async function importAoiRulesJson(
  request: ImportAoiRulesJsonRequest,
  signal?: AbortSignal
): Promise<ClassifierRuleDto[]> {
  return api<ClassifierRuleDto[]>('/classifier-rules/import-rules-json', {
    method: 'POST',
    body: JSON.stringify(request),
    signal,
  })
}

export async function deleteClassifierRule(id: string, signal?: AbortSignal): Promise<void> {
  await api<void>(`/classifier-rules/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    signal,
  })
}

export async function testClassifierRule(
  request: TestClassifierRuleRequest,
  signal?: AbortSignal
): Promise<TestClassifierRuleResponse> {
  return api<TestClassifierRuleResponse>('/classifier-rules/test', {
    method: 'POST',
    body: JSON.stringify(request),
    signal,
  })
}

export const opReconciliationApi = {
  listMappings: (search?: string, machineCode?: string, packageName?: string) =>
    api<OpDefectMappingDto[]>(`/op-defect-mappings${qs({ search, machineCode, packageName })}`),
  createMapping: (body: SaveOpDefectMappingRequest) =>
    api<OpDefectMappingDto>('/op-defect-mappings', { method: 'POST', body: JSON.stringify(body) }),
  updateMapping: (id: string, body: SaveOpDefectMappingRequest) =>
    api<OpDefectMappingDto>(`/op-defect-mappings/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteMapping: (id: string) =>
    api<void>(`/op-defect-mappings/${id}`, { method: 'DELETE' }),
  batchSaveMappings: (mappings: SaveOpDefectMappingRequest[], machineCode?: string, packageName?: string) =>
    api<OpDefectMappingDto[]>('/op-defect-mappings/batch', { method: 'POST', body: JSON.stringify({ mappings, machineCode, packageName }) }),
  analyzeClsf: async (file: File, machineCode?: string, modelCode?: string, packageName?: string) => {
    const formData = new FormData()
    formData.append('file', file)
    if (machineCode) formData.append('machineCode', machineCode)
    if (modelCode) formData.append('modelCode', modelCode)
    if (packageName) formData.append('packageName', packageName)
    formData.append('saveRules', 'true')
    return api<import('./types').ClsfPackageAnalysisResponse>('/op-defect-mappings/analyze-clsf', { method: 'POST', body: formData })
  },
  uploadExcel: async (file: File, lineCode?: string, machineCode?: string): Promise<UploadOpExcelResponse> => {
    const formData = new FormData()
    formData.append('file', file)
    if (lineCode) formData.append('lineCode', lineCode)
    if (machineCode) formData.append('machineCode', machineCode)
    return api<UploadOpExcelResponse>('/op-reconciliation/upload-excel', { method: 'POST', body: formData })
  },
  uploadExcelWithProgress: (
    file: File,
    machineCode: string | undefined,
    lineCode: string | undefined,
    onProgress: (percent: number) => void,
  ): Promise<UploadOpExcelResponse> => new Promise((resolve, reject) => {
    const formData = new FormData()
    formData.append('file', file)
    if (machineCode) formData.append('machineCode', machineCode)
    if (lineCode) formData.append('lineCode', lineCode)

    const base = API_BASE_URL.replace(/\/$/, '')
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${base}/op-reconciliation/upload-excel`)
    const token = getToken()
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress(Math.round((event.loaded / event.total) * 90))
    }
    xhr.onload = () => {
      if (xhr.status === 401) unauthorizedHandler?.()
      let payload: UploadOpExcelResponse & { error?: string; message?: string }
      try {
        payload = JSON.parse(xhr.responseText || '{}')
      } catch {
        reject(new ApiError(xhr.status, xhr.status >= 200 && xhr.status < 300 ? 'The server answered with something that is not a valid upload result.' : ''))
        return
      }
      if (xhr.status < 200 || xhr.status >= 300) {
        reject(new ApiError(xhr.status, payload.error || payload.message || ''))
        return
      }
      onProgress(100)
      resolve(payload)
    }
    xhr.onerror = () => reject(new ApiError(0, 'The connection dropped while uploading the OP review file. Check the network and upload it again.'))
    xhr.onabort = () => reject(new ApiError(0, 'Upload cancelled.'))
    xhr.send(formData)
  }),
  pasteRecords: (request: PasteOpRecordsRequest) =>
    api<UploadOpExcelResponse>('/op-reconciliation/paste', { method: 'POST', body: JSON.stringify(request) }),
  getAnalysis: (params: { machineCode?: string; lineCode?: string; targetDefect?: string; batchId?: string } = {}) =>
    api<OpReconciliationAnalysisResponse>(`/op-reconciliation/analysis${qs(params)}`),
  getRecords: (params: {
    search?: string
    machineCode?: string
    lineCode?: string
    classification?: string
    batchId?: string
    page?: number
    pageSize?: number
  } = {}) => api<OpReconciliationRecordsResponse>(`/op-reconciliation/records${qs(params)}`),
  seedSampleData: () =>
    api<{ message: string }>('/op-reconciliation/seed-sample-data', { method: 'POST' }),
}

export async function getRuleTuningAnalysis(
  ruleId: string,
  params: { machineCode?: string; lineCode?: string } = {},
  signal?: AbortSignal
): Promise<RuleTuningAnalysisResponse> {
  return api<RuleTuningAnalysisResponse>(`/classifier-rules/${encodeURIComponent(ruleId)}/tuning-analysis${qs(params)}`, {
    signal,
  })
}

export async function simulateWhatIf(
  request: SimulateWhatIfRequest,
  signal?: AbortSignal
): Promise<SimulateWhatIfResponse> {
  return api<SimulateWhatIfResponse>('/classifier-rules/simulate-what-if', {
    method: 'POST',
    body: JSON.stringify(request),
    signal,
  })
}

export const classifierRulesApi = {
  listRules: listClassifierRules,
  getRuleById: getClassifierRuleById,
  createRule: createClassifierRule,
  updateRule: updateClassifierRule,
  deleteRule: deleteClassifierRule,
  resolveRule: resolveClassifierRule,
  importAoiRulesJson: importAoiRulesJson,
  testRule: testClassifierRule,
  getTuningAnalysis: getRuleTuningAnalysis,
  simulateWhatIf: simulateWhatIf,
}
