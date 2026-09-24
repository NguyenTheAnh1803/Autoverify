using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace AutoVerify.Api.Services;

public sealed class LeakageProcessingService : ILeakageProcessingService
{
    private static readonly Regex SafeIdentifierRegex = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDatabaseCredentialService _databaseCredentials;
    private readonly ILogger<LeakageProcessingService> _logger;

    private readonly object _progressLock = new();
    private bool _isRunning;
    private Guid? _currentBatchId;
    private int _processedCount;
    private int _totalCount;
    private int _pendingCount;
    private int _leakageCount;
    private int _notFoundCount;
    private int _failedCount;

    public LeakageProcessingService(
        IServiceScopeFactory scopeFactory,
        IDatabaseCredentialService databaseCredentials,
        ILogger<LeakageProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _databaseCredentials = databaseCredentials;
        _logger = logger;
    }

    public async Task<LeakageBatchDto> EnqueuePasteAsync(
        EnqueuePasteRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var rawLines = (request.Text ?? string.Empty)
            .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var batchId = Guid.NewGuid();
        var defaultLine = string.IsNullOrWhiteSpace(request.DefaultLineCode) ? "ALL" : request.DefaultLineCode.Trim();
        var batchName = !string.IsNullOrWhiteSpace(request.BatchName)
            ? request.BatchName.Trim()
            : $"Paste-Batch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        var batch = new LeakageBatchEntity
        {
            Id = batchId,
            Name = batchName,
            SourceType = "Paste",
            RawInputSummary = (request.Text ?? string.Empty).Length > 256 ? (request.Text ?? string.Empty)[..256] + "..." : request.Text,
            CreatedBy = createdBy,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = "Pending"
        };

        var items = new List<LeakageQueueItemEntity>();
        var seenProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in rawLines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Split by tab, comma, semicolon, or whitespace
            var tokens = trimmed.Split(new[] { '\t', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var productId = tokens[0].Trim();
            if (string.IsNullOrWhiteSpace(productId) || seenProductIds.Contains(productId)) continue;
            seenProductIds.Add(productId);

            var lineCode = tokens.Length > 1 && !string.IsNullOrWhiteSpace(tokens[1])
                ? tokens[1].Trim()
                : defaultLine;

            items.Add(new LeakageQueueItemEntity
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                ProductId = productId,
                LineCode = lineCode,
                Status = "Pending",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        batch.TotalCount = items.Count;
        batch.PendingCount = items.Count;

        db.LeakageBatches.Add(batch);
        db.LeakageQueueItems.AddRange(items);
        await db.SaveChangesAsync(cancellationToken);

        return ToBatchDto(batch);
    }

    public async Task<LeakageBatchDto> EnqueueCsvAsync(
        Stream fileStream,
        string fileName,
        string? batchName,
        string? defaultLineCode,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var defaultLine = string.IsNullOrWhiteSpace(defaultLineCode) ? "ALL" : defaultLineCode.Trim();
        var name = !string.IsNullOrWhiteSpace(batchName)
            ? batchName.Trim()
            : Path.GetFileNameWithoutExtension(fileName);

        var batchId = Guid.NewGuid();
        var batch = new LeakageBatchEntity
        {
            Id = batchId,
            Name = string.IsNullOrWhiteSpace(name) ? $"Csv-Batch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}" : name,
            SourceType = "Csv",
            RawInputSummary = $"Uploaded {fileName}",
            CreatedBy = createdBy,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = "Pending"
        };

        var items = new List<LeakageQueueItemEntity>();
        var seenProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(fileStream);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);

        int productIdIndex = 0;
        int lineCodeIndex = 1;

        if (headerLine != null)
        {
            var headerDelim = headerLine.Contains('\t') ? '\t' : (headerLine.Contains(',') ? ',' : ';');
            var headers = headerLine.Split(headerDelim).Select(h => h.Trim().Trim('"')).ToArray();

            var idHeaders = new[] { "id", "keyid", "productid", "product_id", "serial", "barcode", "panelid" };
            var lineHeaders = new[] { "line", "linecode", "line_code", "smt_line" };

            var foundIdIdx = Array.FindIndex(headers, h => idHeaders.Contains(h.ToLowerInvariant()));
            var foundLineIdx = Array.FindIndex(headers, h => lineHeaders.Contains(h.ToLowerInvariant()));

            if (foundIdIdx >= 0)
            {
                productIdIndex = foundIdIdx;
                lineCodeIndex = foundLineIdx;
            }
            else
            {
                // First line might be data if not recognized as header
                ProcessCsvDataLine(headerLine, 0, 1, defaultLine, batchId, items, seenProductIds);
            }
        }

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;
            ProcessCsvDataLine(line, productIdIndex, lineCodeIndex, defaultLine, batchId, items, seenProductIds);
        }

        batch.TotalCount = items.Count;
        batch.PendingCount = items.Count;

        db.LeakageBatches.Add(batch);
        db.LeakageQueueItems.AddRange(items);
        await db.SaveChangesAsync(cancellationToken);

        return ToBatchDto(batch);
    }

    private static void ProcessCsvDataLine(
        string line,
        int idIdx,
        int lineIdx,
        string defaultLine,
        Guid batchId,
        List<LeakageQueueItemEntity> items,
        HashSet<string> seenProductIds)
    {
        var delim = line.Contains('\t') ? '\t' : (line.Contains(',') ? ',' : ';');
        var columns = line.Split(delim).Select(c => c.Trim().Trim('"')).ToArray();

        if (columns.Length <= idIdx) return;
        var productId = columns[idIdx];
        if (string.IsNullOrWhiteSpace(productId) || seenProductIds.Contains(productId)) return;
        seenProductIds.Add(productId);

        var lineCode = lineIdx >= 0 && columns.Length > lineIdx && !string.IsNullOrWhiteSpace(columns[lineIdx])
            ? columns[lineIdx]
            : defaultLine;

        items.Add(new LeakageQueueItemEntity
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            ProductId = productId,
            LineCode = lineCode,
            Status = "Pending",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    public async Task<bool> StartProcessingAsync(
        ProcessLeakageQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_progressLock)
        {
            if (_isRunning) return false;
            _isRunning = true;
        }

        List<Guid> candidateItemIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
            var query = db.LeakageQueueItems.AsNoTracking().Where(x => x.Status == "Pending");

            if (request.ItemIds != null && request.ItemIds.Count > 0)
            {
                query = query.Where(x => request.ItemIds.Contains(x.Id));
            }
            else if (request.BatchId != null)
            {
                query = query.Where(x => x.BatchId == request.BatchId.Value);
            }

            candidateItemIds = await query.Select(x => x.Id).ToListAsync(cancellationToken);
        }

        if (candidateItemIds.Count == 0)
        {
            lock (_progressLock)
            {
                _isRunning = false;
            }
            return false;
        }

        lock (_progressLock)
        {
            _currentBatchId = request.BatchId;
            _totalCount = candidateItemIds.Count;
            _processedCount = 0;
            _pendingCount = candidateItemIds.Count;
            _leakageCount = 0;
            _notFoundCount = 0;
            _failedCount = 0;
        }

        var maxDegree = Math.Clamp(request.MaxDegreeOfParallelism ?? 4, 1, 16);

        // Run background processing
        _ = Task.Run(async () =>
        {
            try
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegree
                };

                await Parallel.ForEachAsync(candidateItemIds, parallelOptions, async (itemId, ct) =>
                {
                    await ProcessSingleQueueItemAsync(itemId, ct);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background leakage processing loop.");
            }
            finally
            {
                lock (_progressLock)
                {
                    _isRunning = false;
                }
            }
        }, CancellationToken.None);

        return true;
    }

    private async Task ProcessSingleQueueItemAsync(Guid itemId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
        var anomalyEngine = scope.ServiceProvider.GetRequiredService<IAnomalyVerificationService>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var item = await db.LeakageQueueItems.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null) return;

        item.Status = "Processing";
        await db.SaveChangesAsync(ct);

        try
        {
            var configs = await db.OverkillPipelineConfigs
                .AsNoTracking()
                .Where(c => c.Enabled)
                .ToListAsync(ct);

            var connections = await db.DatabaseConnections
                .AsNoTracking()
                .ToListAsync(ct);

            // Resolve target pipeline config / connection
            var lineCode = item.LineCode?.Trim() ?? "ALL";
            var candidateConfigs = new List<(OverkillPipelineConfigEntity Config, DatabaseConnectionEntity Connection)>();

            foreach (var cfg in configs)
            {
                var conn = connections.FirstOrDefault(c => c.Id == cfg.DatabaseConnectionId);
                if (conn is null) continue;

                if (!lineCode.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    if (cfg.LineCode.Equals(lineCode, StringComparison.OrdinalIgnoreCase) ||
                        conn.Name.Contains(lineCode, StringComparison.OrdinalIgnoreCase))
                    {
                        candidateConfigs.Add((cfg, conn));
                    }
                }
                else
                {
                    candidateConfigs.Add((cfg, conn));
                }
            }

            // If no matched configs, fallback to connections directly
            if (candidateConfigs.Count == 0)
            {
                foreach (var conn in connections)
                {
                    if (!lineCode.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
                        !conn.Name.Contains(lineCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Synthetic default config for connection
                    var syntheticConfig = new OverkillPipelineConfigEntity
                    {
                        Id = Guid.NewGuid(),
                        DatabaseConnectionId = conn.Id,
                        Name = conn.Name,
                        LineCode = lineCode,
                        MachineCode = conn.MachineCode,
                        ProductTableName = "Product",
                        IdColumnName = "ID",
                        FkIdColumnName = "Fk_ID",
                        JudgeColumnName = "Judge",
                        TimestampColumnName = "InspectedAt",
                        DefectTablesJson = "[]"
                    };
                    candidateConfigs.Add((syntheticConfig, conn));
                }
            }

            bool productFound = false;

            // Try candidate connections
            foreach (var (cfg, conn) in candidateConfigs)
            {
                try
                {
                    var found = await QueryRemoteMariaDbAndIngestAsync(
                        item, cfg, conn, db, anomalyEngine, storage, ct);

                    if (found)
                    {
                        productFound = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed querying connection {ConnectionName} for product {ProductId}", conn.Name, item.ProductId);
                }
            }

            // If still not found, check local database fallback
            if (!productFound)
            {
                productFound = await CheckLocalDatabaseAndVerifyAsync(item, db, anomalyEngine, ct);
            }

            if (!productFound)
            {
                item.Status = "NotFound";
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.ErrorMessage = $"Product '{item.ProductId}' not found in any scanned table or connection.";

                lock (_progressLock)
                {
                    _notFoundCount++;
                }
            }
            else if (item.IsLeakageSuspect)
            {
                lock (_progressLock)
                {
                    _leakageCount++;
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing leakage queue item {ItemId}", itemId);
            item.Status = "Failed";
            item.ErrorMessage = ex.Message;
            item.RetryCount++;
            item.ProcessedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            lock (_progressLock)
            {
                _failedCount++;
            }
        }
        finally
        {
            lock (_progressLock)
            {
                _processedCount++;
                _pendingCount = Math.Max(0, _pendingCount - 1);
            }
            await UpdateBatchCountsAsync(item.BatchId, db, ct);
        }
    }

    private async Task<bool> QueryRemoteMariaDbAndIngestAsync(
        LeakageQueueItemEntity item,
        OverkillPipelineConfigEntity config,
        DatabaseConnectionEntity connection,
        AutoVerifyDbContext db,
        IAnomalyVerificationService anomalyEngine,
        IStorageService storage,
        CancellationToken ct)
    {
        var connString = BuildConnectionString(connection);
        await using var mySqlConnection = new MySqlConnection(connString);
        await mySqlConnection.OpenAsync(ct);

        EnsureValidIdentifier(config.ProductTableName, "Product Table");
        EnsureValidIdentifier(config.IdColumnName, "ID Column");
        EnsureValidIdentifier(config.FkIdColumnName, "Fk_ID Column");
        EnsureValidIdentifier(config.JudgeColumnName, "Judge Column");

        var hasTimestamp = !string.IsNullOrWhiteSpace(config.TimestampColumnName);
        if (hasTimestamp) EnsureValidIdentifier(config.TimestampColumnName!, "Timestamp Column");

        var timeColSelect = hasTimestamp ? $", `{config.TimestampColumnName}`" : "";
        var productSql = $"""
            SELECT `{config.IdColumnName}`, `{config.FkIdColumnName}`, `{config.JudgeColumnName}`{timeColSelect}
            FROM `{config.ProductTableName}`
            WHERE `{config.IdColumnName}` = @id OR `{config.FkIdColumnName}` = @id
            LIMIT 1;
            """;

        await using var productCmd = new MySqlCommand(productSql, mySqlConnection) { CommandTimeout = 30 };
        productCmd.Parameters.AddWithValue("@id", item.ProductId.Trim());

        string? foundId = null;
        string? foundFkId = null;
        string? foundJudge = null;
        DateTime? foundInspectedAt = null;

        await using (var reader = await productCmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                foundId = reader[config.IdColumnName]?.ToString();
                foundFkId = reader[config.FkIdColumnName]?.ToString();
                foundJudge = reader[config.JudgeColumnName]?.ToString();

                if (hasTimestamp && !reader.IsDBNull(reader.GetOrdinal(config.TimestampColumnName!)))
                {
                    var val = reader.GetValue(reader.GetOrdinal(config.TimestampColumnName!));
                    if (val is DateTime dt) foundInspectedAt = dt;
                    else if (DateTime.TryParse(val?.ToString(), out var parsedDt)) foundInspectedAt = parsedDt;
                }
            }
        }

        if (foundId is null && foundFkId is null)
        {
            return false;
        }

        var effectiveKey = !string.IsNullOrWhiteSpace(foundFkId) ? foundFkId : foundId!;
        var effectiveJudge = foundJudge ?? "G";
        var effectiveTimestamp = foundInspectedAt ?? DateTime.UtcNow;

        // Query defect tables
        var defectTables = ParseDefectTables(config.DefectTablesJson);
        var defectDtos = new List<DefectRecordDto>();
        var defectEntitiesToPersist = new List<DefectRecordEntity>();
        var shouldOffloadAnyDefects = config.OffloadDefectsToSeaweedFs;

        foreach (var defectTable in defectTables)
        {
            try
            {
                EnsureValidIdentifier(defectTable.TableName, "Defect Table Name");
                EnsureValidIdentifier(defectTable.FkColumnName, "Defect FK Column");
                EnsureValidIdentifier(defectTable.DefectNameColumn, "Defect Name Column");

                var cols = new List<string> { $"`{defectTable.FkColumnName}`", $"`{defectTable.DefectNameColumn}`" };
                AddOptionalColumn(cols, defectTable.DefectTypeColumn);
                AddOptionalColumn(cols, defectTable.SeverityColumn);
                AddOptionalColumn(cols, defectTable.JudgeColumn);
                AddOptionalColumn(cols, defectTable.ComponentNameColumn);
                AddOptionalColumn(cols, defectTable.CoordXColumn);
                AddOptionalColumn(cols, defectTable.CoordYColumn);
                AddOptionalColumn(cols, defectTable.ImageUriColumn);
                AddOptionalColumn(cols, defectTable.FeatureColumn ?? defectTable.Feature1JsonColumn);
                AddOptionalColumn(cols, defectTable.CustomFeatureColumn ?? defectTable.Feature2JsonColumn);
                AddOptionalColumn(cols, defectTable.ReasonColumn);
                AddOptionalColumn(cols, defectTable.EqpNameColumn);
                AddOptionalColumn(cols, defectTable.EmNoColumn);
                AddOptionalColumn(cols, defectTable.SpecNameColumn);
                AddOptionalColumn(cols, defectTable.SpecJudgeColumn);
                AddOptionalColumn(cols, defectTable.SpecReasonColumn);
                AddOptionalColumn(cols, defectTable.PriorityColumn);

                var judgeFilterClause = DefectBypassRuleEvaluator.BuildSqlFilterClause(defectTable, null);

                var defectSql = $"""
                    SELECT {string.Join(", ", cols)}
                    FROM `{defectTable.TableName}`
                    WHERE `{defectTable.FkColumnName}` = @fkId{judgeFilterClause};
                    """;

                await using var defectCmd = new MySqlCommand(defectSql, mySqlConnection) { CommandTimeout = 30 };
                defectCmd.Parameters.AddWithValue("@fkId", effectiveKey);

                await using var defectReader = await defectCmd.ExecuteReaderAsync(ct);
                while (await defectReader.ReadAsync(ct))
                {
                    var dJudge = GetStringColumnValue(defectReader, defectTable.JudgeColumn) ?? "NG";
                    if (string.Equals(dJudge.Trim(), "G", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dJudge.Trim(), "FD", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dJudge.Trim(), "OK", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dJudge.Trim(), "PASS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dName = defectReader[defectTable.DefectNameColumn]?.ToString() ?? "Defect";
                    var dType = GetStringColumnValue(defectReader, defectTable.DefectTypeColumn) ?? "Optical";
                    var dSev = GetStringColumnValue(defectReader, defectTable.SeverityColumn) ?? "Minor";
                    var comp = GetStringColumnValue(defectReader, defectTable.ComponentNameColumn);
                    var cx = GetDoubleColumnValue(defectReader, defectTable.CoordXColumn);
                    var cy = GetDoubleColumnValue(defectReader, defectTable.CoordYColumn);
                    var img = GetStringColumnValue(defectReader, defectTable.ImageUriColumn);
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

                    if ((!cx.HasValue || !cy.HasValue) && !string.IsNullOrWhiteSpace(featureJson))
                    {
                        var (extractedX, extractedY) = ExtractCoordinatesFromFeatureJson(featureJson, cx, cy);
                        cx = extractedX;
                        cy = extractedY;
                    }

                    if (defectTable.OffloadToStorage == true)
                    {
                        shouldOffloadAnyDefects = true;
                    }

                    var defectId = Guid.NewGuid();
                    var defectEntity = new DefectRecordEntity
                    {
                        Id = defectId,
                        KeyId = effectiveKey,
                        DefectTable = defectTable.DisplayName ?? defectTable.TableName,
                        DefectName = dName,
                        DefectType = dType,
                        ComponentName = comp,
                        CoordinateX = cx,
                        CoordinateY = cy,
                        Severity = dSev,
                        Judge = dJudge,
                        ConfidenceScore = 0.95,
                        ConnectionId = connection.Id,
                        ModelCode = !string.IsNullOrWhiteSpace(connection.ModelCode) ? connection.ModelCode : (!string.IsNullOrWhiteSpace(config.ModelCode) ? config.ModelCode : "UNASSIGNED"),
                        MachineCode = connection.MachineCode,
                        LineCode = connection.LineCode,
                        ImageUri = img,
                        ImageUrl = img,
                        FeatureJson = featureJson,
                        CustomFeatureJson = customFeatureJson,
                        FeatureUrl = null,
                        CustomFeatureUrl = null,
                        Reason = reason,
                        EqpName = eqpName,
                        EmNo = emNo,
                        SpecName = specName,
                        SpecJudge = specJudge,
                        SpecReason = specReason,
                        Priority = priority,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    defectEntitiesToPersist.Add(defectEntity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed scanning defect table {Table} for FK {Fk}", defectTable.TableName, effectiveKey);
            }
        }

        string? defectPackUrl = null;
        // Offload defect features to SeaweedFS using unified Defect Pack JSON per product ID
        if (shouldOffloadAnyDefects && defectEntitiesToPersist.Count > 0)
        {
            try
            {
                defectPackUrl = await storage.UploadProductDefectPackAsync(effectiveKey, defectEntitiesToPersist, ct);
                foreach (var defect in defectEntitiesToPersist)
                {
                    defect.FeatureUrl = $"{defectPackUrl}#{defect.Id:N}";
                    if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
                    {
                        defect.CustomFeatureUrl = $"{defectPackUrl}#{defect.Id:N}_custom";
                    }
                    defect.FeatureJson = null;
                    defect.CustomFeatureJson = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to pack product defect pack for KeyId {KeyId}", effectiveKey);
            }
        }

        // Build defectDtos for downstream anomaly analysis & response
        foreach (var d in defectEntitiesToPersist)
        {
            defectDtos.Add(new DefectRecordDto(
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
                null,
                null,
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
                d.ImageUrl));
        }

        // Upsert ProductRecordEntity
        var existingProduct = await db.ProductRecords.FirstOrDefaultAsync(
            p => p.KeyId == effectiveKey || p.PanelId == foundId, ct);

        ProductRecordEntity prodEntity;
        if (existingProduct != null)
        {
            existingProduct.Judge = effectiveJudge;
            existingProduct.PanelJudge = effectiveJudge;
            existingProduct.MachineCode = config.MachineCode;
            existingProduct.LineCode = config.LineCode;
            existingProduct.InspectedAtUtc = DateTime.SpecifyKind(effectiveTimestamp, DateTimeKind.Utc);
            if (!string.IsNullOrWhiteSpace(defectPackUrl))
            {
                existingProduct.DefectPackUrl = defectPackUrl;
            }
            prodEntity = existingProduct;
        }
        else
        {
            prodEntity = new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = effectiveKey,
                Judge = effectiveJudge,
                PanelId = foundId ?? effectiveKey,
                PanelJudge = effectiveJudge,
                NameDefect = defectDtos.FirstOrDefault()?.DefectName ?? "None",
                MachineCode = config.MachineCode,
                LineCode = config.LineCode,
                InspectedAtUtc = DateTime.SpecifyKind(effectiveTimestamp, DateTimeKind.Utc),
                MetadataJson = JsonSerializer.Serialize(new { foundId, foundFkId, Source = config.Name }),
                DefectPackUrl = defectPackUrl
            };
            db.ProductRecords.Add(prodEntity);
        }

        // Upsert DefectRecordEntities
        var priorDefects = await db.DefectRecords.Where(d => d.KeyId == effectiveKey).ToListAsync(ct);
        if (priorDefects.Count > 0)
        {
            db.DefectRecords.RemoveRange(priorDefects);
        }
        if (defectEntitiesToPersist.Count > 0)
        {
            db.DefectRecords.AddRange(defectEntitiesToPersist);
        }

        // Analyze Leakage
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

        var analyzed = anomalyEngine.AnalyzeLeakage(
            prodDto,
            defectDtos,
            config.LineCode,
            null,
            null);

        // Add VerificationHistory
        var history = new VerificationHistoryEntity
        {
            Id = Guid.NewGuid(),
            Category = "Leakage",
            EntityId = item.ProductId,
            LineCode = config.LineCode,
            MachineCode = config.MachineCode,
            Judge = effectiveJudge,
            PanelJudge = effectiveJudge,
            NameDefect = defectDtos.FirstOrDefault()?.DefectName ?? "None",
            AnomalyStatus = analyzed.AnomalyStatus,
            AnomalyScore = analyzed.AnomalyScore,
            AnalysisNotes = analyzed.AnalysisNotes,
            TotalDefects = defectDtos.Count,
            Eligible = true,
            ConnectionId = connection.Id,
            ConnectionName = connection.Name,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.VerificationHistory.Add(history);

        // Update item
        item.Status = "Completed";
        item.ResolvedFkId = effectiveKey;
        item.StationJudge = effectiveJudge;
        item.AnomalyScore = analyzed.AnomalyScore;
        item.IsLeakageSuspect = analyzed.AnomalyStatus.Equals("Leakage", StringComparison.OrdinalIgnoreCase);
        item.DefectCount = defectDtos.Count;
        item.ConnectionId = connection.Id;
        item.TableScanned = config.ProductTableName;
        item.ErrorMessage = null;
        item.ProcessedAtUtc = DateTimeOffset.UtcNow;

        return true;
    }

    private async Task<bool> CheckLocalDatabaseAndVerifyAsync(
        LeakageQueueItemEntity item,
        AutoVerifyDbContext db,
        IAnomalyVerificationService anomalyEngine,
        CancellationToken ct)
    {
        var localProd = await db.ProductRecords.FirstOrDefaultAsync(
            p => p.KeyId == item.ProductId || p.PanelId == item.ProductId, ct);

        if (localProd is null) return false;

        var localDefects = await db.DefectRecords
            .Where(d => d.KeyId == localProd.KeyId)
            .ToListAsync(ct);

        var defectDtos = localDefects.Select(d => new DefectRecordDto(
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

        var prodDto = new ProductRecordDto(
            localProd.Id,
            localProd.KeyId,
            localProd.Judge,
            localProd.PanelId,
            localProd.PanelJudge,
            localProd.NameDefect,
            localProd.MachineCode,
            localProd.LineCode,
            localProd.InspectedAtUtc,
            localProd.MetadataJson,
            localProd.Zone,
            localProd.DefectName);

        var analyzed = anomalyEngine.AnalyzeLeakage(
            prodDto,
            defectDtos,
            localProd.LineCode,
            null,
            null);

        var history = new VerificationHistoryEntity
        {
            Id = Guid.NewGuid(),
            Category = "Leakage",
            EntityId = item.ProductId,
            LineCode = localProd.LineCode,
            MachineCode = localProd.MachineCode,
            Judge = localProd.Judge,
            PanelJudge = localProd.PanelJudge,
            NameDefect = localProd.NameDefect,
            AnomalyStatus = analyzed.AnomalyStatus,
            AnomalyScore = analyzed.AnomalyScore,
            AnalysisNotes = analyzed.AnalysisNotes,
            TotalDefects = defectDtos.Count,
            Eligible = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.VerificationHistory.Add(history);

        item.Status = "Completed";
        item.ResolvedFkId = localProd.KeyId;
        item.StationJudge = localProd.Judge;
        item.AnomalyScore = analyzed.AnomalyScore;
        item.IsLeakageSuspect = analyzed.AnomalyStatus.Equals("Leakage", StringComparison.OrdinalIgnoreCase);
        item.DefectCount = defectDtos.Count;
        item.TableScanned = "LocalProductRecords";
        item.ErrorMessage = null;
        item.ProcessedAtUtc = DateTimeOffset.UtcNow;

        return true;
    }

    private static async Task UpdateBatchCountsAsync(Guid batchId, AutoVerifyDbContext db, CancellationToken ct)
    {
        var batch = await db.LeakageBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return;

        var counts = await db.LeakageQueueItems
            .Where(x => x.BatchId == batchId)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        batch.TotalCount = counts.Sum(x => x.Count);
        batch.PendingCount = counts.FirstOrDefault(x => x.Status == "Pending")?.Count ?? 0;
        batch.ProcessingCount = counts.FirstOrDefault(x => x.Status == "Processing")?.Count ?? 0;
        batch.CompletedCount = counts.FirstOrDefault(x => x.Status == "Completed")?.Count ?? 0;
        batch.NotFoundCount = counts.FirstOrDefault(x => x.Status == "NotFound")?.Count ?? 0;
        batch.FailedCount = counts.FirstOrDefault(x => x.Status == "Failed")?.Count ?? 0;

        batch.LeakageCount = await db.LeakageQueueItems.CountAsync(
            x => x.BatchId == batchId && x.Status == "Completed" && x.IsLeakageSuspect, ct);
        batch.ConsistentCount = await db.LeakageQueueItems.CountAsync(
            x => x.BatchId == batchId && x.Status == "Completed" && !x.IsLeakageSuspect, ct);

        if (batch.PendingCount == 0 && batch.ProcessingCount == 0)
        {
            batch.ProcessedAtUtc = DateTimeOffset.UtcNow;
            batch.Status = batch.FailedCount > 0 ? "PartiallyFailed" : "Completed";
        }
        else if (batch.ProcessingCount > 0)
        {
            batch.Status = "Processing";
        }

        await db.SaveChangesAsync(ct);
    }

    public LeakageQueueProgressDto GetProgress()
    {
        lock (_progressLock)
        {
            return new LeakageQueueProgressDto(
                _isRunning,
                _currentBatchId,
                _processedCount,
                _totalCount,
                _pendingCount,
                _leakageCount,
                _notFoundCount,
                _failedCount);
        }
    }

    public async Task<int> RetryItemsAsync(
        RetryQueueItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var query = db.LeakageQueueItems.Where(x => x.Status == "NotFound" || x.Status == "Failed");

        if (request.ItemIds != null && request.ItemIds.Count > 0)
        {
            query = query.Where(x => request.ItemIds.Contains(x.Id));
        }
        else if (request.BatchId != null)
        {
            query = query.Where(x => x.BatchId == request.BatchId.Value);
        }

        var itemsToRetry = await query.ToListAsync(cancellationToken);
        if (itemsToRetry.Count == 0) return 0;

        foreach (var item in itemsToRetry)
        {
            item.Status = "Pending";
            item.RetryCount++;
            item.ErrorMessage = null;
            item.ProcessedAtUtc = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        var batchIds = itemsToRetry.Select(x => x.BatchId).Distinct().ToList();
        foreach (var bId in batchIds)
        {
            await UpdateBatchCountsAsync(bId, db, cancellationToken);
        }

        return itemsToRetry.Count;
    }

    public async Task<int> ClearQueueAsync(
        ClearQueueRequest request,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var query = db.LeakageQueueItems.AsQueryable();

        if (request.ClearCompletedOnly)
        {
            query = query.Where(x => x.Status == "Completed");
        }

        if (request.ItemIds != null && request.ItemIds.Count > 0)
        {
            query = query.Where(x => request.ItemIds.Contains(x.Id));
        }
        else if (request.BatchId != null)
        {
            query = query.Where(x => x.BatchId == request.BatchId.Value);
        }

        var itemsToDelete = await query.ToListAsync(cancellationToken);
        if (itemsToDelete.Count == 0) return 0;

        var affectedBatchIds = itemsToDelete.Select(x => x.BatchId).Distinct().ToList();

        db.LeakageQueueItems.RemoveRange(itemsToDelete);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var bId in affectedBatchIds)
        {
            var remaining = await db.LeakageQueueItems.CountAsync(x => x.BatchId == bId, cancellationToken);
            if (remaining == 0)
            {
                var batch = await db.LeakageBatches.FirstOrDefaultAsync(x => x.Id == bId, cancellationToken);
                if (batch != null) db.LeakageBatches.Remove(batch);
            }
            else
            {
                await UpdateBatchCountsAsync(bId, db, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return itemsToDelete.Count;
    }

    public async Task<LeakageQueueItemDetailDto?> GetItemDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var item = await db.LeakageQueueItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;

        var itemDto = ToQueueItemDto(item);

        var effectiveKey = item.ResolvedFkId ?? item.ProductId;
        var productEntity = await db.ProductRecords.AsNoTracking().FirstOrDefaultAsync(
            p => p.KeyId == effectiveKey || p.PanelId == item.ProductId, cancellationToken);

        ProductRecordDto? productDto = null;
        var defectDtos = new List<DefectRecordDto>();

        if (productEntity != null)
        {
            productDto = new ProductRecordDto(
                productEntity.Id,
                productEntity.KeyId,
                productEntity.Judge,
                productEntity.PanelId,
                productEntity.PanelJudge,
                productEntity.NameDefect,
                productEntity.MachineCode,
                productEntity.LineCode,
                productEntity.InspectedAtUtc,
                productEntity.MetadataJson,
                productEntity.Zone,
                productEntity.DefectName);

            var defectEntities = await db.DefectRecords
                .AsNoTracking()
                .Where(d => d.KeyId == productEntity.KeyId)
                .ToListAsync(cancellationToken);

            defectDtos = defectEntities.Select(d => new DefectRecordDto(
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
        }

        return new LeakageQueueItemDetailDto(itemDto, productDto, defectDtos);
    }

    public async Task<IReadOnlyCollection<LeakageBatchDto>> GetBatchesAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var batches = await db.LeakageBatches
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return batches.Select(ToBatchDto).ToList();
    }

    public async Task<PagedResponse<LeakageQueueItemDto>> GetItemsAsync(
        Guid? batchId,
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var query = db.LeakageQueueItems.AsNoTracking();

        if (batchId.HasValue)
        {
            query = query.Where(x => x.BatchId == batchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Equals("Leakage", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.Status == "Completed" && x.IsLeakageSuspect);
            }
            else
            {
                query = query.Where(x => x.Status == status);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x => x.ProductId.Contains(s) || (x.ResolvedFkId != null && x.ResolvedFkId.Contains(s)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var p = Math.Max(1, page);
        var ps = Math.Clamp(pageSize, 1, 500);
        var totalPages = (int)Math.Ceiling(totalCount / (double)ps);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(cancellationToken);

        return new PagedResponse<LeakageQueueItemDto>(
            items.Select(ToQueueItemDto).ToList(),
            p,
            ps,
            totalCount,
            totalPages);
    }

    private string BuildConnectionString(DatabaseConnectionEntity connection)
    {
        return _databaseCredentials.BuildMariaDbConnectionString(connection);
    }

    private static void EnsureValidIdentifier(string name, string paramLabel)
    {
        if (string.IsNullOrWhiteSpace(name) || !SafeIdentifierRegex.IsMatch(name))
        {
            throw new ArgumentException($"Invalid SQL identifier for {paramLabel}: '{name}'.");
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

    private static LeakageBatchDto ToBatchDto(LeakageBatchEntity b) => new(
        b.Id,
        b.Name,
        b.SourceType,
        b.RawInputSummary,
        b.TotalCount,
        b.PendingCount,
        b.ProcessingCount,
        b.CompletedCount,
        b.NotFoundCount,
        b.LeakageCount,
        b.ConsistentCount,
        b.FailedCount,
        b.CreatedBy,
        b.CreatedAtUtc,
        b.ProcessedAtUtc
    );

    private static LeakageQueueItemDto ToQueueItemDto(LeakageQueueItemEntity i) => new(
        i.Id,
        i.BatchId,
        i.ProductId,
        i.LineCode,
        i.Status,
        i.ResolvedFkId,
        i.StationJudge,
        i.AnomalyScore,
        i.IsLeakageSuspect,
        i.DefectCount,
        i.ConnectionId,
        i.TableScanned,
        i.ErrorMessage,
        i.RetryCount,
        i.CreatedAtUtc,
        i.ProcessedAtUtc
    );

    private async Task OffloadLeakageDefectsWithGzipAsync(
        List<DefectRecordEntity> defectEntities,
        string effectiveKey,
        IStorageService storage,
        CancellationToken ct)
    {
        foreach (var defect in defectEntities)
        {
            if (!string.IsNullOrWhiteSpace(defect.FeatureJson))
            {
                try
                {
                    var shardedPath = StoragePathSharder.GetShardedDefectPath(effectiveKey, $"{defect.Id:N}_feature.json.gz");
                    var uploaded = await storage.UploadJsonGzipAsync("defects", shardedPath, defect.FeatureJson, ct);
                    defect.FeatureUrl = uploaded.Uri;
                    defect.FeatureJson = null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to offload FeatureJson to SeaweedFS for KeyId {KeyId}", effectiveKey);
                }
            }

            if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
            {
                try
                {
                    var shardedPath = StoragePathSharder.GetShardedDefectPath(effectiveKey, $"{defect.Id:N}_custom_feature.json.gz");
                    var uploaded = await storage.UploadJsonGzipAsync("defects", shardedPath, defect.CustomFeatureJson, ct);
                    defect.CustomFeatureUrl = uploaded.Uri;
                    defect.CustomFeatureJson = null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to offload CustomFeatureJson to SeaweedFS for KeyId {KeyId}", effectiveKey);
                }
            }
        }
    }
}
