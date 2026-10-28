-- AutoVerify MariaDB bootstrap schema
-- Generated from EF Core model at 2026-09-22T10:36:45.5228376+00:00
-- Execute only against a new/empty database.

ALTER DATABASE CHARACTER SET utf8mb4;


CREATE TABLE `AuditEvents` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `OccurredAtUtc` datetime(6) NOT NULL,
    `Actor` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Action` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `TargetType` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `TargetId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Message` text CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_AuditEvents` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `DatabaseConnections` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Engine` longtext CHARACTER SET utf8mb4 NOT NULL,
    `MachineCode` longtext CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ModelCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Host` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Port` int NOT NULL,
    `DatabaseName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Username` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ApiBaseUrl` longtext CHARACTER SET utf8mb4 NULL,
    `ImageCrawlerApiUrl` text CHARACTER SET utf8mb4 NULL,
    `ImageCrawlerVpMode` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `EncryptedPassword` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Health` longtext CHARACTER SET utf8mb4 NOT NULL,
    `AverageLatencyMs` int NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_DatabaseConnections` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `DataSources` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `Type` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `VerificationCategory` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `SchedulePeriod` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `Environment` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `Health` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `AverageLatencyMs` int NOT NULL,
    `Enabled` tinyint(1) NOT NULL,
    `Host` varchar(256) CHARACTER SET utf8mb4 NULL,
    `Port` int NULL,
    `DatabaseName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `Username` varchar(256) CHARACTER SET utf8mb4 NULL,
    `ApiBaseUrl` text CHARACTER SET utf8mb4 NULL,
    `EncryptedSecret` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_DataSources` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `defect_preview_sessions` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `StorageUri` varchar(1024) CHARACTER SET utf8mb4 NOT NULL,
    `KeyId` varchar(128) CHARACTER SET utf8mb4 NULL,
    `Format` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `DetectedFormat` varchar(64) CHARACTER SET utf8mb4 NULL,
    `ProcessingStage` varchar(64) CHARACTER SET utf8mb4 NULL,
    `Status` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `TotalRowsCount` int NOT NULL,
    `LoadedRowsCount` int NOT NULL,
    `ProcessedRecords` int NOT NULL,
    `NgCount` int NOT NULL,
    `OkCount` int NOT NULL,
    `SeverityBreakdownJson` longtext CHARACTER SET utf8mb4 NULL,
    `SourceSizeBytes` bigint NOT NULL,
    `ErrorMessage` text CHARACTER SET utf8mb4 NULL,
    `WarningMessage` text CHARACTER SET utf8mb4 NULL,
    `RepairsCount` int NOT NULL,
    `FilterSummaryJson` longtext CHARACTER SET utf8mb4 NULL,
    `TtlMinutes` int NOT NULL,
    `MaxRows` int NOT NULL DEFAULT 50000,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `StartedAtUtc` datetime(6) NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `LastAccessedAtUtc` datetime(6) NULL,
    `ExpiresAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_defect_preview_sessions` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `DefectRecords` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `KeyId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `DefectTable` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `DefectName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `DefectType` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ComponentName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `CoordinateX` double NULL,
    `CoordinateY` double NULL,
    `Severity` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `Judge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `ConfidenceScore` double NOT NULL,
    `ImageUri` text CHARACTER SET utf8mb4 NULL,
    `ImageUrl` text CHARACTER SET utf8mb4 NULL,
    `Feature1StorageUri` text CHARACTER SET utf8mb4 NULL,
    `Feature2StorageUri` text CHARACTER SET utf8mb4 NULL,
    `FeatureUrl` text CHARACTER SET utf8mb4 NULL,
    `CustomFeatureUrl` text CHARACTER SET utf8mb4 NULL,
    `FeatureJson` longtext CHARACTER SET utf8mb4 NULL,
    `CustomFeatureJson` longtext CHARACTER SET utf8mb4 NULL,
    `Reason` text CHARACTER SET utf8mb4 NULL,
    `EqpName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `EmNo` varchar(45) CHARACTER SET utf8mb4 NULL,
    `SpecName` varchar(50) CHARACTER SET utf8mb4 NULL,
    `SpecJudge` varchar(256) CHARACTER SET utf8mb4 NULL,
    `SpecReason` varchar(256) CHARACTER SET utf8mb4 NULL,
    `Priority` int NULL,
    `ModelCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'UNASSIGNED',
    `ConnectionId` char(36) COLLATE ascii_general_ci NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'MACHINE-A',
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'LINE-1',
    `CreatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_DefectRecords` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `LeakageBatches` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
    `SourceType` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `RawInputSummary` text CHARACTER SET utf8mb4 NULL,
    `TotalCount` int NOT NULL,
    `PendingCount` int NOT NULL,
    `ProcessingCount` int NOT NULL,
    `CompletedCount` int NOT NULL,
    `NotFoundCount` int NOT NULL,
    `LeakageCount` int NOT NULL,
    `ConsistentCount` int NOT NULL,
    `FailedCount` int NOT NULL,
    `CreatedBy` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `ProcessedAtUtc` datetime(6) NULL,
    CONSTRAINT `PK_LeakageBatches` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `machine_classifier_rules` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `RuleCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `RuleName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `Version` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ProductKey` varchar(128) CHARACTER SET utf8mb4 NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `StationCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DefectFamily` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DefectClass` varchar(128) CHARACTER SET utf8mb4 NULL,
    `ThresholdsConfigJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ModelMetadataJson` longtext CHARACTER SET utf8mb4 NULL,
    `ScriptContent` longtext CHARACTER SET utf8mb4 NULL,
    `Priority` int NOT NULL,
    `DefectTypeId` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `EffectiveFromUtc` datetime(6) NOT NULL,
    `EffectiveToUtc` datetime(6) NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `CreatedBy` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(1024) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_machine_classifier_rules` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `op_defect_mappings` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `PackageName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `OpDefectName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `MachineDefectName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `DefectFamily` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `ComponentGroup` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `IsConfirmedDefect` tinyint(1) NOT NULL,
    `Description` varchar(512) CHARACTER SET utf8mb4 NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    CONSTRAINT `PK_op_defect_mappings` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `OverkillPipelineConfigs` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `DatabaseConnectionId` char(36) COLLATE ascii_general_ci NOT NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `ModelCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Enabled` tinyint(1) NOT NULL,
    `ScheduleIntervalMinutes` int NOT NULL,
    `LastRunAtUtc` datetime(6) NULL,
    `NextRunAtUtc` datetime(6) NULL,
    `LastRunStatus` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `LastRunMessage` text CHARACTER SET utf8mb4 NULL,
    `LastRunProductsCount` int NOT NULL,
    `LastRunDefectsCount` int NOT NULL,
    `ProductTableName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `IdColumnName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `FkIdColumnName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `JudgeColumnName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `JudgeFilterValuesJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `IncludeOkProducts` tinyint(1) NOT NULL,
    `TimestampColumnName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `ZoneColumnName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DefectNameColumnName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DateTimeInspectionColumnName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `LookbackHours` int NOT NULL,
    `BatchSize` int NOT NULL,
    `DefectTablesJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `OffloadDefectsToSeaweedFs` tinyint(1) NOT NULL,
    `ImageCrawlerApiUrl` text CHARACTER SET utf8mb4 NULL,
    `AutoCrawlImages` tinyint(1) NOT NULL,
    `ImageCrawlerVpMode` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `DefectBypassRuleJson` longtext CHARACTER SET utf8mb4 NULL,
    `LastRunDefectsBypassedCount` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_OverkillPipelineConfigs` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `OverkillPipelineExecutions` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `ConfigId` char(36) COLLATE ascii_general_ci NOT NULL,
    `PipelineName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `TriggerType` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `Status` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ProductsScanned` int NOT NULL,
    `DefectsExtracted` int NOT NULL,
    `OverkillCandidatesCount` int NOT NULL,
    `DefectsBypassed` int NOT NULL,
    `DefectsScanned` int NOT NULL,
    `BypassRuleSummary` varchar(512) CHARACTER SET utf8mb4 NULL,
    `LatencyMs` int NOT NULL,
    `ErrorMessage` text CHARACTER SET utf8mb4 NULL,
    `ExecutionSummaryJson` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_OverkillPipelineExecutions` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `ProductQueries` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `QueryKey` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `ProductId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `SourceSystem` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Status` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `PayloadJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `JudgeResult` longtext CHARACTER SET utf8mb4 NULL,
    `Message` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_ProductQueries` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `ProductRecords` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `KeyId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Judge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `PanelId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `PanelJudge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `NameDefect` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `Zone` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DefectName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `ModelCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `OpReviewMark` varchar(32) CHARACTER SET utf8mb4 NULL,
    `OpReviewJudge` varchar(16) CHARACTER SET utf8mb4 NULL,
    `OpReviewDefectName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `OpReviewMappedDefect` varchar(256) CHARACTER SET utf8mb4 NULL,
    `OpReviewNote` varchar(1024) CHARACTER SET utf8mb4 NULL,
    `OpReviewedAtUtc` datetime(6) NULL,
    `InspectedAtUtc` datetime(6) NOT NULL,
    `MetadataJson` longtext CHARACTER SET utf8mb4 NULL,
    `DefectPackUrl` varchar(1024) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_ProductRecords` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `QueryProfiles` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `DataSourceId` char(36) COLLATE ascii_general_ci NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `VerificationCategory` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `Concept` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `SourceSystem` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `SqlTemplate` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ParameterSchemaJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TimeoutSeconds` int NOT NULL,
    `MaxConcurrency` int NOT NULL,
    `Enabled` tinyint(1) NOT NULL,
    `Notes` text CHARACTER SET utf8mb4 NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_QueryProfiles` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `ScanExecutions` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `ScanListId` char(36) COLLATE ascii_general_ci NOT NULL,
    `ScanListName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `Status` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `SourceNamesJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TotalItems` int NOT NULL,
    `PassedItems` int NOT NULL,
    `WarningItems` int NOT NULL,
    `FailedItems` int NOT NULL,
    `TechnicalErrorItems` int NOT NULL,
    `AnomalyItems` int NOT NULL,
    `EvidenceUri` text CHARACTER SET utf8mb4 NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_ScanExecutions` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `ScanListItems` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `ScanListId` char(36) COLLATE ascii_general_ci NOT NULL,
    `RowNumber` int NOT NULL,
    `EntityId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `MappingInt` int NULL,
    `Mapping` varchar(128) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_ScanListItems` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `ScanLists` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `MappingName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `VerificationCategory` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `AcceptedCount` int NOT NULL,
    `RejectedCount` int NOT NULL,
    `DuplicateCount` int NOT NULL,
    `Checksum` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `EvidenceUri` text CHARACTER SET utf8mb4 NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_ScanLists` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `StorageObjects` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Backend` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `Bucket` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `ObjectName` varchar(512) CHARACTER SET utf8mb4 NOT NULL,
    `ContentType` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `SizeBytes` bigint NOT NULL,
    `Uri` text CHARACTER SET utf8mb4 NOT NULL,
    `Path` text CHARACTER SET utf8mb4 NOT NULL,
    `MetadataJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Purpose` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_StorageObjects` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `Users` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Username` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `DisplayName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `PasswordHash` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_Users` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `VerificationHistory` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `Category` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `EntityId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Mapping` varchar(128) CHARACTER SET utf8mb4 NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Judge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `PanelJudge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `NameDefect` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `AnomalyStatus` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `AnomalyScore` double NOT NULL,
    `AnalysisNotes` text CHARACTER SET utf8mb4 NOT NULL,
    `TotalDefects` int NOT NULL,
    `Eligible` tinyint(1) NOT NULL,
    `ScanListId` char(36) COLLATE ascii_general_ci NULL,
    `QueryProfileId` char(36) COLLATE ascii_general_ci NULL,
    `ConnectionId` char(36) COLLATE ascii_general_ci NULL,
    `ConnectionName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `QueryName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `PayloadJson` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_VerificationHistory` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `VerificationResults` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `ExecutionId` char(36) COLLATE ascii_general_ci NOT NULL,
    `EntityId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `SourceName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `Status` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `Severity` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `RuleCode` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `Message` text CHARACTER SET utf8mb4 NOT NULL,
    `AnomalyScore` double NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_VerificationResults` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;


CREATE TABLE `defect_analytics_jobs` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `SessionId` char(36) COLLATE ascii_general_ci NOT NULL,
    `AnalysisType` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `Status` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `ProgressPercent` int NOT NULL,
    `RequestHash` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ConfigurationJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ResultJson` longtext CHARACTER SET utf8mb4 NULL,
    `WarningsJson` longtext CHARACTER SET utf8mb4 NULL,
    `ErrorMessage` text CHARACTER SET utf8mb4 NULL,
    `TtlMinutes` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `StartedAtUtc` datetime(6) NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `ExpiresAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_defect_analytics_jobs` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_defect_analytics_jobs_defect_preview_sessions_SessionId` FOREIGN KEY (`SessionId`) REFERENCES `defect_preview_sessions` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;


CREATE TABLE `defect_preview_features` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `SessionId` char(36) COLLATE ascii_general_ci NOT NULL,
    `ObservationId` char(36) COLLATE ascii_general_ci NOT NULL,
    `RowIndex` int NOT NULL,
    `FeatureName` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `FeatureValue` double NOT NULL,
    CONSTRAINT `PK_defect_preview_features` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_defect_preview_features_defect_preview_sessions_SessionId` FOREIGN KEY (`SessionId`) REFERENCES `defect_preview_sessions` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;


CREATE TABLE `defect_preview_records` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `SessionId` char(36) COLLATE ascii_general_ci NOT NULL,
    `ObservationId` char(36) COLLATE ascii_general_ci NOT NULL,
    `RowIndex` int NOT NULL,
    `DefectId` varchar(128) CHARACTER SET utf8mb4 NULL,
    `KeyId` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DefectName` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `DefectTable` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `DefectType` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ComponentName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `CoordinateX` double NULL,
    `CoordinateY` double NULL,
    `Severity` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `Judge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `ConfidenceScore` double NOT NULL,
    `Reason` text CHARACTER SET utf8mb4 NULL,
    `EqpName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `EmNo` varchar(64) CHARACTER SET utf8mb4 NULL,
    `SpecName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `SpecJudge` varchar(256) CHARACTER SET utf8mb4 NULL,
    `SpecReason` varchar(256) CHARACTER SET utf8mb4 NULL,
    `Priority` int NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `StationCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `ClassifierRuleCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `ClassifierRuleName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `ClassifierRuleVersion` varchar(64) CHARACTER SET utf8mb4 NULL,
    `DefectFamily` varchar(128) CHARACTER SET utf8mb4 NULL,
    `DefectClass` varchar(128) CHARACTER SET utf8mb4 NULL,
    `HasFeatureJson` tinyint(1) NOT NULL,
    `FeatureJsonSnippet` varchar(512) CHARACTER SET utf8mb4 NULL,
    `RawFeatureUri` varchar(1024) CHARACTER SET utf8mb4 NULL,
    `RawImageUri` varchar(1024) CHARACTER SET utf8mb4 NULL,
    `InspectedAtUtc` datetime(6) NULL,
    CONSTRAINT `PK_defect_preview_records` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_defect_preview_records_defect_preview_sessions_SessionId` FOREIGN KEY (`SessionId`) REFERENCES `defect_preview_sessions` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;


CREATE TABLE `LeakageQueueItems` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `BatchId` char(36) COLLATE ascii_general_ci NOT NULL,
    `ProductId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `Status` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ResolvedFkId` varchar(128) CHARACTER SET utf8mb4 NULL,
    `StationJudge` varchar(32) CHARACTER SET utf8mb4 NULL,
    `AnomalyScore` double NOT NULL,
    `IsLeakageSuspect` tinyint(1) NOT NULL,
    `DefectCount` int NOT NULL,
    `ConnectionId` char(36) COLLATE ascii_general_ci NULL,
    `TableScanned` varchar(256) CHARACTER SET utf8mb4 NULL,
    `ErrorMessage` text CHARACTER SET utf8mb4 NULL,
    `RetryCount` int NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `ProcessedAtUtc` datetime(6) NULL,
    CONSTRAINT `PK_LeakageQueueItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LeakageQueueItems_LeakageBatches_BatchId` FOREIGN KEY (`BatchId`) REFERENCES `LeakageBatches` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;


CREATE TABLE `op_inspection_records` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `BatchId` char(36) COLLATE ascii_general_ci NOT NULL,
    `ProductRecordId` char(36) COLLATE ascii_general_ci NULL,
    `KeyId` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `OpJudge` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    `OpDefectName` varchar(128) CHARACTER SET utf8mb4 NULL,
    `MappedMachineDefect` varchar(128) CHARACTER SET utf8mb4 NULL,
    `MappedDefectFamily` varchar(128) CHARACTER SET utf8mb4 NULL,
    `IsRealDefectConfirmed` tinyint(1) NULL,
    `LineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `MachineCode` varchar(128) CHARACTER SET utf8mb4 NULL,
    `Notes` varchar(512) CHARACTER SET utf8mb4 NULL,
    `InspectedAtUtc` datetime(6) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_op_inspection_records` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_op_inspection_records_ProductRecords_ProductRecordId` FOREIGN KEY (`ProductRecordId`) REFERENCES `ProductRecords` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;


CREATE INDEX `IX_AuditEvents_OccurredAtUtc` ON `AuditEvents` (`OccurredAtUtc`);


CREATE UNIQUE INDEX `IX_DatabaseConnections_Name` ON `DatabaseConnections` (`Name`);


CREATE INDEX `IX_DataSources_MachineCode` ON `DataSources` (`MachineCode`);


CREATE UNIQUE INDEX `IX_DataSources_Name` ON `DataSources` (`Name`);


CREATE INDEX `IX_DefectAnalytics_Session_Hash` ON `defect_analytics_jobs` (`SessionId`, `RequestHash`);


CREATE INDEX `IX_DefectAnalytics_Session_Type` ON `defect_analytics_jobs` (`SessionId`, `AnalysisType`);


CREATE INDEX `IX_DefectAnalytics_Status_Expires` ON `defect_analytics_jobs` (`Status`, `ExpiresAtUtc`);


CREATE INDEX `IX_DefectPreview_Feature_Session_Name` ON `defect_preview_features` (`SessionId`, `FeatureName`);


CREATE INDEX `IX_DefectPreview_Feature_Session_ObservationId` ON `defect_preview_features` (`SessionId`, `ObservationId`);


CREATE INDEX `IX_DefectPreview_Feature_Session_RowIndex` ON `defect_preview_features` (`SessionId`, `RowIndex`);


CREATE INDEX `IX_DefectPreview_Session_Component` ON `defect_preview_records` (`SessionId`, `ComponentName`);


CREATE INDEX `IX_DefectPreview_Session_DefectFamily` ON `defect_preview_records` (`SessionId`, `DefectFamily`);


CREATE INDEX `IX_DefectPreview_Session_DefectName` ON `defect_preview_records` (`SessionId`, `DefectName`);


CREATE INDEX `IX_DefectPreview_Session_Judge` ON `defect_preview_records` (`SessionId`, `Judge`);


CREATE INDEX `IX_DefectPreview_Session_Line_Machine` ON `defect_preview_records` (`SessionId`, `LineCode`, `MachineCode`);


CREATE INDEX `IX_DefectPreview_Session_ObservationId` ON `defect_preview_records` (`SessionId`, `ObservationId`);


CREATE INDEX `IX_DefectPreview_Session_RowIndex` ON `defect_preview_records` (`SessionId`, `RowIndex`);


CREATE INDEX `IX_DefectPreview_Session_Rule_Version` ON `defect_preview_records` (`SessionId`, `ClassifierRuleCode`, `ClassifierRuleVersion`);


CREATE INDEX `IX_defect_preview_sessions_ExpiresAtUtc` ON `defect_preview_sessions` (`ExpiresAtUtc`);


CREATE INDEX `IX_defect_preview_sessions_Status` ON `defect_preview_sessions` (`Status`);


CREATE INDEX `IX_DefectRecords_ConnectionId` ON `DefectRecords` (`ConnectionId`);


CREATE INDEX `IX_DefectRecords_DefectTable` ON `DefectRecords` (`DefectTable`);


CREATE INDEX `IX_DefectRecords_KeyId` ON `DefectRecords` (`KeyId`);


CREATE INDEX `IX_DefectRecords_LineCode` ON `DefectRecords` (`LineCode`);


CREATE INDEX `IX_DefectRecords_MachineCode` ON `DefectRecords` (`MachineCode`);


CREATE INDEX `IX_DefectRecords_ModelCode` ON `DefectRecords` (`ModelCode`);


CREATE INDEX `IX_LeakageBatches_CreatedAtUtc` ON `LeakageBatches` (`CreatedAtUtc`);


CREATE INDEX `IX_LeakageQueueItems_BatchId` ON `LeakageQueueItems` (`BatchId`);


CREATE INDEX `IX_LeakageQueueItems_ProductId` ON `LeakageQueueItems` (`ProductId`);


CREATE INDEX `IX_LeakageQueueItems_Status` ON `LeakageQueueItems` (`Status`);


CREATE INDEX `IX_LeakageQueueItems_Status_CreatedAtUtc` ON `LeakageQueueItems` (`Status`, `CreatedAtUtc`);


CREATE INDEX `IX_MachineClassifierRules_Active_Window` ON `machine_classifier_rules` (`IsActive`, `EffectiveFromUtc`, `EffectiveToUtc`);


CREATE INDEX `IX_MachineClassifierRules_Code_Version` ON `machine_classifier_rules` (`RuleCode`, `Version`);


CREATE INDEX `IX_MachineClassifierRules_Family_Class` ON `machine_classifier_rules` (`DefectFamily`, `DefectClass`);


CREATE INDEX `IX_MachineClassifierRules_Scope` ON `machine_classifier_rules` (`LineCode`, `MachineCode`, `StationCode`);


CREATE INDEX `IX_OpDefectMappings_MachineName` ON `op_defect_mappings` (`MachineDefectName`);


CREATE INDEX `IX_OpDefectMappings_OpName` ON `op_defect_mappings` (`OpDefectName`);


CREATE INDEX `IX_OpDefectMappings_Package_OpName` ON `op_defect_mappings` (`MachineCode`, `PackageName`, `OpDefectName`);


CREATE INDEX `IX_OpInspectionRecords_BatchId` ON `op_inspection_records` (`BatchId`);


CREATE INDEX `IX_OpInspectionRecords_KeyId` ON `op_inspection_records` (`KeyId`);


CREATE UNIQUE INDEX `UX_OpInspectionRecords_ProductRecordId` ON `op_inspection_records` (`ProductRecordId`);


CREATE INDEX `IX_OverkillPipelineConfigs_Enabled_NextRunAtUtc` ON `OverkillPipelineConfigs` (`Enabled`, `NextRunAtUtc`);


CREATE INDEX `IX_OverkillPipelineExecutions_ConfigId` ON `OverkillPipelineExecutions` (`ConfigId`);


CREATE INDEX `IX_OverkillPipelineExecutions_StartedAtUtc` ON `OverkillPipelineExecutions` (`StartedAtUtc`);


CREATE INDEX `IX_ProductQueries_QueryKey` ON `ProductQueries` (`QueryKey`);


CREATE INDEX `IX_ProductQueries_Status` ON `ProductQueries` (`Status`);


CREATE INDEX `IX_ProductRecords_Judge` ON `ProductRecords` (`Judge`);


CREATE INDEX `IX_ProductRecords_KeyId` ON `ProductRecords` (`KeyId`);


CREATE INDEX `IX_ProductRecords_PanelId` ON `ProductRecords` (`PanelId`);


CREATE INDEX `IX_ProductRecords_InspectedAtUtc` ON `ProductRecords` (`InspectedAtUtc`);


CREATE INDEX `IX_ProductRecords_Machine_Line_InspectedAtUtc` ON `ProductRecords` (`MachineCode`, `LineCode`, `InspectedAtUtc`);


CREATE INDEX `IX_ProductRecords_Judge_InspectedAtUtc` ON `ProductRecords` (`Judge`, `InspectedAtUtc`);


CREATE TABLE IF NOT EXISTS `MasterStationMappings` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `MachineCode` varchar(128) NOT NULL,
    `LineCode` varchar(128) NOT NULL,
    `ModelCode` varchar(128) NOT NULL,
    `StationId` varchar(128) NOT NULL,
    `ProductId` varchar(128) NOT NULL,
    `Enabled` tinyint(1) NOT NULL DEFAULT 1,
    `AutoSubmit` tinyint(1) NOT NULL DEFAULT 0,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_MasterStationMappings` PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_MasterStationMappings_Scope` (`MachineCode`, `LineCode`, `ModelCode`)
) CHARACTER SET=utf8mb4;


CREATE TABLE IF NOT EXISTS `AiVerdicts` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `DefectId` char(36) COLLATE ascii_general_ci NOT NULL,
    `KeyId` varchar(128) NOT NULL,
    `MachineCode` varchar(128) NOT NULL,
    `LineCode` varchar(128) NOT NULL,
    `ModelCode` varchar(128) NOT NULL,
    `StationId` varchar(128) NOT NULL,
    `ProductId` varchar(128) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `MasterJobId` varchar(64) NULL,
    `Verdict` varchar(16) NULL,
    `AnomalyScore` double NULL,
    `DefectClass` varchar(255) NULL,
    `BoxesJson` longtext NULL,
    `ModelVersionsJson` text NULL,
    `ClientMode` varchar(32) NULL,
    `Error` text NULL,
    `SubmittedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    CONSTRAINT `PK_AiVerdicts` PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_AiVerdicts_DefectId` (`DefectId`),
    KEY `IX_AiVerdicts_KeyId` (`KeyId`),
    KEY `IX_AiVerdicts_Status_MasterJobId` (`Status`, `MasterJobId`)
) CHARACTER SET=utf8mb4;


CREATE INDEX `IX_QueryProfiles_MachineCode_LineCode_Concept_SourceSystem` ON `QueryProfiles` (`MachineCode`, `LineCode`, `Concept`, `SourceSystem`);


CREATE INDEX `IX_ScanExecutions_StartedAtUtc` ON `ScanExecutions` (`StartedAtUtc`);


CREATE INDEX `IX_ScanListItems_EntityId` ON `ScanListItems` (`EntityId`);


CREATE INDEX `IX_ScanListItems_ScanListId` ON `ScanListItems` (`ScanListId`);


CREATE INDEX `IX_ScanLists_Checksum` ON `ScanLists` (`Checksum`);


CREATE INDEX `IX_StorageObjects_CreatedAtUtc` ON `StorageObjects` (`CreatedAtUtc`);


CREATE UNIQUE INDEX `IX_Users_Username` ON `Users` (`Username`);


CREATE INDEX `IX_VerificationHistory_Category_MachineCode_LineCode` ON `VerificationHistory` (`Category`, `MachineCode`, `LineCode`);


CREATE INDEX `IX_VerificationHistory_CreatedAtUtc` ON `VerificationHistory` (`CreatedAtUtc`);


CREATE INDEX `IX_VerificationHistory_EntityId` ON `VerificationHistory` (`EntityId`);


CREATE INDEX `IX_VerificationResults_ExecutionId` ON `VerificationResults` (`ExecutionId`);


