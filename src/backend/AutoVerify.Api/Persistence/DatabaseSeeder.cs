using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Storage;

namespace AutoVerify.Api.Persistence;

public static class DatabaseSeeder
{
    public static readonly string MasterStationMappingsCreateTableSql = """
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
        """;

    public static readonly string AiVerdictsCreateTableSql = """
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
        """;

    public static readonly string DefectPreviewSessionsCreateTableSql = """
        CREATE TABLE IF NOT EXISTS `defect_preview_sessions` (
            `Id` char(36) COLLATE ascii_general_ci NOT NULL,
            `StorageUri` varchar(1024) NOT NULL,
            `KeyId` varchar(128) NULL,
            `Format` varchar(64) NOT NULL DEFAULT 'DefectPackJson',
            `DetectedFormat` varchar(64) NULL,
            `ProcessingStage` varchar(64) NULL,
            `Status` varchar(32) NOT NULL DEFAULT 'Pending',
            `TotalRowsCount` int NOT NULL DEFAULT 0,
            `LoadedRowsCount` int NOT NULL DEFAULT 0,
            `ProcessedRecords` int NOT NULL DEFAULT 0,
            `NgCount` int NOT NULL DEFAULT 0,
            `OkCount` int NOT NULL DEFAULT 0,
            `SeverityBreakdownJson` text NULL,
            `SourceSizeBytes` bigint NOT NULL DEFAULT 0,
            `ErrorMessage` text NULL,
            `WarningMessage` text NULL,
            `RepairsCount` int NOT NULL DEFAULT 0,
            `FilterSummaryJson` longtext NULL,
            `TtlMinutes` int NOT NULL DEFAULT 60,
            `MaxRows` int NOT NULL DEFAULT 50000,
            `CreatedAtUtc` datetime(6) NOT NULL,
            `StartedAtUtc` datetime(6) NULL,
            `CompletedAtUtc` datetime(6) NULL,
            `LastAccessedAtUtc` datetime(6) NULL,
            `ExpiresAtUtc` datetime(6) NOT NULL,
            `CreatedBy` varchar(128) NOT NULL DEFAULT 'system',
            CONSTRAINT `PK_defect_preview_sessions` PRIMARY KEY (`Id`)
        ) CHARACTER SET=utf8mb4;
        """;

    public static readonly IReadOnlyList<string> DefectPreviewSessionsUpgradeSqls = new[]
    {
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `StorageUri` varchar(1024) NOT NULL DEFAULT '';",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `KeyId` varchar(128) NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `Format` varchar(64) NOT NULL DEFAULT 'DefectPackJson';",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `DetectedFormat` varchar(64) NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `ProcessingStage` varchar(64) NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `Status` varchar(32) NOT NULL DEFAULT 'Pending';",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `TotalRowsCount` int NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `LoadedRowsCount` int NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `ProcessedRecords` int NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `NgCount` int NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `OkCount` int NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `SeverityBreakdownJson` text NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `SourceSizeBytes` bigint NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `ErrorMessage` text NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `WarningMessage` text NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `RepairsCount` int NOT NULL DEFAULT 0;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `FilterSummaryJson` longtext NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `TtlMinutes` int NOT NULL DEFAULT 60;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `MaxRows` int NOT NULL DEFAULT 50000;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `CreatedAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6);",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `StartedAtUtc` datetime(6) NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `CompletedAtUtc` datetime(6) NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `LastAccessedAtUtc` datetime(6) NULL;",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `ExpiresAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6);",
        "ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS `CreatedBy` varchar(128) NOT NULL DEFAULT 'system';"
    };

    public static readonly string DefectPreviewRecordsCreateTableSql = """
        CREATE TABLE IF NOT EXISTS `defect_preview_records` (
            `Id` bigint NOT NULL AUTO_INCREMENT,
            `SessionId` char(36) COLLATE ascii_general_ci NOT NULL,
            `ObservationId` char(36) COLLATE ascii_general_ci NULL,
            `RowIndex` int NOT NULL,
            `DefectId` varchar(128) NULL,
            `KeyId` varchar(128) NULL,
            `DefectName` varchar(256) NOT NULL DEFAULT '',
            `DefectTable` varchar(128) NOT NULL DEFAULT 'AOI_DEFECTS',
            `DefectType` varchar(64) NOT NULL DEFAULT 'Optical',
            `ComponentName` varchar(128) NULL,
            `LineCode` varchar(128) NULL,
            `MachineCode` varchar(128) NULL,
            `StationCode` varchar(128) NULL,
            `ClassifierRuleCode` varchar(128) NULL,
            `ClassifierRuleName` varchar(256) NULL,
            `ClassifierRuleVersion` varchar(64) NULL,
            `DefectFamily` varchar(128) NULL,
            `DefectClass` varchar(128) NULL,
            `CoordinateX` double NULL,
            `CoordinateY` double NULL,
            `Severity` varchar(32) NOT NULL DEFAULT 'Minor',
            `Judge` varchar(32) NOT NULL DEFAULT 'NG',
            `ConfidenceScore` double NOT NULL DEFAULT 1.0,
            `Reason` text NULL,
            `EqpName` varchar(256) NULL,
            `EmNo` varchar(64) NULL,
            `SpecName` varchar(128) NULL,
            `SpecJudge` varchar(256) NULL,
            `SpecReason` varchar(256) NULL,
            `Priority` int NULL,
            `HasFeatureJson` tinyint(1) NOT NULL DEFAULT 0,
            `FeatureJsonSnippet` varchar(512) NULL,
            `RawFeatureUri` varchar(1024) NULL,
            `RawImageUri` varchar(1024) NULL,
            `InspectedAtUtc` datetime(6) NULL,
            CONSTRAINT `PK_defect_preview_records` PRIMARY KEY (`Id`),
            CONSTRAINT `FK_defect_preview_records_SessionId`
                FOREIGN KEY (`SessionId`) REFERENCES `defect_preview_sessions` (`Id`) ON DELETE CASCADE
        ) CHARACTER SET=utf8mb4;
        """;

    public static readonly IReadOnlyList<string> DefectPreviewRecordsUpgradeSqls = new[]
    {
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `ObservationId` char(36) COLLATE ascii_general_ci NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `LineCode` varchar(128) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `MachineCode` varchar(128) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `StationCode` varchar(128) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `ClassifierRuleCode` varchar(128) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `ClassifierRuleName` varchar(256) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `ClassifierRuleVersion` varchar(64) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `DefectFamily` varchar(128) NULL;",
        "ALTER TABLE `defect_preview_records` ADD COLUMN IF NOT EXISTS `DefectClass` varchar(128) NULL;"
    };

    public static async Task ExecuteSqlRawIdempotentAsync(
        AutoVerifyDbContext db,
        string sql,
        ILogger? logger = null,
        bool isCritical = false)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Duplicate column", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Duplicate key name", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogDebug("Schema element already exists for statement: {Sql}", sql);
                return;
            }

            logger?.LogError(ex, "Failed executing schema statement: {Sql}", sql);
            if (isCritical)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Connects to MariaDB/MySQL server without specifying a database and ensures target database is created.
    /// </summary>
    public static async Task EnsureDatabaseCreatedAsync(string connectionString, ILogger? logger = null)
    {
        try
        {
            var csb = new MySqlConnectionStringBuilder(connectionString);
            var targetDatabase = csb.Database;
            if (string.IsNullOrWhiteSpace(targetDatabase)) return;

            // Connect to server root without specifying database
            var serverCsb = new MySqlConnectionStringBuilder(connectionString)
            {
                Database = ""
            };

            using var conn = new MySqlConnection(serverCsb.ConnectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{targetDatabase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await cmd.ExecuteNonQueryAsync();
            logger?.LogInformation("Database `{Database}` verified/auto-created on server.", targetDatabase);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("EnsureDatabaseCreated notice: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Checks information_schema.COLUMNS before altering table to guarantee no duplicate column errors.
    /// </summary>
    public static async Task EnsureColumnExistsAsync(
        AutoVerifyDbContext db,
        string tableName,
        string columnName,
        string columnDefinition,
        ILogger? logger = null)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        try
        {
            var conn = db.Database.GetDbConnection();
            var shouldClose = false;
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }

            try
            {
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}' AND COLUMN_NAME = '{columnName}'";
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = $"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {columnDefinition}";
                    await alterCmd.ExecuteNonQueryAsync();
                    logger?.LogInformation("Added column `{Column}` to table `{Table}`.", columnName, tableName);
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await conn.CloseAsync();
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug("EnsureColumnExists notice for {Table}.{Column}: {Message}", tableName, columnName, ex.Message);
        }
    }

    public static async Task EnsureOperationalTablesAsync(AutoVerifyDbContext db, ILogger? logger = null)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `QueryProfiles` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `Name` varchar(256) NOT NULL,
                `Engine` varchar(32) NOT NULL DEFAULT 'MariaDB',
                `MachineCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED',
                `LineCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED',
                `Concept` varchar(128) NOT NULL,
                `SourceSystem` varchar(128) NOT NULL,
                `SqlTemplate` longtext NOT NULL,
                `ParameterSchemaJson` longtext NOT NULL,
                `TimeoutSeconds` int NOT NULL,
                `MaxConcurrency` int NOT NULL,
                `Enabled` tinyint(1) NOT NULL,
                `Notes` text NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                CONSTRAINT `PK_QueryProfiles` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_QueryProfiles_MachineCode_LineCode_Concept_SourceSystem` ON `QueryProfiles` (`MachineCode`, `LineCode`, `Concept`, `SourceSystem`);");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `StorageObjects` ADD COLUMN IF NOT EXISTS `Path` text NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `StorageObjects` ADD COLUMN IF NOT EXISTS `MetadataJson` longtext NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `MachineCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `VerificationCategory` varchar(32) NOT NULL DEFAULT 'Overkill';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `SchedulePeriod` varchar(32) NOT NULL DEFAULT 'Weekly';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `Host` varchar(256) NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `Port` int NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `DatabaseName` varchar(256) NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `Username` varchar(256) NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `ApiBaseUrl` text NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DataSources` ADD COLUMN IF NOT EXISTS `EncryptedSecret` longtext NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ScanLists` ADD COLUMN IF NOT EXISTS `MachineCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ScanLists` ADD COLUMN IF NOT EXISTS `LineCode` varchar(128) NOT NULL DEFAULT 'ALL';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ScanLists` ADD COLUMN IF NOT EXISTS `VerificationCategory` varchar(32) NOT NULL DEFAULT 'Leakage';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `QueryProfiles` ADD COLUMN IF NOT EXISTS `DataSourceId` char(36) NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `QueryProfiles` ADD COLUMN IF NOT EXISTS `VerificationCategory` varchar(32) NOT NULL DEFAULT 'Overkill';");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `ScanListItems` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `ScanListId` char(36) COLLATE ascii_general_ci NOT NULL,
                `RowNumber` int NOT NULL,
                `EntityId` varchar(128) NOT NULL,
                `LineCode` varchar(128) NOT NULL DEFAULT 'ALL',
                `MappingInt` int NULL,
                `Mapping` varchar(128) NULL,
                CONSTRAINT `PK_ScanListItems` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_ScanListItems_ScanListId` ON `ScanListItems` (`ScanListId`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_ScanListItems_EntityId` ON `ScanListItems` (`EntityId`);");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `VerificationHistory` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `Category` varchar(32) NOT NULL,
                `EntityId` varchar(128) NOT NULL,
                `LineCode` varchar(128) NOT NULL DEFAULT 'ALL',
                `Mapping` varchar(128) NULL,
                `MachineCode` varchar(128) NOT NULL,
                `Judge` varchar(32) NOT NULL,
                `PanelJudge` varchar(32) NOT NULL,
                `NameDefect` varchar(256) NOT NULL,
                `AnomalyStatus` varchar(64) NOT NULL,
                `AnomalyScore` double NOT NULL,
                `AnalysisNotes` text NOT NULL,
                `TotalDefects` int NOT NULL,
                `Eligible` tinyint(1) NOT NULL,
                `ScanListId` char(36) NULL,
                `QueryProfileId` char(36) NULL,
                `ConnectionId` char(36) NULL,
                `ConnectionName` varchar(256) NULL,
                `QueryName` varchar(256) NULL,
                `PayloadJson` longtext NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                CONSTRAINT `PK_VerificationHistory` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_VerificationHistory_CreatedAtUtc` ON `VerificationHistory` (`CreatedAtUtc`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_VerificationHistory_Category_MachineCode_LineCode` ON `VerificationHistory` (`Category`, `MachineCode`, `LineCode`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_VerificationHistory_EntityId` ON `VerificationHistory` (`EntityId`);");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `DatabaseConnections` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `Name` varchar(256) NOT NULL,
                `Host` varchar(256) NOT NULL,
                `Port` int NOT NULL,
                `DatabaseName` varchar(256) NOT NULL,
                `Username` varchar(256) NOT NULL,
                `ApiBaseUrl` text NULL,
                `EncryptedPassword` longtext NOT NULL,
                `Health` varchar(64) NOT NULL,
                `AverageLatencyMs` int NOT NULL,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                CONSTRAINT `PK_DatabaseConnections` PRIMARY KEY (`Id`),
                CONSTRAINT `AK_DatabaseConnections_Name` UNIQUE (`Name`)
            ) CHARACTER SET=utf8mb4;
            """);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `Engine` varchar(32) NOT NULL DEFAULT 'MariaDB';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `MachineCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `LineCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `ApiBaseUrl` text NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `ImageCrawlerApiUrl` text NULL;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DatabaseConnections` ADD COLUMN IF NOT EXISTS `ImageCrawlerVpMode` varchar(32) NOT NULL DEFAULT '0';");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DataSources_MachineCode` ON `DataSources` (`MachineCode`);");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `MachineCode` = 'MACHINE-A' WHERE `Name` = 'Core Customer MariaDB' AND `MachineCode` = 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `MachineCode` = 'MACHINE-B' WHERE `Name` = 'Billing MariaDB' AND `MachineCode` = 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `MachineCode` = 'MACHINE-A' WHERE `Name` = 'Partner Verification API' AND `MachineCode` = 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `MachineCode` = 'MACHINE-C' WHERE `Name` = 'Legacy Orders MariaDB' AND `MachineCode` = 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `VerificationCategory` = 'Leakage', `SchedulePeriod` = 'Monthly' WHERE `Name` = 'Billing MariaDB';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `VerificationCategory` = 'Leakage', `SchedulePeriod` = 'Weekly' WHERE `Name` = 'Partner Verification API';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `VerificationCategory` = 'Overkill', `SchedulePeriod` = 'Weekly' WHERE `Name` = 'Core Customer MariaDB';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `DataSources` SET `VerificationCategory` = 'Overkill', `SchedulePeriod` = 'Monthly' WHERE `Name` = 'Legacy Orders MariaDB';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `ScanLists` SET `MachineCode` = 'MACHINE-A' WHERE `MachineCode` = 'UNASSIGNED';");
        await db.Database.ExecuteSqlRawAsync("UPDATE `StorageObjects` SET `Path` = `Uri` WHERE `Path` IS NULL OR `Path` = ''; ");
        await db.Database.ExecuteSqlRawAsync("UPDATE `StorageObjects` SET `MetadataJson` = '{{}}' WHERE `MetadataJson` IS NULL OR `MetadataJson` = ''; ");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `ProductRecords` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `KeyId` varchar(128) NOT NULL,
                `Judge` varchar(32) NOT NULL DEFAULT 'OK',
                `PanelId` varchar(128) NOT NULL DEFAULT 'UNASSIGNED',
                `PanelJudge` varchar(32) NOT NULL DEFAULT 'OK',
                `NameDefect` varchar(256) NOT NULL DEFAULT 'None',
                `Zone` varchar(128) NULL,
                `DefectName` varchar(256) NULL,
                `MachineCode` varchar(128) NOT NULL DEFAULT 'MACHINE-A',
                `LineCode` varchar(128) NOT NULL DEFAULT 'LINE-1',
                `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED',
                `OpReviewMark` varchar(32) NULL,
                `OpReviewJudge` varchar(16) NULL,
                `OpReviewDefectName` varchar(256) NULL,
                `OpReviewMappedDefect` varchar(256) NULL,
                `OpReviewNote` varchar(1024) NULL,
                `OpReviewedAtUtc` datetime(6) NULL,
                `InspectedAtUtc` datetime(6) NOT NULL,
                `MetadataJson` longtext NULL,
                CONSTRAINT `PK_ProductRecords` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_ProductRecords_KeyId` ON `ProductRecords` (`KeyId`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_ProductRecords_PanelId` ON `ProductRecords` (`PanelId`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_ProductRecords_Judge` ON `ProductRecords` (`Judge`);");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN IF NOT EXISTS `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';");
        await EnsureColumnExistsAsync(db, "ProductRecords", "OpReviewMark", "varchar(32) NULL", logger);
        await EnsureColumnExistsAsync(db, "ProductRecords", "OpReviewJudge", "varchar(16) NULL", logger);
        await EnsureColumnExistsAsync(db, "ProductRecords", "OpReviewDefectName", "varchar(256) NULL", logger);
        await EnsureColumnExistsAsync(db, "ProductRecords", "OpReviewMappedDefect", "varchar(256) NULL", logger);
        await EnsureColumnExistsAsync(db, "ProductRecords", "OpReviewNote", "varchar(1024) NULL", logger);
        await EnsureColumnExistsAsync(db, "ProductRecords", "OpReviewedAtUtc", "datetime(6) NULL", logger);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_ProductRecords_ModelCode` ON `ProductRecords` (`ModelCode`);");
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN IF NOT EXISTS `Zone` varchar(128) NULL;");
        }
        catch
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN `Zone` varchar(128) NULL;"); } catch { }
        }
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN IF NOT EXISTS `DefectName` varchar(256) NULL;");
        }
        catch
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN `DefectName` varchar(256) NULL;"); } catch { }
        }
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN IF NOT EXISTS `DefectPackUrl` varchar(1024) NULL;");
        }
        catch
        {
            try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductRecords` ADD COLUMN `DefectPackUrl` varchar(1024) NULL;"); } catch { }
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `DefectRecords` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `KeyId` varchar(128) NOT NULL,
                `DefectTable` varchar(128) NOT NULL DEFAULT 'AOI_DEFECTS',
                `DefectName` varchar(256) NOT NULL,
                `DefectType` varchar(64) NOT NULL DEFAULT 'Optical',
                `ComponentName` varchar(128) NULL,
                `CoordinateX` double NULL,
                `CoordinateY` double NULL,
                `Severity` varchar(32) NOT NULL DEFAULT 'Minor',
                `Judge` varchar(32) NOT NULL DEFAULT 'NG',
                `ConfidenceScore` double NOT NULL DEFAULT 1.0,
                `ImageUri` text NULL,
                `Feature1StorageUri` text NULL,
                `Feature2StorageUri` text NULL,
                `FeatureJson` longtext NULL,
                `CustomFeatureJson` longtext NULL,
                `Reason` text NULL,
                `EqpName` varchar(256) NULL,
                `EmNo` varchar(45) NULL,
                `SpecName` varchar(50) NULL,
                `SpecJudge` varchar(256) NULL,
                `SpecReason` varchar(256) NULL,
                `Priority` int NULL,
                `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED',
                `ConnectionId` char(36) NULL,
                `MachineCode` varchar(128) NOT NULL DEFAULT 'MACHINE-A',
                `LineCode` varchar(128) NOT NULL DEFAULT 'LINE-1',
                `CreatedAtUtc` datetime(6) NOT NULL,
                CONSTRAINT `PK_DefectRecords` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `Feature1StorageUri` text NULL;"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `Feature2StorageUri` text NULL;"); } catch { }

        string[] newDefectCols = [
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `FeatureJson` longtext NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `CustomFeatureJson` longtext NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `Reason` text NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `EqpName` varchar(256) NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `EmNo` varchar(45) NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `SpecName` varchar(50) NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `SpecJudge` varchar(256) NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `SpecReason` varchar(256) NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `Priority` int NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `FeatureUrl` text NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `CustomFeatureUrl` text NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `ImageUrl` text NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `ConnectionId` char(36) NULL;",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `MachineCode` varchar(128) NOT NULL DEFAULT 'MACHINE-A';",
            "ALTER TABLE `DefectRecords` ADD COLUMN IF NOT EXISTS `LineCode` varchar(128) NOT NULL DEFAULT 'LINE-1';"
        ];
        string[] newDefectColsSqlite = [
            "ALTER TABLE DefectRecords ADD COLUMN FeatureJson TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN CustomFeatureJson TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN Reason TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN EqpName TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN EmNo TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN SpecName TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN SpecJudge TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN SpecReason TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN Priority INTEGER;",
            "ALTER TABLE DefectRecords ADD COLUMN FeatureUrl TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN CustomFeatureUrl TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN ImageUrl TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN ModelCode TEXT DEFAULT 'UNASSIGNED';",
            "ALTER TABLE DefectRecords ADD COLUMN ConnectionId TEXT;",
            "ALTER TABLE DefectRecords ADD COLUMN MachineCode TEXT DEFAULT 'MACHINE-A';",
            "ALTER TABLE DefectRecords ADD COLUMN LineCode TEXT DEFAULT 'LINE-1';"
        ];
        for (int i = 0; i < newDefectCols.Length; i++)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(newDefectCols[i]);
            }
            catch
            {
                try { await db.Database.ExecuteSqlRawAsync(newDefectColsSqlite[i]); } catch { }
            }
        }

        try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectRecords_KeyId` ON `DefectRecords` (`KeyId`);"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectRecords_DefectTable` ON `DefectRecords` (`DefectTable`);"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectRecords_ModelCode` ON `DefectRecords` (`ModelCode`);"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectRecords_ConnectionId` ON `DefectRecords` (`ConnectionId`);"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectRecords_MachineCode` ON `DefectRecords` (`MachineCode`);"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectRecords_LineCode` ON `DefectRecords` (`LineCode`);"); } catch { }

        // Self-healing database deduplication: remove any historical duplicate ProductRecords
        try
        {
            var isMySql = db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
            if (isMySql)
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DELETE p1 FROM `ProductRecords` p1
                    INNER JOIN `ProductRecords` p2
                    WHERE p1.`KeyId` = p2.`KeyId` AND p1.`InspectedAtUtc` < p2.`InspectedAtUtc`;
                """);
            }
        }
        catch { }

        // Self-healing database cleanup: purge empty non-defects and non-NG products accidentally ingested into Overkill tables
        try
        {
            var isMySql = db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
            if (isMySql)
            {
                // Clean up any empty or garbage defect rows
                await db.Database.ExecuteSqlRawAsync("""
                    DELETE FROM `DefectRecords` 
                    WHERE `DefectName` = '' 
                       OR `DefectName` IS NULL 
                       OR `DefectName` = 'None' 
                       OR `DefectName` = '-'
                       OR `DefectName` = '0'
                       OR `Judge` = '' 
                       OR `Judge` IS NULL 
                       OR `Judge` IN ('G', 'OK', 'GOOD', 'PASS', 'FD', '-', '0');
                """);
            }
        }
        catch { }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `OverkillPipelineConfigs` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `Name` varchar(256) NOT NULL,
                `DatabaseConnectionId` char(36) COLLATE ascii_general_ci NOT NULL,
                `MachineCode` varchar(128) NOT NULL DEFAULT 'MACHINE-A',
                `LineCode` varchar(128) NOT NULL DEFAULT 'LINE-1',
                `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED',
                `Enabled` tinyint(1) NOT NULL DEFAULT 1,
                `ScheduleIntervalMinutes` int NOT NULL DEFAULT 120,
                `LastRunAtUtc` datetime(6) NULL,
                `NextRunAtUtc` datetime(6) NULL,
                `LastRunStatus` varchar(64) NOT NULL DEFAULT 'Idle',
                `LastRunMessage` text NULL,
                `LastRunProductsCount` int NOT NULL DEFAULT 0,
                `LastRunDefectsCount` int NOT NULL DEFAULT 0,
                `ProductTableName` varchar(128) NOT NULL DEFAULT 'Product',
                `IdColumnName` varchar(128) NOT NULL DEFAULT 'ID',
                `FkIdColumnName` varchar(128) NOT NULL DEFAULT 'Fk_ID',
                `JudgeColumnName` varchar(128) NOT NULL DEFAULT 'Judge',
                `JudgeFilterValuesJson` longtext NOT NULL,
                `IncludeOkProducts` tinyint(1) NOT NULL DEFAULT 1,
                `TimestampColumnName` varchar(128) NULL,
                `ZoneColumnName` varchar(128) NULL DEFAULT 'zone',
                `DefectNameColumnName` varchar(128) NULL DEFAULT 'defect_name',
                `DateTimeInspectionColumnName` varchar(128) NULL DEFAULT 'datetime_inspection',
                `LookbackHours` int NOT NULL DEFAULT 6,
                `BatchSize` int NOT NULL DEFAULT 1000,
                `DefectTablesJson` longtext NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `UpdatedAtUtc` datetime(6) NOT NULL,
                CONSTRAINT `PK_OverkillPipelineConfigs` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_OverkillPipelineConfigs_DatabaseConnections`
                    FOREIGN KEY (`DatabaseConnectionId`) REFERENCES `DatabaseConnections` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET=utf8mb4;
            """);
        var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        if (isSqlite)
        {
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `ZoneColumnName` varchar(128) NULL DEFAULT 'zone';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `DefectNameColumnName` varchar(128) NULL DEFAULT 'defect_name';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `DateTimeInspectionColumnName` varchar(128) NULL DEFAULT 'datetime_inspection';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `OffloadDefectsToSeaweedFs` INTEGER DEFAULT 1;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `DefectBypassRuleJson` longtext NULL;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `LastRunDefectsBypassedCount` int NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `ImageCrawlerApiUrl` text NULL;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `AutoCrawlImages` INTEGER DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `ImageCrawlerVpMode` varchar(32) NOT NULL DEFAULT '0';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';", logger, isCritical: false);
        }
        else
        {
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `ZoneColumnName` varchar(128) NULL DEFAULT 'zone';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `DefectNameColumnName` varchar(128) NULL DEFAULT 'defect_name';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `DateTimeInspectionColumnName` varchar(128) NULL DEFAULT 'datetime_inspection';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `OffloadDefectsToSeaweedFs` tinyint(1) NOT NULL DEFAULT 1;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `DefectBypassRuleJson` longtext NULL;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `LastRunDefectsBypassedCount` int NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `ImageCrawlerApiUrl` text NULL;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `AutoCrawlImages` tinyint(1) NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `ImageCrawlerVpMode` varchar(32) NOT NULL DEFAULT '0';", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineConfigs` ADD COLUMN IF NOT EXISTS `ModelCode` varchar(128) NOT NULL DEFAULT 'UNASSIGNED';", logger, isCritical: false);
            await EnsureColumnExistsAsync(db, "OverkillPipelineConfigs", "IncludeOkProducts", "tinyint(1) NOT NULL DEFAULT 1", logger);
        }

        if (!isSqlite)
        {
            // ModelCode is owned by DatabaseConnections. Pipeline rows are a denormalized read cache only.
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE `OverkillPipelineConfigs` p
                INNER JOIN `DatabaseConnections` c ON c.`Id` = p.`DatabaseConnectionId`
                SET p.`ModelCode` = c.`ModelCode`
                WHERE p.`ModelCode` <> c.`ModelCode` OR p.`ModelCode` IS NULL;
                """);

            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS `trg_overkill_pipeline_model_insert`;");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER `trg_overkill_pipeline_model_insert`
                BEFORE INSERT ON `OverkillPipelineConfigs`
                FOR EACH ROW
                SET NEW.`ModelCode` = COALESCE(
                    (SELECT c.`ModelCode` FROM `DatabaseConnections` c WHERE c.`Id` = NEW.`DatabaseConnectionId` LIMIT 1),
                    'UNASSIGNED');
                """);

            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS `trg_overkill_pipeline_model_update`;");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER `trg_overkill_pipeline_model_update`
                BEFORE UPDATE ON `OverkillPipelineConfigs`
                FOR EACH ROW
                SET NEW.`ModelCode` = COALESCE(
                    (SELECT c.`ModelCode` FROM `DatabaseConnections` c WHERE c.`Id` = NEW.`DatabaseConnectionId` LIMIT 1),
                    'UNASSIGNED');
                """);

            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS `trg_database_connection_model_pipeline_sync`;");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER `trg_database_connection_model_pipeline_sync`
                AFTER UPDATE ON `DatabaseConnections`
                FOR EACH ROW
                UPDATE `OverkillPipelineConfigs`
                SET `ModelCode` = NEW.`ModelCode`
                WHERE `DatabaseConnectionId` = NEW.`Id`
                  AND (`ModelCode` <> NEW.`ModelCode` OR `ModelCode` IS NULL);
                """);
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `OverkillPipelineExecutions` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `ConfigId` char(36) COLLATE ascii_general_ci NOT NULL,
                `PipelineName` varchar(256) NOT NULL,
                `TriggerType` varchar(32) NOT NULL DEFAULT 'Scheduled',
                `StartedAtUtc` datetime(6) NOT NULL,
                `CompletedAtUtc` datetime(6) NULL,
                `Status` varchar(64) NOT NULL DEFAULT 'Running',
                `ProductsScanned` int NOT NULL DEFAULT 0,
                `DefectsScanned` int NOT NULL DEFAULT 0,
                `DefectsExtracted` int NOT NULL DEFAULT 0,
                `OverkillCandidatesCount` int NOT NULL DEFAULT 0,
                `DefectsBypassed` int NOT NULL DEFAULT 0,
                `BypassRuleSummary` varchar(512) NULL,
                `LatencyMs` int NOT NULL DEFAULT 0,
                `ErrorMessage` text NULL,
                `ExecutionSummaryJson` longtext NULL,
                CONSTRAINT `PK_OverkillPipelineExecutions` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_OverkillPipelineExecutions_ConfigId`
                    FOREIGN KEY (`ConfigId`) REFERENCES `OverkillPipelineConfigs` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET=utf8mb4;
            """);

        if (isSqlite)
        {
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineExecutions` ADD COLUMN `DefectsScanned` int NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineExecutions` ADD COLUMN `DefectsBypassed` int NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineExecutions` ADD COLUMN `BypassRuleSummary` varchar(512) NULL;", logger, isCritical: false);
        }
        else
        {
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineExecutions` ADD COLUMN IF NOT EXISTS `DefectsScanned` int NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineExecutions` ADD COLUMN IF NOT EXISTS `DefectsBypassed` int NOT NULL DEFAULT 0;", logger, isCritical: false);
            await ExecuteSqlRawIdempotentAsync(db, "ALTER TABLE `OverkillPipelineExecutions` ADD COLUMN IF NOT EXISTS `BypassRuleSummary` varchar(512) NULL;", logger, isCritical: false);
        }

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OverkillPipelineConfigs_Enabled_NextRunAtUtc` ON `OverkillPipelineConfigs` (`Enabled`, `NextRunAtUtc`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OverkillPipelineExecutions_StartedAtUtc` ON `OverkillPipelineExecutions` (`StartedAtUtc`);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `LeakageBatches` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `Name` varchar(256) NOT NULL,
                `SourceType` varchar(32) NOT NULL DEFAULT 'Paste',
                `RawInputSummary` text NULL,
                `TotalCount` int NOT NULL DEFAULT 0,
                `PendingCount` int NOT NULL DEFAULT 0,
                `ProcessingCount` int NOT NULL DEFAULT 0,
                `CompletedCount` int NOT NULL DEFAULT 0,
                `NotFoundCount` int NOT NULL DEFAULT 0,
                `LeakageCount` int NOT NULL DEFAULT 0,
                `ConsistentCount` int NOT NULL DEFAULT 0,
                `FailedCount` int NOT NULL DEFAULT 0,
                `CreatedBy` varchar(128) NOT NULL DEFAULT 'admin',
                `CreatedAtUtc` datetime(6) NOT NULL,
                `ProcessedAtUtc` datetime(6) NULL,
                CONSTRAINT `PK_LeakageBatches` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `LeakageQueueItems` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `BatchId` char(36) COLLATE ascii_general_ci NOT NULL,
                `ProductId` varchar(128) NOT NULL,
                `LineCode` varchar(128) NULL,
                `Status` varchar(64) NOT NULL DEFAULT 'Pending',
                `ResolvedFkId` varchar(128) NULL,
                `StationJudge` varchar(32) NULL,
                `AnomalyScore` double NOT NULL DEFAULT 0,
                `IsLeakageSuspect` tinyint(1) NOT NULL DEFAULT 0,
                `DefectCount` int NOT NULL DEFAULT 0,
                `ConnectionId` char(36) NULL,
                `TableScanned` varchar(256) NULL,
                `ErrorMessage` text NULL,
                `RetryCount` int NOT NULL DEFAULT 0,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `ProcessedAtUtc` datetime(6) NULL,
                CONSTRAINT `PK_LeakageQueueItems` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_LeakageQueueItems_BatchId`
                    FOREIGN KEY (`BatchId`) REFERENCES `LeakageBatches` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET=utf8mb4;
            """);

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_LeakageBatches_CreatedAtUtc` ON `LeakageBatches` (`CreatedAtUtc`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_LeakageQueueItems_BatchId` ON `LeakageQueueItems` (`BatchId`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_LeakageQueueItems_ProductId` ON `LeakageQueueItems` (`ProductId`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_LeakageQueueItems_Status` ON `LeakageQueueItems` (`Status`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_LeakageQueueItems_Status_CreatedAtUtc` ON `LeakageQueueItems` (`Status`, `CreatedAtUtc`);");

        await ExecuteSqlRawIdempotentAsync(db, MasterStationMappingsCreateTableSql, logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, AiVerdictsCreateTableSql, logger, isCritical: true);
        await EnsureColumnExistsAsync(db, "MasterStationMappings", "AutoSubmit", "tinyint(1) NOT NULL DEFAULT 0", logger);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectRecords_CreatedAtUtc` ON `DefectRecords` (`CreatedAtUtc`);", logger, isCritical: false);

        await ExecuteSqlRawIdempotentAsync(db, DefectPreviewSessionsCreateTableSql, logger, isCritical: true);
        foreach (var sql in DefectPreviewSessionsUpgradeSqls)
        {
            await ExecuteSqlRawIdempotentAsync(db, sql, logger, isCritical: true);
        }

        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_defect_preview_sessions_ExpiresAtUtc` ON `defect_preview_sessions` (`ExpiresAtUtc`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_defect_preview_sessions_Status` ON `defect_preview_sessions` (`Status`);", logger, isCritical: true);

        await ExecuteSqlRawIdempotentAsync(db, DefectPreviewRecordsCreateTableSql, logger, isCritical: true);
        foreach (var sql in DefectPreviewRecordsUpgradeSqls)
        {
            await ExecuteSqlRawIdempotentAsync(db, sql, logger, isCritical: true);
        }
        await ExecuteSqlRawIdempotentAsync(db, "UPDATE `defect_preview_records` SET `ObservationId` = UUID() WHERE `ObservationId` IS NULL OR `ObservationId` = '' OR `ObservationId` = '00000000-0000-0000-0000-000000000000';", logger, isCritical: false);

        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_RowIndex` ON `defect_preview_records` (`SessionId`, `RowIndex`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_ObservationId` ON `defect_preview_records` (`SessionId`, `ObservationId`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_Judge` ON `defect_preview_records` (`SessionId`, `Judge`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_DefectName` ON `defect_preview_records` (`SessionId`, `DefectName`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_Component` ON `defect_preview_records` (`SessionId`, `ComponentName`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_Line_Machine` ON `defect_preview_records` (`SessionId`, `LineCode`, `MachineCode`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_Rule_Version` ON `defect_preview_records` (`SessionId`, `ClassifierRuleCode`, `ClassifierRuleVersion`);", logger, isCritical: true);
        await ExecuteSqlRawIdempotentAsync(db, "CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Session_DefectFamily` ON `defect_preview_records` (`SessionId`, `DefectFamily`);", logger, isCritical: true);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `defect_preview_features` (
                `Id` bigint NOT NULL AUTO_INCREMENT,
                `SessionId` char(36) COLLATE ascii_general_ci NOT NULL,
                `ObservationId` char(36) COLLATE ascii_general_ci NULL,
                `RowIndex` int NOT NULL,
                `FeatureName` varchar(128) NOT NULL,
                `FeatureValue` double NOT NULL,
                CONSTRAINT `PK_defect_preview_features` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_defect_preview_features_SessionId`
                    FOREIGN KEY (`SessionId`) REFERENCES `defect_preview_sessions` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET=utf8mb4;
            """);

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `defect_preview_features` ADD COLUMN IF NOT EXISTS `ObservationId` char(36) COLLATE ascii_general_ci NULL;"); } catch { }

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Feature_Session_Name` ON `defect_preview_features` (`SessionId`, `FeatureName`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Feature_Session_RowIndex` ON `defect_preview_features` (`SessionId`, `RowIndex`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectPreview_Feature_Session_ObservationId` ON `defect_preview_features` (`SessionId`, `ObservationId`);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `machine_classifier_rules` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `RuleCode` varchar(128) NOT NULL,
                `RuleName` varchar(256) NOT NULL,
                `Version` varchar(64) NOT NULL DEFAULT '1.0.0',
                `ProductKey` varchar(128) NULL,
                `LineCode` varchar(128) NULL,
                `MachineCode` varchar(128) NULL,
                `StationCode` varchar(128) NULL,
                `DefectFamily` varchar(128) NULL,
                `DefectClass` varchar(128) NULL,
                `ThresholdsConfigJson` longtext NOT NULL,
                `ModelMetadataJson` longtext NULL,
                `ScriptContent` longtext NULL,
                `Priority` int NOT NULL DEFAULT 0,
                `DefectTypeId` varchar(128) NULL,
                `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                `EffectiveFromUtc` datetime(6) NOT NULL,
                `EffectiveToUtc` datetime(6) NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `UpdatedAtUtc` datetime(6) NULL,
                `CreatedBy` varchar(128) NOT NULL DEFAULT 'system',
                `Description` varchar(1024) NULL,
                CONSTRAINT `PK_machine_classifier_rules` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_MachineClassifierRules_Code_Version` ON `machine_classifier_rules` (`RuleCode`, `Version`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_MachineClassifierRules_Scope` ON `machine_classifier_rules` (`LineCode`, `MachineCode`, `StationCode`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_MachineClassifierRules_Family_Class` ON `machine_classifier_rules` (`DefectFamily`, `DefectClass`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_MachineClassifierRules_Active_Window` ON `machine_classifier_rules` (`IsActive`, `EffectiveFromUtc`, `EffectiveToUtc`);");

        await EnsureColumnExistsAsync(db, "machine_classifier_rules", "ScriptContent", "longtext NULL", logger);
        await EnsureColumnExistsAsync(db, "machine_classifier_rules", "Priority", "int NOT NULL DEFAULT 0", logger);
        await EnsureColumnExistsAsync(db, "machine_classifier_rules", "DefectTypeId", "varchar(128) NULL", logger);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `defect_analytics_jobs` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `SessionId` char(36) COLLATE ascii_general_ci NOT NULL,
                `AnalysisType` varchar(64) NOT NULL,
                `Status` varchar(32) NOT NULL DEFAULT 'Queued',
                `ProgressPercent` int NOT NULL DEFAULT 0,
                `RequestHash` varchar(64) NOT NULL,
                `ConfigurationJson` longtext NOT NULL,
                `ResultJson` longtext NULL,
                `WarningsJson` longtext NULL,
                `ErrorMessage` text NULL,
                `TtlMinutes` int NOT NULL DEFAULT 120,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `StartedAtUtc` datetime(6) NULL,
                `CompletedAtUtc` datetime(6) NULL,
                `ExpiresAtUtc` datetime(6) NOT NULL,
                `CreatedBy` varchar(128) NOT NULL DEFAULT 'system',
                CONSTRAINT `PK_defect_analytics_jobs` PRIMARY KEY (`Id`),
                CONSTRAINT `FK_defect_analytics_jobs_SessionId`
                    FOREIGN KEY (`SessionId`) REFERENCES `defect_preview_sessions` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET=utf8mb4;
            """);

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectAnalytics_Session_Type` ON `defect_analytics_jobs` (`SessionId`, `AnalysisType`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectAnalytics_Session_Hash` ON `defect_analytics_jobs` (`SessionId`, `RequestHash`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_DefectAnalytics_Status_Expires` ON `defect_analytics_jobs` (`Status`, `ExpiresAtUtc`);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `op_defect_mappings` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `MachineCode` varchar(128) NOT NULL DEFAULT 'ALL',
                `PackageName` varchar(128) NOT NULL DEFAULT 'Default',
                `OpDefectName` varchar(128) NOT NULL,
                `MachineDefectName` varchar(128) NOT NULL,
                `DefectFamily` varchar(128) NOT NULL DEFAULT 'Optical',
                `ComponentGroup` varchar(128) NOT NULL DEFAULT '',
                `IsConfirmedDefect` tinyint(1) NOT NULL DEFAULT 1,
                `Description` varchar(512) NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                `UpdatedAtUtc` datetime(6) NULL,
                CONSTRAINT `PK_op_defect_mappings` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OpDefectMappings_OpName` ON `op_defect_mappings` (`OpDefectName`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OpDefectMappings_MachineName` ON `op_defect_mappings` (`MachineDefectName`);");
        await EnsureColumnExistsAsync(db, "op_defect_mappings", "MachineCode", "varchar(128) NOT NULL DEFAULT 'ALL'", logger);
        await EnsureColumnExistsAsync(db, "op_defect_mappings", "PackageName", "varchar(128) NOT NULL DEFAULT 'Default'", logger);
        await EnsureColumnExistsAsync(db, "op_defect_mappings", "ComponentGroup", "varchar(128) NOT NULL DEFAULT ''", logger);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OpDefectMappings_Package_OpName` ON `op_defect_mappings` (`MachineCode`, `PackageName`, `OpDefectName`);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `op_inspection_records` (
                `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                `BatchId` char(36) COLLATE ascii_general_ci NOT NULL,
                `ProductRecordId` char(36) COLLATE ascii_general_ci NULL,
                `KeyId` varchar(128) NOT NULL,
                `OpJudge` varchar(32) NOT NULL DEFAULT 'OK',
                `OpDefectName` varchar(128) NULL,
                `MappedMachineDefect` varchar(128) NULL,
                `MappedDefectFamily` varchar(128) NULL,
                `IsRealDefectConfirmed` tinyint(1) NULL,
                `LineCode` varchar(128) NULL,
                `MachineCode` varchar(128) NULL,
                `Notes` varchar(512) NULL,
                `InspectedAtUtc` datetime(6) NOT NULL,
                `CreatedAtUtc` datetime(6) NOT NULL,
                CONSTRAINT `PK_op_inspection_records` PRIMARY KEY (`Id`)
            ) CHARACTER SET=utf8mb4;
            """);
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OpInspectionRecords_KeyId` ON `op_inspection_records` (`KeyId`);");
        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS `IX_OpInspectionRecords_BatchId` ON `op_inspection_records` (`BatchId`);");
        await EnsureColumnExistsAsync(db, "op_inspection_records", "ProductRecordId", "char(36) COLLATE ascii_general_ci NULL", logger);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS `UX_OpInspectionRecords_ProductRecordId` ON `op_inspection_records` (`ProductRecordId`);");
    }

    public static async Task SeedAsync(AutoVerifyDbContext db, IDatabaseCredentialService databaseCredentials)
    {
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AppUserEntity { Id = Guid.NewGuid(), Username = "admin", DisplayName = "Administrator", PasswordHash = AutoVerifyStore.HashPassword("admin!"), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        }

        if (!await db.DataSources.AnyAsync())
        {
            db.DataSources.AddRange(
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999655"), Name = "Core Customer MariaDB", Type = "MariaDB", MachineCode = "MACHINE-A", VerificationCategory = "Overkill", SchedulePeriod = "Weekly", Environment = "Production", Health = "Healthy", AverageLatencyMs = 42, Enabled = true },
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999656"), Name = "Billing MariaDB", Type = "MariaDB", MachineCode = "MACHINE-B", VerificationCategory = "Leakage", SchedulePeriod = "Monthly", Environment = "Production", Health = "Warning", AverageLatencyMs = 119, Enabled = true },
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999657"), Name = "Partner Verification API", Type = "REST API", MachineCode = "MACHINE-A", VerificationCategory = "Leakage", SchedulePeriod = "Weekly", Environment = "Production", Health = "Healthy", AverageLatencyMs = 87, Enabled = true },
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999658"), Name = "Legacy Orders MariaDB", Type = "MariaDB", MachineCode = "MACHINE-C", VerificationCategory = "Overkill", SchedulePeriod = "Monthly", Environment = "Staging", Health = "Healthy", AverageLatencyMs = 66, Enabled = false });
        }

        await db.SaveChangesAsync();

        if (!await db.ScanLists.AnyAsync())
        {
            var store = new AutoVerifyStore(db, databaseCredentials, new Verification.AnomalyVerificationService(), new Services.QueryEngineService(), new Services.ImportValidationService());
            var scanList = await store.CreateScanListAsync(new CreateScanListRequest("Sample Pilot IDs", "entityId", "CUS-1001\nCUS-1002\nCUS-1003\nCUS-1004"), null);
            await store.CreateScanExecutionAsync(new CreateScanExecutionRequest(scanList.Id, "Initial Pilot Verification", [Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999655"), Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999656"), Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999657")]), new NullSeedStorageManager());
        }

        if (!await db.ProductQueries.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.ProductQueries.AddRange(
                new ProductQueryEntity
                {
                    Id = Guid.NewGuid(),
                    QueryKey = "PILOT-PENDING-001",
                    ProductId = "AUTO-000001",
                    SourceSystem = "Manual",
                    Status = ProductQueryStatuses.Pending,
                    PayloadJson = "{\"entityId\":\"AUTO-000001\",\"reason\":\"Waiting for verification\"}",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                },
                new ProductQueryEntity
                {
                    Id = Guid.NewGuid(),
                    QueryKey = "PILOT-DOING-001",
                    ProductId = "AUTO-000002",
                    SourceSystem = "Manual",
                    Status = ProductQueryStatuses.Doing,
                    PayloadJson = "{\"entityId\":\"AUTO-000002\",\"reason\":\"Currently processing\"}",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                },
                new ProductQueryEntity
                {
                    Id = Guid.NewGuid(),
                    QueryKey = "PILOT-JUDGE-001",
                    ProductId = "AUTO-000003",
                    SourceSystem = "Manual",
                    Status = ProductQueryStatuses.Judge,
                    PayloadJson = "{\"entityId\":\"AUTO-000003\",\"reason\":\"Ready for judgment\"}",
                    JudgeResult = "NeedsReview",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
        }

        if (!await db.QueryProfiles.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.QueryProfiles.AddRange(
                new QueryProfileEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "Machine A Line 1 Product Status",
                    MachineCode = "MACHINE-A",
                    LineCode = "LINE-1",
                    Concept = "PRODUCT-STATUS",
                    SourceSystem = "CORE-CUSTOMER",
                    SqlTemplate = "SELECT product_id, status, updated_at FROM products WHERE product_id = {{productId}} AND line_code = {{lineCode}} LIMIT 1",
                    ParameterSchemaJson = "{\"productId\":\"string\",\"lineCode\":\"string\"}",
                    TimeoutSeconds = 30,
                    MaxConcurrency = 2,
                    Enabled = true,
                    VerificationCategory = "Overkill",
                    Notes = "Sample query profile for DevOps customization.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                },
                new QueryProfileEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "Machine B Line 7 Judgment Feed",
                    MachineCode = "MACHINE-B",
                    LineCode = "LINE-7",
                    Concept = "JUDGMENT-FEED",
                    SourceSystem = "BILLING",
                    SqlTemplate = "SELECT entity_id, judge_state, reason_code FROM verification_feed WHERE entity_id = {{entityId}} LIMIT 1",
                    ParameterSchemaJson = "{\"entityId\":\"string\"}",
                    TimeoutSeconds = 45,
                    MaxConcurrency = 1,
                    Enabled = true,
                    VerificationCategory = "Leakage",
                    Notes = "Line-specific concept with a different source shape.",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
        }

        if (!await db.ProductRecords.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.ProductRecords.AddRange(
                new ProductRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1001",
                    Judge = "OK",
                    PanelId = "PANEL-2026-A1",
                    PanelJudge = "OK",
                    NameDefect = "None",
                    MachineCode = "MACHINE-A",
                    LineCode = "LINE-1",
                    InspectedAtUtc = now.AddMinutes(-40)
                },
                new ProductRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1002",
                    Judge = "NG",
                    PanelId = "PANEL-2026-A1",
                    PanelJudge = "NG",
                    NameDefect = "Missing Resistor R102",
                    MachineCode = "MACHINE-A",
                    LineCode = "LINE-1",
                    InspectedAtUtc = now.AddMinutes(-38)
                },
                new ProductRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1003",
                    Judge = "NG",
                    PanelId = "PANEL-2026-A2",
                    PanelJudge = "OK",
                    NameDefect = "Surface Stain",
                    MachineCode = "MACHINE-A",
                    LineCode = "LINE-1",
                    InspectedAtUtc = now.AddMinutes(-25)
                },
                new ProductRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1004",
                    Judge = "OK",
                    PanelId = "PANEL-2026-B1",
                    PanelJudge = "OK",
                    NameDefect = "None",
                    MachineCode = "MACHINE-B",
                    LineCode = "LINE-2",
                    InspectedAtUtc = now.AddMinutes(-15)
                },
                new ProductRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1005",
                    Judge = "NG",
                    PanelId = "PANEL-2026-B1",
                    PanelJudge = "NG",
                    NameDefect = "Tombstone Capacitor C305",
                    MachineCode = "MACHINE-B",
                    LineCode = "LINE-2",
                    InspectedAtUtc = now.AddMinutes(-10)
                }
            );

            db.DefectRecords.AddRange(
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1002",
                    DefectTable = "AOI_DEFECTS",
                    DefectName = "Missing Component",
                    DefectType = "Placement",
                    ComponentName = "R102",
                    CoordinateX = 142.5,
                    CoordinateY = 88.2,
                    Severity = "Critical",
                    Judge = "NG",
                    ConfidenceScore = 0.99,
                    FeatureJson = "{\"bbox\":{\"x\":142.5,\"y\":88.2,\"w\":12.4,\"h\":8.6},\"area_px\":106.64,\"contrast\":0.85,\"intensity_mean\":224.1,\"intensity_std\":12.8}",
                    CustomFeatureJson = "{\"model_version\":\"yolo-smt-v3\",\"defect_prob\":0.992,\"class_weights\":{\"missing\":0.992,\"offset\":0.005,\"ok\":0.003},\"embedding_vector\":[0.42,0.88,-0.15,0.63,0.91]}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1002_feat1_aoi.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1002_feat2_aoi.json",
                    CreatedAtUtc = now.AddMinutes(-38)
                },
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1002",
                    DefectTable = "AI_DEFECTS",
                    DefectName = "Missing Resistor Neural Confirmed",
                    DefectType = "Optical",
                    ComponentName = "R102",
                    CoordinateX = 142.5,
                    CoordinateY = 88.2,
                    Severity = "Critical",
                    Judge = "NG",
                    ConfidenceScore = 0.98,
                    FeatureJson = "{\"crop_window\":{\"x1\":136,\"y1\":82,\"x2\":148,\"y2\":94},\"pad_color_variance\":3.1,\"solder_pad_exposed\":true}",
                    CustomFeatureJson = "{\"resnet_feature_layer\":\"conv5_block3_out\",\"anomaly_vector_norm\":4.82,\"threshold\":1.20,\"judgment\":\"Confirmed_NG\"}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1002_feat1_ai.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1002_feat2_ai.json",
                    CreatedAtUtc = now.AddMinutes(-38)
                },
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1003",
                    DefectTable = "AOI_DEFECTS",
                    DefectName = "Optical Discoloration / Dust",
                    DefectType = "Surface",
                    ComponentName = "PCB-EDGE-L3",
                    CoordinateX = 12.0,
                    CoordinateY = 240.5,
                    Severity = "Minor",
                    Judge = "Review",
                    ConfidenceScore = 0.40,
                    FeatureJson = "{\"bbox\":{\"x\":12.0,\"y\":240.5,\"w\":4.2,\"h\":3.1},\"area_px\":13.02,\"intensity_mean\":180.5,\"reflection_factor\":0.12}",
                    CustomFeatureJson = "{\"model_version\":\"yolo-smt-v3\",\"defect_prob\":0.395,\"class_weights\":{\"dust\":0.605,\"scratch\":0.395},\"embedding_vector\":[0.05,0.12,-0.02,0.14,0.08]}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1003_feat1_aoi.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1003_feat2_aoi.json",
                    CreatedAtUtc = now.AddMinutes(-25)
                },
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1003",
                    DefectTable = "AI_DEFECTS",
                    DefectName = "Clean Non-Functional Dust Passed",
                    DefectType = "Surface",
                    ComponentName = "PCB-EDGE-L3",
                    CoordinateX = 12.0,
                    CoordinateY = 240.5,
                    Severity = "None",
                    Judge = "OK",
                    ConfidenceScore = 0.96,
                    FeatureJson = "{\"particle_type\":\"dust_speck\",\"conductive\":false,\"circuit_interference\":0.0}",
                    CustomFeatureJson = "{\"deep_filter_verdict\":\"IGNORE_OVERKILL\",\"confidence\":0.965,\"safe_to_ship\":true}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1003_feat1_ai.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1003_feat2_ai.json",
                    CreatedAtUtc = now.AddMinutes(-25)
                },
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1004",
                    DefectTable = "SPI_DEFECTS",
                    DefectName = "Solder Bridge High Conductivity",
                    DefectType = "Solder",
                    ComponentName = "IC-U401-PIN12",
                    CoordinateX = 210.0,
                    CoordinateY = 154.6,
                    Severity = "Critical",
                    Judge = "NG",
                    ConfidenceScore = 0.97,
                    FeatureJson = "{\"pin_span\":[\"PIN11\",\"PIN12\"],\"volume_pct\":245.0,\"height_um\":185.2,\"bridge_thickness_um\":42.0}",
                    CustomFeatureJson = "{\"spi_3d_profile\":{\"coplanarity_error\":true,\"excess_paste_risk\":0.98},\"conductivity_risk\":\"High\"}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1004_feat1_spi.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1004_feat2_spi.json",
                    CreatedAtUtc = now.AddMinutes(-15)
                },
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1005",
                    DefectTable = "AOI_DEFECTS",
                    DefectName = "Tombstone Standing Component",
                    DefectType = "Placement",
                    ComponentName = "C305",
                    CoordinateX = 68.4,
                    CoordinateY = 212.1,
                    Severity = "Major",
                    Judge = "NG",
                    ConfidenceScore = 0.94,
                    FeatureJson = "{\"elevation_angle_deg\":68.5,\"pad1_contact\":false,\"pad2_contact\":true}",
                    CustomFeatureJson = "{\"vision_model\":\"smt-3d-pose-v2\",\"tombstone_prob\":0.942,\"pad_separation_um\":320.0}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1005_feat1_aoi.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1005_feat2_aoi.json",
                    CreatedAtUtc = now.AddMinutes(-10)
                },
                new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = "CUS-1005",
                    DefectTable = "SPI_DEFECTS",
                    DefectName = "Insufficient Solder Paste Volume",
                    DefectType = "Solder",
                    ComponentName = "C305",
                    CoordinateX = 68.4,
                    CoordinateY = 212.1,
                    Severity = "Major",
                    Judge = "NG",
                    ConfidenceScore = 0.91,
                    FeatureJson = "{\"pad_id\":\"C305-P1\",\"target_vol_nl\":14.2,\"measured_vol_nl\":3.1,\"volume_ratio\":0.218}",
                    CustomFeatureJson = "{\"spi_alert\":\"VOLUME_DEFICIT_78PCT\",\"reflow_wetting_risk\":0.93}",
                    Feature1StorageUri = "seaweedfs://defect-features/CUS-1005_feat1_spi.json",
                    Feature2StorageUri = "seaweedfs://defect-features/CUS-1005_feat2_spi.json",
                    CreatedAtUtc = now.AddMinutes(-10)
                }
            );
        }

        if (!await db.MachineClassifierRules.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.MachineClassifierRules.AddRange(
                new MachineClassifierRuleEntity
                {
                    Id = Guid.Parse("5c117490-152e-4bae-a9a9-11326689753b"),
                    RuleCode = "Light_Fail",
                    RuleName = "Light_Fail",
                    Version = "1.0.0",
                    LineCode = "LINE-1",
                    MachineCode = "MACHINE-A",
                    StationCode = "STATION-AOI-01",
                    DefectFamily = "Optical",
                    DefectClass = "Light_Fail",
                    Priority = 0,
                    DefectTypeId = "{c79df3f9-4f5a-43d3-821c-7f62c2cc03ba}",
                    ScriptContent = """
#MCL Block1
MCL_Position1 = (G010_VpID==1) and (G008_LightID ==1) and (G009_RoiID == 24 or G009_RoiID == 25)
MCL_Light1 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
MCL_Block1 = MCL_Position1 and MCL_Light1

#MCL Block2
MCL_Position2 = (G010_VpID==1) and (G008_LightID ==2) and (G009_RoiID == 3 or G009_RoiID == 4)
MCL_Light2 = (G062_OrgGrayMax < 30 and G064_OrgGrayStdDev < 5)
MCL_Block2 = MCL_Position2 and MCL_Light2

#MCL Block4
MCL_Position4 = (G010_VpID==1) and (G008_LightID ==4) and (G009_RoiID == 8 or G009_RoiID == 9)
MCL_Light4 = (G062_OrgGrayMax < 30 and G064_OrgGrayStdDev < 5)
MCL_Block4 = MCL_Position4 and MCL_Light4

#SR_Block1
SR_Position1 = (G010_VpID==2) and (G008_LightID ==1) and (G009_RoiID == 2 or G009_RoiID == 3)
SR_Light1 = (G062_OrgGrayMax < 50 and G064_OrgGrayStdDev < 5)
SR_Block1 = SR_Position1 and SR_Light1

#SR_Block2
SR_Position2 = (G010_VpID==2) and (G008_LightID ==2) and (G009_RoiID == 5 or G009_RoiID == 6)
SR_Light2 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
SR_Block2 = SR_Position2 and SR_Light2

#SR_Block3
SR_Position3 = (G010_VpID==2) and (G008_LightID ==3) and (G009_RoiID == 2 or G009_RoiID == 3)
SR_Light3 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
SR_Block3 = SR_Position3 and SR_Light3

return MCL_Block1 or MCL_Block2 or MCL_Block4 or SR_Block1 or SR_Block2 or SR_Block3
""",
                    IsActive = true,
                    EffectiveFromUtc = now.AddMonths(-6),
                    CreatedBy = "aoi_admin",
                    Description = "AOI Priority 0 Inspection Rule: Light_Fail"
                },
                new MachineClassifierRuleEntity
                {
                    Id = Guid.Parse("925918e0-9daa-4ca2-b30b-48b7842a0a97"),
                    RuleCode = "DIC_Crack",
                    RuleName = "DIC_Crack",
                    Version = "1.0.0",
                    LineCode = "LINE-1",
                    MachineCode = "MACHINE-A",
                    StationCode = "STATION-AOI-01",
                    DefectFamily = "Optical",
                    DefectClass = "DIC_Crack",
                    Priority = 1,
                    DefectTypeId = "{12084a83-674f-4380-803b-071fa389f4e6}",
                    ThresholdsConfigJson = "{\"ConfidenceScore\":0.90,\"CoordinateX\":{\"min\":0,\"max\":1000},\"CoordinateY\":{\"min\":0,\"max\":1000}}",
                    ScriptContent = """
if( G010_VpID != 1):
	return False

if( G008_LightID != 1 ):
	return False
	
if( G009_RoiID != 1 ):
	return False
	
if(G131_Real_DicToLeft>400 and G132_Real_DicToRigt>400 and G133_Real_DicToTop>400 and G134_Real_DicToBot>400 and G063_OrgGrayMin> 120):
	return False

IP_Cond = V003_VP_IP == 200131 or V003_VP_IP == 200231
if(V003_VP_IP == 1):
	IP_Cond = 1

VP_Info1 = (G008_LightID ==1) and (G009_RoiID == 1)

Cond1 = ( G020_PxlArea > 90 and (G017_PxlWidth > 20 or G018_PxlHeight > 20))
Cond2 = ( G038_PrcGrayMin <  45 )
#Improve DIC CRACK line  240807
AVG_Line = G018_PxlHeight /G017_PxlWidth
if(G018_PxlHeight <  10):
	Cond2 = (G038_PrcGrayMin < 220)
if(Cond2 ==1 and AVG_Line < 0.8):
	Cond1 = (G020_PxlArea > 20 )
	
if(G017_PxlWidth > 80):
	Cond2 = (G038_PrcGrayMin <  150)

DL_Cond = MLClass01 > 90

return IP_Cond and VP_Info1 and Cond1 and Cond2 and DL_Cond
""",
                    IsActive = true,
                    EffectiveFromUtc = now.AddMonths(-6),
                    CreatedBy = "aoi_admin",
                    Description = "AOI Priority 1 Inspection Rule: DIC_Crack"
                }
            );
        }

        if (!await db.OpDefectMappings.AnyAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.OpDefectMappings.AddRange(
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "No Defect",
                    MachineDefectName = "NONE",
                    DefectFamily = "Clean",
                    IsConfirmedDefect = false,
                    Description = "Operator confirmed clean / false alarm",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Dust Particle",
                    MachineDefectName = "Dust",
                    DefectFamily = "Optical",
                    IsConfirmedDefect = false,
                    Description = "Surface dust, cleanable without product impairment (Overkill)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Optical Reflection",
                    MachineDefectName = "Reflection",
                    DefectFamily = "Optical",
                    IsConfirmedDefect = false,
                    Description = "Optical reflection or glare (Overkill)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Corner Crack",
                    MachineDefectName = "DIC_Crack",
                    DefectFamily = "Optical",
                    IsConfirmedDefect = true,
                    Description = "Die/IC corner crack mapped to machine DIC_Crack (Real Defect)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Die Crack",
                    MachineDefectName = "DIC_Crack",
                    DefectFamily = "Optical",
                    IsConfirmedDefect = true,
                    Description = "Die trace/body crack mapped to machine DIC_Crack (Real Defect)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Lighting Failure",
                    MachineDefectName = "Light_Fail",
                    DefectFamily = "Optical",
                    IsConfirmedDefect = true,
                    Description = "Optical lighting/illumination failure mapped to Light_Fail (Real Defect)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Dark ROI",
                    MachineDefectName = "Light_Fail",
                    DefectFamily = "Optical",
                    IsConfirmedDefect = true,
                    Description = "Inspection ROI excessively dark mapped to Light_Fail (Real Defect)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Solder Bridge",
                    MachineDefectName = "Bridging",
                    DefectFamily = "Solder",
                    IsConfirmedDefect = true,
                    Description = "Pin bridging / short circuit (Real Defect)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "Missing Component",
                    MachineDefectName = "Missing Component",
                    DefectFamily = "Component",
                    IsConfirmedDefect = true,
                    Description = "Missing surface-mount component (Real Defect)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "FOP bonding vết nhấn",
                    MachineDefectName = "FOP_Dent",
                    DefectFamily = "Bonding",
                    ComponentGroup = "FOP",
                    IsConfirmedDefect = true,
                    Description = "FOP bonding press mark / dent (Vết nhấn FOP)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "POL đâm",
                    MachineDefectName = "POL_Damage",
                    DefectFamily = "Polarizer",
                    ComponentGroup = "POL",
                    IsConfirmedDefect = true,
                    Description = "Polarizer puncture / dent damage (POL đâm)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "B/P vết nhấn",
                    MachineDefectName = "BP_Dent",
                    DefectFamily = "Backplane",
                    ComponentGroup = "B/P",
                    IsConfirmedDefect = true,
                    Description = "Backplane press mark (Vết nhấn B/P)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "COP Bonding dị vật",
                    MachineDefectName = "COP_Particle",
                    DefectFamily = "Particle",
                    ComponentGroup = "COP",
                    IsConfirmedDefect = true,
                    Description = "COP bonding foreign material / particle (Dị vật COP Bonding)",
                    CreatedAtUtc = now
                },
                new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = "POL vết nhấn",
                    MachineDefectName = "POL_Dent",
                    DefectFamily = "Polarizer",
                    ComponentGroup = "POL",
                    IsConfirmedDefect = true,
                    Description = "Polarizer press mark (Vết nhấn POL)",
                    CreatedAtUtc = now
                }
            );
        }

        // Ensure common FOG defect mappings are present even if mappings were previously seeded
        var fogMappings = new (string OpName, string MachineName, string Family, string Component, string Desc)[]
        {
            ("FOP bonding vết nhấn", "FOP_Dent", "Bonding", "FOP", "FOP bonding press mark / dent (Vết nhấn FOP)"),
            ("POL đâm", "POL_Damage", "Polarizer", "POL", "Polarizer puncture / dent damage (POL đâm)"),
            ("POL  đâm", "POL_Damage", "Polarizer", "POL", "Polarizer puncture / dent damage (POL đâm - 2 spaces)"),
            ("B/P vết nhấn", "BP_Dent", "Backplane", "B/P", "Backplane press mark (Vết nhấn B/P)"),
            ("COP Bonding dị vật", "COP_Particle", "Particle", "COP", "COP bonding foreign material / particle (Dị vật COP Bonding)"),
            ("COP  Bonding dị vật", "COP_Particle", "Particle", "COP", "COP bonding foreign material / particle (Dị vật COP Bonding - 2 spaces)"),
            ("POL vết nhấn", "POL_Dent", "Polarizer", "POL", "Polarizer press mark (Vết nhấn POL)")
        };

        var existingNames = await db.OpDefectMappings.Select(m => m.OpDefectName).ToListAsync();
        var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var missingFog = new List<OpDefectMappingEntity>();
        var ensureNow = DateTimeOffset.UtcNow;

        foreach (var (opName, machineName, family, component, desc) in fogMappings)
        {
            if (!existingSet.Contains(opName))
            {
                missingFog.Add(new OpDefectMappingEntity
                {
                    Id = Guid.NewGuid(),
                    OpDefectName = opName,
                    MachineDefectName = machineName,
                    DefectFamily = family,
                    ComponentGroup = component,
                    IsConfirmedDefect = true,
                    Description = desc,
                    CreatedAtUtc = ensureNow
                });
                existingSet.Add(opName);
            }
        }

        if (missingFog.Count > 0)
        {
            db.OpDefectMappings.AddRange(missingFog);
        }

        await db.SaveChangesAsync();
    }
}
