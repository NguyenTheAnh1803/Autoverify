using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace AutoVerify.Api.Services;

public sealed partial class OverkillQueryEngineService(
    AutoVerifyDbContext db,
    IDatabaseCredentialService databaseCredentials,
    IAnomalyVerificationService anomalyEngine,
    IStorageService storage,
    ILogger<OverkillQueryEngineService> logger,
    IImageCrawlerService? imageCrawler = null) : IOverkillQueryEngineService
{
    private static readonly Regex SafeIdentifierRegex = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ExecutionLocks = new();

    public async Task<TestPipelineMappingResponse> TestMappingAsync(
        TestPipelineMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var generatedDefectSqlList = new List<string>();
        try
        {
            var connection = await db.DatabaseConnections
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == request.DatabaseConnectionId, cancellationToken);

            if (connection is null)
            {
                return new TestPipelineMappingResponse(
                    false, 0, [], new Dictionary<string, int>(), [], 0,
                    $"Database connection with ID '{request.DatabaseConnectionId}' was not found.");
            }

            EnsureValidIdentifier(request.ProductTableName, "Product Table Name");
            EnsureValidIdentifier(request.IdColumnName, "ID Column Name");
            EnsureValidIdentifier(request.FkIdColumnName, "FK ID Column Name");
            EnsureValidIdentifier(request.JudgeColumnName, "Judge Column Name");

            var connString = BuildConnectionString(connection);
            await using var mySqlConnection = new MySqlConnection(connString);
            await mySqlConnection.OpenAsync(cancellationToken);

            var productColumns = await GetTableColumnsAsync(mySqlConnection, request.ProductTableName, cancellationToken);

            var zoneCol = !string.IsNullOrWhiteSpace(request.ZoneColumnName) && (productColumns.Count == 0 || productColumns.Contains(request.ZoneColumnName))
                ? request.ZoneColumnName
                : null;
            if (!string.IsNullOrWhiteSpace(zoneCol)) EnsureValidIdentifier(zoneCol, "Zone Column Name");

            var prodDefectNameCol = !string.IsNullOrWhiteSpace(request.DefectNameColumnName) && (productColumns.Count == 0 || productColumns.Contains(request.DefectNameColumnName))
                ? request.DefectNameColumnName
                : null;
            if (!string.IsNullOrWhiteSpace(prodDefectNameCol)) EnsureValidIdentifier(prodDefectNameCol, "Defect Name Column Name");

            var rawInspectionCol = !string.IsNullOrWhiteSpace(request.InspectedAtColumnName)
                ? request.InspectedAtColumnName
                : request.DateTimeInspectionColumnName;
            var inspectionCol = !string.IsNullOrWhiteSpace(rawInspectionCol) && (productColumns.Count == 0 || productColumns.Contains(rawInspectionCol))
                ? rawInspectionCol
                : null;
            if (!string.IsNullOrWhiteSpace(inspectionCol)) EnsureValidIdentifier(inspectionCol, "DateTime Inspection Column Name");

            var sampleLimit = Math.Clamp(request.SampleLimit, 1, 50);
            var judgeFilters = request.JudgeFilterValues is { Count: > 0 }
                ? request.JudgeFilterValues
                : ["N", "NG"];

            // 1. Query sample products
            var judgeParamNames = judgeFilters.Select((_, i) => $"@judge{i}").ToList();
            var judgeInClause = string.Join(", ", judgeParamNames);

            var selectProductCols = new List<string>
            {
                $"`{request.IdColumnName}`",
                $"`{request.FkIdColumnName}`",
                $"`{request.JudgeColumnName}`"
            };
            if (!string.IsNullOrWhiteSpace(zoneCol) && !selectProductCols.Contains($"`{zoneCol}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{zoneCol}`");
            }
            if (!string.IsNullOrWhiteSpace(prodDefectNameCol) && !selectProductCols.Contains($"`{prodDefectNameCol}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{prodDefectNameCol}`");
            }
            if (!string.IsNullOrWhiteSpace(inspectionCol) && !selectProductCols.Contains($"`{inspectionCol}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{inspectionCol}`");
            }

            var nowUtc = DateTime.UtcNow;
            var maxInspectionTimeUtc = nowUtc.AddSeconds(-60);

            var productSql = $"""
                SELECT {string.Join(", ", selectProductCols)}
                FROM `{request.ProductTableName}`
                WHERE `{request.JudgeColumnName}` IN ({judgeInClause})
                """;

            if (!string.IsNullOrWhiteSpace(inspectionCol))
            {
                productSql += $" AND `{inspectionCol}` <= @maxInspectionTimeUtc ORDER BY `{inspectionCol}` DESC";
            }
            productSql += $" LIMIT {sampleLimit};";

            await using var productCmd = new MySqlCommand(productSql, mySqlConnection) { CommandTimeout = 120 };
            for (var i = 0; i < judgeFilters.Count; i++)
            {
                productCmd.Parameters.AddWithValue($"@judge{i}", judgeFilters.ElementAt(i));
            }
            if (!string.IsNullOrWhiteSpace(inspectionCol))
            {
                productCmd.Parameters.AddWithValue("@maxInspectionTimeUtc", maxInspectionTimeUtc);
            }

            var sampleProducts = new List<IReadOnlyDictionary<string, object?>>();
            var sampleFkIds = new List<string>();

            await using (var reader = await productCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var colName = reader.GetName(i);
                        var val = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                        row[colName] = val;
                    }

                    // Standardize preview aliases
                    if (!string.IsNullOrWhiteSpace(zoneCol) && row.TryGetValue(zoneCol, out var zVal))
                    {
                        row["Zone"] = zVal;
                    }
                    if (!string.IsNullOrWhiteSpace(prodDefectNameCol) && row.TryGetValue(prodDefectNameCol, out var dnVal))
                    {
                        row["Defect Name"] = dnVal;
                    }
                    if (!string.IsNullOrWhiteSpace(inspectionCol) && row.TryGetValue(inspectionCol, out var insVal))
                    {
                        row["DateTime Inspection"] = insVal;
                    }

                    sampleProducts.Add(row);

                    var fkVal = row.GetValueOrDefault(request.FkIdColumnName)?.ToString();
                    var idVal = row.GetValueOrDefault(request.IdColumnName)?.ToString();
                    var effectiveKey = IsUsableForeignKey(fkVal) ? fkVal : idVal;
                    var prodJudge = row.GetValueOrDefault(request.JudgeColumnName)?.ToString()?.Trim() ?? string.Empty;
                    var isNgProduct = prodJudge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                                      prodJudge.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                                      prodJudge.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                                      prodJudge.Equals("NA", StringComparison.OrdinalIgnoreCase);
                    if (isNgProduct && !string.IsNullOrWhiteSpace(effectiveKey))
                    {
                        sampleFkIds.Add(effectiveKey);
                    }
                }
            }

            // Check Deduplication Lock for sample products: Which products are already saved in db.ProductRecords?
            var sampleTokensToCheck = sampleFkIds
                .Concat(sampleProducts.Select(r => r.GetValueOrDefault(request.IdColumnName)?.ToString()))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var savedKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sampleTokensToCheck.Count > 0)
            {
                var matchedByKeyId = await db.ProductRecords
                    .AsNoTracking()
                    .Where(p => sampleTokensToCheck.Contains(p.KeyId))
                    .Select(p => new { p.KeyId, p.PanelId })
                    .ToListAsync(cancellationToken);
                var matchedByPanelId = await db.ProductRecords
                    .AsNoTracking()
                    .Where(p => sampleTokensToCheck.Contains(p.PanelId))
                    .Select(p => new { p.KeyId, p.PanelId })
                    .ToListAsync(cancellationToken);

                foreach (var p in matchedByKeyId.Concat(matchedByPanelId))
                {
                    if (!string.IsNullOrWhiteSpace(p.KeyId)) savedKeySet.Add(p.KeyId);
                    if (!string.IsNullOrWhiteSpace(p.PanelId)) savedKeySet.Add(p.PanelId);
                }
            }

            var alreadySavedCount = 0;
            foreach (var prodRow in sampleProducts)
            {
                if (prodRow is Dictionary<string, object?> mutRow)
                {
                    var id = mutRow.GetValueOrDefault(request.IdColumnName)?.ToString();
                    var fk = mutRow.GetValueOrDefault(request.FkIdColumnName)?.ToString();
                    var isSaved = (!string.IsNullOrWhiteSpace(id) && savedKeySet.Contains(id)) || (IsUsableForeignKey(fk) && savedKeySet.Contains(fk!));
                    mutRow["Lock Status"] = isSaved ? "🔒 Saved (Skipped)" : "✨ New";
                    if (isSaved) alreadySavedCount++;
                }
            }

            // 2. Query defect tables using sample FK IDs
            var defectCountsByTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sampleDefects = new List<IReadOnlyDictionary<string, object?>>();
            var totalBypassedDefects = 0;
            var totalScannedDefects = 0;
            var totalAcceptedDefects = 0;
            var bypassRule = request.BypassRule;

            foreach (var defectTable in request.DefectTables)
            {
                EnsureValidIdentifier(defectTable.TableName, $"Defect Table '{defectTable.TableName}'");
                var fkCol = string.IsNullOrWhiteSpace(defectTable.FkColumnName) ? "Fk_ID" : defectTable.FkColumnName;
                var defectNameCol = string.IsNullOrWhiteSpace(defectTable.DefectNameColumn) ? "DefectName" : defectTable.DefectNameColumn;
                EnsureValidIdentifier(fkCol, "Defect FK Column");
                EnsureValidIdentifier(defectNameCol, "Defect Name Column");

                if (sampleFkIds.Count == 0)
                {
                    defectCountsByTable[defectTable.TableName] = 0;
                    continue;
                }

                var fkParamNames = sampleFkIds.Select((_, i) => $"@fk{i}").ToList();
                var fkInClause = string.Join(", ", fkParamNames);

                var selectCols = new List<string> { $"`{fkCol}`", $"`{defectNameCol}`" };
                AddOptionalColumn(selectCols, defectTable.DefectTypeColumn);
                AddOptionalColumn(selectCols, defectTable.SeverityColumn);
                AddOptionalColumn(selectCols, defectTable.JudgeColumn);
                AddOptionalColumn(selectCols, defectTable.ComponentNameColumn);
                AddOptionalColumn(selectCols, defectTable.CoordXColumn);
                AddOptionalColumn(selectCols, defectTable.CoordYColumn);
                AddOptionalColumn(selectCols, defectTable.ImageUriColumn);
                AddOptionalColumn(selectCols, defectTable.FeatureColumn ?? defectTable.Feature1JsonColumn);
                AddOptionalColumn(selectCols, defectTable.CustomFeatureColumn ?? defectTable.Feature2JsonColumn);
                AddOptionalColumn(selectCols, defectTable.ReasonColumn);
                AddOptionalColumn(selectCols, defectTable.EqpNameColumn);
                AddOptionalColumn(selectCols, defectTable.EmNoColumn);
                AddOptionalColumn(selectCols, defectTable.SpecNameColumn);
                AddOptionalColumn(selectCols, defectTable.SpecJudgeColumn);
                AddOptionalColumn(selectCols, defectTable.SpecReasonColumn);
                AddOptionalColumn(selectCols, defectTable.PriorityColumn);

                // Push down defect bypass filtering directly to MariaDB to avoid fetching 'G' rows
                var judgeFilterClause = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, bypassRule);
                var defectLimit = Math.Clamp(request.DefectSampleLimit > 0 ? request.DefectSampleLimit : 2000, 100, 10000);

                var defectSql = $"""
                    SELECT {string.Join(", ", selectCols)}
                    FROM `{defectTable.TableName}`
                    WHERE `{fkCol}` IN ({fkInClause}){judgeFilterClause}
                    LIMIT {defectLimit};
                    """;
                generatedDefectSqlList.Add($"-- [{defectTable.TableName}] (FK Column: `{fkCol}`)\n" + defectSql);

                await using var defectCmd = new MySqlCommand(defectSql, mySqlConnection) { CommandTimeout = 120 };
                for (var i = 0; i < sampleFkIds.Count; i++)
                {
                    defectCmd.Parameters.AddWithValue($"@fk{i}", sampleFkIds[i]);
                }

                var tableDefectCount = 0;
                var tableSampleCount = 0;
                var maxSamplesPerTable = Math.Max(50, 200 / Math.Max(1, request.DefectTables.Count));

                await using (var defectReader = await defectCmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await defectReader.ReadAsync(cancellationToken))
                    {
                        var defectRow = new Dictionary<string, object?> { ["_DefectTable"] = defectTable.TableName };
                        for (var i = 0; i < defectReader.FieldCount; i++)
                        {
                            var colName = defectReader.GetName(i);
                            if (await defectReader.IsDBNullAsync(i, cancellationToken))
                            {
                                defectRow[colName] = null;
                            }
                            else
                            {
                                var val = defectReader.GetValue(i);
                                if (val is byte[] bytes)
                                {
                                    defectRow[colName] = Encoding.UTF8.GetString(bytes);
                                }
                                else
                                {
                                    defectRow[colName] = val;
                                }
                            }
                        }

                        totalScannedDefects++;

                        // Evaluate configurable Defect bypass rule (or legacy fallback when rule is null/disabled)
                        if (DefectBypassRuleEvaluator.ShouldBypass(defectRow, defectTable, bypassRule))
                        {
                            totalBypassedDefects++;
                            continue;
                        }

                        totalAcceptedDefects++;
                        tableDefectCount++;
                        if (tableSampleCount < maxSamplesPerTable && sampleDefects.Count < 300)
                        {
                            tableSampleCount++;
                            // If feature column was decoded, check if coordinates can be extracted/summarized
                            var featCol = defectTable.FeatureColumn ?? defectTable.Feature1JsonColumn;
                            if (!string.IsNullOrWhiteSpace(featCol) && defectRow.TryGetValue(featCol, out var featVal) && featVal is string featStr && !string.IsNullOrWhiteSpace(featStr))
                            {
                                var (cx, cy) = ExtractCoordinatesFromFeatureJson(featStr, null, null);
                                if (cx.HasValue && cy.HasValue && !defectRow.ContainsKey("RealX"))
                                {
                                    defectRow["_ExtractedCoordX"] = cx.Value;
                                    defectRow["_ExtractedCoordY"] = cy.Value;
                                }
                            }

                            sampleDefects.Add(defectRow);
                        }
                    }
                }

                defectCountsByTable[defectTable.TableName] = tableDefectCount;
            }

            // If Defect Name was not in the product table or empty in sample product, fall back to first non-empty defect name found
            if (sampleDefects.Count > 0)
            {
                var firstDefectByFk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var defect in sampleDefects)
                {
                    string? fkKey = null;
                    string? dName = null;
                    foreach (var (k, v) in defect)
                    {
                        if (k.StartsWith("Fk", StringComparison.OrdinalIgnoreCase) || k.Equals("KeyId", StringComparison.OrdinalIgnoreCase))
                        {
                            fkKey = v?.ToString();
                        }
                        if (k.Contains("Defect", StringComparison.OrdinalIgnoreCase) && !k.Equals("_DefectTable", StringComparison.OrdinalIgnoreCase) && !k.Contains("Type", StringComparison.OrdinalIgnoreCase) && !k.Contains("Count", StringComparison.OrdinalIgnoreCase))
                        {
                            if (v != null && !string.IsNullOrWhiteSpace(v.ToString()) && !v.ToString()!.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                            {
                                dName = v.ToString();
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(fkKey) && !string.IsNullOrWhiteSpace(dName) && !firstDefectByFk.ContainsKey(fkKey))
                    {
                        firstDefectByFk[fkKey] = dName;
                    }
                }

                foreach (var prodRow in sampleProducts)
                {
                    if (prodRow is Dictionary<string, object?> mutableRow)
                    {
                        var fkVal = mutableRow.GetValueOrDefault(request.FkIdColumnName)?.ToString();
                        var idVal = mutableRow.GetValueOrDefault(request.IdColumnName)?.ToString();
                        var effectiveKey = IsUsableForeignKey(fkVal) ? fkVal : idVal;

                        if (effectiveKey != null && firstDefectByFk.TryGetValue(effectiveKey, out var fallbackName))
                        {
                            if (!mutableRow.TryGetValue("Defect Name", out var curD) || curD == null || string.IsNullOrWhiteSpace(curD.ToString()))
                            {
                                mutableRow["Defect Name"] = fallbackName;
                            }
                        }
                        if (!mutableRow.ContainsKey("Zone")) mutableRow["Zone"] = null;
                        if (!mutableRow.ContainsKey("DateTime Inspection")) mutableRow["DateTime Inspection"] = null;
                        if (!mutableRow.ContainsKey("Defect Name")) mutableRow["Defect Name"] = null;
                    }
                }
            }
            else
            {
                foreach (var prodRow in sampleProducts)
                {
                    if (prodRow is Dictionary<string, object?> mutableRow)
                    {
                        if (!mutableRow.ContainsKey("Zone")) mutableRow["Zone"] = null;
                        if (!mutableRow.ContainsKey("DateTime Inspection")) mutableRow["DateTime Inspection"] = null;
                        if (!mutableRow.ContainsKey("Defect Name")) mutableRow["Defect Name"] = null;
                    }
                }
            }

            stopwatch.Stop();
            var allGeneratedSql = generatedDefectSqlList.Count > 0
                ? string.Join("\n\n", generatedDefectSqlList)
                : null;

            return new TestPipelineMappingResponse(
                true,
                sampleProducts.Count,
                sampleProducts,
                defectCountsByTable,
                sampleDefects,
                (int)stopwatch.ElapsedMilliseconds,
                null,
                totalBypassedDefects,
                DefectBypassRuleEvaluator.FormatSummary(bypassRule),
                totalScannedDefects,
                totalAcceptedDefects,
                allGeneratedSql,
                alreadySavedCount);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "TestMappingAsync failed for database connection {ConnectionId}", request.DatabaseConnectionId);
            var allGeneratedSql = generatedDefectSqlList.Count > 0
                ? string.Join("\n\n", generatedDefectSqlList)
                : null;

            return new TestPipelineMappingResponse(
                false,
                0,
                [],
                new Dictionary<string, int>(),
                [],
                (int)stopwatch.ElapsedMilliseconds,
                ex.Message,
                0,
                DefectBypassRuleEvaluator.FormatSummary(request.BypassRule),
                0,
                0,
                allGeneratedSql,
                0);
        }
    }

    public async Task<OverkillPipelineExecutionEntity> ExecutePipelineAsync(
        Guid configId,
        string triggerType = "Scheduled",
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var config = await db.OverkillPipelineConfigs
            .SingleOrDefaultAsync(x => x.Id == configId, cancellationToken);

        if (config is null)
        {
            throw new KeyNotFoundException($"Overkill pipeline configuration '{configId}' was not found.");
        }

        // Global execution lock per pipeline: Rejects concurrent overlapping runs to prevent duplicate product/defect inserts
        var semaphore = ExecutionLocks.GetOrAdd(configId, _ => new SemaphoreSlim(1, 1));
        var lockAcquired = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        if (!lockAcquired)
        {
            logger.LogWarning("Pipeline '{PipelineName}' ({ConfigId}) is already running. Concurrent run rejected to prevent duplicate records.", config.Name, configId);
            return new OverkillPipelineExecutionEntity
            {
                Id = Guid.NewGuid(),
                ConfigId = configId,
                PipelineName = config.Name,
                TriggerType = triggerType,
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Status = "Skipped",
                ProductsScanned = 0,
                DefectsScanned = 0,
                DefectsExtracted = 0,
                OverkillCandidatesCount = 0,
                LatencyMs = 0,
                ErrorMessage = "Pipeline is already executing. Concurrent run skipped to prevent duplicate records."
            };
        }

        var execution = new OverkillPipelineExecutionEntity
        {
            Id = Guid.NewGuid(),
            ConfigId = config.Id,
            PipelineName = config.Name,
            TriggerType = triggerType,
            StartedAtUtc = startedAt,
            Status = "Running"
        };
        db.OverkillPipelineExecutions.Add(execution);

        config.LastRunStatus = "Running";
        config.LastRunAtUtc = startedAt;
        config.LastRunMessage = $"Pipeline execution initiated via {triggerType}.";
        config.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var connection = await db.DatabaseConnections
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == config.DatabaseConnectionId, cancellationToken);

            if (connection is null)
            {
                throw new InvalidOperationException($"Referenced Database Connection with ID '{config.DatabaseConnectionId}' does not exist.");
            }

            EnsureValidIdentifier(config.ProductTableName, "Product Table Name");
            EnsureValidIdentifier(config.IdColumnName, "ID Column Name");
            EnsureValidIdentifier(config.FkIdColumnName, "FK ID Column Name");
            EnsureValidIdentifier(config.JudgeColumnName, "Judge Column Name");

            var connString = BuildConnectionString(connection);
            await using var mySqlConnection = new MySqlConnection(connString);
            await mySqlConnection.OpenAsync(cancellationToken);

            var productColumns = await GetTableColumnsAsync(mySqlConnection, config.ProductTableName, cancellationToken);

            var zoneCol = !string.IsNullOrWhiteSpace(config.ZoneColumnName) && (productColumns.Count == 0 || productColumns.Contains(config.ZoneColumnName))
                ? config.ZoneColumnName
                : null;
            if (!string.IsNullOrWhiteSpace(zoneCol)) EnsureValidIdentifier(zoneCol, "Zone Column Name");

            var prodDefectNameCol = !string.IsNullOrWhiteSpace(config.DefectNameColumnName) && (productColumns.Count == 0 || productColumns.Contains(config.DefectNameColumnName))
                ? config.DefectNameColumnName
                : null;
            if (!string.IsNullOrWhiteSpace(prodDefectNameCol)) EnsureValidIdentifier(prodDefectNameCol, "Defect Name Column Name");

            var rawInspectionCol = !string.IsNullOrWhiteSpace(config.DateTimeInspectionColumnName)
                ? config.DateTimeInspectionColumnName
                : (!string.IsNullOrWhiteSpace(config.InspectedAtColumnName)
                    ? config.InspectedAtColumnName
                    : config.TimestampColumnName);
            var inspectionCol = !string.IsNullOrWhiteSpace(rawInspectionCol) && (productColumns.Count == 0 || productColumns.Contains(rawInspectionCol))
                ? rawInspectionCol
                : null;
            if (!string.IsNullOrWhiteSpace(inspectionCol)) EnsureValidIdentifier(inspectionCol, "DateTime Inspection Column Name");
            if (!string.IsNullOrWhiteSpace(config.TimestampColumnName)) EnsureValidIdentifier(config.TimestampColumnName, "Timestamp Column Name");

            var timeCol = !string.IsNullOrWhiteSpace(inspectionCol) ? inspectionCol : config.TimestampColumnName;

            var judgeFilters = ParseJudgeFilterValues(config.JudgeFilterValuesJson);
            if (config.IncludeOkProducts)
            {
                var standardOkJudges = new[] { "G", "OK", "GOOD", "PASS" };
                foreach (var okJudge in standardOkJudges)
                {
                    if (!judgeFilters.Contains(okJudge, StringComparer.OrdinalIgnoreCase))
                    {
                        judgeFilters.Add(okJudge);
                    }
                }
            }
            var defectTables = ParseDefectTables(config.DefectTablesJson);

            // Step 1: Query Products from target MariaDB
            var judgeParamNames = judgeFilters.Select((_, i) => $"@judge{i}").ToList();
            var judgeInClause = judgeParamNames.Count > 0 ? string.Join(", ", judgeParamNames) : "''";
            var nowUtc = DateTime.UtcNow;
            var lookbackUtc = nowUtc.AddHours(-Math.Max(1, config.LookbackHours));
            // Safety buffer: step back 60s from current time so boards currently undergoing inspection are not prematurely scanned with 0 defects (NA)
            var maxInspectionTimeUtc = nowUtc.AddSeconds(-60);
            var batchSize = Math.Clamp(config.BatchSize, 10, 10000);

            var selectProductCols = new List<string>
            {
                $"`{config.IdColumnName}`",
                $"`{config.FkIdColumnName}`",
                $"`{config.JudgeColumnName}`"
            };
            if (!string.IsNullOrWhiteSpace(zoneCol) && !selectProductCols.Contains($"`{zoneCol}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{zoneCol}`");
            }
            if (!string.IsNullOrWhiteSpace(prodDefectNameCol) && !selectProductCols.Contains($"`{prodDefectNameCol}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{prodDefectNameCol}`");
            }
            if (!string.IsNullOrWhiteSpace(inspectionCol) && !selectProductCols.Contains($"`{inspectionCol}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{inspectionCol}`");
            }
            else if (!string.IsNullOrWhiteSpace(config.TimestampColumnName) && !selectProductCols.Contains($"`{config.TimestampColumnName}`", StringComparer.OrdinalIgnoreCase))
            {
                selectProductCols.Add($"`{config.TimestampColumnName}`");
            }

            var productSql = $"""
                SELECT {string.Join(", ", selectProductCols)}
                FROM `{config.ProductTableName}`
                WHERE `{config.JudgeColumnName}` IN ({judgeInClause})
                """;

            if (!string.IsNullOrWhiteSpace(timeCol))
            {
                if (config.LookbackHours > 0)
                {
                    productSql += $" AND `{timeCol}` >= @lookbackUtc";
                }
                productSql += $" AND `{timeCol}` <= @maxInspectionTimeUtc";
            }

            if (!string.IsNullOrWhiteSpace(timeCol))
            {
                productSql += $" ORDER BY `{timeCol}` DESC";
            }
            else
            {
                productSql += $" ORDER BY `{config.IdColumnName}` DESC";
            }
            productSql += $" LIMIT {batchSize};";

            await using var productCmd = new MySqlCommand(productSql, mySqlConnection) { CommandTimeout = 180 };
            for (var i = 0; i < judgeFilters.Count; i++)
            {
                productCmd.Parameters.AddWithValue($"@judge{i}", judgeFilters[i]);
            }
            if (!string.IsNullOrWhiteSpace(timeCol))
            {
                if (config.LookbackHours > 0)
                {
                    productCmd.Parameters.AddWithValue("@lookbackUtc", lookbackUtc);
                }
                productCmd.Parameters.AddWithValue("@maxInspectionTimeUtc", maxInspectionTimeUtc);
            }

            var scannedProducts = new List<RawProductRow>();
            await using (var reader = await productCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var idVal = reader[config.IdColumnName]?.ToString()?.Trim() ?? string.Empty;
                    var fkVal = reader[config.FkIdColumnName]?.ToString()?.Trim() ?? string.Empty;
                    var judgeVal = reader[config.JudgeColumnName]?.ToString()?.Trim() ?? "NG";

                    string? zoneVal = null;
                    if (!string.IsNullOrWhiteSpace(zoneCol))
                    {
                        var ord = reader.GetOrdinal(zoneCol);
                        if (!await reader.IsDBNullAsync(ord, cancellationToken))
                        {
                            zoneVal = reader.GetValue(ord)?.ToString()?.Trim();
                        }
                    }

                    string? prodDefectName = null;
                    if (!string.IsNullOrWhiteSpace(prodDefectNameCol))
                    {
                        var ord = reader.GetOrdinal(prodDefectNameCol);
                        if (!await reader.IsDBNullAsync(ord, cancellationToken))
                        {
                            prodDefectName = reader.GetValue(ord)?.ToString()?.Trim();
                        }
                    }

                    DateTime? timestamp = null;
                    if (!string.IsNullOrWhiteSpace(timeCol))
                    {
                        var ordinal = reader.GetOrdinal(timeCol);
                        if (!await reader.IsDBNullAsync(ordinal, cancellationToken))
                        {
                            var obj = reader.GetValue(ordinal);
                            if (obj is DateTime dt) timestamp = dt;
                            else if (DateTime.TryParse(obj?.ToString(), out var parsedDt)) timestamp = parsedDt;
                        }
                    }

                    // Good products often carry FK placeholders such as 0 or '-'. They must use
                    // their Product ID or every OK board collapses into one deduplication key.
                    var keyId = IsUsableForeignKey(fkVal) ? fkVal : idVal;
                    if (!string.IsNullOrWhiteSpace(keyId))
                    {
                        scannedProducts.Add(new RawProductRow(
                            keyId,
                            idVal,
                            fkVal,
                            judgeVal,
                            timestamp ?? DateTime.UtcNow,
                            zoneVal,
                            prodDefectName));
                    }
                }
            }

            // In-batch deduplication: if MariaDB returns multiple rows for the same product, keep newest inspection row
            scannedProducts = scannedProducts
                .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.InspectedAt).First())
                .ToList();

            // Step 1.5: Deduplication Lock / Lookup - Filter out products whose ID or FK Key have already been saved
            var candidateKeyIds = scannedProducts.Select(p => p.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var candidateIds = scannedProducts.Select(p => p.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var candidateFks = scannedProducts.Select(p => p.FkId).Where(IsUsableForeignKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var keyLookupTokens = candidateKeyIds
                .Concat(candidateFks)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var panelLookupTokens = candidateIds
                .Concat(candidateFks)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var alreadySavedKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in keyLookupTokens.Chunk(2000))
            {
                var chunkKeys = chunk.ToArray();
                var matchedByKey = await db.ProductRecords
                    .AsNoTracking()
                    .Where(p => chunkKeys.Contains(p.KeyId))
                    .Select(p => new { p.KeyId, p.PanelId })
                    .ToListAsync(cancellationToken);
                foreach (var p in matchedByKey)
                {
                    if (!string.IsNullOrWhiteSpace(p.KeyId)) alreadySavedKeySet.Add(p.KeyId.Trim());
                    if (!string.IsNullOrWhiteSpace(p.PanelId)) alreadySavedKeySet.Add(p.PanelId.Trim());
                }
            }

            foreach (var chunk in panelLookupTokens.Chunk(2000))
            {
                var chunkPanels = chunk.ToArray();
                var matchedByPanel = await db.ProductRecords
                    .AsNoTracking()
                    .Where(p => chunkPanels.Contains(p.PanelId))
                    .Select(p => new { p.KeyId, p.PanelId })
                    .ToListAsync(cancellationToken);
                foreach (var p in matchedByPanel)
                {
                    if (!string.IsNullOrWhiteSpace(p.KeyId)) alreadySavedKeySet.Add(p.KeyId.Trim());
                    if (!string.IsNullOrWhiteSpace(p.PanelId)) alreadySavedKeySet.Add(p.PanelId.Trim());
                }
            }

            var newProducts = scannedProducts
                .Where(p => !alreadySavedKeySet.Contains(p.KeyId) &&
                            !alreadySavedKeySet.Contains(p.Id) &&
                            (!IsUsableForeignKey(p.FkId) || !alreadySavedKeySet.Contains(p.FkId)))
                .ToList();

            var skippedAlreadySavedCount = scannedProducts.Count - newProducts.Count;
            if (skippedAlreadySavedCount > 0)
            {
                logger.LogInformation(
                    "Pipeline '{PipelineName}': Deduplication lock skipped {SkippedCount} products already saved in database (matched existing ID/FK). Processing {NewCount} new product(s).",
                    config.Name, skippedAlreadySavedCount, newProducts.Count);
            }

            if (newProducts.Count == 0)
            {
                stopwatch.Stop();
                var skippedExecution = new OverkillPipelineExecutionEntity
                {
                    Id = Guid.NewGuid(),
                    ConfigId = config.Id,
                    PipelineName = config.Name,
                    TriggerType = triggerType,
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Status = "Success",
                    ProductsScanned = scannedProducts.Count,
                    DefectsExtracted = 0,
                    OverkillCandidatesCount = 0,
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    ErrorMessage = null,
                    ExecutionSummaryJson = JsonSerializer.Serialize(new
                    {
                        totalScannedFromRemote = scannedProducts.Count,
                        skippedAlreadySaved = skippedAlreadySavedCount,
                        newProductsProcessed = 0,
                        note = "All scanned products have already been saved previously. Skipped defect queries to prevent duplication."
                    })
                };
                db.OverkillPipelineExecutions.Add(skippedExecution);
                config.LastRunAtUtc = startedAt;
                config.LastRunStatus = "Success";
                config.LastRunProductsCount = scannedProducts.Count;
                config.LastRunDefectsCount = 0;
                config.LastRunMessage = $"Deduplication lock: All {scannedProducts.Count} scanned products were already saved. 0 duplicates added.";
                await db.SaveChangesAsync(cancellationToken);
                return skippedExecution;
            }

            // Only process brand new products that have not been saved before
            scannedProducts = newProducts;

            // Step 2: Query Defect Tables ONLY for NG/N products (filter only pulls defects of NG/N; OK products have 0 defects)
            var ngProducts = scannedProducts
                .Where(p => p.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                            p.Judge.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                            p.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                            p.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var distinctKeyIds = ngProducts.Select(p => p.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var allExtractedDefects = new List<RawDefectRow>();
            var defectCountsByTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bypassRule = DefectBypassRuleEvaluator.ParseRule(config.DefectBypassRuleJson);
            var totalBypassedDefects = 0;
            var totalScannedDefects = 0;

            if (distinctKeyIds.Count > 0 && defectTables.Count > 0)
            {
                const int chunkSize = 75;
                var chunks = distinctKeyIds.Chunk(chunkSize).ToList();

                foreach (var defectTable in defectTables)
                {
                    EnsureValidIdentifier(defectTable.TableName, $"Defect Table '{defectTable.TableName}'");
                    var fkCol = string.IsNullOrWhiteSpace(defectTable.FkColumnName) ? "Fk_ID" : defectTable.FkColumnName;
                    var defectNameCol = string.IsNullOrWhiteSpace(defectTable.DefectNameColumn) ? "DefectName" : defectTable.DefectNameColumn;
                    EnsureValidIdentifier(fkCol, "Defect FK Column");
                    EnsureValidIdentifier(defectNameCol, "Defect Name Column");

                    var tableDefectCount = 0;
                    var selectDefectCols = new List<string> { $"`{fkCol}`", $"`{defectNameCol}`" };
                    AddOptionalColumn(selectDefectCols, defectTable.DefectTypeColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.SeverityColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.JudgeColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.ComponentNameColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.CoordXColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.CoordYColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.ImageUriColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.FeatureColumn ?? defectTable.Feature1JsonColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.CustomFeatureColumn ?? defectTable.Feature2JsonColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.ReasonColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.EqpNameColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.EmNoColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.SpecNameColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.SpecJudgeColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.SpecReasonColumn);
                    AddOptionalColumn(selectDefectCols, defectTable.PriorityColumn);

                    // Push down defect bypass filtering directly to MariaDB to prevent pulling 'G' non-defect rows
                    var judgeFilterClause = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, bypassRule);

                    async Task ProcessChunkWithRetryAsync(string[] chunkItems, int depth = 0)
                    {
                        var fkParamNames = chunkItems.Select((_, i) => $"@fk{i}").ToList();
                        var fkInClause = string.Join(", ", fkParamNames);
                        var defectSql = $"""
                            SELECT {string.Join(", ", selectDefectCols)}
                            FROM `{defectTable.TableName}`
                            WHERE `{fkCol}` IN ({fkInClause}){judgeFilterClause};
                            """;

                        try
                        {
                            await using var defectCmd = new MySqlCommand(defectSql, mySqlConnection) { CommandTimeout = 180 };
                            for (var i = 0; i < chunkItems.Length; i++)
                            {
                                defectCmd.Parameters.AddWithValue($"@fk{i}", chunkItems[i]);
                            }

                            await using var defectReader = await defectCmd.ExecuteReaderAsync(cancellationToken);
                            while (await defectReader.ReadAsync(cancellationToken))
                            {
                                var fk = defectReader[fkCol]?.ToString() ?? string.Empty;
                                var defectName = defectReader[defectNameCol]?.ToString()?.Trim() ?? string.Empty;
                                var defectType = GetStringColumnValue(defectReader, defectTable.DefectTypeColumn) ?? "Optical";
                                var severity = GetStringColumnValue(defectReader, defectTable.SeverityColumn) ?? "Minor";
                                var judge = GetStringColumnValue(defectReader, defectTable.JudgeColumn)?.Trim() ?? string.Empty;

                                totalScannedDefects++;

                                // Explicit safeguard: bypass non-defects (empty name, 'None', or Good/OK judge)
                                if (string.IsNullOrWhiteSpace(defectName) ||
                                    defectName.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                                    defectName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                                    defectName == "-" || defectName == "0")
                                {
                                    totalBypassedDefects++;
                                    continue;
                                }

                                if (string.IsNullOrWhiteSpace(judge) ||
                                    string.Equals(judge, "G", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(judge, "OK", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(judge, "GOOD", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(judge, "PASS", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(judge, "FD", StringComparison.OrdinalIgnoreCase) ||
                                    judge == "-" || judge == "0")
                                {
                                    totalBypassedDefects++;
                                    continue;
                                }

                                var component = GetStringColumnValue(defectReader, defectTable.ComponentNameColumn);
                                var coordX = GetDoubleColumnValue(defectReader, defectTable.CoordXColumn);
                                var coordY = GetDoubleColumnValue(defectReader, defectTable.CoordYColumn);
                                var imageUri = GetStringColumnValue(defectReader, defectTable.ImageUriColumn);
                                var featCol = defectTable.FeatureColumn ?? defectTable.Feature1JsonColumn;
                                var customFeatCol = defectTable.CustomFeatureColumn ?? defectTable.Feature2JsonColumn;
                                var featureJson = GetBlobOrStringColumnValue(defectReader, featCol);
                                var customFeatureJson = GetBlobOrStringColumnValue(defectReader, customFeatCol);
                                var reason = GetStringColumnValue(defectReader, defectTable.ReasonColumn);
                                var eqpName = GetStringColumnValue(defectReader, defectTable.EqpNameColumn);
                                var emNo = GetStringColumnValue(defectReader, defectTable.EmNoColumn);
                                var specName = GetStringColumnValue(defectReader, defectTable.SpecNameColumn);
                                var specJudge = GetStringColumnValue(defectReader, defectTable.SpecJudgeColumn);
                                var specReason = GetStringColumnValue(defectReader, defectTable.SpecReasonColumn);
                                var priority = GetIntColumnValue(defectReader, defectTable.PriorityColumn);

                                if ((!coordX.HasValue || !coordY.HasValue) && !string.IsNullOrWhiteSpace(featureJson))
                                {
                                    var (extractedX, extractedY) = ExtractCoordinatesFromFeatureJson(featureJson, coordX, coordY);
                                    coordX = extractedX;
                                    coordY = extractedY;
                                }

                                var rawDefect = new RawDefectRow(
                                    fk,
                                    defectTable.DisplayName ?? defectTable.TableName,
                                    defectName,
                                    defectType,
                                    component,
                                    coordX,
                                    coordY,
                                    severity,
                                    judge,
                                    imageUri,
                                    featureJson,
                                    customFeatureJson,
                                    reason,
                                    eqpName,
                                    emNo,
                                    specName,
                                    specJudge,
                                    specReason,
                                    priority);

                                // Evaluate configurable Defect bypass rule (or legacy fallback when rule is null/disabled)
                                if (DefectBypassRuleEvaluator.ShouldBypass(rawDefect, bypassRule))
                                {
                                    totalBypassedDefects++;
                                    continue;
                                }

                                allExtractedDefects.Add(rawDefect);
                                tableDefectCount++;
                            }
                        }
                        catch (MySqlException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) && chunkItems.Length > 20 && depth < 2)
                        {
                            logger.LogWarning("Command timeout for table {Table} with {Count} keys. Retrying with smaller sub-chunks...", defectTable.TableName, chunkItems.Length);
                            var subChunks = chunkItems.Chunk(Math.Max(10, chunkItems.Length / 3)).ToList();
                            foreach (var sub in subChunks)
                            {
                                await ProcessChunkWithRetryAsync(sub, depth + 1);
                            }
                        }
                    }

                    foreach (var chunk in chunks)
                    {
                        await ProcessChunkWithRetryAsync(chunk);
                    }

                    defectCountsByTable[defectTable.TableName] = tableDefectCount;
                }
            }

            // Step 3: Persist Products and Defects into AutoVerify persistence layer
            var productKeyList = distinctKeyIds;
            var existingProducts = await db.ProductRecords
                .Where(p => productKeyList.Contains(p.KeyId))
                .ToListAsync(cancellationToken);

            // Clean up any historical duplicate product records for these keys
            var duplicateExisting = existingProducts
                .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Skip(1))
                .ToList();
            if (duplicateExisting.Count > 0)
            {
                db.ProductRecords.RemoveRange(duplicateExisting);
            }

            var productByKey = existingProducts
                .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.InspectedAtUtc).First(), StringComparer.OrdinalIgnoreCase);

            // Group defects to support fallback when product defect name is not configured or empty
            var fallbackDefectByKey = allExtractedDefects
                .Where(d => !string.IsNullOrWhiteSpace(d.DefectName))
                .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.FirstOrDefault(d => !d.DefectName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))?.DefectName ?? g.First().DefectName,
                    StringComparer.OrdinalIgnoreCase);

            var savedProductEntities = new List<ProductRecordEntity>();
            foreach (var rawProd in scannedProducts)
            {
                var effectiveDefectName = !string.IsNullOrWhiteSpace(rawProd.DefectName)
                    ? rawProd.DefectName
                    : (fallbackDefectByKey.TryGetValue(rawProd.KeyId, out var fbDefect) ? fbDefect : null);

                if (productByKey.TryGetValue(rawProd.KeyId, out var existingProd))
                {
                    existingProd.Judge = rawProd.Judge;
                    existingProd.MachineCode = config.MachineCode;
                    existingProd.LineCode = config.LineCode;
                    existingProd.ModelCode = config.ModelCode;
                    existingProd.InspectedAtUtc = DateTime.SpecifyKind(rawProd.InspectedAt, DateTimeKind.Utc);
                    if (!string.IsNullOrWhiteSpace(rawProd.Zone))
                    {
                        existingProd.Zone = rawProd.Zone;
                    }
                    if (!string.IsNullOrWhiteSpace(effectiveDefectName))
                    {
                        existingProd.DefectName = effectiveDefectName;
                        if (string.IsNullOrWhiteSpace(existingProd.NameDefect) || existingProd.NameDefect == "None")
                        {
                            existingProd.NameDefect = effectiveDefectName;
                        }
                    }
                    savedProductEntities.Add(existingProd);
                }
                else
                {
                    var newProd = new ProductRecordEntity
                    {
                        Id = Guid.NewGuid(),
                        KeyId = rawProd.KeyId,
                        Judge = rawProd.Judge,
                        PanelId = rawProd.Id,
                        PanelJudge = rawProd.Judge,
                        NameDefect = effectiveDefectName ?? "None",
                        DefectName = effectiveDefectName,
                        Zone = rawProd.Zone,
                        MachineCode = config.MachineCode,
                        LineCode = config.LineCode,
                        ModelCode = config.ModelCode,
                        InspectedAtUtc = DateTime.SpecifyKind(rawProd.InspectedAt, DateTimeKind.Utc),
                        MetadataJson = JsonSerializer.Serialize(new { rawProd.Id, rawProd.FkId, Source = config.Name })
                    };
                    db.ProductRecords.Add(newProd);
                    productByKey[rawProd.KeyId] = newProd;
                    savedProductEntities.Add(newProd);
                }
            }

            // Delete prior defects for these products to prevent duplication across sync iterations
            if (productKeyList.Count > 0)
            {
                var priorDefects = await db.DefectRecords
                    .Where(d => productKeyList.Contains(d.KeyId))
                    .ToListAsync(cancellationToken);
                if (priorDefects.Count > 0)
                {
                    db.DefectRecords.RemoveRange(priorDefects);
                }
            }

            // In-batch deduplication of extracted defects: ensure unique defect coordinates and labels per product
            allExtractedDefects = allExtractedDefects
                .GroupBy(d => $"{d.KeyId}|{d.DefectTable}|{d.DefectName}|{d.CoordinateX:F2}|{d.CoordinateY:F2}|{d.ComponentName}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var shouldOffloadDefects = config.OffloadDefectsToSeaweedFs;
            var defectMappingDict = defectTables.ToDictionary(m => m.TableName, m => m, StringComparer.OrdinalIgnoreCase);

            var savedDefectEntities = new List<DefectRecordEntity>();
            foreach (var rawDefect in allExtractedDefects)
            {
                var defectEntity = new DefectRecordEntity
                {
                    Id = Guid.NewGuid(),
                    KeyId = rawDefect.KeyId,
                    DefectTable = rawDefect.DefectTable,
                    DefectName = rawDefect.DefectName,
                    DefectType = rawDefect.DefectType,
                    ComponentName = rawDefect.ComponentName,
                    CoordinateX = rawDefect.CoordinateX,
                    CoordinateY = rawDefect.CoordinateY,
                    Severity = rawDefect.Severity,
                    Judge = rawDefect.Judge,
                    ConfidenceScore = 1.0,
                    ConnectionId = config.DatabaseConnectionId != Guid.Empty ? config.DatabaseConnectionId : null,
                    ModelCode = !string.IsNullOrWhiteSpace(config.ModelCode) ? config.ModelCode : "UNASSIGNED",
                    MachineCode = config.MachineCode,
                    LineCode = config.LineCode,
                    ImageUri = rawDefect.ImageUri,
                    ImageUrl = rawDefect.ImageUri,
                    FeatureJson = rawDefect.FeatureJson,
                    CustomFeatureJson = rawDefect.CustomFeatureJson,
                    Reason = rawDefect.Reason,
                    EqpName = rawDefect.EqpName,
                    EmNo = rawDefect.EmNo,
                    SpecName = rawDefect.SpecName,
                    SpecJudge = rawDefect.SpecJudge,
                    SpecReason = rawDefect.SpecReason,
                    Priority = rawDefect.Priority,
                    CreatedAtUtc = DateTime.UtcNow
                };
                savedDefectEntities.Add(defectEntity);
            }

            // Offload defect features to SeaweedFS using unified Defect Pack JSON per Product ID
            var defectsByProd = savedDefectEntities
                .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase);

            foreach (var prodGroup in defectsByProd)
            {
                var prodKey = prodGroup.Key;
                var prodDefects = prodGroup.ToList();
                var shouldOffloadGroup = shouldOffloadDefects ||
                    prodDefects.Any(d => defectMappingDict.GetValueOrDefault(d.DefectTable)?.OffloadToStorage == true);

                if (!shouldOffloadGroup)
                {
                    continue;
                }

                try
                {
                    var packUrl = await storage.UploadProductDefectPackAsync(prodKey, prodDefects, cancellationToken);
                    if (productByKey.TryGetValue(prodKey, out var prodEntity))
                    {
                        prodEntity.DefectPackUrl = packUrl;
                    }

                    foreach (var defect in prodDefects)
                    {
                        defect.FeatureUrl = $"{packUrl}#{defect.Id:N}";
                        if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
                        {
                            defect.CustomFeatureUrl = $"{packUrl}#{defect.Id:N}_custom";
                        }
                        defect.FeatureJson = null;
                        defect.CustomFeatureJson = null;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to pack product defect pack for KeyId {KeyId}", prodKey);
                }
            }

            db.DefectRecords.AddRange(savedDefectEntities);

            // Step 3.1: If AutoCrawlImages is enabled on pipeline, proactively crawl defect images from PC API (Only NG / N defects, default VP0)
            if (config.AutoCrawlImages && imageCrawler != null)
            {
                var defectsToCrawl = savedDefectEntities
                    .Where(d => !string.IsNullOrWhiteSpace(d.ImageUri) &&
                                (d.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                                 d.Judge.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                                 d.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                                 d.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase)))
                    .Take(100)
                    .ToList();

                foreach (var def in defectsToCrawl)
                {
                    try
                    {
                        var crawlVp = !string.IsNullOrWhiteSpace(config.ImageCrawlerVpMode) && config.ImageCrawlerVpMode != "auto"
                            ? config.ImageCrawlerVpMode
                            : "0";
                        await imageCrawler.CrawlAndCacheDefectImageAsync(def.Id, forceRefresh: false, config.ImageCrawlerApiUrl, customVp: crawlVp, cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "AutoCrawl failed for defect {DefectId}: {Message}", def.Id, ex.Message);
                    }
                }
            }

            // Step 4: Overkill analysis & verification history recording
            var defectsByKey = savedDefectEntities
                .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var overkillCandidatesCount = 0;
            var verificationHistoryEntries = new List<VerificationHistoryEntity>();
            var recordNowUtc = DateTimeOffset.UtcNow;

            foreach (var prodEntity in savedProductEntities)
            {
                var defectsForProduct = defectsByKey.GetValueOrDefault(prodEntity.KeyId) ?? [];
                var prodDto = new ProductRecordDto(
                    prodEntity.Id,
                    prodEntity.KeyId,
                    prodEntity.Judge,
                    prodEntity.PanelId,
                    prodEntity.PanelJudge,
                    prodEntity.NameDefect,
                    prodEntity.MachineCode,
                    prodEntity.LineCode,
                    prodEntity.InspectedAtUtc,
                    prodEntity.MetadataJson,
                    prodEntity.Zone,
                    prodEntity.DefectName,
                    prodEntity.DefectPackUrl);

                var defectDtos = defectsForProduct.Select(d => new DefectRecordDto(
                    d.Id,
                    d.KeyId,
                    d.DefectTable,
                    d.DefectName,
                    d.DefectType,
                    d.ComponentName,
                    d.CoordinateX,
                    d.CoordinateY,
                    d.Severity,
                    d.Judge,
                    d.ConfidenceScore,
                    d.ImageUri,
                    d.Feature1StorageUri,
                    d.Feature2StorageUri,
                    d.CreatedAtUtc,
                    d.FeatureJson,
                    d.CustomFeatureJson,
                    d.Reason,
                    d.EqpName,
                    d.EmNo,
                    d.SpecName,
                    d.SpecJudge,
                    d.SpecReason,
                    d.Priority,
                    d.FeatureUrl,
                    d.CustomFeatureUrl,
                    d.ImageUrl)).ToList();

                var analysis = anomalyEngine.AnalyzeOverkill(prodDto, defectDtos);
                if (analysis.AnomalyStatus.Equals("Overkill", StringComparison.OrdinalIgnoreCase))
                {
                    overkillCandidatesCount++;
                }

                verificationHistoryEntries.Add(new VerificationHistoryEntity
                {
                    Id = Guid.NewGuid(),
                    Category = "Overkill",
                    EntityId = prodEntity.KeyId,
                    LineCode = config.LineCode,
                    Mapping = "Automated MariaDB Pipeline",
                    MachineCode = config.MachineCode,
                    Judge = prodEntity.Judge,
                    PanelJudge = prodEntity.PanelJudge,
                    NameDefect = prodEntity.NameDefect,
                    AnomalyStatus = analysis.AnomalyStatus,
                    AnomalyScore = analysis.AnomalyScore,
                    AnalysisNotes = analysis.AnalysisNotes,
                    TotalDefects = analysis.TotalDefects,
                    Eligible = true,
                    ConnectionId = config.DatabaseConnectionId,
                    ConnectionName = connection.Name,
                    QueryName = config.Name,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        prodEntity.KeyId,
                        prodEntity.Judge,
                        DefectCount = analysis.TotalDefects,
                        ConfigId = config.Id,
                        PipelineName = config.Name
                    }),
                    CreatedAtUtc = nowUtc
                });
            }

            if (productKeyList.Count > 0)
            {
                var priorHistories = await db.VerificationHistory
                    .Where(h => productKeyList.Contains(h.EntityId))
                    .ToListAsync(cancellationToken);
                if (priorHistories.Count > 0)
                {
                    db.VerificationHistory.RemoveRange(priorHistories);
                }
            }

            if (verificationHistoryEntries.Count > 0)
            {
                db.VerificationHistory.AddRange(verificationHistoryEntries);
            }

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            var bypassSummary = DefectBypassRuleEvaluator.FormatSummary(bypassRule);
            execution.CompletedAtUtc = DateTimeOffset.UtcNow;
            execution.Status = "Success";
            execution.ProductsScanned = scannedProducts.Count + skippedAlreadySavedCount;
            execution.DefectsScanned = totalScannedDefects;
            execution.DefectsExtracted = allExtractedDefects.Count;
            execution.DefectsBypassed = totalBypassedDefects;
            execution.BypassRuleSummary = bypassSummary;
            execution.OverkillCandidatesCount = overkillCandidatesCount;
            execution.LatencyMs = latency;
            execution.ExecutionSummaryJson = JsonSerializer.Serialize(new
            {
                TotalScannedFromRemote = scannedProducts.Count + skippedAlreadySavedCount,
                SkippedAlreadySaved = skippedAlreadySavedCount,
                NewProductsProcessed = scannedProducts.Count,
                DefectsScanned = totalScannedDefects,
                DefectsExtracted = allExtractedDefects.Count,
                DefectsBypassed = totalBypassedDefects,
                BypassRuleSummary = bypassSummary,
                OverkillCandidates = overkillCandidatesCount,
                DefectCountsByTable = defectCountsByTable,
                LatencyMs = latency
            });

            config.LastRunStatus = "Success";
            config.LastRunMessage = $"Deduplication lock: {skippedAlreadySavedCount} already saved products skipped. Synchronized {scannedProducts.Count} new products and {allExtractedDefects.Count} defects ({totalBypassedDefects} bypassed, {overkillCandidatesCount} Overkill suspects) in {latency}ms.";
            config.LastRunProductsCount = scannedProducts.Count;
            config.LastRunDefectsCount = allExtractedDefects.Count;
            config.LastRunDefectsBypassedCount = totalBypassedDefects;
            config.NextRunAtUtc = DateTimeOffset.UtcNow.AddMinutes(config.ScheduleIntervalMinutes);
            config.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            AutoVerifyStore.InvalidateProductCaches();

            logger.LogInformation("Overkill pipeline '{PipelineName}' ({ConfigId}) executed successfully in {Latency}ms: {Products} products, {Defects} defects, {Overkill} overkill suspects",
                config.Name, config.Id, latency, scannedProducts.Count, allExtractedDefects.Count, overkillCandidatesCount);

            return execution;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            logger.LogError(ex, "Overkill pipeline '{PipelineName}' ({ConfigId}) execution failed", config.Name, config.Id);

            execution.CompletedAtUtc = DateTimeOffset.UtcNow;
            execution.Status = "Failed";
            execution.LatencyMs = latency;
            execution.ErrorMessage = ex.Message;

            config.LastRunStatus = "Failed";
            config.LastRunMessage = $"Execution failed: {ex.Message}";
            config.NextRunAtUtc = DateTimeOffset.UtcNow.AddMinutes(config.ScheduleIntervalMinutes);
            config.UpdatedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to persist error state for pipeline {ConfigId}", config.Id);
            }

            return execution;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private string BuildConnectionString(DatabaseConnectionEntity connection)
    {
        return databaseCredentials.BuildMariaDbConnectionString(
            connection,
            connectionTimeoutSeconds: 30,
            commandTimeoutSeconds: 180);
    }

    private static void EnsureValidIdentifier(string name, string paramLabel)
    {
        if (string.IsNullOrWhiteSpace(name) || !SafeIdentifierRegex.IsMatch(name))
        {
            throw new ArgumentException($"Invalid SQL identifier for {paramLabel}: '{name}'. Identifiers can only contain alphanumeric characters and underscores.");
        }
    }

    private static void AddOptionalColumn(List<string> columns, string? columnName)
    {
        if (!string.IsNullOrWhiteSpace(columnName))
        {
            EnsureValidIdentifier(columnName, "Defect Column");
            columns.Add($"`{columnName}`");
        }
    }

    private static string? GetStringColumnValue(DbDataReader reader, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetBlobOrStringColumnValue(DbDataReader reader, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal)) return null;
            var val = reader.GetValue(ordinal);
            if (val is byte[] bytes)
            {
                return Encoding.UTF8.GetString(bytes);
            }
            return val?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int? GetIntColumnValue(DbDataReader reader, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal)) return null;
            var val = reader.GetValue(ordinal);
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is short s) return s;
            if (val is byte b) return b;
            return int.TryParse(val?.ToString(), out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static double? GetDoubleColumnValue(DbDataReader reader, string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return null;
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal)) return null;
            var val = reader.GetValue(ordinal);
            if (val is double d) return d;
            if (val is float f) return f;
            if (val is decimal dec) return (double)dec;
            return double.TryParse(val?.ToString(), out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static (double? CoordX, double? CoordY) ExtractCoordinatesFromFeatureJson(string? json, double? currentX, double? currentY)
    {
        if (string.IsNullOrWhiteSpace(json) || (currentX.HasValue && currentY.HasValue))
        {
            return (currentX, currentY);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (currentX, currentY);
            }

            var x = currentX;
            var y = currentY;

            if (!x.HasValue)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("G001_RealX") ||
                        prop.NameEquals("RealX") ||
                        prop.NameEquals("real_x") ||
                        prop.NameEquals("Real_X") ||
                        prop.Name.Equals("x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var parsedNum))
                        {
                            x = parsedNum;
                            break;
                        }
                        if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var parsedStr))
                        {
                            x = parsedStr;
                            break;
                        }
                    }
                }
            }

            if (!y.HasValue)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("G002_RealY") ||
                        prop.NameEquals("RealY") ||
                        prop.NameEquals("real_y") ||
                        prop.NameEquals("Real_Y") ||
                        prop.Name.Equals("y", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var parsedNum))
                        {
                            y = parsedNum;
                            break;
                        }
                        if (prop.Value.ValueKind == JsonValueKind.String && double.TryParse(prop.Value.GetString(), out var parsedStr))
                        {
                            y = parsedStr;
                            break;
                        }
                    }
                }
            }

            return (x, y);
        }
        catch
        {
            return (currentX, currentY);
        }
    }

    private static List<string> ParseJudgeFilterValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ["N", "NG"];
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed is { Count: > 0 })
            {
                var normalized = parsed
                    .SelectMany(value => (value ?? string.Empty)
                        .Replace("\\", string.Empty)
                        .Trim('[', ']', '"', '\'', ' ')
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Select(value => value.Trim('[', ']', '"', '\'', ' '))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return normalized.Count > 0 ? normalized : ["N", "NG"];
            }
            return ["N", "NG"];
        }
        catch
        {
            var normalized = json
                .Replace("\\", string.Empty)
                .Trim('[', ']', '"', '\'', ' ')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.Trim('[', ']', '"', '\'', ' '))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return normalized.Count > 0 ? normalized : ["N", "NG"];
        }
    }

    private static bool IsUsableForeignKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        return normalized != "0" &&
               normalized != "-" &&
               !normalized.Equals("NULL", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("NONE", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("N/A", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Equals("NA", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DefectTableMapping> ParseDefectTables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<DefectTableMapping>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<HashSet<string>> GetTableColumnsAsync(MySqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cmd = new MySqlCommand($"SHOW COLUMNS FROM `{tableName}`", connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var colName = reader["Field"]?.ToString();
                if (!string.IsNullOrWhiteSpace(colName))
                {
                    columns.Add(colName);
                }
            }
        }
        catch
        {
            // If permissions or table inspect fails, fallback to empty set so queries use config column names
        }
        return columns;
    }

    private sealed record RawProductRow(
        string KeyId,
        string Id,
        string FkId,
        string Judge,
        DateTime InspectedAt,
        string? Zone = null,
        string? DefectName = null);

}
