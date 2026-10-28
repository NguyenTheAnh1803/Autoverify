public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(string AccessToken, UserProfile User);
public sealed record UserProfile(string Username, string DisplayName, IReadOnlyCollection<string> Roles);
public sealed record ImportPreviewRequest(string RawText, string MappingName = "entityId", string? MachineCode = null, string? LineCode = null, string VerificationCategory = "Leakage");
public sealed record ImportPreviewResponse(int AcceptedCount, int RejectedCount, int DuplicateCount, string Checksum, string VerificationCategory, string MachineCode, string? LineCode, bool HasLineWarning, IReadOnlyCollection<ImportAcceptedRow> AcceptedRows, IReadOnlyCollection<ImportRejectedRow> RejectedRows);
public sealed record ImportAcceptedRow(int RowNumber, string EntityId, string? LineCode = null, int? MappingInt = null, string? Mapping = null, string? Warning = null);
public sealed record ImportRejectedRow(int RowNumber, string RawValue, string Reason);
public sealed record CreateScanListRequest(string Name, string MappingName, string RawText, string? MachineCode = null, string? LineCode = null, string VerificationCategory = "Leakage");
public sealed record ScanListResponse(Guid Id, string Name, string MappingName, string MachineCode, string LineCode, string VerificationCategory, int AcceptedCount, int RejectedCount, int DuplicateCount, string Checksum, string? EvidenceUri, DateTimeOffset CreatedAtUtc, string CreatedBy);
public sealed record OverkillCandidateDto(string KeyId, string Judge, string PanelId, string PanelJudge, string NameDefect, string MachineCode, string LineCode, DateTimeOffset InspectedAtUtc);
public sealed record CreateScanExecutionRequest(Guid ScanListId, string Name, IReadOnlyCollection<Guid> SourceIds);
public sealed record ScanExecutionResponse(Guid Id, string Name, Guid ScanListId, string ScanListName, string Status, IReadOnlyCollection<string> SourceNames, int TotalItems, int PassedItems, int WarningItems, int FailedItems, int TechnicalErrorItems, int AnomalyItems, string? EvidenceUri, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc);
public sealed record VerificationResultResponse(Guid Id, Guid ExecutionId, string EntityId, string SourceName, string Status, string Severity, string RuleCode, string Message, double AnomalyScore, DateTimeOffset CompletedAtUtc);
public sealed record DataSourceResponse(Guid Id, string Name, string Type, string MachineCode, string VerificationCategory, string SchedulePeriod, string Environment, string Health, int AverageLatencyMs, bool Enabled, string? Host, int? Port, string? DatabaseName, string? ApiBaseUrl, bool HasSecret);
public sealed record CreateDataSourceRequest(string Name, string Type, string MachineCode, string VerificationCategory, string SchedulePeriod, string Environment, bool Enabled);
public sealed record DatabaseConnectionResponse(Guid Id, string Name, string Engine, string MachineCode, string LineCode, string Host, int Port, string DatabaseName, string Username, string? ApiBaseUrl, string Health, int AverageLatencyMs, bool HasSecret, string CredentialStatus, DateTimeOffset UpdatedAtUtc, string? ImageCrawlerApiUrl = null, string? ImageCrawlerVpMode = "0", string ModelCode = "UNASSIGNED");
public sealed record CreateDatabaseConnectionRequest(string Name, string Engine, string MachineCode, string LineCode, string? Host = null, int Port = 3306, string? DatabaseName = null, string? Username = null, string? Password = null, string? ApiBaseUrl = null, string? ApiKey = null, string? ImageCrawlerApiUrl = null, string? ImageCrawlerVpMode = "0", string? ModelCode = null);
public sealed record ConnectionTestResult(Guid SourceId, string SourceName, bool Success, int LatencyMs, string Message);
public sealed record DashboardRateTrendResponse(DateTime Date, int ProductionItems, int EvaluatedItems, int AnomalyItems, double AnomalyRate);
public sealed record DashboardDefectNameResponse(string DefectName, int Count, double Share);
public sealed record DashboardSectionReportResponse(
    int ProductionItems,
    int EvaluatedItems,
    int AnomalyItems,
    double AnomalyRate,
    int DefectRecords,
    DateTimeOffset? LastUpdatedAtUtc,
    IReadOnlyCollection<DashboardRateTrendResponse> Trend,
    IReadOnlyCollection<DashboardDefectNameResponse> DefectsByName);
public sealed record DashboardProductionReportResponse(
    int Days,
    DateTimeOffset GeneratedAtUtc,
    DashboardSectionReportResponse Overkill,
    DashboardSectionReportResponse Leakage);
public sealed record AuditEventResponse(Guid Id, DateTimeOffset OccurredAtUtc, string Actor, string Action, string TargetType, string TargetId, string Message);
public sealed record SystemHealthResponse(string Status, DateTimeOffset CheckedAtUtc, string Service, string Version, string StorageMode);
public sealed record UploadJsonRequest(string Bucket, string ObjectName, object Payload);
public sealed record StoredObjectResponse(Guid Id, string Backend, string Bucket, string ObjectName, string ContentType, long SizeBytes, string Uri, DateTimeOffset CreatedAtUtc);

public sealed record SeaweedFsConfigDto(
    string MasterUrl,
    string FilerUrl,
    string PublicBaseUrl,
    string DefaultBucket,
    bool OffloadDefects,
    string LocalFallbackPath = "data/storage"
);

public sealed record UpdateSeaweedFsConfigRequest(
    string? MasterUrl,
    string? FilerUrl,
    string? PublicBaseUrl,
    string? DefaultBucket,
    bool? OffloadDefects,
    string? LocalFallbackPath = null
);

public sealed record StorageTestConnectionRequest(
    string? MasterUrl = null,
    string? FilerUrl = null,
    string? PublicBaseUrl = null,
    string? DefaultBucket = null
);

public sealed record StorageComponentCheck(
    string Name,
    bool Reachable,
    long ResponseTimeMs,
    string Details,
    int? StatusCode = null
);

public sealed record StorageTestConnectionResponse(
    bool Success,
    string StatusMessage,
    StorageComponentCheck Master,
    StorageComponentCheck Filer,
    StorageComponentCheck WriteReadDeleteTest,
    IReadOnlyDictionary<string, string> Diagnostics
);

public sealed record DefectStoragePreviewItem(
    Guid Id,
    string KeyId,
    Guid? ProductId,
    string? ProductName,
    string DefectName,
    string DefectTable,
    string Judge,
    string Severity,
    bool HasInlineFeatureJson,
    int FeatureJsonBytes,
    string? FeatureUrl,
    string? CustomFeatureUrl,
    DateTimeOffset CreatedAtUtc
);

public sealed record MigrateDefectsRequest(
    IReadOnlyList<Guid>? DefectIds = null,
    bool? AllWithInlineJson = null,
    int? Limit = 100
);

public sealed record MigrateSingleDefectResponse(
    Guid DefectId,
    string KeyId,
    bool Success,
    string? FeatureUrl,
    string? CustomFeatureUrl,
    long BytesOffloaded,
    string Message
);

public sealed record MigrateBatchDefectsResponse(
    int TotalProcessed,
    int SuccessCount,
    int FailedCount,
    long BytesOffloaded,
    IReadOnlyList<MigrateSingleDefectResponse> Details
);

public sealed record StorageShardingInfoResponse(
    string Strategy,
    int Levels,
    int MaxEntriesPerDirectory,
    string SampleKey,
    string SampleShardedPath,
    string SampleBundlePath
);

public sealed record MigrateBundleItemResponse(
    string KeyId,
    int DefectsCount,
    bool Success,
    string? BundleUrl,
    long OriginalBytes,
    long CompressedBytes,
    double CompressionRatio,
    string Message
);

public sealed record MigrateBundlesResponse(
    int TotalKeyIdsProcessed,
    int TotalDefectsProcessed,
    int SuccessCount,
    int FailedCount,
    long TotalOriginalBytes,
    long TotalCompressedBytes,
    double OverallCompressionRatio,
    IReadOnlyList<MigrateBundleItemResponse> Bundles
);

public sealed record MigratePackItemResponse(
    string KeyId,
    int DefectsCount,
    bool Success,
    string? PackUrl,
    string Message
);

public sealed record MigratePacksResponse(
    int TotalKeyIdsProcessed,
    int TotalDefectsProcessed,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<MigratePackItemResponse> Packs
);
public sealed record CreateProductQueryRequest(string QueryKey, string ProductId, string PayloadJson, string? SourceSystem = null);
public sealed record UpdateProductQueryStatusRequest(string Status, string? JudgeResult = null, string? Message = null);
public sealed record ProductQueryResponse(Guid Id, string QueryKey, string ProductId, string SourceSystem, string Status, string PayloadJson, string? JudgeResult, string? Message, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateQueryProfileRequest(string Name, Guid? DataSourceId, string? MachineCode = null, string? LineCode = null, string Concept = "PRODUCT-STATUS", string SourceSystem = "MARIADB", string SqlTemplate = "", string ParameterSchemaJson = "{}", int TimeoutSeconds = 30, int MaxConcurrency = 2, string? Notes = null, string VerificationCategory = "Overkill");
public sealed record RenderQueryProfileRequest(IReadOnlyDictionary<string, string> Parameters);
public sealed record QueryProfileResponse(Guid Id, string Name, Guid? DataSourceId, string? ConnectionName, string MachineCode, string LineCode, string VerificationCategory, string Concept, string SourceSystem, string SqlTemplate, string ParameterSchemaJson, int TimeoutSeconds, int MaxConcurrency, bool Enabled, string? Notes, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record RenderedQueryResponse(Guid QueryProfileId, string RenderedSql, IReadOnlyCollection<string> MissingParameters, IReadOnlyCollection<string> Warnings);
public sealed record QueryTestResponse(Guid QueryProfileId, bool Success, int RowCount, IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, string Message, int LatencyMs);
public sealed record ProductRecordDto(Guid Id, string KeyId, string Judge, string PanelId, string PanelJudge, string NameDefect, string MachineCode, string LineCode, DateTimeOffset InspectedAtUtc, string? MetadataJson, string? Zone = null, string? DefectName = null, string? DefectPackUrl = null, string? OpReviewMark = null, string? OpReviewJudge = null, string? OpReviewDefectName = null, string? OpReviewMappedDefect = null, string? OpReviewNote = null, DateTimeOffset? OpReviewedAtUtc = null);
public sealed record ProductFilterOptionsDto(
    IReadOnlyCollection<string> Judges,
    IReadOnlyCollection<string> Zones,
    IReadOnlyCollection<string> DefectNames,
    IReadOnlyCollection<string> MachineCodes,
    IReadOnlyCollection<string> LineCodes
);
public sealed record DefectRecordDto(
    Guid Id,
    string KeyId,
    string DefectTable,
    string DefectName,
    string DefectType,
    string? ComponentName,
    double? CoordinateX,
    double? CoordinateY,
    string Severity,
    string Judge,
    double ConfidenceScore,
    string? ImageUri,
    string? Feature1StorageUri,
    string? Feature2StorageUri,
    DateTimeOffset CreatedAtUtc,
    string? FeatureJson = null,
    string? CustomFeatureJson = null,
    string? Reason = null,
    string? EqpName = null,
    string? EmNo = null,
    string? SpecName = null,
    string? SpecJudge = null,
    string? SpecReason = null,
    int? Priority = null,
    string? FeatureUrl = null,
    string? CustomFeatureUrl = null,
    string? ImageUrl = null,
    string? ModelCode = null,
    Guid? ConnectionId = null,
    string? MachineCode = null,
    string? LineCode = null
);
public sealed record ProductScanItemDto(ProductRecordDto Product, IReadOnlyCollection<DefectRecordDto> Defects, int TotalDefects, string AnomalyStatus, double AnomalyScore, string AnalysisNotes);
public sealed record MultiTableScanRequest(Guid? ConnectionId, IReadOnlyCollection<string>? KeyIds, string? PanelId, string? MachineCode, string? LineCode, bool IncludeDefects = true);
public sealed record MultiTableScanResponse(int ScannedProductsCount, int TotalDefectsFound, int OverkillCount, int LeakageCount, IReadOnlyCollection<ProductScanItemDto> Items, int ExecutionLatencyMs);
public sealed record CreateProductRecordRequest(string KeyId, string Judge, string PanelId, string PanelJudge, string NameDefect, string MachineCode, string LineCode, string? Zone = null, string? DefectName = null);
public sealed record CreateDefectRecordRequest(
    string KeyId,
    string DefectTable,
    string DefectName,
    string DefectType,
    string? ComponentName,
    double? CoordinateX,
    double? CoordinateY,
    string Severity,
    string Judge,
    double ConfidenceScore,
    string? ImageUri = null,
    string? FeatureJson = null,
    string? CustomFeatureJson = null,
    string? Reason = null,
    string? EqpName = null,
    string? EmNo = null,
    string? SpecName = null,
    string? SpecJudge = null,
    string? SpecReason = null,
    int? Priority = null,
    string? FeatureUrl = null,
    string? CustomFeatureUrl = null,
    string? ImageUrl = null,
    string? ModelCode = null,
    Guid? ConnectionId = null,
    string? MachineCode = null,
    string? LineCode = null
);
public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record ScanListItemDto(int RowNumber, string EntityId, string LineCode, int? MappingInt, string? Mapping);
public sealed record VerifyBatchRequest(Guid? ScanListId, Guid? ConnectionId, Guid? QueryProfileId, string? RawText, string? MachineCode, string? LineCode, string MappingName = "entityId");
public sealed record VerificationHistoryResponse(Guid Id, string Category, string EntityId, string LineCode, string? Mapping, string MachineCode, string Judge, string PanelJudge, string NameDefect, string AnomalyStatus, double AnomalyScore, string AnalysisNotes, int TotalDefects, bool Eligible, Guid? ScanListId, Guid? QueryProfileId, Guid? ConnectionId, string? ConnectionName, string? QueryName, DateTimeOffset CreatedAtUtc);
public sealed record VerifyBatchResponse(string Category, int TotalItems, int EligibleItems, int SkippedItems, int OverkillCount, int LeakageCount, int ConsistentNgCount, Guid? ScanListId, Guid? ConnectionId, string? ConnectionName, IReadOnlyCollection<VerificationHistoryResponse> Items);
public sealed record OverkillSystemShareDto(string SystemName, string DefectType, int TotalDefects, int NgDefects, double NgShare, int LinkedNgProducts, int OverkillSuspects, string Guidance, IReadOnlyCollection<string> SampleKeyIds);
public sealed record OverkillAnalysisResponse(string MachineCode, string LineCode, int EligibleNgProducts, int TotalDefects, IReadOnlyCollection<OverkillSystemShareDto> Systems, IReadOnlyCollection<string> Recommendations);

public sealed record DefectTableMapping(
    string TableName,
    string? DisplayName = null,
    string FkColumnName = "Fk_ID",
    string DefectNameColumn = "DefectName",
    string? DefectTypeColumn = null,
    string? SeverityColumn = null,
    string? JudgeColumn = null,
    string? ComponentNameColumn = null,
    string? CoordXColumn = null,
    string? CoordYColumn = null,
    string? ImageUriColumn = null,
    string? Feature1JsonColumn = null,
    string? Feature2JsonColumn = null,
    string? FeatureColumn = null,
    string? CustomFeatureColumn = null,
    string? ReasonColumn = null,
    string? EqpNameColumn = null,
    string? EmNoColumn = null,
    string? SpecNameColumn = null,
    string? SpecJudgeColumn = null,
    string? SpecReasonColumn = null,
    string? PriorityColumn = null,
    bool? OffloadToStorage = null
);

public sealed record DefectBypassConditionDto(
    string Field,
    string Operator,
    string? Value = null,
    IReadOnlyCollection<string>? Values = null
);

public sealed record DefectBypassNameConditionDto(
    string Field = "DefectName",
    string Operator = "NOT IN",
    IReadOnlyCollection<string>? Values = null
);

public sealed record DefectBypassRuleDto(
    bool Enabled = false,
    string Conjunction = "AND",
    DefectBypassConditionDto? NoDefectCondition = null,
    DefectBypassNameConditionDto? NameCondition = null,
    bool CaseInsensitive = true,
    string NullPolicy = "PreserveRow"
);

public sealed record RawDefectRow(
    string KeyId,
    string DefectTable,
    string DefectName,
    string DefectType,
    string? ComponentName,
    double? CoordinateX,
    double? CoordinateY,
    string Severity,
    string Judge,
    string? ImageUri,
    string? FeatureJson = null,
    string? CustomFeatureJson = null,
    string? Reason = null,
    string? EqpName = null,
    string? EmNo = null,
    string? SpecName = null,
    string? SpecJudge = null,
    string? SpecReason = null,
    int? Priority = null
);

public sealed record DefectTableMappingDto(
    string TableName,
    string? DisplayName,
    string FkColumn,
    string DefectNameColumn,
    string? DefectTypeColumn,
    string? SeverityColumn,
    string? JudgeColumn,
    string? ComponentColumn,
    string? CoordXColumn,
    string? CoordYColumn,
    string? ImageUriColumn,
    string? Feature1JsonColumn = null,
    string? Feature2JsonColumn = null,
    string? FeatureColumn = null,
    string? CustomFeatureColumn = null,
    string? ReasonColumn = null,
    string? EqpNameColumn = null,
    string? EmNoColumn = null,
    string? SpecNameColumn = null,
    string? SpecJudgeColumn = null,
    string? SpecReasonColumn = null,
    string? PriorityColumn = null,
    bool? OffloadToStorage = null
);

public sealed record OverkillPipelineConfigDto(
    Guid Id,
    string Name,
    Guid DatabaseConnectionId,
    string ConnectionName,
    string MachineCode,
    string LineCode,
    string ModelCode,
    bool Enabled,
    int ScheduleIntervalMinutes,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc,
    string LastRunStatus,
    string? LastRunMessage,
    int LastRunProductsCount,
    int LastRunDefectsCount,
    string ProductTableName,
    string IdColumnName,
    string FkIdColumnName,
    string JudgeColumnName,
    IReadOnlyCollection<string> JudgeFilterValues,
    string? TimestampColumnName,
    int LookbackHours,
    int BatchSize,
    IReadOnlyCollection<DefectTableMapping> DefectTables,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    string? DateTimeInspectionColumnName = "datetime_inspection",
    string? InspectedAtColumnName = null,
    bool OffloadDefectsToSeaweedFs = true,
    bool IncludeOkProducts = true
);

public sealed record SaveOverkillPipelineConfigRequest(
    string Name,
    Guid DatabaseConnectionId,
    string? MachineCode = null,
    string? LineCode = null,
    bool Enabled = true,
    int ScheduleIntervalMinutes = 120,
    string ProductTableName = "Product",
    string IdColumnName = "ID",
    string FkIdColumnName = "Fk_ID",
    string JudgeColumnName = "Judge",
    IReadOnlyCollection<string>? JudgeFilterValues = null,
    string? TimestampColumnName = null,
    int LookbackHours = 6,
    int BatchSize = 1000,
    IReadOnlyCollection<DefectTableMapping>? DefectTables = null,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    string? DateTimeInspectionColumnName = "datetime_inspection",
    string? InspectedAtColumnName = null,
    bool OffloadDefectsToSeaweedFs = true,
    DefectBypassRuleDto? BypassRule = null,
    string? DefectBypassRuleJson = null,
    bool IncludeOkProducts = true,
    string? ImageCrawlerApiUrl = null,
    bool AutoCrawlImages = false
);

public sealed record OverkillPipelineConfigResponse(
    Guid Id,
    string Name,
    Guid DatabaseConnectionId,
    string ConnectionName,
    string MachineCode,
    string LineCode,
    bool Enabled,
    int ScheduleIntervalMinutes,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc,
    string LastRunStatus,
    string? LastRunMessage,
    int LastRunProductsCount,
    int LastRunDefectsCount,
    string ProductTableName,
    string IdColumnName,
    string FkIdColumnName,
    string JudgeColumnName,
    IReadOnlyCollection<string> JudgeFilterValues,
    string? TimestampColumnName,
    int LookbackHours,
    int BatchSize,
    IReadOnlyCollection<DefectTableMapping> DefectTables,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    string? DateTimeInspectionColumnName = "datetime_inspection",
    string? InspectedAtColumnName = null,
    bool OffloadDefectsToSeaweedFs = true,
    DefectBypassRuleDto? BypassRule = null,
    int LastRunDefectsBypassedCount = 0,
    bool IncludeOkProducts = true,
    string? ImageCrawlerApiUrl = null,
    bool AutoCrawlImages = false,
    string ImageCrawlerVpMode = "0",
    string ModelCode = "UNASSIGNED"
);

public sealed record CreateOverkillPipelineConfigRequest(
    string Name,
    Guid DatabaseConnectionId,
    string? MachineCode = null,
    string? LineCode = null,
    bool Enabled = true,
    int ScheduleIntervalMinutes = 120,
    string ProductTableName = "Product",
    string IdColumnName = "ID",
    string FkIdColumnName = "Fk_ID",
    string JudgeColumnName = "Judge",
    IReadOnlyCollection<string>? JudgeFilterValues = null,
    string? TimestampColumnName = null,
    int LookbackHours = 6,
    int BatchSize = 1000,
    IReadOnlyCollection<DefectTableMapping>? DefectTables = null,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    string? DateTimeInspectionColumnName = "datetime_inspection",
    string? InspectedAtColumnName = null,
    bool OffloadDefectsToSeaweedFs = true,
    DefectBypassRuleDto? BypassRule = null,
    string? DefectBypassRuleJson = null,
    bool IncludeOkProducts = true,
    string? ImageCrawlerApiUrl = null,
    bool AutoCrawlImages = false,
    string ImageCrawlerVpMode = "0",
    string ModelCode = "UNASSIGNED"
);

public sealed record TestOverkillMappingRequest(
    Guid DatabaseConnectionId,
    string ProductTableName,
    string IdColumnName,
    string FkIdColumnName,
    string JudgeColumnName,
    IReadOnlyCollection<string>? JudgeFilterValues,
    IReadOnlyCollection<DefectTableMapping> DefectTables,
    int SampleLimit = 5,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    string? DateTimeInspectionColumnName = "datetime_inspection",
    string? InspectedAtColumnName = null
);

public sealed record TestOverkillMappingResponse(
    bool Success,
    int SampleProductsCount,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> SampleProducts,
    IReadOnlyDictionary<string, int> DefectCountsByTable,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> SampleDefects,
    int LatencyMs,
    string? ErrorMessage
);

public sealed record TestPipelineMappingRequest(
    Guid DatabaseConnectionId,
    string ProductTableName,
    string IdColumnName,
    string FkIdColumnName,
    string JudgeColumnName,
    IReadOnlyCollection<string>? JudgeFilterValues,
    IReadOnlyCollection<DefectTableMapping> DefectTables,
    int SampleLimit = 5,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    string? DateTimeInspectionColumnName = "datetime_inspection",
    string? InspectedAtColumnName = null,
    DefectBypassRuleDto? BypassRule = null,
    int DefectSampleLimit = 2000
);

public sealed record TestPipelineMappingResponse(
    bool Success,
    int SampleProductsCount,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> SampleProducts,
    IReadOnlyDictionary<string, int> DefectCountsByTable,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> SampleDefects,
    int LatencyMs,
    string? ErrorMessage,
    int BypassedDefectsCount = 0,
    string? BypassRuleSummary = null,
    int ScannedDefectsCount = 0,
    int AcceptedDefectsCount = 0,
    string? GeneratedDefectSql = null,
    int AlreadySavedProductsCount = 0
);

public sealed record OverkillPipelineExecutionDto(
    Guid Id,
    Guid ConfigId,
    string PipelineName,
    string TriggerType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Status,
    int ProductsScanned,
    int DefectsExtracted,
    int OverkillCandidatesCount,
    int LatencyMs,
    string? ErrorMessage,
    string? ExecutionSummaryJson = null,
    int DefectsBypassed = 0,
    string? BypassRuleSummary = null,
    int DefectsScanned = 0
);

public sealed record OverkillPipelineExecutionResponse(
    Guid Id,
    Guid ConfigId,
    string PipelineName,
    string TriggerType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string Status,
    int ProductsScanned,
    int DefectsExtracted,
    int OverkillCandidatesCount,
    int LatencyMs,
    string? ErrorMessage,
    string? ExecutionSummaryJson = null,
    int DefectsBypassed = 0,
    string? BypassRuleSummary = null,
    int DefectsScanned = 0
);

public sealed record OverkillRunResultDto(
    Guid ExecutionId,
    Guid ConfigId,
    string Status,
    string Message,
    int ProductsScanned = 0,
    int DefectsExtracted = 0,
    int OverkillCandidatesCount = 0
);

public sealed record TriggerPipelineExecutionResponse(
    Guid ExecutionId,
    Guid ConfigId,
    string Status,
    string Message
);

// Leakage Manual Query & Verification Contracts
public sealed record LeakageQueueItemDto(
    Guid Id,
    Guid BatchId,
    string ProductId,
    string? LineCode,
    string Status,
    string? ResolvedFkId,
    string? StationJudge,
    double AnomalyScore,
    bool IsLeakageSuspect,
    int DefectCount,
    Guid? ConnectionId,
    string? TableScanned,
    string? ErrorMessage,
    int RetryCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ProcessedAtUtc
);

public sealed record LeakageBatchDto(
    Guid Id,
    string Name,
    string SourceType,
    string? RawInputSummary,
    int TotalCount,
    int PendingCount,
    int ProcessingCount,
    int CompletedCount,
    int NotFoundCount,
    int LeakageCount,
    int ConsistentCount,
    int FailedCount,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ProcessedAtUtc
);

public sealed record LeakageQueueItemDetailDto(
    LeakageQueueItemDto QueueItem,
    ProductRecordDto? Product,
    IReadOnlyCollection<DefectRecordDto> Defects
);

public sealed record EnqueuePasteRequest(
    string Text,
    string? BatchName = null,
    string? DefaultLineCode = null
);

public sealed record ProcessLeakageQueueRequest(
    Guid? BatchId = null,
    IReadOnlyCollection<Guid>? ItemIds = null,
    int? MaxDegreeOfParallelism = null
);

public sealed record LeakageQueueProgressDto(
    bool IsRunning,
    Guid? CurrentBatchId,
    int ProcessedCount,
    int TotalCount,
    int PendingCount,
    int LeakageCount,
    int NotFoundCount,
    int FailedCount
);

public sealed record RetryQueueItemsRequest(
    Guid? BatchId = null,
    IReadOnlyCollection<Guid>? ItemIds = null
);

public sealed record ClearQueueRequest(
    Guid? BatchId = null,
    IReadOnlyCollection<Guid>? ItemIds = null,
    bool ClearCompletedOnly = false
);

public sealed record CreateDefectPreviewSessionRequest(
    string? StorageUri = null,
    string? SourceUri = null,
    string? KeyId = null,
    int? TtlMinutes = 120,
    string? Format = null,
    int? MaxRows = null
)
{
    public string EffectiveUri => !string.IsNullOrWhiteSpace(StorageUri)
        ? StorageUri
        : (SourceUri ?? string.Empty);
}

public sealed record DefectPreviewSessionDto(
    Guid SessionId,
    string Status,
    string SourceUri,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? KeyId = null,
    int ProcessedRecords = 0,
    int TotalRecords = 0,
    string? DetectedFormat = null,
    string? ProcessingStage = null,
    string? WarningMessage = null
);

public sealed record DefectPreviewFilterSummaryDto(
    int Total,
    int NgCount,
    int OkCount,
    IReadOnlyDictionary<string, int> SeverityBreakdown
);

public sealed record DefectPreviewProgressDto(
    Guid SessionId,
    string Status,
    int ProcessedRecords,
    int TotalRecords,
    double PercentComplete,
    DefectPreviewFilterSummaryDto? FilterSummary,
    string? ErrorMessage,
    string? DetectedFormat = null,
    string? ProcessingStage = null,
    string? WarningMessage = null
);

public sealed record DefectPreviewRecordDto(
    long Id,
    int RowIndex,
    string? DefectId,
    string? KeyId,
    string? DefectName,
    string? DefectTable,
    string? DefectType,
    string? ComponentName,
    double? CoordinateX,
    double? CoordinateY,
    string? Severity,
    string? Judge,
    double? ConfidenceScore,
    bool HasFeatureJson,
    string? FeatureJsonSnippet,
    string? RawFeatureUri,
    string? RawImageUri,
    DateTimeOffset? InspectedAtUtc,
    Guid? ObservationId = null,
    string? LineCode = null,
    string? MachineCode = null,
    string? StationCode = null,
    string? ClassifierRuleCode = null,
    string? ClassifierRuleName = null,
    string? ClassifierRuleVersion = null,
    string? DefectFamily = null,
    string? DefectClass = null
);

public sealed record DefectPreviewPagedResponse(
    IReadOnlyList<DefectPreviewRecordDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

public sealed record DefectPreviewQueryParams(
    int Page = 1,
    int PageSize = 50,
    string? SortBy = "rowIndex",
    string? SortDir = "asc",
    string? Keyword = null,
    string? Judge = null,
    string? Severity = null,
    string? DefectTable = null,
    string? LineCode = null,
    string? MachineCode = null,
    string? StationCode = null,
    string? ClassifierRuleCode = null,
    string? ClassifierRuleVersion = null,
    string? DefectFamily = null,
    Guid? ObservationId = null
);

// -------------------------------------------------------------
// Classifier-Driven Overkill, Real NG, Leakage & Thumbnail Contracts
// -------------------------------------------------------------

public sealed record OverkillTrendSummaryDto(
    int TotalInspected,
    int StationNgCount,
    int RealNgCount,
    int OverkillCount,
    int NaCount,
    int LeakageCount,
    int ConsistentOkCount,
    int OpReviewedCount,
    int OpRealDefectCount,
    int OpOverkillCount,
    double StationNgRate,
    double RealNgRate,
    double OverkillRate,
    double OverkillShare,
    double LeakageRate
);

public sealed record OverkillTrendPointDto(
    DateTimeOffset Timestamp,
    string DateLabel,
    int TotalInspected,
    int StationNgCount,
    int RealNgCount,
    int OverkillCount,
    int NaCount,
    int LeakageCount,
    double StationNgRate,
    double RealNgRate,
    double OverkillRate,
    double OverkillShare
);

public sealed record DefectFamilyOverkillDto(
    string DefectFamily,
    int TotalDefects,
    int RealNgCount,
    int OverkillCount,
    double OverkillShare,
    string? ActiveRuleCode,
    IReadOnlyCollection<string> TopDefectNames,
    double? MeanConfidence,
    double? MeanArea
);

public sealed record DefectClassOverkillDto(
    string DefectName,
    string DefectFamily,
    int TotalDefects,
    int RealNgCount,
    int OverkillCount,
    double OverkillShare,
    string? RuleCode,
    string EvaluationGuidance
);

public sealed record LineOverkillComparisonDto(
    string LineCode,
    string MachineCode,
    int TotalInspected,
    int OkCount,
    int StationNgCount,
    double StationNgRate,
    int RealNgCount,
    double RealNgRate,
    int OverkillCount,
    int NaCount,
    double OverkillRate,
    double OverkillShare,
    int LeakageCount,
    int OpMappedCount,
    double OpMappedRate,
    string RiskLevel,
    string StatusAssessment
);

public sealed record OverkillTrendResponse(
    string MachineCode,
    string LineCode,
    string DefectFamily,
    OverkillTrendSummaryDto Summary,
    IReadOnlyCollection<OverkillTrendPointDto> TrendPoints,
    IReadOnlyCollection<DefectFamilyOverkillDto> FamilyBreakdown,
    IReadOnlyCollection<DefectClassOverkillDto> ClassBreakdown,
    IReadOnlyCollection<LineOverkillComparisonDto> LineComparison,
    IReadOnlyCollection<string> Recommendations
);

public sealed record AnomalyThumbnailItemDto(
    string KeyId,
    Guid? DefectId,
    Guid ProductId,
    string StationJudge,
    string AnomalyCategory, // "Overkill", "Leakage", "RealNG", "ConsistentOK"
    double AnomalyScore,
    string MachineCode,
    string LineCode,
    string ModelCode,
    string? StationCode,
    DateTimeOffset InspectedAtUtc,
    string? DefectName,
    string? DefectFamily,
    string? DefectType,
    string? Severity,
    string? ComponentName,
    string? Coordinates,
    double? ConfidenceScore,
    string? ThumbnailUrl,
    string? ImageUri,
    string? RuleCode,
    string? RuleVersion,
    string? RuleVerdict,
    Dictionary<string, double>? KeyFeatures,
    string DecisionReason,
    string? OriginalVp = null,
    string? PanelId = null,
    string? OpReviewMark = null,
    string? ProductNameDefect = null,
    string? ProductDefectName = null,
    string? AiVerdict = null,
    string? AiStatus = null,
    double? AiAnomalyScore = null,
    string? DecisionRoute = null
);

public sealed record AnomalyThumbnailQueryRequest(
    string? Category = "All",
    string? DefectFamily = null,
    string? MachineCode = null,
    string? LineCode = null,
    string? ModelCode = null,
    string? DefectCluster = null,
    string? Search = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Page = 1,
    int PageSize = 24
);

public sealed record SyncDbSourceRequest(
    Guid? ConnectionId = null,
    string? LineCode = null,
    int? LookbackHours = 24,
    int Limit = 100
);

public sealed record SyncDbSourceResponse(
    bool Success,
    int SyncedProductsCount,
    int SyncedDefectsCount,
    int OverkillCount,
    int LeakageCount,
    string Message
);

// -------------------------------------------------------------
// OP Defect Mapping & Excel Reconciliation Contracts
// -------------------------------------------------------------

public sealed record OpDefectMappingDto(
    Guid Id,
    string MachineCode,
    string PackageName,
    string OpDefectName,
    string MachineDefectName,
    string DefectFamily,
    string ComponentGroup,
    bool IsConfirmedDefect,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

public sealed record SaveOpDefectMappingRequest(
    string OpDefectName,
    string MachineDefectName,
    string? DefectFamily = "Optical",
    string? ComponentGroup = null,
    string? MachineCode = "ALL",
    string? PackageName = "Default",
    bool IsConfirmedDefect = true,
    string? Description = null
);

public sealed record BatchSaveOpDefectMappingRequest(
    List<SaveOpDefectMappingRequest> Mappings,
    string? MachineCode = null,
    string? PackageName = null
);

public sealed record ClsfRuleOccurrenceDto(
    int Priority,
    string RuleName,
    string? DefectTypeId,
    bool IsOccurring,
    int OccurrenceCount,
    double OccurrencePercent,
    string? VpScope,
    string? Script
);

public sealed record ClsfPackageAnalysisResponse(
    bool Success,
    string FileName,
    string PackageName,
    string MachineCode,
    string ModelCode,
    int TotalRules,
    int ActiveRuleCount,
    int TotalDefectOccurrences,
    IReadOnlyCollection<ClsfRuleOccurrenceDto> Rules,
    string Message
);

public sealed record UploadOpExcelResponse(
    bool Success,
    Guid BatchId,
    int TotalRecordsParsed,
    int TotalRecordsSaved,
    int CreatedCount,
    int UpdatedCount,
    int RejectedCount,
    int MappedCount,
    int UnmappedCount,
    int MappingRulesImported,
    IReadOnlyCollection<string> SheetNames,
    string Message
);

public sealed record PasteOpRecordsRequest(
    string Text,
    string? MachineCode = null,
    string? LineCode = null,
    string? DefaultJudge = null
);

public sealed record OpReconciliationSummaryDto(
    int TotalReconciled,
    int TotalMachineNg,
    int RealDefectCount,
    int OverkillCount,
    int LeakageCount,
    int ConsistentOkCount,
    int NoDetectCount,
    double RealDefectRate,
    double OverkillRate,
    double LeakageRate
);

public sealed record OpDefectReconciliationItemDto(
    string MachineDefectName,
    string DefectFamily,
    int TotalMachineNg,
    int RealDefectCount,
    int OverkillCount,
    double RealDefectRate,
    double OverkillRate,
    IReadOnlyCollection<string> AssociatedOpDefectNames,
    string OverkillSeverityAssessment
);

public sealed record OverkillFeatureDistributionDto(
    string FeatureName,
    double OverkillMean,
    double OverkillMin,
    double OverkillMax,
    double RealDefectMean,
    double RealDefectMin,
    double RealDefectMax,
    string SeparationAssessment
);

public sealed record OverkillSampleDto(
    string KeyId,
    string MachineDefectName,
    string DefectFamily,
    string? OpDefectName,
    string OpJudge,
    DateTimeOffset InspectedAtUtc,
    string? LineCode,
    string? MachineCode,
    Dictionary<string, double>? KeyFeatures,
    string? ThumbnailUrl,
    string Reason
);

public sealed record OverkillDeepDiveAnalysisDto(
    string TargetDefectName,
    int OverkillCount,
    int RealDefectCount,
    double OverkillRate,
    IReadOnlyCollection<OverkillFeatureDistributionDto> FeatureDistributions,
    IReadOnlyCollection<string> ThresholdTuningRecommendations,
    IReadOnlyCollection<OverkillSampleDto> Samples
);

public sealed record OpReconciliationAnalysisResponse(
    OpReconciliationSummaryDto Summary,
    IReadOnlyCollection<OpDefectReconciliationItemDto> DefectBreakdown,
    OverkillDeepDiveAnalysisDto? DeepDive,
    IReadOnlyCollection<string> AvailableDefectTypes,
    IReadOnlyCollection<string> Recommendations
);

public sealed record OpReconciliationRecordDto(
    Guid Id,
    Guid BatchId,
    string KeyId,
    string OpJudge,
    string? OpDefectName,
    string? MappedMachineDefect,
    string? MappedDefectFamily,
    string? MachineCode,
    string? LineCode,
    DateTimeOffset OpInspectedAtUtc,
    bool ProductFound,
    string? ProductJudge,
    string? ProductNameDefect,
    string? ProductDefectName,
    IReadOnlyCollection<OpReconciliationDefectDto> Defects,
    string? ProductMachineCode,
    string? ProductLineCode,
    DateTimeOffset? ProductInspectedAtUtc,
    string Classification
);

public sealed record OpReconciliationDefectDto(
    Guid Id,
    string DefectName,
    string DefectType,
    string DefectTable,
    string? Severity
);

public sealed record OpReconciliationRecordsResponse(
    IReadOnlyCollection<OpReconciliationRecordDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int MatchedCount,
    int NotFoundCount,
    int RealDefectCount,
    int OverkillCount,
    int LeakageCount,
    int ConsistentOkCount,
    int NoDetectCount,
    double RealDefectRate,
    double OverkillRate
);

// -------------------------------------------------------------
// Multi-Line / Multi-Machine Batch Provisioning Contracts
// -------------------------------------------------------------

public sealed record BatchProvisionLineItem(
    string LineCode,
    string MachineCode,
    string? Host = null,
    string? DatabaseName = null,
    string? Username = null,
    string? Password = null,
    string? ModelCode = null
);

public sealed record BatchProvisionRequest(
    string Engine = "MariaDB",
    string Host = "127.0.0.1",
    int Port = 3306,
    string DatabaseName = "ProductDB",
    string? Username = null,
    string? Password = null,
    List<string>? Paths = null,
    List<BatchProvisionLineItem>? Lines = null,
    string? LinePrefix = "LINE-",
    int? LineStart = null,
    int? LineEnd = null,
    int LinePad = 2,
    string? MachinePrefix = "AOI-",
    int? MachineStart = null,
    int? MachineEnd = null,
    int MachinePad = 2,
    bool CreatePipelines = true,
    int ScheduleIntervalMinutes = 60,
    int LookbackHours = 6,
    int BatchSize = 1000,
    string ProductTableName = "Product",
    string IdColumnName = "ID",
    string FkIdColumnName = "Fk_ID",
    string JudgeColumnName = "Judge",
    List<string>? JudgeFilterValues = null,
    bool IncludeOkProducts = true,
    string? TimestampColumnName = "datetime_inspection",
    string? DateTimeInspectionColumnName = null,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    List<DefectTableMapping>? DefectTables = null,
    bool OffloadDefectsToSeaweedFs = true,
    DefectBypassRuleDto? BypassRule = null,
    string? ImageCrawlerApiUrl = null,
    bool AutoCrawlImages = false,
    string ImageCrawlerVpMode = "0"
);

public sealed record ScanDirectoryPathsRequest(
    string RootPath,
    string? SearchPattern = null,
    int MaxDepth = 1,
    string DefaultEngine = "MariaDB",
    string DefaultHost = "127.0.0.1",
    int DefaultPort = 3306,
    string DefaultDbPattern = "aoi_{line}",
    string? DefaultUsername = "root",
    string? DefaultPassword = null
);

public sealed record ScannedPathItem(
    string FullPath,
    string LineCode,
    string MachineCode,
    string ModelCode,
    string Host,
    string DatabaseName
);

public sealed record ScanDirectoryPathsResponse(
    bool Success,
    string RootPath,
    int TotalFound,
    List<ScannedPathItem> Items,
    string? ErrorMessage = null
);

public sealed record BatchProvisionResponse(
    bool Success,
    int CreatedConnectionsCount,
    int UpdatedConnectionsCount,
    int CreatedPipelinesCount,
    int UpdatedPipelinesCount,
    int TotalItemsProcessed,
    List<string> ProcessedItems,
    string Message
);

public sealed record BatchGeneratePipelinesForConnectionsRequest(
    List<Guid>? ConnectionIds = null,
    int ScheduleIntervalMinutes = 60,
    int LookbackHours = 6,
    int BatchSize = 1000,
    string ProductTableName = "Product",
    string IdColumnName = "ID",
    string FkIdColumnName = "Fk_ID",
    string JudgeColumnName = "Judge",
    List<string>? JudgeFilterValues = null,
    bool IncludeOkProducts = true,
    string? TimestampColumnName = "datetime_inspection",
    string? DateTimeInspectionColumnName = null,
    string? ZoneColumnName = "zone",
    string? DefectNameColumnName = "defect_name",
    List<DefectTableMapping>? DefectTables = null,
    bool OffloadDefectsToSeaweedFs = true,
    DefectBypassRuleDto? BypassRule = null,
    string? ImageCrawlerApiUrl = null,
    bool AutoCrawlImages = false,
    string ImageCrawlerVpMode = "0",
    string? ModelCode = null
);

public sealed record BatchDeleteConnectionsRequest(
    List<Guid> ConnectionIds,
    bool DeleteLinkedPipelines = true
);

public sealed record BatchDeletePipelinesRequest(
    List<Guid> PipelineIds
);

// -------------------------------------------------------------
// Image Crawler Contracts (Per-PC / Per-Machine Image Fetching)
// -------------------------------------------------------------
public sealed record ImageCrawlResult(
    bool Success,
    byte[]? ImageBytes,
    string? ContentType,
    string? StoredUrl,
    string? ErrorMessage,
    int StatusCode = 200,
    int LatencyMs = 0
);

public sealed record CrawlDefectImageRequest(
    Guid DefectId,
    bool ForceRefresh = false,
    string? CustomApiUrl = null,
    string? CustomVp = null
);

public sealed record CrawlDefectImageResponse(
    bool Success,
    Guid DefectId,
    string? ImagePath,
    string? ImageUrl,
    string? ContentType,
    long SizeBytes,
    string? ErrorMessage,
    int LatencyMs
);

public sealed record TestImageCrawlerApiRequest(
    string ApiUrl,
    string ImagePath,
    string Method = "GET"
);

public sealed record TestImageCrawlerApiResponse(
    bool Success,
    int StatusCode,
    string? ContentType,
    long SizeBytes,
    string? Base64Preview,
    string? ErrorMessage,
    int LatencyMs
);

public sealed record BatchImageCrawlRequest(
    List<Guid>? DefectIds = null,
    string? KeyId = null,
    string? LineCode = null,
    string? MachineCode = null,
    int Limit = 50,
    bool ForceRefresh = false
);

public sealed record VpImageBrowseContext(
    string RawImagePath,
    string PanelId,
    string Line,
    string? ParsedLine,
    string Zone,
    string Vp,
    DateTime Date,
    string? TargetFileName
);

public sealed record BatchImageCrawlResponse(
    bool Success,
    int TotalRequested,
    int CrawledCount,
    int FailedCount,
    List<string> Messages
);

public sealed record TestDefectCrawlRequest(
    Guid DefectId,
    string? CustomApiUrl = null,
    string? CustomVp = null
);

public sealed record TestDefectCrawlResponse(
    bool Success,
    Guid DefectId,
    string DefectName,
    string KeyId,
    string? ImagePath,
    string ApiUrlUsed,
    object? RequestPayload,
    int StatusCode,
    string? ContentType,
    long SizeBytes,
    string? Base64Preview,
    string? ErrorMessage,
    int LatencyMs
);

public sealed record TestPathCrawlRequest(
    string ImagePath,
    string? ApiUrl = null,
    string? FeatureJson = null,
    string? Vp = null
);

public sealed record TestPathCrawlResponse(
    bool Success,
    string ImagePath,
    string ApiUrlUsed,
    object RequestPayload,
    int StatusCode,
    string? ContentType,
    long SizeBytes,
    string? Base64Preview,
    string? ErrorMessage,
    int LatencyMs
);

public sealed record SampleDefectForTestDto(
    Guid Id,
    string KeyId,
    string DefectName,
    string DefectTable,
    string? ImagePath,
    DateTimeOffset CreatedAtUtc
);
