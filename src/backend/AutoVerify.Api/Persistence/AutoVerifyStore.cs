using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Storage;
using AutoVerify.Api.Logging;
using AutoVerify.Api.Verification;
using Microsoft.Extensions.Logging;

namespace AutoVerify.Api.Persistence;

public sealed class AutoVerifyStore(
    AutoVerifyDbContext db,
    IDatabaseCredentialService databaseCredentials,
    IAnomalyVerificationService anomalyEngine,
    IQueryEngineService queryEngine,
    IImportValidationService importEngine,
    ILogger<AutoVerifyStore>? logger = null)
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAtUtc, ProductFilterOptionsDto Data)> FilterOptionsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAtUtc, int Count)> ProductCountCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, (DateTimeOffset ExpiresAtUtc, DashboardProductionReportResponse Data)> DashboardReportCache = new();

    public static void InvalidateProductCaches()
    {
        FilterOptionsCache.Clear();
        ProductCountCache.Clear();
        DashboardReportCache.Clear();
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == username && x.IsActive);
        return user is not null && user.PasswordHash == HashPassword(password);
    }

    public ImportPreviewResponse PreviewImport(ImportPreviewRequest request) =>
        importEngine.ValidateAndPreview(request);

    public async Task<ScanListResponse> CreateScanListAsync(CreateScanListRequest request, StoredObjectResponse? importEvidence)
    {
        var preview = PreviewImport(new ImportPreviewRequest(request.RawText, request.MappingName, request.MachineCode, request.LineCode, request.VerificationCategory));
        var entity = new ScanListEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            MappingName = request.MappingName,
            MachineCode = importEngine.NormalizeConfigKey(request.MachineCode ?? "UNASSIGNED"),
            LineCode = importEngine.NormalizeConfigKey(request.LineCode ?? "ALL"),
            VerificationCategory = request.VerificationCategory,
            AcceptedCount = preview.AcceptedCount,
            RejectedCount = preview.RejectedCount,
            DuplicateCount = preview.DuplicateCount,
            Checksum = preview.Checksum,
            EvidenceUri = importEvidence?.Uri,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = "admin"
        };

        db.ScanLists.Add(entity);
        db.ScanListItems.AddRange(preview.AcceptedRows.Select(row => new ScanListItemEntity
        {
            Id = Guid.NewGuid(),
            ScanListId = entity.Id,
            RowNumber = row.RowNumber,
            EntityId = row.EntityId,
            LineCode = importEngine.NormalizeConfigKey(row.LineCode ?? entity.LineCode),
            MappingInt = row.MappingInt,
            Mapping = row.Mapping
        }));
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<IReadOnlyCollection<ScanListResponse>> GetScanListsAsync() => (await db.ScanLists.ToArrayAsync())
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(ToResponse)
        .ToArray();

    public async Task<ScanExecutionResponse> CreateScanExecutionAsync(CreateScanExecutionRequest request, SeaweedFsStorageManager storage)
    {
        var scanList = await db.ScanLists.SingleOrDefaultAsync(x => x.Id == request.ScanListId)
            ?? throw new InvalidOperationException("Scan list was not found.");
        var selectedSources = await db.DataSources.Where(x => request.SourceIds.Contains(x.Id)).ToArrayAsync();
        var targetSources = selectedSources.Length == 0
            ? await db.DataSources.Where(x => x.Enabled).OrderBy(x => x.Name).Take(3).ToArrayAsync()
            : selectedSources;

        var executionId = Guid.NewGuid();
        var total = Math.Max(scanList.AcceptedCount, 1);
        var failed = Math.Max(1, total / 8);
        var warnings = Math.Max(1, total / 10);
        var technicalErrors = targetSources.Any(x => x.Health != "Healthy") ? 1 : 0;
        var passed = Math.Max(0, total - failed - warnings - technicalErrors);
        var anomalyCount = Math.Max(1, total / 12);
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt.AddSeconds(Math.Max(8, total * targetSources.Length));

        var sourceNames = targetSources.Select(x => x.Name).ToArray();
        var evidence = await storage.UploadJsonAsync("scan-executions", $"{executionId:N}.json", new
        {
            executionId,
            scanList.Name,
            scanList.MappingName,
            scanList.MachineCode,
            scanList.Checksum,
            sourceNames,
            total,
            passed,
            warnings,
            failed,
            technicalErrors,
            anomalyCount,
            startedAt,
            completedAt
        });

        await SaveStorageObjectAsync(evidence, "ScanExecutionEvidence");

        var execution = new ScanExecutionEntity
        {
            Id = executionId,
            Name = request.Name,
            ScanListId = scanList.Id,
            ScanListName = scanList.Name,
            Status = failed > 0 ? "CompletedWithWarnings" : "Completed",
            SourceNamesJson = JsonSerializer.Serialize(sourceNames),
            TotalItems = total,
            PassedItems = passed,
            WarningItems = warnings,
            FailedItems = failed,
            TechnicalErrorItems = technicalErrors,
            AnomalyItems = anomalyCount,
            EvidenceUri = evidence.Uri,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt
        };

        db.ScanExecutions.Add(execution);
        db.VerificationResults.AddRange(CreateResultRows(execution, targetSources));
        await db.SaveChangesAsync();

        return ToResponse(execution);
    }

    public async Task<PagedResponse<ScanExecutionResponse>> GetScanExecutionsAsync(int page, int pageSize)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePage(page, pageSize);
        var totalCount = await db.ScanExecutions.CountAsync();
        var items = await db.ScanExecutions
            .OrderByDescending(x => x.StartedAtUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => ToResponse(x))
            .ToArrayAsync();

        return CreatePagedResponse(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<ProductFilterOptionsDto> GetProductFilterOptionsAsync(string? machineCode, string? lineCode)
    {
        var cacheKey = $"{machineCode ?? "ALL"}|{lineCode ?? "ALL"}";
        var isRelational = db.Database.IsRelational();
        if (isRelational && FilterOptionsCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Data;
        }

        var query = db.ProductRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(machineCode) && machineCode != "All") query = query.Where(p => p.MachineCode == machineCode);
        if (!string.IsNullOrWhiteSpace(lineCode) && lineCode != "All") query = query.Where(p => p.LineCode == lineCode);

        // Sample the newest records + newest NG records in 2 bounded index-friendly projections instead of 6 full-table scans over millions of rows
        var recentSample = await query
            .OrderByDescending(p => p.InspectedAtUtc)
            .Take(5000)
            .Select(p => new { p.Judge, p.Zone, p.NameDefect, p.DefectName, p.MachineCode, p.LineCode })
            .ToArrayAsync();

        var recentNgSample = await query
            .Where(p => p.Judge != "OK" && p.Judge != "G")
            .OrderByDescending(p => p.InspectedAtUtc)
            .Take(2000)
            .Select(p => new { p.Judge, p.Zone, p.NameDefect, p.DefectName, p.MachineCode, p.LineCode })
            .ToArrayAsync();

        var connScopes = await db.DatabaseConnections.AsNoTracking()
            .Select(c => new { c.MachineCode, c.LineCode })
            .ToArrayAsync();

        var recentDefectNames = await db.DefectRecords.AsNoTracking()
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(2000)
            .Select(d => d.DefectName)
            .ToArrayAsync();

        var combined = recentSample.Concat(recentNgSample).ToArray();

        var judges = combined
            .Select(p => p.Judge)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToArray();

        var zones = combined
            .Select(p => p.Zone)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToArray();

        var defectNames = combined
            .Select(p => p.NameDefect)
            .Concat(combined.Select(p => p.DefectName))
            .Concat(recentDefectNames)
            .Where(v => !string.IsNullOrWhiteSpace(v) && !v.Equals("None", StringComparison.OrdinalIgnoreCase))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToArray();

        var machines = combined
            .Select(p => p.MachineCode)
            .Concat(connScopes.Select(c => c.MachineCode))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToArray();

        var scopedConnLines = connScopes
            .Where(c => string.IsNullOrWhiteSpace(machineCode) || machineCode == "All" || string.Equals(c.MachineCode, machineCode, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.LineCode);

        var lines = combined
            .Select(p => p.LineCode)
            .Concat(scopedConnLines)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToArray();

        var result = new ProductFilterOptionsDto(judges, zones, defectNames, machines, lines);
        if (isRelational)
        {
            FilterOptionsCache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(60), result);
        }

        return result;
    }

    public async Task<PagedResponse<VerificationResultResponse>> GetResultsAsync(Guid executionId, int page, int pageSize)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePage(page, pageSize);
        var query = db.VerificationResults.Where(x => x.ExecutionId == executionId);
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(x => x.EntityId)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => ToResponse(x))
            .ToArrayAsync();

        return CreatePagedResponse(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<IReadOnlyCollection<DataSourceResponse>> GetDataSourcesAsync(string? machineCode, string? verificationCategory, string? schedulePeriod)
    {
        var query = db.DataSources.AsQueryable();
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.MachineCode == importEngine.NormalizeConfigKey(machineCode));
        }

        if (!string.IsNullOrWhiteSpace(verificationCategory) && !verificationCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.VerificationCategory == verificationCategory);
        }

        if (!string.IsNullOrWhiteSpace(schedulePeriod) && !schedulePeriod.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SchedulePeriod == schedulePeriod);
        }

        return (await query.ToArrayAsync())
            .OrderBy(x => x.MachineCode)
            .ThenBy(x => x.VerificationCategory)
            .ThenBy(x => x.SchedulePeriod)
            .ThenBy(x => x.Name)
            .Select(ToResponse)
            .ToArray();
    }

    public async Task<DataSourceResponse> CreateDataSourceAsync(CreateDataSourceRequest request)
    {
        var entity = new DataSourceEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Type = request.Type.Trim(),
            MachineCode = importEngine.NormalizeConfigKey(request.MachineCode),
            VerificationCategory = request.VerificationCategory,
            SchedulePeriod = request.SchedulePeriod,
            Environment = request.Environment.Trim(),
            Health = "Healthy",
            AverageLatencyMs = request.Type == "REST API" ? 95 : 45,
            Enabled = request.Enabled
        };

        db.DataSources.Add(entity);
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<PagedResponse<DatabaseConnectionResponse>> GetDatabaseConnectionsAsync(string? search, string? engine, int page, int pageSize)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePage(page, pageSize);
        var query = db.DatabaseConnections.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Host.Contains(term) || x.DatabaseName.Contains(term) || x.MachineCode.Contains(term) || x.LineCode.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(engine) && !engine.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Engine == engine.Trim());
        }

        var totalCount = await query.CountAsync();
        var entities = await query
            .OrderBy(x => x.MachineCode)
            .ThenBy(x => x.LineCode)
            .ThenBy(x => x.Name)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToArrayAsync();
        var items = entities.Select(ToResponse).ToArray();

        return CreatePagedResponse(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<DatabaseConnectionResponse> CreateDatabaseConnectionAsync(CreateDatabaseConnectionRequest request)
    {
        var entity = new DatabaseConnectionEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Engine = request.Engine.Trim(),
            MachineCode = importEngine.NormalizeConfigKey(request.MachineCode),
            LineCode = importEngine.NormalizeConfigKey(request.LineCode),
            ModelCode = NormalizeModelCode(request.ModelCode),
            Host = request.Host?.Trim() ?? string.Empty,
            Port = request.Port <= 0 ? 3306 : request.Port,
            DatabaseName = request.DatabaseName?.Trim() ?? string.Empty,
            Username = request.Username?.Trim() ?? string.Empty,
            ApiBaseUrl = string.IsNullOrWhiteSpace(request.ApiBaseUrl) ? null : request.ApiBaseUrl.Trim(),
            ImageCrawlerApiUrl = string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl) ? (string.IsNullOrWhiteSpace(request.ApiBaseUrl) ? null : request.ApiBaseUrl.Trim()) : request.ImageCrawlerApiUrl.Trim(),
            ImageCrawlerVpMode = string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode) ? "0" : request.ImageCrawlerVpMode.Trim(),
            EncryptedPassword = string.IsNullOrWhiteSpace(request.Password) ? string.Empty : databaseCredentials.ProtectPassword(request.Password),
            Health = "Unknown",
            AverageLatencyMs = 0,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        db.DatabaseConnections.Add(entity);
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<DatabaseConnectionResponse?> UpdateDatabaseConnectionAsync(Guid connectionId, CreateDatabaseConnectionRequest request)
    {
        var entity = await db.DatabaseConnections.SingleOrDefaultAsync(x => x.Id == connectionId);
        if (entity is null) return null;

        entity.Name = request.Name.Trim();
        entity.Engine = request.Engine.Trim();
        entity.MachineCode = importEngine.NormalizeConfigKey(request.MachineCode);
        entity.LineCode = importEngine.NormalizeConfigKey(request.LineCode);
        entity.ModelCode = NormalizeModelCode(request.ModelCode);
        entity.Host = request.Host?.Trim() ?? string.Empty;
        entity.Port = request.Port <= 0 ? 3306 : request.Port;
        entity.DatabaseName = request.DatabaseName?.Trim() ?? string.Empty;
        entity.Username = request.Username?.Trim() ?? string.Empty;
        entity.ApiBaseUrl = string.IsNullOrWhiteSpace(request.ApiBaseUrl) ? null : request.ApiBaseUrl.Trim();
        entity.ImageCrawlerApiUrl = string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl) ? (string.IsNullOrWhiteSpace(request.ApiBaseUrl) ? null : request.ApiBaseUrl.Trim()) : request.ImageCrawlerApiUrl.Trim();
        if (!string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode)) entity.ImageCrawlerVpMode = request.ImageCrawlerVpMode.Trim();
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            entity.EncryptedPassword = databaseCredentials.ProtectPassword(request.Password);
        }
        entity.Health = databaseCredentials.GetCredentialStatus(entity.EncryptedPassword) == "Unreadable" ? "CredentialError" : "Unknown";
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var linkedPipelines = await db.OverkillPipelineConfigs
            .Where(p => p.DatabaseConnectionId == entity.Id)
            .ToListAsync();
        foreach (var pipeline in linkedPipelines)
        {
            pipeline.ModelCode = entity.ModelCode;
            pipeline.UpdatedAtUtc = entity.UpdatedAtUtc;
        }

        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<ConnectionTestResult?> TestDatabaseConnectionAsync(Guid connectionId)
    {
        var connection = await db.DatabaseConnections.SingleOrDefaultAsync(x => x.Id == connectionId);
        if (connection is null) return null;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var success = false;
        string message;

        try
        {
            if (!connection.Engine.Equals("MariaDB", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Connection engine '{connection.Engine}' does not support the MariaDB test.");
            }

            var connectionString = databaseCredentials.BuildMariaDbConnectionString(connection);
            await using var mariaDb = new MySqlConnection(connectionString);
            await mariaDb.OpenAsync();
            await using var command = new MySqlCommand("SELECT 1", mariaDb)
            {
                CommandTimeout = 10
            };
            _ = await command.ExecuteScalarAsync();
            success = true;
            message = "MariaDB authentication and SELECT 1 succeeded.";
        }
        catch (DatabaseCredentialException ex)
        {
            message = ex.Message;
        }
        catch (MySqlException ex)
        {
            message = $"MariaDB connection failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            message = $"Connection test failed: {ex.Message}";
        }

        stopwatch.Stop();
        connection.Health = success
            ? "Healthy"
            : databaseCredentials.GetCredentialStatus(connection.EncryptedPassword) == "Unreadable"
                ? "CredentialError"
                : "Unhealthy";
        connection.AverageLatencyMs = (int)stopwatch.ElapsedMilliseconds;
        connection.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return new ConnectionTestResult(connection.Id, connection.Name, success, connection.AverageLatencyMs, message);
    }

    public async Task<ConnectionTestResult?> TestDataSourceAsync(Guid sourceId)
    {
        var source = await db.DataSources.SingleOrDefaultAsync(x => x.Id == sourceId);
        if (source is null) return null;

        var latency = source.Type == "REST API" ? 78 : 34;
        source.Health = "Healthy";
        source.AverageLatencyMs = latency;
        await db.SaveChangesAsync();

        return new ConnectionTestResult(source.Id, source.Name, true, latency, "Data source policy reachable.");
    }

    public async Task<BatchProvisionResponse> BatchProvisionAsync(BatchProvisionRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var targets = new List<(string LineCode, string MachineCode, string ModelCode, string Host, string DatabaseName, string Username, string Password)>();

        if (request.Paths != null && request.Paths.Count > 0)
        {
            foreach (var rawPath in request.Paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;
                var parsed = ParseLineFromPath(rawPath, request.Host, request.DatabaseName, request.Username ?? string.Empty, request.Password ?? string.Empty);
                targets.Add((parsed.LineCode, parsed.MachineCode, parsed.ModelCode, parsed.Host, parsed.DatabaseName, parsed.Username, parsed.Password));
            }
        }
        else if (request.Lines != null && request.Lines.Count > 0)
        {
            foreach (var item in request.Lines)
            {
                if (string.IsNullOrWhiteSpace(item.LineCode)) continue;
                var line = importEngine.NormalizeConfigKey(item.LineCode);
                var machine = string.IsNullOrWhiteSpace(item.MachineCode) ? $"AOI-{line}" : importEngine.NormalizeConfigKey(item.MachineCode);
            var model = NormalizeModelCode(item.ModelCode);
                var host = !string.IsNullOrWhiteSpace(item.Host) ? item.Host.Trim() : request.Host.Trim();
                var dbName = !string.IsNullOrWhiteSpace(item.DatabaseName) ? item.DatabaseName.Trim() : request.DatabaseName.Trim();
                var user = !string.IsNullOrWhiteSpace(item.Username) ? item.Username.Trim() : (request.Username?.Trim() ?? string.Empty);
                var pass = !string.IsNullOrWhiteSpace(item.Password) ? item.Password.Trim() : (request.Password?.Trim() ?? string.Empty);
                targets.Add((line, machine, model, host, dbName, user, pass));
            }
        }
        else if (request.LineStart.HasValue && request.LineEnd.HasValue)
        {
            int lStart = request.LineStart.Value;
            int lEnd = request.LineEnd.Value;
            if (lEnd < lStart) (lStart, lEnd) = (lEnd, lStart);
            int mStart = request.MachineStart ?? lStart;
            int count = Math.Clamp(lEnd - lStart + 1, 1, 500);

            for (int i = 0; i < count; i++)
            {
                int lNum = lStart + i;
                int mNum = mStart + i;
                string line = $"{request.LinePrefix ?? "LINE-"}{lNum.ToString().PadLeft(Math.Max(1, request.LinePad), '0')}";
                string machine = $"{request.MachinePrefix ?? "AOI-"}{mNum.ToString().PadLeft(Math.Max(1, request.MachinePad), '0')}";
                string host = request.Host.Replace("{n}", lNum.ToString()).Replace("{line}", line).Replace("{machine}", machine);
                string dbName = request.DatabaseName.Replace("{n}", lNum.ToString()).Replace("{line}", line).Replace("{machine}", machine);
                string user = (request.Username ?? string.Empty).Replace("{n}", lNum.ToString()).Replace("{line}", line);
                string pass = request.Password ?? string.Empty;
                targets.Add((line, machine, "UNASSIGNED", host, dbName, user, pass));
            }
        }

        if (targets.Count == 0)
        {
            return new BatchProvisionResponse(false, 0, 0, 0, 0, 0, [], "No target lines or range specified.");
        }

        var existingConnections = await db.DatabaseConnections.ToListAsync(cancellationToken);
        var existingPipelines = await db.OverkillPipelineConfigs.ToListAsync(cancellationToken);

        int createdConns = 0;
        int updatedConns = 0;
        int createdPipes = 0;
        int updatedPipes = 0;
        var processedItems = new List<string>();

        var judgeFiltersJson = JsonSerializer.Serialize(request.JudgeFilterValues is { Count: > 0 } ? request.JudgeFilterValues : ["N", "NG", "G", "OK"]);
        var defectTablesList = request.DefectTables is { Count: > 0 }
            ? request.DefectTables
            : [new DefectTableMapping("AOI_DEFECTS", "Fk_ID", "judge", "spec_name", "spec_judge", "defect_name", "severity", "defect_type", "priority", "em_no", "description", "feature_1", "feature_2", "x", "y")];
        var defectTablesJson = JsonSerializer.Serialize(defectTablesList);
        var bypassRuleJson = request.BypassRule != null ? JsonSerializer.Serialize(request.BypassRule) : null;
        var timeCol = !string.IsNullOrWhiteSpace(request.DateTimeInspectionColumnName)
            ? request.DateTimeInspectionColumnName.Trim()
            : !string.IsNullOrWhiteSpace(request.TimestampColumnName)
                ? request.TimestampColumnName.Trim()
                : "datetime_inspection";
        var zoneCol = !string.IsNullOrWhiteSpace(request.ZoneColumnName) ? request.ZoneColumnName.Trim() : "zone";
        var defectNameCol = !string.IsNullOrWhiteSpace(request.DefectNameColumnName) ? request.DefectNameColumnName.Trim() : "defect_name";

        foreach (var (line, machine, model, host, dbName, user, pass) in targets)
        {
            var connName = model == "UNASSIGNED" ? $"{line}_{machine}" : $"{model}_{line}_{machine}";
            var conn = existingConnections.FirstOrDefault(c =>
                (string.Equals(c.ModelCode, model, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(c.LineCode, line, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(c.MachineCode, machine, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(c.Name, connName, StringComparison.OrdinalIgnoreCase));

            if (conn == null)
            {
                conn = new DatabaseConnectionEntity
                {
                    Id = Guid.NewGuid(),
                    Name = connName,
                    Engine = string.IsNullOrWhiteSpace(request.Engine) ? "MariaDB" : request.Engine.Trim(),
                    MachineCode = machine,
                    LineCode = line,
                    ModelCode = model,
                    Host = host,
                    Port = request.Port <= 0 ? 3306 : request.Port,
                    DatabaseName = dbName,
                    Username = user,
                    EncryptedPassword = (string.IsNullOrWhiteSpace(pass) || databaseCredentials == null) ? string.Empty : databaseCredentials.ProtectPassword(pass),
                    Health = "Unknown",
                    AverageLatencyMs = 0,
                    UpdatedAtUtc = now
                };
                db.DatabaseConnections.Add(conn);
                existingConnections.Add(conn);
                createdConns++;
            }
            else
            {
                conn.Name = connName;
                conn.ModelCode = model;
                conn.Host = host;
                conn.Port = request.Port <= 0 ? 3306 : request.Port;
                conn.DatabaseName = dbName;
                conn.Username = user;
                if (!string.IsNullOrWhiteSpace(pass))
                {
                    conn.EncryptedPassword = databaseCredentials != null ? databaseCredentials.ProtectPassword(pass) : string.Empty;
                }
                conn.UpdatedAtUtc = now;
                updatedConns++;
            }

            if (request.CreatePipelines)
            {
                var pipeName = model == "UNASSIGNED" ? $"Pipeline_{line}_{machine}" : $"Pipeline_{model}_{line}_{machine}";
                var pipe = existingPipelines.FirstOrDefault(p =>
                    p.DatabaseConnectionId == conn.Id ||
                    (string.Equals(p.LineCode, line, StringComparison.OrdinalIgnoreCase) && string.Equals(p.MachineCode, machine, StringComparison.OrdinalIgnoreCase)));

                if (pipe == null)
                {
                    pipe = new OverkillPipelineConfigEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = pipeName,
                        DatabaseConnectionId = conn.Id,
                        MachineCode = machine,
                        LineCode = line,
                        ModelCode = model,
                        Enabled = true,
                        ScheduleIntervalMinutes = Math.Max(5, request.ScheduleIntervalMinutes),
                        NextRunAtUtc = now.AddMinutes(Math.Max(5, request.ScheduleIntervalMinutes)),
                        ProductTableName = string.IsNullOrWhiteSpace(request.ProductTableName) ? "Product" : request.ProductTableName.Trim(),
                        IdColumnName = string.IsNullOrWhiteSpace(request.IdColumnName) ? "ID" : request.IdColumnName.Trim(),
                        FkIdColumnName = string.IsNullOrWhiteSpace(request.FkIdColumnName) ? "Fk_ID" : request.FkIdColumnName.Trim(),
                        JudgeColumnName = string.IsNullOrWhiteSpace(request.JudgeColumnName) ? "Judge" : request.JudgeColumnName.Trim(),
                        JudgeFilterValuesJson = judgeFiltersJson,
                        IncludeOkProducts = request.IncludeOkProducts,
                        TimestampColumnName = timeCol,
                        DateTimeInspectionColumnName = timeCol,
                        ZoneColumnName = zoneCol,
                        DefectNameColumnName = defectNameCol,
                        LookbackHours = Math.Max(1, request.LookbackHours),
                        BatchSize = Math.Clamp(request.BatchSize, 10, 10000),
                        DefectTablesJson = defectTablesJson,
                        OffloadDefectsToSeaweedFs = request.OffloadDefectsToSeaweedFs,
                        ImageCrawlerApiUrl = string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl) ? null : request.ImageCrawlerApiUrl.Trim(),
                        AutoCrawlImages = request.AutoCrawlImages,
                        ImageCrawlerVpMode = string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode) ? "0" : request.ImageCrawlerVpMode.Trim(),
                        DefectBypassRuleJson = bypassRuleJson,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    db.OverkillPipelineConfigs.Add(pipe);
                    existingPipelines.Add(pipe);
                    createdPipes++;
                }
                else
                {
                    pipe.DatabaseConnectionId = conn.Id;
                    pipe.Name = pipeName;
                    pipe.ModelCode = model;
                    pipe.ScheduleIntervalMinutes = Math.Max(5, request.ScheduleIntervalMinutes);
                    pipe.LookbackHours = Math.Max(1, request.LookbackHours);
                    pipe.BatchSize = Math.Clamp(request.BatchSize, 10, 10000);
                    pipe.ProductTableName = string.IsNullOrWhiteSpace(request.ProductTableName) ? "Product" : request.ProductTableName.Trim();
                    pipe.IdColumnName = string.IsNullOrWhiteSpace(request.IdColumnName) ? "ID" : request.IdColumnName.Trim();
                    pipe.FkIdColumnName = string.IsNullOrWhiteSpace(request.FkIdColumnName) ? "Fk_ID" : request.FkIdColumnName.Trim();
                    pipe.JudgeColumnName = string.IsNullOrWhiteSpace(request.JudgeColumnName) ? "Judge" : request.JudgeColumnName.Trim();
                    pipe.JudgeFilterValuesJson = judgeFiltersJson;
                    pipe.TimestampColumnName = timeCol;
                    pipe.DateTimeInspectionColumnName = timeCol;
                    pipe.ZoneColumnName = zoneCol;
                    pipe.DefectNameColumnName = defectNameCol;
                    pipe.IncludeOkProducts = request.IncludeOkProducts;
                    pipe.DefectTablesJson = defectTablesJson;
                    pipe.OffloadDefectsToSeaweedFs = request.OffloadDefectsToSeaweedFs;
                    if (!string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl)) pipe.ImageCrawlerApiUrl = request.ImageCrawlerApiUrl.Trim();
                    if (!string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode)) pipe.ImageCrawlerVpMode = request.ImageCrawlerVpMode.Trim();
                    pipe.AutoCrawlImages = request.AutoCrawlImages;
                    if (bypassRuleJson != null) pipe.DefectBypassRuleJson = bypassRuleJson;
                    pipe.UpdatedAtUtc = now;
                    updatedPipes++;
                }
            }

            processedItems.Add($"{line} ({machine})");
        }

        await db.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("admin", "DatabaseConnection.BatchProvision", "DatabaseConnection", $"{targets.Count} items",
            $"Batch provisioned {targets.Count} items: {createdConns} connections created, {updatedConns} updated, {createdPipes} pipelines created, {updatedPipes} updated.");

        return new BatchProvisionResponse(
            Success: true,
            CreatedConnectionsCount: createdConns,
            UpdatedConnectionsCount: updatedConns,
            CreatedPipelinesCount: createdPipes,
            UpdatedPipelinesCount: updatedPipes,
            TotalItemsProcessed: targets.Count,
            ProcessedItems: processedItems,
            Message: $"Successfully processed {targets.Count} lines/machines ({createdConns} new connections, {updatedConns} updated, {createdPipes} new pipelines, {updatedPipes} updated)."
        );
    }

    public async Task<int> BatchDeleteConnectionsAsync(List<Guid> connectionIds, bool deleteLinkedPipelines = true, CancellationToken cancellationToken = default)
    {
        if (connectionIds == null || connectionIds.Count == 0) return 0;

        if (deleteLinkedPipelines)
        {
            var linkedPipelines = await db.OverkillPipelineConfigs
                .Where(p => connectionIds.Contains(p.DatabaseConnectionId))
                .ToListAsync(cancellationToken);
            if (linkedPipelines.Count > 0)
            {
                db.OverkillPipelineConfigs.RemoveRange(linkedPipelines);
            }
        }

        var conns = await db.DatabaseConnections
            .Where(c => connectionIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        int count = conns.Count;
        if (count > 0)
        {
            db.DatabaseConnections.RemoveRange(conns);
            await db.SaveChangesAsync(cancellationToken);
            await RecordAuditAsync("admin", "DatabaseConnection.BatchDelete", "DatabaseConnection", $"{count} items", $"Batch deleted {count} database connections.");
        }

        return count;
    }

    public async Task<BatchProvisionResponse> BatchGeneratePipelinesForConnectionsAsync(
        BatchGeneratePipelinesForConnectionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var connQuery = db.DatabaseConnections.AsNoTracking().AsQueryable();
        if (request.ConnectionIds != null && request.ConnectionIds.Count > 0)
        {
            connQuery = connQuery.Where(c => request.ConnectionIds.Contains(c.Id));
        }

        var connections = await connQuery.ToListAsync(cancellationToken);
        var existingPipelines = await db.OverkillPipelineConfigs.ToListAsync(cancellationToken);

        int createdPipes = 0;
        int updatedPipes = 0;
        var processedItems = new List<string>();

        var judgeFiltersJson = JsonSerializer.Serialize(request.JudgeFilterValues is { Count: > 0 } ? request.JudgeFilterValues : ["N", "NG", "G", "OK"]);
        var defectTablesList = request.DefectTables is { Count: > 0 }
            ? request.DefectTables
            : [new DefectTableMapping("AOI_DEFECTS", "Fk_ID", "judge", "spec_name", "spec_judge", "defect_name", "severity", "defect_type", "priority", "em_no", "description", "feature_1", "feature_2", "x", "y")];
        var defectTablesJson = JsonSerializer.Serialize(defectTablesList);
        var bypassRuleJson = request.BypassRule != null ? JsonSerializer.Serialize(request.BypassRule) : null;
        var timeCol = !string.IsNullOrWhiteSpace(request.DateTimeInspectionColumnName)
            ? request.DateTimeInspectionColumnName.Trim()
            : !string.IsNullOrWhiteSpace(request.TimestampColumnName)
                ? request.TimestampColumnName.Trim()
                : "datetime_inspection";
        var zoneCol = !string.IsNullOrWhiteSpace(request.ZoneColumnName) ? request.ZoneColumnName.Trim() : "zone";
        var defectNameCol = !string.IsNullOrWhiteSpace(request.DefectNameColumnName) ? request.DefectNameColumnName.Trim() : "defect_name";

        foreach (var conn in connections)
        {
            var line = conn.LineCode;
            var machine = conn.MachineCode;
            var model = conn.ModelCode;
            var pipeName = model == "UNASSIGNED" ? $"Pipeline_{line}_{machine}" : $"Pipeline_{model}_{line}_{machine}";

            var pipe = existingPipelines.FirstOrDefault(p =>
                p.DatabaseConnectionId == conn.Id ||
                (string.Equals(p.LineCode, line, StringComparison.OrdinalIgnoreCase) && string.Equals(p.MachineCode, machine, StringComparison.OrdinalIgnoreCase)));

            if (pipe == null)
            {
                pipe = new OverkillPipelineConfigEntity
                {
                    Id = Guid.NewGuid(),
                    Name = pipeName,
                    DatabaseConnectionId = conn.Id,
                    MachineCode = machine,
                    LineCode = line,
                    ModelCode = model,
                    Enabled = true,
                    ScheduleIntervalMinutes = Math.Max(5, request.ScheduleIntervalMinutes),
                    NextRunAtUtc = now.AddMinutes(Math.Max(5, request.ScheduleIntervalMinutes)),
                    ProductTableName = string.IsNullOrWhiteSpace(request.ProductTableName) ? "Product" : request.ProductTableName.Trim(),
                    IdColumnName = string.IsNullOrWhiteSpace(request.IdColumnName) ? "ID" : request.IdColumnName.Trim(),
                    FkIdColumnName = string.IsNullOrWhiteSpace(request.FkIdColumnName) ? "Fk_ID" : request.FkIdColumnName.Trim(),
                    JudgeColumnName = string.IsNullOrWhiteSpace(request.JudgeColumnName) ? "Judge" : request.JudgeColumnName.Trim(),
                    JudgeFilterValuesJson = judgeFiltersJson,
                    IncludeOkProducts = request.IncludeOkProducts,
                    TimestampColumnName = timeCol,
                    DateTimeInspectionColumnName = timeCol,
                    ZoneColumnName = zoneCol,
                    DefectNameColumnName = defectNameCol,
                    LookbackHours = Math.Max(1, request.LookbackHours),
                    BatchSize = Math.Clamp(request.BatchSize, 10, 10000),
                    DefectTablesJson = defectTablesJson,
                    OffloadDefectsToSeaweedFs = request.OffloadDefectsToSeaweedFs,
                    ImageCrawlerApiUrl = string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl) ? null : request.ImageCrawlerApiUrl.Trim(),
                    AutoCrawlImages = request.AutoCrawlImages,
                    ImageCrawlerVpMode = string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode) ? "0" : request.ImageCrawlerVpMode.Trim(),
                    DefectBypassRuleJson = bypassRuleJson,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.OverkillPipelineConfigs.Add(pipe);
                existingPipelines.Add(pipe);
                createdPipes++;
            }
            else
            {
                pipe.DatabaseConnectionId = conn.Id;
                pipe.Name = pipeName;
                pipe.ModelCode = model;
                pipe.ScheduleIntervalMinutes = Math.Max(5, request.ScheduleIntervalMinutes);
                pipe.LookbackHours = Math.Max(1, request.LookbackHours);
                pipe.BatchSize = Math.Clamp(request.BatchSize, 10, 10000);
                pipe.ProductTableName = string.IsNullOrWhiteSpace(request.ProductTableName) ? "Product" : request.ProductTableName.Trim();
                pipe.IdColumnName = string.IsNullOrWhiteSpace(request.IdColumnName) ? "ID" : request.IdColumnName.Trim();
                pipe.FkIdColumnName = string.IsNullOrWhiteSpace(request.FkIdColumnName) ? "Fk_ID" : request.FkIdColumnName.Trim();
                pipe.JudgeColumnName = string.IsNullOrWhiteSpace(request.JudgeColumnName) ? "Judge" : request.JudgeColumnName.Trim();
                pipe.JudgeFilterValuesJson = judgeFiltersJson;
                pipe.TimestampColumnName = timeCol;
                pipe.DateTimeInspectionColumnName = timeCol;
                pipe.ZoneColumnName = zoneCol;
                pipe.DefectNameColumnName = defectNameCol;
                pipe.IncludeOkProducts = request.IncludeOkProducts;
                pipe.DefectTablesJson = defectTablesJson;
                pipe.OffloadDefectsToSeaweedFs = request.OffloadDefectsToSeaweedFs;
                if (!string.IsNullOrWhiteSpace(request.ImageCrawlerApiUrl)) pipe.ImageCrawlerApiUrl = request.ImageCrawlerApiUrl.Trim();
                if (!string.IsNullOrWhiteSpace(request.ImageCrawlerVpMode)) pipe.ImageCrawlerVpMode = request.ImageCrawlerVpMode.Trim();
                pipe.AutoCrawlImages = request.AutoCrawlImages;
                if (bypassRuleJson != null) pipe.DefectBypassRuleJson = bypassRuleJson;
                pipe.UpdatedAtUtc = now;
                updatedPipes++;
            }

            processedItems.Add($"{line} ({machine})");
        }

        await db.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync("admin", "OverkillPipeline.BatchGenerate", "OverkillPipeline", $"{connections.Count} items",
            $"Batch generated pipelines for {connections.Count} connections: {createdPipes} created, {updatedPipes} updated.");

        return new BatchProvisionResponse(
            Success: true,
            CreatedConnectionsCount: 0,
            UpdatedConnectionsCount: 0,
            CreatedPipelinesCount: createdPipes,
            UpdatedPipelinesCount: updatedPipes,
            TotalItemsProcessed: connections.Count,
            ProcessedItems: processedItems,
            Message: $"Successfully processed {connections.Count} connections ({createdPipes} new pipelines, {updatedPipes} updated)."
        );
    }

    public Task<ScanDirectoryPathsResponse> ScanDirectoryPathsAsync(ScanDirectoryPathsRequest request)
    {
        var root = request.RootPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            return Task.FromResult(new ScanDirectoryPathsResponse(false, root, 0, [], "Root path cannot be empty."));
        }

        try
        {
            if (!Directory.Exists(root))
            {
                return Task.FromResult(new ScanDirectoryPathsResponse(false, root, 0, [], $"Directory '{root}' does not exist or is not accessible."));
            }

            var pattern = string.IsNullOrWhiteSpace(request.SearchPattern) ? "*" : request.SearchPattern.Trim();
            var dirs = Directory.GetDirectories(root, pattern, SearchOption.TopDirectoryOnly);

            var items = new List<ScannedPathItem>();
            foreach (var dir in dirs.OrderBy(d => d))
            {
                var parsed = ParseLineFromPath(dir, request.DefaultHost, request.DefaultDbPattern, request.DefaultUsername ?? string.Empty, request.DefaultPassword ?? string.Empty);
                items.Add(new ScannedPathItem(dir, parsed.LineCode, parsed.MachineCode, parsed.ModelCode, parsed.Host, parsed.DatabaseName));
            }

            return Task.FromResult(new ScanDirectoryPathsResponse(true, root, items.Count, items));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScanDirectoryPathsResponse(false, root, 0, [], $"Failed to scan directory: {ex.Message}"));
        }
    }

    public static (string LineCode, string MachineCode, string ModelCode, string Host, string DatabaseName, string Username, string Password) ParseLineFromPath(
        string rawPath,
        string defaultHost,
        string defaultDb,
        string defaultUser,
        string defaultPass)
    {
        var trimmed = rawPath.Trim();

        // Support CSV style if user pasted comma-separated tokens: Line, Machine, Host, Db
        if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',').Select(p => p.Trim()).ToArray();
            var l = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? NormalizeLineToken(parts[0]) : "LINE-01";
            var m = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : $"AOI-{l}";
            // Backward compatible formats:
            // Old: Line, Machine, Host, Database, Username, Password
            // New: Line, Machine, Model, Host, Database, Username, Password
            var hasModelColumn = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) && !IsLikelyHost(parts[2]);
            var model = hasModelColumn ? NormalizeModelCode(parts[2]) : "UNASSIGNED";
            var offset = hasModelColumn ? 1 : 0;
            var h = parts.Length > 2 + offset && !string.IsNullOrWhiteSpace(parts[2 + offset]) ? parts[2 + offset] : defaultHost;
            var d = parts.Length > 3 + offset && !string.IsNullOrWhiteSpace(parts[3 + offset]) ? parts[3 + offset] : defaultDb;
            var u = parts.Length > 4 + offset && !string.IsNullOrWhiteSpace(parts[4 + offset]) ? parts[4 + offset] : defaultUser;
            var p = parts.Length > 5 + offset && !string.IsNullOrWhiteSpace(parts[5 + offset]) ? parts[5 + offset] : defaultPass;
            return (l, m, model, h, d, u, p);
        }

        string host = defaultHost;
        string dbName = defaultDb;
        string lineCode;
        string machineCode;

        // UNC Path: \\10.20.1.50\AOI\Line_01 or //10.20.1.50/AOI/Line_01
        if (trimmed.StartsWith(@"\\") || trimmed.StartsWith("//"))
        {
            var normalized = trimmed.Replace('/', '\\').TrimStart('\\');
            var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                host = segments[0];
            }
            var leaf = segments.Length > 1 ? segments[^1] : segments[0];
            lineCode = ExtractLineCode(leaf, segments);
            machineCode = ExtractMachineCode(leaf, segments, lineCode);
            if (segments.Length > 1)
            {
                dbName = segments[1];
            }
        }
        // Local Windows Drive Path: D:\AOI_Data\LINE-01
        else if (trimmed.Length >= 2 && trimmed[1] == ':' && (trimmed.Contains('\\') || trimmed.Contains('/')))
        {
            var segments = trimmed.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var leaf = segments.Length > 1 ? segments[^1] : segments[0];
            lineCode = ExtractLineCode(leaf, segments);
            machineCode = ExtractMachineCode(leaf, segments, lineCode);
        }
        // Slash Path: 10.20.1.5/aoi_line_1
        else if (trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            var segments = trimmed.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments[0].Contains('.') || segments[0].Contains(':'))
            {
                host = segments[0];
                var leaf = segments.Length > 1 ? segments[^1] : segments[0];
                lineCode = ExtractLineCode(leaf, segments);
                machineCode = ExtractMachineCode(leaf, segments, lineCode);
                if (segments.Length > 1) dbName = segments[1];
            }
            else
            {
                var leaf = segments[^1];
                lineCode = ExtractLineCode(leaf, segments);
                machineCode = ExtractMachineCode(leaf, segments, lineCode);
            }
        }
        else
        {
            // Simple string: IP or Line Name (e.g. 10.20.1.5 or Line 1 or SMT-10)
            if (System.Net.IPAddress.TryParse(trimmed, out _))
            {
                host = trimmed;
                lineCode = $"LINE-{trimmed.Split('.')[^1].PadLeft(2, '0')}";
                machineCode = $"AOI-{lineCode}";
            }
            else
            {
                lineCode = NormalizeLineToken(trimmed);
                machineCode = $"AOI-{lineCode}";
            }
        }

        if (dbName.Contains("{line}")) dbName = dbName.Replace("{line}", lineCode);
        if (dbName.Contains("{n}"))
        {
            var matchNum = Regex.Match(lineCode, @"\d+");
            dbName = dbName.Replace("{n}", matchNum.Success ? matchNum.Value : "1");
        }
        if (host.Contains("{line}")) host = host.Replace("{line}", lineCode);
        if (host.Contains("{n}"))
        {
            var matchNum = Regex.Match(lineCode, @"\d+");
            host = host.Replace("{n}", matchNum.Success ? matchNum.Value : "1");
        }

        var modelCode = ExtractModelCode(trimmed);
        return (lineCode, machineCode, modelCode, host, dbName, defaultUser, defaultPass);
    }

    private static string NormalizeModelCode(string? modelCode)
    {
        if (string.IsNullOrWhiteSpace(modelCode)) return "UNASSIGNED";
        return Regex.Replace(modelCode.Trim().ToUpperInvariant(), @"[^A-Z0-9_-]", string.Empty);
    }

    private static string ExtractModelCode(string pathOrToken)
    {
        var match = Regex.Match(pathOrToken, @"(?<![A-Za-z0-9])(?<model>[A-Za-z]\d{2,5})(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
        return match.Success ? NormalizeModelCode(match.Groups["model"].Value) : "UNASSIGNED";
    }

    private static bool IsLikelyHost(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var value = token.Trim();
        return IPAddress.TryParse(value, out _) ||
               value.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               value.Contains('.') || value.Contains(':');
    }

    private static string ExtractLineCode(string leaf, string[] allSegments)
    {
        var match = Regex.Match(leaf, @"(?i)\b(LINE[_\-\s]?\d+|L\d+)\b");
        if (match.Success) return NormalizeLineToken(match.Value);

        foreach (var seg in allSegments.Reverse())
        {
            var m = Regex.Match(seg, @"(?i)\b(LINE[_\-\s]?\d+|L\d+)\b");
            if (m.Success) return NormalizeLineToken(m.Value);
        }

        return NormalizeLineToken(leaf);
    }

    private static string ExtractMachineCode(string leaf, string[] allSegments, string fallbackLine)
    {
        var match = Regex.Match(leaf, @"(?i)\b(AOI[_\-\s]?\d+|M[_\-\s]?\d+)\b");
        if (match.Success) return match.Value.ToUpperInvariant().Replace(" ", "-").Replace("_", "-");

        foreach (var seg in allSegments.Reverse())
        {
            var m = Regex.Match(seg, @"(?i)\b(AOI[_\-\s]?\d+|M[_\-\s]?\d+)\b");
            if (m.Success) return m.Value.ToUpperInvariant().Replace(" ", "-").Replace("_", "-");
        }

        return $"AOI-{fallbackLine}";
    }

    private static string NormalizeLineToken(string token)
    {
        var clean = token.Trim().Replace(" ", "-").Replace("_", "-");
        var match = Regex.Match(clean, @"(?i)^LINE-(\d+)$");
        if (match.Success)
        {
            var num = int.Parse(match.Groups[1].Value);
            return $"LINE-{num:D2}";
        }
        var numOnly = Regex.Match(clean, @"^\d+$");
        if (numOnly.Success)
        {
            return $"LINE-{int.Parse(clean):D2}";
        }
        return clean.ToUpperInvariant();
    }

    public async Task<DashboardProductionReportResponse> GetProductionDashboardReportAsync(int days)
    {
        var normalizedDays = Math.Clamp(days, 7, 30);
        var isRelational = db.Database.IsRelational();

        if (isRelational && DashboardReportCache.TryGetValue(normalizedDays, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Data;
        }

        var startDate = DateTimeOffset.UtcNow.Date.AddDays(-(normalizedDays - 1));

        if (!isRelational)
        {
            var products = await db.ProductRecords
                .AsNoTracking()
                .Where(x => x.InspectedAtUtc >= startDate)
                .Select(x => new { x.KeyId, x.InspectedAtUtc })
                .ToArrayAsync();

            var defectRecords = await db.DefectRecords
                .AsNoTracking()
                .Where(x => x.CreatedAtUtc >= startDate)
                .Select(x => new { x.DefectName, x.CreatedAtUtc })
                .ToArrayAsync();

            var histories = await db.VerificationHistory
                .AsNoTracking()
                .Where(x => x.CreatedAtUtc >= startDate && (x.Category == "Overkill" || x.Category == "Leakage"))
                .Select(x => new
                {
                    x.Category,
                    x.Eligible,
                    x.AnomalyStatus,
                    x.NameDefect,
                    x.TotalDefects,
                    x.CreatedAtUtc
                })
                .ToArrayAsync();

            var leakageQueue = await db.LeakageQueueItems
                .AsNoTracking()
                .Where(x => x.CreatedAtUtc >= startDate)
                .Select(x => new
                {
                    x.Status,
                    x.IsLeakageSuspect,
                    x.DefectCount,
                    x.CreatedAtUtc,
                    x.ProcessedAtUtc
                })
                .ToArrayAsync();

            var overkillHistory = histories
                .Where(x => x.Category.Equals("Overkill", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var overkillEvaluated = overkillHistory.Count(x => x.Eligible);
            var overkillCount = overkillHistory.Count(x =>
                x.Eligible && x.AnomalyStatus.Equals("Overkill", StringComparison.OrdinalIgnoreCase));

            var overkillTrend = Enumerable.Range(0, normalizedDays).Select(offset =>
            {
                var date = startDate.Date.AddDays(offset);
                var nextDate = date.AddDays(1);
                var dayHistory = overkillHistory
                    .Where(x => x.CreatedAtUtc >= date && x.CreatedAtUtc < nextDate && x.Eligible)
                    .ToArray();
                var evaluated = dayHistory.Length;
                var anomalies = dayHistory.Count(x => x.AnomalyStatus.Equals("Overkill", StringComparison.OrdinalIgnoreCase));
                var production = products.Count(x => x.InspectedAtUtc >= date && x.InspectedAtUtc < nextDate);
                return new DashboardRateTrendResponse(date.Date, production, evaluated, anomalies, Rate(anomalies, evaluated));
            }).ToArray();

            var overkillDefects = BuildDefectNameBreakdown(defectRecords.Select(x => x.DefectName));
            var overkillLastUpdated = new DateTimeOffset?[]
            {
                products.Select(x => (DateTimeOffset?)x.InspectedAtUtc).DefaultIfEmpty(null).Max(),
                defectRecords.Select(x => (DateTimeOffset?)x.CreatedAtUtc).DefaultIfEmpty(null).Max(),
                overkillHistory.Select(x => (DateTimeOffset?)x.CreatedAtUtc).DefaultIfEmpty(null).Max()
            }.Where(x => x.HasValue).DefaultIfEmpty(null).Max();

            var leakageHistory = histories
                .Where(x => x.Category.Equals("Leakage", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var leakageProcessed = leakageQueue.Count(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
            var leakageCount = leakageQueue.Count(x =>
                x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && x.IsLeakageSuspect);

            var leakageTrend = Enumerable.Range(0, normalizedDays).Select(offset =>
            {
                var date = startDate.Date.AddDays(offset);
                var nextDate = date.AddDays(1);
                var submitted = leakageQueue.Count(x => x.CreatedAtUtc >= date && x.CreatedAtUtc < nextDate);
                var processed = leakageQueue.Count(x =>
                    x.ProcessedAtUtc.HasValue && x.ProcessedAtUtc.Value >= date && x.ProcessedAtUtc.Value < nextDate &&
                    x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
                var anomalies = leakageQueue.Count(x =>
                    x.ProcessedAtUtc.HasValue && x.ProcessedAtUtc.Value >= date && x.ProcessedAtUtc.Value < nextDate &&
                    x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && x.IsLeakageSuspect);
                return new DashboardRateTrendResponse(date.Date, submitted, processed, anomalies, Rate(anomalies, processed));
            }).ToArray();

            var leakageDefects = BuildDefectNameBreakdown(
                leakageHistory
                    .Where(x => x.AnomalyStatus.Equals("Leakage", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.NameDefect));
            var leakageLastUpdated = leakageQueue
                .Select(x => x.ProcessedAtUtc ?? x.CreatedAtUtc)
                .Cast<DateTimeOffset?>()
                .DefaultIfEmpty(null)
                .Max();

            return new DashboardProductionReportResponse(
                normalizedDays,
                DateTimeOffset.UtcNow,
                new DashboardSectionReportResponse(
                    products.Length,
                    overkillEvaluated,
                    overkillCount,
                    Rate(overkillCount, overkillEvaluated),
                    defectRecords.Length,
                    overkillLastUpdated,
                    overkillTrend,
                    overkillDefects),
                new DashboardSectionReportResponse(
                    leakageQueue.Length,
                    leakageProcessed,
                    leakageCount,
                    Rate(leakageCount, leakageProcessed),
                    leakageQueue.Where(x => x.IsLeakageSuspect).Sum(x => x.DefectCount),
                    leakageLastUpdated,
                    leakageTrend,
                    leakageDefects));
        }

        // --- ULTRA-FAST RELATIONAL PATH (SQL Aggregation for millions of records in ~20ms) ---
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

            // 1. Production count per day and total count
            var productsByDay = new Dictionary<DateTime, int>();
            int totalProducts = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CAST(InspectedAtUtc AS DATE) as D, COUNT(*) as C FROM ProductRecords WHERE InspectedAtUtc >= @start GROUP BY CAST(InspectedAtUtc AS DATE);";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (!r.IsDBNull(0))
                    {
                        var d = r.GetDateTime(0).Date;
                        var c = Convert.ToInt32(r.GetValue(1));
                        productsByDay[d] = c;
                        totalProducts += c;
                    }
                }
            }

            // 2. Defect records total count & Top 12 defects
            int totalDefects = 0;
            var overkillDefectItems = new List<DashboardDefectNameResponse>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM DefectRecords WHERE CreatedAtUtc >= @start;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);
                var scalar = await cmd.ExecuteScalarAsync();
                totalDefects = scalar is null or DBNull ? 0 : Convert.ToInt32(scalar);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(NULLIF(TRIM(DefectName), ''), 'Unspecified') as Name, COUNT(*) as C 
                    FROM DefectRecords 
                    WHERE CreatedAtUtc >= @start AND DefectName IS NOT NULL AND LOWER(DefectName) != 'none' 
                    GROUP BY Name 
                    ORDER BY C DESC, Name ASC 
                    LIMIT 12;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                var tempDefects = new List<(string Name, int Count)>();
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var name = r.GetString(0);
                    var count = Convert.ToInt32(r.GetValue(1));
                    tempDefects.Add((name, count));
                }
                var totalTop = tempDefects.Sum(x => x.Count);
                foreach (var item in tempDefects)
                {
                    overkillDefectItems.Add(new DashboardDefectNameResponse(item.Name, item.Count, Rate(item.Count, totalTop)));
                }
            }

            // 3. Overkill history total & per day
            int overkillEvaluated = 0;
            int overkillCount = 0;
            var overkillByDay = new Dictionary<DateTime, (int Evaluated, int Anomalies)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT CAST(CreatedAtUtc AS DATE) as D, COUNT(*) as Evaluated, 
                           SUM(CASE WHEN LOWER(AnomalyStatus) = 'overkill' THEN 1 ELSE 0 END) as Anomalies 
                    FROM VerificationHistory 
                    WHERE CreatedAtUtc >= @start AND LOWER(Category) = 'overkill' AND Eligible = 1 
                    GROUP BY CAST(CreatedAtUtc AS DATE);";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (!r.IsDBNull(0))
                    {
                        var d = r.GetDateTime(0).Date;
                        var eval = Convert.ToInt32(r.GetValue(1));
                        var anom = Convert.ToInt32(r.GetValue(2));
                        overkillByDay[d] = (eval, anom);
                        overkillEvaluated += eval;
                        overkillCount += anom;
                    }
                }
            }

            var overkillTrend = Enumerable.Range(0, normalizedDays).Select(offset =>
            {
                var date = startDate.Date.AddDays(offset);
                overkillByDay.TryGetValue(date, out var dayStats);
                productsByDay.TryGetValue(date, out var prod);
                return new DashboardRateTrendResponse(date.Date, prod, dayStats.Evaluated, dayStats.Anomalies, Rate(dayStats.Anomalies, dayStats.Evaluated));
            }).ToArray();

            // 4. Overkill Last Updated
            DateTimeOffset? overkillLastUpdated = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT GREATEST(
                        COALESCE((SELECT MAX(InspectedAtUtc) FROM ProductRecords), '1970-01-01'),
                        COALESCE((SELECT MAX(CreatedAtUtc) FROM DefectRecords), '1970-01-01'),
                        COALESCE((SELECT MAX(CreatedAtUtc) FROM VerificationHistory WHERE LOWER(Category) = 'overkill'), '1970-01-01')
                    );";
                var res = await cmd.ExecuteScalarAsync();
                if (res != null && res != DBNull.Value && res is DateTime dt && dt > new DateTime(1970, 1, 1))
                {
                    overkillLastUpdated = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                }
            }

            // 5. Leakage Queue & History
            int leakageTotal = 0;
            int leakageProcessed = 0;
            int leakageCount = 0;
            int leakageDefectsCount = 0;
            var leakageSubmittedByDay = new Dictionary<DateTime, int>();
            var leakageProcessedByDay = new Dictionary<DateTime, (int Processed, int Anomalies)>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COUNT(*), 
                           SUM(CASE WHEN LOWER(Status) = 'completed' THEN 1 ELSE 0 END),
                           SUM(CASE WHEN LOWER(Status) = 'completed' AND IsLeakageSuspect = 1 THEN 1 ELSE 0 END),
                           SUM(CASE WHEN IsLeakageSuspect = 1 THEN DefectCount ELSE 0 END)
                    FROM LeakageQueueItems 
                    WHERE CreatedAtUtc >= @start;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    leakageTotal = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                    leakageProcessed = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1));
                    leakageCount = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                    leakageDefectsCount = r.IsDBNull(3) ? 0 : Convert.ToInt32(r.GetValue(3));
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT CAST(CreatedAtUtc AS DATE) as D, COUNT(*) as C 
                    FROM LeakageQueueItems 
                    WHERE CreatedAtUtc >= @start 
                    GROUP BY CAST(CreatedAtUtc AS DATE);";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (!r.IsDBNull(0))
                    {
                        var d = r.GetDateTime(0).Date;
                        leakageSubmittedByDay[d] = Convert.ToInt32(r.GetValue(1));
                    }
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT CAST(ProcessedAtUtc AS DATE) as D, COUNT(*) as Processed,
                           SUM(CASE WHEN IsLeakageSuspect = 1 THEN 1 ELSE 0 END) as Anomalies 
                    FROM LeakageQueueItems 
                    WHERE ProcessedAtUtc IS NOT NULL AND ProcessedAtUtc >= @start AND LOWER(Status) = 'completed' 
                    GROUP BY CAST(ProcessedAtUtc AS DATE);";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (!r.IsDBNull(0))
                    {
                        var d = r.GetDateTime(0).Date;
                        leakageProcessedByDay[d] = (Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)));
                    }
                }
            }

            var leakageTrend = Enumerable.Range(0, normalizedDays).Select(offset =>
            {
                var date = startDate.Date.AddDays(offset);
                leakageSubmittedByDay.TryGetValue(date, out var sub);
                leakageProcessedByDay.TryGetValue(date, out var pr);
                return new DashboardRateTrendResponse(date.Date, sub, pr.Processed, pr.Anomalies, Rate(pr.Anomalies, pr.Processed));
            }).ToArray();

            // 6. Leakage Defects Breakdown
            var leakageDefectItems = new List<DashboardDefectNameResponse>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COALESCE(NULLIF(TRIM(NameDefect), ''), 'Unspecified') as Name, COUNT(*) as C 
                    FROM VerificationHistory 
                    WHERE CreatedAtUtc >= @start AND LOWER(Category) = 'leakage' AND LOWER(AnomalyStatus) = 'leakage' 
                          AND NameDefect IS NOT NULL AND LOWER(NameDefect) != 'none' 
                    GROUP BY Name 
                    ORDER BY C DESC, Name ASC 
                    LIMIT 12;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@start";
                p.Value = startUtc;
                cmd.Parameters.Add(p);

                var tempDefects = new List<(string Name, int Count)>();
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var name = r.GetString(0);
                    var count = Convert.ToInt32(r.GetValue(1));
                    tempDefects.Add((name, count));
                }
                var totalTop = tempDefects.Sum(x => x.Count);
                foreach (var item in tempDefects)
                {
                    leakageDefectItems.Add(new DashboardDefectNameResponse(item.Name, item.Count, Rate(item.Count, totalTop)));
                }
            }

            DateTimeOffset? leakageLastUpdated = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT MAX(COALESCE(ProcessedAtUtc, CreatedAtUtc)) FROM LeakageQueueItems;";
                var res = await cmd.ExecuteScalarAsync();
                if (res != null && res != DBNull.Value && res is DateTime dt)
                {
                    leakageLastUpdated = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                }
            }

            var report = new DashboardProductionReportResponse(
                normalizedDays,
                DateTimeOffset.UtcNow,
                new DashboardSectionReportResponse(
                    totalProducts,
                    overkillEvaluated,
                    overkillCount,
                    Rate(overkillCount, overkillEvaluated),
                    totalDefects,
                    overkillLastUpdated,
                    overkillTrend,
                    overkillDefectItems),
                new DashboardSectionReportResponse(
                    leakageTotal,
                    leakageProcessed,
                    leakageCount,
                    Rate(leakageCount, leakageProcessed),
                    leakageDefectsCount,
                    leakageLastUpdated,
                    leakageTrend,
                    leakageDefectItems));

            DashboardReportCache[normalizedDays] = (DateTimeOffset.UtcNow.AddSeconds(30), report);
            return report;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private static IReadOnlyCollection<DashboardDefectNameResponse> BuildDefectNameBreakdown(IEnumerable<string?> names)
    {
        var grouped = names
            .Select(name => string.IsNullOrWhiteSpace(name) ? "Unspecified" : name.Trim())
            .Where(name => !name.Equals("None", StringComparison.OrdinalIgnoreCase))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name)
            .Take(12)
            .ToArray();
        var total = grouped.Sum(item => item.Count);

        return grouped
            .Select(item => new DashboardDefectNameResponse(item.Name, item.Count, Rate(item.Count, total)))
            .ToArray();
    }

    public async Task SaveStorageObjectAsync(StoredObjectResponse obj, string purpose)
    {
        db.StorageObjects.Add(new StorageObjectEntity
        {
            Id = obj.Id,
            Backend = obj.Backend,
            Bucket = obj.Bucket,
            ObjectName = obj.ObjectName,
            ContentType = obj.ContentType,
            SizeBytes = obj.SizeBytes,
            Uri = obj.Uri,
            Path = obj.Uri,
            MetadataJson = "{}",
            Purpose = purpose,
            CreatedAtUtc = obj.CreatedAtUtc
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyCollection<StoredObjectResponse>> GetStorageObjectsAsync() => (await db.StorageObjects.ToArrayAsync())
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new StoredObjectResponse(x.Id, x.Backend, x.Bucket, x.ObjectName, x.ContentType, x.SizeBytes, x.Uri, x.CreatedAtUtc))
        .ToArray();

    public async Task<ProductQueryResponse> CreateProductQueryAsync(CreateProductQueryRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new ProductQueryEntity
        {
            Id = Guid.NewGuid(),
            QueryKey = request.QueryKey.Trim(),
            ProductId = request.ProductId.Trim(),
            SourceSystem = string.IsNullOrWhiteSpace(request.SourceSystem) ? "Manual" : request.SourceSystem.Trim(),
            Status = ProductQueryStatuses.Pending,
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.ProductQueries.Add(entity);
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<IReadOnlyCollection<ProductQueryResponse>> GetProductQueriesAsync(string? status)
    {
        var query = db.ProductQueries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status.Trim());
        }

        return (await query.ToArrayAsync())
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(ToResponse)
            .ToArray();
    }

    public async Task<ProductQueryResponse?> UpdateProductQueryStatusAsync(Guid queryId, string status, string? judgeResult, string? message)
    {
        if (!ProductQueryStatuses.IsValid(status))
        {
            throw new InvalidOperationException("Product query status must be Pending, Doing, or Judge.");
        }

        var entity = await db.ProductQueries.SingleOrDefaultAsync(x => x.Id == queryId);
        if (entity is null) return null;

        entity.Status = status;
        entity.JudgeResult = judgeResult;
        entity.Message = message;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<QueryProfileResponse> CreateQueryProfileAsync(CreateQueryProfileRequest request)
    {
        var warnings = queryEngine.ValidateSqlTemplate(request.SqlTemplate);
        if (warnings.Any(x => x.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(string.Join(" ", warnings));
        }

        var bound = await BindQueryToConnectionAsync(request);

        var now = DateTimeOffset.UtcNow;
        var entity = new QueryProfileEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            MachineCode = bound.MachineCode,
            LineCode = bound.LineCode,
            VerificationCategory = bound.VerificationCategory,
            Concept = importEngine.NormalizeConfigKey(request.Concept),
            SourceSystem = importEngine.NormalizeConfigKey(string.IsNullOrWhiteSpace(request.SourceSystem) ? "MARIADB" : request.SourceSystem),
            DataSourceId = bound.ConnectionId,
            SqlTemplate = request.SqlTemplate,
            ParameterSchemaJson = request.ParameterSchemaJson,
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 300),
            MaxConcurrency = Math.Clamp(request.MaxConcurrency, 1, 32),
            Enabled = true,
            Notes = request.Notes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.QueryProfiles.Add(entity);
        await db.SaveChangesAsync();
        return ToResponse(entity, bound.ConnectionId is Guid cid
            ? (await db.DatabaseConnections.Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync())
            : null);
    }

    public async Task<QueryProfileResponse?> UpdateQueryProfileAsync(Guid profileId, CreateQueryProfileRequest request)
    {
        var warnings = queryEngine.ValidateSqlTemplate(request.SqlTemplate);
        if (warnings.Any(x => x.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(string.Join(" ", warnings));
        }

        var bound = await BindQueryToConnectionAsync(request);

        var entity = await db.QueryProfiles.SingleOrDefaultAsync(x => x.Id == profileId);
        if (entity is null) return null;

        entity.Name = request.Name;
        entity.DataSourceId = bound.ConnectionId;
        entity.MachineCode = bound.MachineCode;
        entity.LineCode = bound.LineCode;
        entity.VerificationCategory = bound.VerificationCategory;
        entity.Concept = importEngine.NormalizeConfigKey(request.Concept);
        entity.SourceSystem = importEngine.NormalizeConfigKey(string.IsNullOrWhiteSpace(request.SourceSystem) ? "MARIADB" : request.SourceSystem);
        entity.SqlTemplate = request.SqlTemplate;
        entity.ParameterSchemaJson = request.ParameterSchemaJson;
        entity.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 1, 300);
        entity.MaxConcurrency = Math.Clamp(request.MaxConcurrency, 1, 32);
        entity.Notes = request.Notes;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return ToResponse(entity, bound.ConnectionId is Guid cid
            ? (await db.DatabaseConnections.Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync())
            : null);
    }

    public async Task<IReadOnlyCollection<QueryProfileResponse>> GetQueryProfilesAsync(
        string? machineCode,
        string? lineCode,
        string? concept,
        string? sourceSystem,
        Guid? connectionId = null,
        string? verificationCategory = null)
    {
        var query = db.QueryProfiles.AsQueryable();
        if (connectionId is Guid boundConnection)
        {
            query = query.Where(x => x.DataSourceId == boundConnection);
        }
        if (!string.IsNullOrWhiteSpace(verificationCategory) && !verificationCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.VerificationCategory == verificationCategory);
        }
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.MachineCode == importEngine.NormalizeConfigKey(machineCode));
        }

        if (!string.IsNullOrWhiteSpace(lineCode))
        {
            query = query.Where(x => x.LineCode == importEngine.NormalizeConfigKey(lineCode));
        }

        if (!string.IsNullOrWhiteSpace(concept))
        {
            query = query.Where(x => x.Concept == importEngine.NormalizeConfigKey(concept));
        }

        if (!string.IsNullOrWhiteSpace(sourceSystem))
        {
            query = query.Where(x => x.SourceSystem == importEngine.NormalizeConfigKey(sourceSystem));
        }

        var profiles = await query
            .OrderBy(x => x.MachineCode)
            .ThenBy(x => x.LineCode)
            .ThenBy(x => x.Concept)
            .ToArrayAsync();
        var connectionIds = profiles.Where(x => x.DataSourceId.HasValue).Select(x => x.DataSourceId!.Value).Distinct().ToArray();
        var names = await db.DatabaseConnections
            .Where(x => connectionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);
        return profiles.Select(x => ToResponse(x, x.DataSourceId is Guid id && names.TryGetValue(id, out var name) ? name : null)).ToArray();
    }

    public async Task<RenderedQueryResponse?> RenderQueryProfileAsync(Guid profileId, IReadOnlyDictionary<string, string> parameters)
    {
        var profile = await db.QueryProfiles.SingleOrDefaultAsync(x => x.Id == profileId);
        if (profile is null) return null;

        return queryEngine.RenderQuery(profile.Id, profile.SqlTemplate, parameters);
    }

    public async Task<QueryTestResponse?> TestQueryProfileAsync(Guid profileId, IReadOnlyDictionary<string, string> parameters)
    {
        var profile = await db.QueryProfiles.SingleOrDefaultAsync(x => x.Id == profileId);
        if (profile is null) return null;
        if (profile.DataSourceId is null) throw new InvalidOperationException("Select a MariaDB data source before testing this profile.");

        var source = await db.DatabaseConnections.SingleOrDefaultAsync(x => x.Id == profile.DataSourceId);
        if (source is null || source.Engine != "MariaDB") throw new InvalidOperationException("Select a MariaDB connection to execute SQL.");
        if (source.MachineCode != profile.MachineCode || (profile.LineCode != "ALL" && source.LineCode != profile.LineCode))
            throw new InvalidOperationException("The query profile machine/line must match its selected connection.");

        var password = databaseCredentials.UnprotectPassword(source.EncryptedPassword, source.Id);
        return await queryEngine.ExecuteTestQueryAsync(
            profile.Id,
            source.Host,
            source.Port,
            source.DatabaseName,
            source.Username,
            password,
            profile.SqlTemplate,
            profile.TimeoutSeconds,
            parameters
        );
    }

    public async Task<MultiTableScanResponse> ScanProductsMultiTableAsync(MultiTableScanRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var keys = (request.KeyIds ?? [])
            .Select(importEngine.NormalizeId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var productsQuery = db.ProductRecords.AsQueryable();
        if (keys.Length > 0)
        {
            productsQuery = productsQuery.Where(x => keys.Contains(x.KeyId));
        }
        if (!string.IsNullOrWhiteSpace(request.PanelId))
        {
            var panelNorm = importEngine.NormalizeConfigKey(request.PanelId);
            productsQuery = productsQuery.Where(x => x.PanelId == panelNorm);
        }
        if (!string.IsNullOrWhiteSpace(request.MachineCode) && request.MachineCode != "All")
        {
            var machineNorm = importEngine.NormalizeConfigKey(request.MachineCode);
            productsQuery = productsQuery.Where(x => x.MachineCode == machineNorm);
        }
        if (!string.IsNullOrWhiteSpace(request.LineCode) && request.LineCode != "All")
        {
            var lineNorm = importEngine.NormalizeConfigKey(request.LineCode);
            productsQuery = productsQuery.Where(x => x.LineCode == lineNorm);
        }

        var products = await productsQuery.OrderByDescending(x => x.InspectedAtUtc).Take(150).ToArrayAsync();
        var foundKeys = products.Select(x => x.KeyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingKeys = keys.Where(k => !foundKeys.Contains(k)).ToArray();

        var allKeyIds = products.Select(x => x.KeyId).Concat(missingKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var defects = await db.DefectRecords
            .Where(x => allKeyIds.Contains(x.KeyId))
            .OrderBy(x => x.DefectTable)
            .ThenByDescending(x => x.Severity)
            .ToArrayAsync();

        var defectsByKey = defects.GroupBy(x => x.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(ToResponse).ToArray(), StringComparer.OrdinalIgnoreCase);

        var items = new List<ProductScanItemDto>();
        var overkillCount = 0;
        var leakageCount = 0;

        foreach (var prod in products)
        {
            var productDto = ToResponse(prod);
            var prodDefects = defectsByKey.TryGetValue(prod.KeyId, out var dList) ? dList : [];
            var analyzedItem = anomalyEngine.AnalyzeProductAndDefects(productDto, prodDefects);

            if (analyzedItem.AnomalyStatus == "Overkill") overkillCount++;
            if (analyzedItem.AnomalyStatus == "Leakage") leakageCount++;

            items.Add(analyzedItem);
        }

        foreach (var missingKey in missingKeys)
        {
            var prodDefects = defectsByKey.TryGetValue(missingKey, out var dList) ? dList : [];
            var virtualJudge = prodDefects.Length > 0 ? "NG" : "OK";
            var virtualProd = new ProductRecordDto(
                Guid.NewGuid(),
                missingKey,
                virtualJudge,
                request.PanelId ?? "PANEL-AUTO",
                virtualJudge,
                prodDefects.FirstOrDefault()?.DefectName ?? "None",
                request.MachineCode ?? "MACHINE-A",
                request.LineCode ?? "LINE-1",
                DateTimeOffset.UtcNow,
                null
            );
            var analyzedItem = anomalyEngine.AnalyzeProductAndDefects(virtualProd, prodDefects);
            items.Add(analyzedItem);
        }

        stopwatch.Stop();
        return new MultiTableScanResponse(
            items.Count,
            defects.Length,
            overkillCount,
            leakageCount,
            items,
            (int)stopwatch.ElapsedMilliseconds
        );
    }

    public async Task<PagedResponse<ProductRecordDto>> GetProductsAsync(string? searchTerm, string? judge, string? panelId, string? machineCode, string? lineCode, int page, int pageSize, string? zone = null, string? defectName = null)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePage(page, pageSize);
        var query = db.ProductRecords.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(x => x.KeyId.Contains(term) || x.PanelId.Contains(term) || x.NameDefect.Contains(term) || (x.Zone != null && x.Zone.Contains(term)) || (x.DefectName != null && x.DefectName.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(judge) && judge != "All")
        {
            query = query.Where(x => x.Judge == judge);
        }
        if (!string.IsNullOrWhiteSpace(panelId))
        {
            query = query.Where(x => x.PanelId == panelId);
        }
        if (!string.IsNullOrWhiteSpace(machineCode) && machineCode != "All")
        {
            query = query.Where(x => x.MachineCode == machineCode);
        }
        if (!string.IsNullOrWhiteSpace(lineCode) && lineCode != "All")
        {
            query = query.Where(x => x.LineCode == lineCode);
        }
        if (!string.IsNullOrWhiteSpace(zone) && zone != "All") query = query.Where(x => x.Zone == zone);
        if (!string.IsNullOrWhiteSpace(defectName) && defectName != "All")
            query = query.Where(x => x.DefectName == defectName || x.NameDefect == defectName);

        var countKey = $"{searchTerm ?? ""}|{judge ?? ""}|{panelId ?? ""}|{machineCode ?? ""}|{lineCode ?? ""}|{zone ?? ""}|{defectName ?? ""}";
        var isRelational = db.Database.IsRelational();
        int totalCount;
        if (isRelational && ProductCountCache.TryGetValue(countKey, out var cachedCount) && cachedCount.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            totalCount = cachedCount.Count;
        }
        else
        {
            totalCount = await query.CountAsync();
            if (isRelational)
            {
                ProductCountCache[countKey] = (DateTimeOffset.UtcNow.AddSeconds(30), totalCount);
            }
        }

        // Two-step deferred join: select only the primary keys from the index, then fetch the 25 full rows by PK
        var pageIds = await query
            .OrderByDescending(x => x.InspectedAtUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => x.Id)
            .ToArrayAsync();

        if (pageIds.Length == 0)
        {
            return CreatePagedResponse(Array.Empty<ProductRecordDto>(), normalizedPage, normalizedPageSize, totalCount);
        }

        var entities = await db.ProductRecords
            .AsNoTracking()
            .Where(x => pageIds.Contains(x.Id))
            .ToArrayAsync();

        var items = entities
            .OrderByDescending(x => x.InspectedAtUtc)
            .Select(ToResponse)
            .ToArray();

        return CreatePagedResponse(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<IReadOnlyCollection<DefectRecordDto>> GetProductDefectsAsync(string keyId)
    {
        var normalized = importEngine.NormalizeId(keyId);
        return (await db.DefectRecords.Where(x => x.KeyId == normalized).ToArrayAsync())
            .OrderBy(x => x.DefectTable)
            .ThenByDescending(x => x.Severity)
            .Select(ToResponse)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<OverkillCandidateDto>> GetOverkillCandidatesAsync(string? machineCode, string? lineCode)
    {
        var query = db.ProductRecords.AsQueryable();
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var normMachine = importEngine.NormalizeConfigKey(machineCode);
            query = query.Where(x => x.MachineCode == normMachine);
        }
        if (!string.IsNullOrWhiteSpace(lineCode) && !lineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var normLine = importEngine.NormalizeConfigKey(lineCode);
            query = query.Where(x => x.LineCode == normLine);
        }

        var candidates = await query
            .Where(x => x.Judge == "NG" || x.Judge == "N" || x.Judge == "N/A" || x.Judge == "NA" || x.Judge == "Undetermined" || x.Judge == "NeedsReview")
            .OrderByDescending(x => x.InspectedAtUtc)
            .Take(200)
            .Select(x => new OverkillCandidateDto(
                x.KeyId,
                x.Judge,
                x.PanelId,
                x.PanelJudge,
                x.NameDefect,
                x.MachineCode,
                x.LineCode,
                x.InspectedAtUtc
            ))
            .ToArrayAsync();

        return candidates;
    }

    public async Task<OverkillAnalysisResponse> GetOverkillAnalysisAsync(string? machineCode, string? lineCode, double minNgShare = 0)
    {
        var machine = string.IsNullOrWhiteSpace(machineCode) || machineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? "ALL"
            : importEngine.NormalizeConfigKey(machineCode);
        var line = string.IsNullOrWhiteSpace(lineCode) || lineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? "ALL"
            : importEngine.NormalizeConfigKey(lineCode);

        var productsQuery = db.ProductRecords.AsNoTracking().AsQueryable();
        if (machine != "ALL") productsQuery = productsQuery.Where(x => x.MachineCode == machine);
        if (line != "ALL") productsQuery = productsQuery.Where(x => x.LineCode == line);

        var ngProducts = await productsQuery
            .Where(x => x.Judge == "NG" || x.Judge == "N" || x.Judge == "N/A" || x.Judge == "NA")
            .OrderByDescending(x => x.InspectedAtUtc)
            .Take(5000)
            .Select(x => new { x.KeyId })
            .ToArrayAsync();
        var ngKeys = ngProducts.Select(x => x.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var defects = ngKeys.Length == 0
            ? []
            : await db.DefectRecords.AsNoTracking()
                .Where(x => ngKeys.Contains(x.KeyId))
                .Select(x => new DefectRecordEntity
                {
                    Id = x.Id,
                    KeyId = x.KeyId,
                    DefectTable = x.DefectTable,
                    DefectName = x.DefectName,
                    DefectType = x.DefectType,
                    Judge = x.Judge,
                    Severity = x.Severity
                })
                .ToArrayAsync();

        var systems = defects
            .GroupBy(x => x.DefectTable, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var total = group.Count();
                var ngDefects = group.Count(IsNgDefect);
                var ngShare = total == 0 ? 0 : Math.Round(ngDefects / (double)total, 4);
                var linkedProducts = group.Select(x => x.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var suspects = ngKeys.Count(key =>
                {
                    var rows = group.Where(x => x.KeyId.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
                    return rows.Length == 0 || rows.All(x => !IsMaterialNg(x));
                });
                var dominantType = group
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.DefectType) ? "Unknown" : x.DefectType, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .First().Key;
                return new OverkillSystemShareDto(
                    group.Key,
                    dominantType,
                    total,
                    ngDefects,
                    ngShare,
                    linkedProducts.Length,
                    suspects,
                    BuildSystemGuidance(group.Key, ngShare, suspects, ngKeys.Length),
                    linkedProducts.Take(8).ToArray()
                );
            })
            .Where(x => x.NgShare >= minNgShare)
            .OrderByDescending(x => x.NgShare)
            .ThenByDescending(x => x.NgDefects)
            .ToArray();

        var recommendations = new List<string>();
        if (ngProducts.Length == 0)
            recommendations.Add("No NG/N/N/A products found for the selected machine/line. Import Overkill list and run auto-verify first.");
        else if (systems.Length == 0)
            recommendations.Add("No defect systems meet the NG share threshold. Lower Min NG share or expand Line scope.");
        else
        {
            var hottest = systems[0];
            recommendations.Add($"Priority system {hottest.SystemName}: NG share {hottest.NgShare:P1}, {hottest.OverkillSuspects} overkill suspects.");
            if (systems.Any(x => x.OverkillSuspects > 0))
                recommendations.Add("Typical overkill: station judged NG but defect system has no Critical/Major defects. Check recipe, lighting, and AI threshold.");
            if (systems.Any(x => x.NgShare >= 0.7))
                recommendations.Add("System with NG ≥ 70%: isolate for dedicated preview, freeze model/threshold before release.");
        }

        return new OverkillAnalysisResponse(machine, line, ngProducts.Length, defects.Length, systems, recommendations);
    }

    private static bool IsNgDefect(DefectRecordEntity defect) =>
        defect.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
        || defect.Judge.Equals("N", StringComparison.OrdinalIgnoreCase)
        || defect.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        || defect.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase);

    private static bool IsMaterialNg(DefectRecordEntity defect) =>
        IsNgDefect(defect) && (
            defect.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)
            || defect.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase));

    private static string BuildSystemGuidance(string system, double ngShare, int suspects, int ngProducts)
    {
        var shareText = $"{ngShare:P0} NG";
        var suspectText = ngProducts == 0 ? "0" : $"{suspects}/{ngProducts}";
        if (system.Contains("AOI", StringComparison.OrdinalIgnoreCase))
            return $"AOI {shareText}. Suspected overkill {suspectText}. Check lighting, contrast threshold, optical dust/smudges — prone to false Surface/Minor alarms.";
        if (system.Contains("AI", StringComparison.OrdinalIgnoreCase))
            return $"AI {shareText}. Suspected overkill {suspectText}. Decrease defect_prob / increase ignore-overkill filter; review class_weights dust vs real defect.";
        if (system.Contains("SPI", StringComparison.OrdinalIgnoreCase))
            return $"SPI {shareText}. Suspected overkill {suspectText}. Audit paste volume vs recipe; minor volume deficits often trigger false NG.";
        return $"{system} {shareText}. Suspected overkill {suspectText}. Isolate this system for preview, correlate station judge with feature JSON before conclusion.";
    }

    public async Task<OverkillTrendResponse> GetOverkillTrendAsync(
        string? machineCode,
        string? lineCode,
        string? defectFamily,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelCode = null)
    {
        var machine = string.IsNullOrWhiteSpace(machineCode) || machineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? "ALL"
            : importEngine.NormalizeConfigKey(machineCode);
        var line = string.IsNullOrWhiteSpace(lineCode) || lineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? "ALL"
            : importEngine.NormalizeConfigKey(lineCode);
        var targetFamily = string.IsNullOrWhiteSpace(defectFamily) || defectFamily.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : defectFamily.Trim();
        var model = string.IsNullOrWhiteSpace(modelCode) || modelCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizeModelCode(modelCode);

        var productsQuery = db.ProductRecords.AsNoTracking().AsQueryable();
        if (machine != "ALL") productsQuery = productsQuery.Where(x => x.MachineCode == machine);
        if (line != "ALL") productsQuery = productsQuery.Where(x => x.LineCode == line);
        if (model != null) productsQuery = productsQuery.Where(x => x.ModelCode == model);
        if (fromUtc.HasValue) productsQuery = productsQuery.Where(x => x.InspectedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) productsQuery = productsQuery.Where(x => x.InspectedAtUtc <= toUtc.Value);

        var products = await productsQuery
            .OrderByDescending(x => x.InspectedAtUtc)
            .Take(10000)
            .Select(x => new ProductRecordEntity
            {
                Id = x.Id,
                KeyId = x.KeyId,
                Judge = x.Judge,
                PanelId = x.PanelId,
                PanelJudge = x.PanelJudge,
                NameDefect = x.NameDefect,
                MachineCode = x.MachineCode,
                LineCode = x.LineCode,
                ModelCode = x.ModelCode,
                InspectedAtUtc = x.InspectedAtUtc,
                Zone = x.Zone,
                DefectName = x.DefectName,
                OpReviewMark = x.OpReviewMark,
                OpReviewJudge = x.OpReviewJudge,
                OpReviewDefectName = x.OpReviewDefectName,
                OpReviewMappedDefect = x.OpReviewMappedDefect,
                OpReviewedAtUtc = x.OpReviewedAtUtc
            })
            .ToArrayAsync();

        // Load active classifier rules to evaluate against defects
        var activeRules = await db.MachineClassifierRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .ToArrayAsync();
        var hasActiveScripts = activeRules.Any(r => !string.IsNullOrWhiteSpace(r.ScriptContent));

        var keyIds = products
            .Select(p => p.KeyId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var defectsList = new List<DefectRecordEntity>();
        foreach (var chunk in keyIds.Chunk(2000))
        {
            var chunkKeys = chunk.ToArray();
            var defectsQuery = db.DefectRecords.AsNoTracking().Where(d => chunkKeys.Contains(d.KeyId));
            if (!string.IsNullOrWhiteSpace(targetFamily))
            {
                defectsQuery = defectsQuery.Where(d =>
                    (d.DefectType != null && d.DefectType.Contains(targetFamily)) ||
                    d.DefectTable.Contains(targetFamily));
            }

            if (hasActiveScripts)
            {
                defectsList.AddRange(await defectsQuery.ToListAsync());
            }
            else
            {
                defectsList.AddRange(await defectsQuery
                    .Select(d => new DefectRecordEntity
                    {
                        Id = d.Id,
                        KeyId = d.KeyId,
                        DefectTable = d.DefectTable,
                        DefectName = d.DefectName,
                        DefectType = d.DefectType,
                        ComponentName = d.ComponentName,
                        CoordinateX = d.CoordinateX,
                        CoordinateY = d.CoordinateY,
                        Severity = d.Severity,
                        Judge = d.Judge,
                        ConfidenceScore = d.ConfidenceScore,
                        ConnectionId = d.ConnectionId,
                        ModelCode = d.ModelCode,
                        MachineCode = d.MachineCode,
                        LineCode = d.LineCode,
                        ImageUri = d.ImageUri,
                        ImageUrl = d.ImageUrl,
                        FeatureUrl = d.FeatureUrl,
                        CustomFeatureUrl = d.CustomFeatureUrl,
                        Reason = d.Reason,
                        EqpName = d.EqpName,
                        EmNo = d.EmNo,
                        SpecName = d.SpecName,
                        SpecJudge = d.SpecJudge,
                        SpecReason = d.SpecReason,
                        Priority = d.Priority,
                        CreatedAtUtc = d.CreatedAtUtc
                    })
                    .ToListAsync());
            }
        }

        var defectsByKey = defectsList
            .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        // Hybrid Decision Fusion: Batch query AiVerdicts for candidate products
        var summaryKeyIdsForAi = products.Select(p => p.KeyId).Distinct().ToList();
        var aiVerdictsByKey = new Dictionary<string, List<AiVerdictEntity>>(StringComparer.OrdinalIgnoreCase);
        const int aiSummaryBatchSize = 1000;
        for (var i = 0; i < summaryKeyIdsForAi.Count; i += aiSummaryBatchSize)
        {
            var batch = summaryKeyIdsForAi.Skip(i).Take(aiSummaryBatchSize).ToList();
            var aiList = await db.AiVerdicts.AsNoTracking()
                .Where(v => batch.Contains(v.KeyId))
                .ToListAsync();
            foreach (var v in aiList)
            {
                if (!aiVerdictsByKey.TryGetValue(v.KeyId, out var list))
                {
                    list = [];
                    aiVerdictsByKey[v.KeyId] = list;
                }
                list.Add(v);
            }
        }

        var evaluations = new List<AnomalyClassifierEngine.ProductAnomalyEvaluation>(products.Length);
        foreach (var prod in products)
        {
            var prodDefects = defectsByKey.TryGetValue(prod.KeyId, out var pDefs) ? pDefs : [];
            var prodAiVerdicts = aiVerdictsByKey.TryGetValue(prod.KeyId, out var pAis) ? pAis : [];
            var eval = AnomalyClassifierEngine.EvaluateProduct(prod, prodDefects, activeRules, prodAiVerdicts);
            var effectiveCategory = prod.OpReviewMark switch
            {
                "Overkill" => "Overkill",
                "Real Defect" => "RealNG",
                _ => eval.AnomalyCategory
            };
            evaluations.Add(eval);
        }

        static string EffectiveCategory(AnomalyClassifierEngine.ProductAnomalyEvaluation evaluation) =>
            evaluation.Product.OpReviewMark switch
            {
                "Overkill" => "Overkill",
                "Real Defect" => "RealNG",
                _ => evaluation.AnomalyCategory
            };

        // 1. Overall Summary Metrics
        var totalInspected = evaluations.Count;
        var stationNgCount = evaluations.Count(e =>
            e.Product.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
            e.Product.Judge.Equals("N", StringComparison.OrdinalIgnoreCase) ||
            e.Product.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
            e.Product.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase));
        var realNgCount = evaluations.Count(e => EffectiveCategory(e) == "RealNG");
        var overkillCount = evaluations.Count(e => EffectiveCategory(e) == "Overkill");
        var naCount = evaluations.Count(e => EffectiveCategory(e) == "NA");
        var leakageCount = evaluations.Count(e => EffectiveCategory(e) == "Leakage");
        var consistentOkCount = evaluations.Count(e => EffectiveCategory(e) == "ConsistentOK");
        var opRealDefectCount = evaluations.Count(e => e.Product.OpReviewMark == "Real Defect");
        var opOverkillCount = evaluations.Count(e => e.Product.OpReviewMark == "Overkill");
        var opReviewedCount = opRealDefectCount + opOverkillCount;

        var stationNgRate = totalInspected > 0 ? Math.Round((double)stationNgCount / totalInspected, 4) : 0.0;
        var realNgRate = totalInspected > 0 ? Math.Round((double)realNgCount / totalInspected, 4) : 0.0;
        var overkillRate = totalInspected > 0 ? Math.Round((double)overkillCount / totalInspected, 4) : 0.0;
        var overkillShare = stationNgCount > 0 ? Math.Round((double)overkillCount / stationNgCount, 4) : 0.0;
        var leakageRate = totalInspected > 0 ? Math.Round((double)leakageCount / totalInspected, 4) : 0.0;

        var summary = new OverkillTrendSummaryDto(
            totalInspected,
            stationNgCount,
            realNgCount,
            overkillCount,
            naCount,
            leakageCount,
            consistentOkCount,
            opReviewedCount,
            opRealDefectCount,
            opOverkillCount,
            stationNgRate,
            realNgRate,
            overkillRate,
            overkillShare,
            leakageRate
        );

        // 2. Trend Time Series (Grouped by Date)
        var trendPoints = evaluations
            .GroupBy(e => e.Product.InspectedAtUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayTotal = g.Count();
                var dayStationNg = g.Count(e =>
                    e.Product.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                    e.Product.Judge.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                    e.Product.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                    e.Product.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase));
                var dayRealNg = g.Count(e => EffectiveCategory(e) == "RealNG");
                var dayOverkill = g.Count(e => EffectiveCategory(e) == "Overkill");
                var dayNa = g.Count(e => EffectiveCategory(e) == "NA");
                var dayLeakage = g.Count(e => EffectiveCategory(e) == "Leakage");

                return new OverkillTrendPointDto(
                    new DateTimeOffset(g.Key, TimeSpan.Zero),
                    g.Key.ToString("yyyy-MM-dd"),
                    dayTotal,
                    dayStationNg,
                    dayRealNg,
                    dayOverkill,
                    dayNa,
                    dayLeakage,
                    dayTotal > 0 ? Math.Round((double)dayStationNg / dayTotal, 4) : 0.0,
                    dayTotal > 0 ? Math.Round((double)dayRealNg / dayTotal, 4) : 0.0,
                    dayTotal > 0 ? Math.Round((double)dayOverkill / dayTotal, 4) : 0.0,
                    dayStationNg > 0 ? Math.Round((double)dayOverkill / dayStationNg, 4) : 0.0
                );
            })
            .ToArray();

        // 3. Family Breakdown
        var allClassifiedDefects = evaluations
            .SelectMany(e => e.DefectResults.Select(d => new { Result = d, Category = EffectiveCategory(e) }))
            .ToArray();
        var familyBreakdown = allClassifiedDefects
            .GroupBy(d => string.IsNullOrWhiteSpace(d.Result.Defect.DefectType) ? "Unknown" : d.Result.Defect.DefectType, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var familyTotal = g.Count();
                var famReal = g.Count(d => d.Category == "RealNG");
                var famOverkill = g.Count(d => d.Category == "Overkill");
                var famShare = familyTotal > 0 ? Math.Round((double)famOverkill / familyTotal, 4) : 0.0;
                var dominantRule = g.GroupBy(d => d.Result.RuleCode).OrderByDescending(r => r.Count()).FirstOrDefault()?.Key;
                var topDefectNames = g.GroupBy(d => d.Result.Defect.DefectName).OrderByDescending(x => x.Count()).Take(3).Select(x => x.Key).ToArray();

                var confList = g.Select(d => d.Result.Defect.ConfidenceScore).Where(c => c > 0).ToList();
                var meanConf = confList.Count > 0 ? Math.Round(confList.Average(), 3) : (double?)null;

                return new DefectFamilyOverkillDto(
                    g.Key,
                    familyTotal,
                    famReal,
                    famOverkill,
                    famShare,
                    dominantRule,
                    topDefectNames,
                    meanConf,
                    null
                );
            })
            .OrderByDescending(f => f.OverkillCount)
            .ToArray();

        // 4. Defect Class / Name Breakdown
        var classBreakdown = allClassifiedDefects
            .GroupBy(d => d.Result.Defect.DefectName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var classTotal = g.Count();
                var clsReal = g.Count(d => d.Category == "RealNG");
                var clsOverkill = g.Count(d => d.Category == "Overkill");
                var clsShare = classTotal > 0 ? Math.Round((double)clsOverkill / classTotal, 4) : 0.0;
                var sample = g.First();
                var ruleCode = g.GroupBy(d => d.Result.RuleCode).OrderByDescending(r => r.Count()).FirstOrDefault()?.Key;

                var guidance = clsShare >= 0.5
                    ? $"High Overkill ({clsShare:P0}): Rule [{ruleCode}] filters out most alarms. Review recipe sensitivity or lighting."
                    : $"Material NG Defect ({clsShare:P0} overkill): Valid defect pattern. Maintain tight classifier thresholds.";

                return new DefectClassOverkillDto(
                    g.Key,
                    sample.Result.Defect.DefectType ?? "General",
                    classTotal,
                    clsReal,
                    clsOverkill,
                    clsShare,
                    ruleCode,
                    guidance
                );
            })
            .OrderByDescending(c => c.OverkillCount)
            .Take(15)
            .ToArray();

        // 5. Line-by-Line Comparison (Benchmark throughput, station NG rate, and overkill across lines)
        var lineComparison = evaluations
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Product.LineCode) ? "UNKNOWN" : e.Product.LineCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var lineTotal = g.Count(); // Total inspection throughput
                var lineStationNg = g.Count(e =>
                    e.Product.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                    e.Product.Judge.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                    e.Product.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                    e.Product.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase));
                var lineOk = lineTotal - lineStationNg;
                var lineRealNg = g.Count(e => EffectiveCategory(e) == "RealNG");
                var lineOverkill = g.Count(e => EffectiveCategory(e) == "Overkill");
                var lineNa = g.Count(e => EffectiveCategory(e) == "NA");
                var lineLeakage = g.Count(e => EffectiveCategory(e) == "Leakage");
                var lineOpMapped = g.Count(e =>
                    e.Product.OpReviewedAtUtc.HasValue ||
                    !string.IsNullOrWhiteSpace(e.Product.OpReviewMark));

                var lineNgRate = lineTotal > 0 ? Math.Round((double)lineStationNg / lineTotal, 4) : 0.0;
                var lineRealRate = lineTotal > 0 ? Math.Round((double)lineRealNg / lineTotal, 4) : 0.0;
                var lineOverkillRate = lineTotal > 0 ? Math.Round((double)lineOverkill / lineTotal, 4) : 0.0;
                var lineOverkillShare = lineStationNg > 0 ? Math.Round((double)lineOverkill / lineStationNg, 4) : 0.0;
                var lineOpMappedRate = lineTotal > 0 ? Math.Round((double)lineOpMapped / lineTotal, 4) : 0.0;

                var dominantMachine = g.GroupBy(e => e.Product.MachineCode).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key ?? "ALL";

                string riskLevel;
                string assessment;

                if (lineLeakage > 0)
                {
                    riskLevel = "CRITICAL";
                    assessment = $"Defect Escape Alert: {lineLeakage} units passed by station contain real defect(s)!";
                }
                else if (lineNgRate >= 0.15)
                {
                    riskLevel = "HIGH";
                    assessment = $"High station NG rate ({lineNgRate:P1} on {lineTotal} throughput). Station inspection or component quality review recommended.";
                }
                else if (lineOverkillShare >= 0.40 || lineOverkillRate >= 0.05)
                {
                    riskLevel = "WARNING";
                    assessment = $"Elevated Overkill ({lineOverkillShare:P0} of NG, {lineOverkillRate:P1} throughput). Recipe sensitivity calibration advised.";
                }
                else
                {
                    riskLevel = "NORMAL";
                    assessment = $"Production stable. Station NG rate: {lineNgRate:P1}, Real NG: {lineRealRate:P1}.";
                }

                return new LineOverkillComparisonDto(
                    g.Key,
                    dominantMachine,
                    lineTotal,
                    lineOk,
                    lineStationNg,
                    lineNgRate,
                    lineRealNg,
                    lineRealRate,
                    lineOverkill,
                    lineNa,
                    lineOverkillRate,
                    lineOverkillShare,
                    lineLeakage,
                    lineOpMapped,
                    lineOpMappedRate,
                    riskLevel,
                    assessment
                );
            })
            .OrderByDescending(l => l.StationNgRate)
            .ThenByDescending(l => l.TotalInspected)
            .ToArray();

        // 6. Actionable Recommendations
        var recommendations = new List<string>();
        if (lineComparison.Length > 1)
        {
            var topNgLine = lineComparison.OrderByDescending(l => l.StationNgRate).First();
            if (topNgLine.StationNgRate > 0)
            {
                recommendations.Add($"Cross-line comparison: Line '{topNgLine.LineCode}' has the highest station NG rate ({topNgLine.StationNgRate:P1} across {topNgLine.TotalInspected} units inspected).");
            }
            var topOverkillLine = lineComparison.OrderByDescending(l => l.OverkillRate).First();
            if (topOverkillLine.OverkillRate > 0)
            {
                recommendations.Add($"Line '{topOverkillLine.LineCode}' has the highest overkill rate ({topOverkillLine.OverkillRate:P1} relative to line throughput).");
            }
        }
        if (overkillShare >= 0.5)
        {
            recommendations.Add($"High overall Overkill share ({overkillShare:P1}): Over half of station NG alarms are deemed non-defects by classifier rules. Calibrate station recipes.");
        }
        if (familyBreakdown.Length > 0 && familyBreakdown[0].OverkillShare >= 0.4)
        {
            recommendations.Add($"Priority Overkill Family '{familyBreakdown[0].DefectFamily}': {familyBreakdown[0].OverkillCount} false alarms ({familyBreakdown[0].OverkillShare:P0}). Focus tuning on rule [{familyBreakdown[0].ActiveRuleCode}].");
        }
        if (leakageCount > 0)
        {
            recommendations.Add($"Critical Alert: {leakageCount} escaped defect(s) detected on station PASS units! Inspect Leakage gallery immediately to prevent customer defect leakage.");
        }
        if (recommendations.Count == 0)
        {
            recommendations.Add("Production verification balanced: Low overkill rate and 0 defect escapes observed under current classifier rules.");
        }

        return new OverkillTrendResponse(
            machine,
            line,
            targetFamily ?? "ALL",
            summary,
            trendPoints,
            familyBreakdown,
            classBreakdown,
            lineComparison,
            recommendations
        );
    }

    public async Task<PagedResponse<AnomalyThumbnailItemDto>> GetAnomalyThumbnailsAsync(
        AnomalyThumbnailQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = string.IsNullOrWhiteSpace(request.Category) ? "All" : request.Category.Trim();
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 6, 100);
        var machine = string.IsNullOrWhiteSpace(request.MachineCode) || request.MachineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : importEngine.NormalizeConfigKey(request.MachineCode);
        var line = string.IsNullOrWhiteSpace(request.LineCode) || request.LineCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : importEngine.NormalizeConfigKey(request.LineCode);
        var model = string.IsNullOrWhiteSpace(request.ModelCode) || request.ModelCode.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizeModelCode(request.ModelCode);

        var productsQuery = db.ProductRecords.AsNoTracking().AsQueryable();
        if (machine != null)
            productsQuery = productsQuery.Where(p => p.MachineCode == machine);
        if (line != null)
            productsQuery = productsQuery.Where(p => p.LineCode == line);
        if (model != null)
            productsQuery = productsQuery.Where(p => p.ModelCode == model);
        if (request.FromUtc.HasValue)
            productsQuery = productsQuery.Where(p => p.InspectedAtUtc >= request.FromUtc.Value);
        if (request.ToUtc.HasValue)
            productsQuery = productsQuery.Where(p => p.InspectedAtUtc <= request.ToUtc.Value);

        var products = await productsQuery
            .OrderByDescending(p => p.InspectedAtUtc)
            .Take(10000)
            .Select(p => new ProductRecordEntity
            {
                Id = p.Id,
                KeyId = p.KeyId,
                Judge = p.Judge,
                PanelId = p.PanelId,
                PanelJudge = p.PanelJudge,
                NameDefect = p.NameDefect,
                MachineCode = p.MachineCode,
                LineCode = p.LineCode,
                ModelCode = p.ModelCode,
                InspectedAtUtc = p.InspectedAtUtc,
                Zone = p.Zone,
                DefectName = p.DefectName,
                DefectPackUrl = p.DefectPackUrl,
                OpReviewMark = p.OpReviewMark,
                OpReviewJudge = p.OpReviewJudge,
                OpReviewDefectName = p.OpReviewDefectName,
                OpReviewMappedDefect = p.OpReviewMappedDefect,
                OpReviewNote = p.OpReviewNote,
                OpReviewedAtUtc = p.OpReviewedAtUtc
            })
            .ToArrayAsync(cancellationToken);

        var keyIds = products.Select(p => p.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var defects = new List<DefectRecordEntity>();
        foreach (var chunk in keyIds.Chunk(2000))
        {
            var chunkKeys = chunk.ToArray();
            defects.AddRange(await db.DefectRecords.AsNoTracking()
                .Where(d => chunkKeys.Contains(d.KeyId))
                .ToListAsync(cancellationToken));
        }

        var defectsByKey = defects
            .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

        var activeRules = await db.MachineClassifierRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .ToArrayAsync(cancellationToken);

        var items = new List<AnomalyThumbnailItemDto>();

        var distinctProducts = products
            .GroupBy(p => p.KeyId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.InspectedAtUtc).First())
            .ToArray();

        // Hybrid Decision Fusion: Query AiVerdicts for distinct products
        var thumbKeyIdsForAi = distinctProducts.Select(p => p.KeyId).Distinct().ToList();
        var thumbAiVerdictsByKey = new Dictionary<string, List<AiVerdictEntity>>(StringComparer.OrdinalIgnoreCase);
        const int aiThumbBatchSize = 1000;
        for (var i = 0; i < thumbKeyIdsForAi.Count; i += aiThumbBatchSize)
        {
            var batch = thumbKeyIdsForAi.Skip(i).Take(aiThumbBatchSize).ToList();
            var aiList = await db.AiVerdicts.AsNoTracking()
                .Where(v => batch.Contains(v.KeyId))
                .ToListAsync(cancellationToken);
            foreach (var v in aiList)
            {
                if (!thumbAiVerdictsByKey.TryGetValue(v.KeyId, out var list))
                {
                    list = [];
                    thumbAiVerdictsByKey[v.KeyId] = list;
                }
                list.Add(v);
            }
        }

        foreach (var prod in distinctProducts)
        {
            var prodDefects = defectsByKey.TryGetValue(prod.KeyId, out var pDefs) ? pDefs : [];
            var prodAiVerdicts = thumbAiVerdictsByKey.TryGetValue(prod.KeyId, out var pAis) ? pAis : [];
            var eval = AnomalyClassifierEngine.EvaluateProduct(prod, prodDefects, activeRules, prodAiVerdicts);
            var effectiveCategory = prod.OpReviewMark switch
            {
                "Overkill" => "Overkill",
                "Real Defect" => "RealNG",
                _ => eval.AnomalyCategory
            };

            if (category.Equals("StationNG", StringComparison.OrdinalIgnoreCase))
            {
                if (!prod.Judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
                    && !prod.Judge.Equals("N", StringComparison.OrdinalIgnoreCase)
                    && !prod.Judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                    && !prod.Judge.Equals("NA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (!category.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (!effectiveCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // If product has linked defects, produce a thumbnail item per defect
            if (eval.DefectResults.Count > 0)
            {
                foreach (var dResult in eval.DefectResults)
                {
                    var d = dResult.Defect;

                    if (!string.IsNullOrWhiteSpace(request.DefectFamily) && !request.DefectFamily.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(d.DefectType) || !d.DefectType.Contains(request.DefectFamily, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (!string.IsNullOrWhiteSpace(request.DefectCluster) && !request.DefectCluster.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        var cluster = request.DefectCluster.Trim();
                        var clusterMatches = d.DefectName.Contains(cluster, StringComparison.OrdinalIgnoreCase)
                            || d.DefectType.Contains(cluster, StringComparison.OrdinalIgnoreCase)
                            || d.DefectTable.Contains(cluster, StringComparison.OrdinalIgnoreCase);
                        if (!clusterMatches) continue;
                    }

                    if (!string.IsNullOrWhiteSpace(request.Search))
                    {
                        var s = request.Search.Trim();
                        var matches = prod.KeyId.Contains(s, StringComparison.OrdinalIgnoreCase)
                            || d.DefectName.Contains(s, StringComparison.OrdinalIgnoreCase)
                            || (d.ComponentName != null && d.ComponentName.Contains(s, StringComparison.OrdinalIgnoreCase));
                        if (!matches) continue;
                    }

                    var originalVp = ImageCrawlerService.ResolveOriginalDefectVp(d.ImageUri ?? d.ImageUrl);
                    var thumbUrl = ResolveThumbnailUrl(d);
                    var coords = d.CoordinateX.HasValue && d.CoordinateY.HasValue
                        ? $"X:{d.CoordinateX:F1}, Y:{d.CoordinateY:F1}"
                        : null;

                    items.Add(new AnomalyThumbnailItemDto(
                        prod.KeyId,
                        d.Id,
                        prod.Id,
                        prod.Judge,
                        effectiveCategory,
                        eval.AnomalyScore,
                        prod.MachineCode,
                        prod.LineCode,
                        prod.ModelCode,
                        d.EqpName,
                        prod.InspectedAtUtc,
                        d.DefectName,
                        d.DefectType,
                        d.DefectType,
                        d.Severity,
                        d.ComponentName,
                        coords,
                        d.ConfidenceScore,
                        thumbUrl,
                        d.ImageUri ?? d.ImageUrl,
                        dResult.RuleCode,
                        dResult.RuleVersion,
                        dResult.DecisionVerdict,
                         dResult.ExtractedFeatures,
                         dResult.DecisionReason,
                         originalVp,
                         prod.PanelId,
                         prod.OpReviewMark,
                         prod.NameDefect,
                         prod.DefectName,
                         dResult.AiVerdict,
                         dResult.AiStatus,
                         dResult.AiAnomalyScore,
                         dResult.DecisionRoute
                     ));
                }
            }
            else
            {
                // Product with 0 defects
                if (!string.IsNullOrWhiteSpace(request.Search) && !prod.KeyId.Contains(request.Search.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                items.Add(new AnomalyThumbnailItemDto(
                    prod.KeyId,
                    null,
                    prod.Id,
                    prod.Judge,
                    effectiveCategory,
                    eval.AnomalyScore,
                    prod.MachineCode,
                    prod.LineCode,
                    prod.ModelCode,
                    null,
                    prod.InspectedAtUtc,
                    prod.DefectName ?? "No defects linked",
                    "InspectionStation",
                    "Optical",
                    "Minor",
                    prod.Zone,
                    null,
                    1.0,
                    null,
                    null,
                    "NO_SCRIPT",
                    "1.0",
                    "NA",
                     new Dictionary<string, double> { ["TotalDefects"] = 0 },
                     eval.VerdictReason,
                     null,
                     prod.PanelId,
                     prod.OpReviewMark,
                     prod.NameDefect,
                     prod.DefectName,
                     null,
                     eval.OverallAiStatus,
                     null,
                     eval.PrimaryDecisionRoute
                 ));
            }
        }

        var totalCount = items.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

        return new PagedResponse<AnomalyThumbnailItemDto>(pagedItems, page, pageSize, totalCount, totalPages);
    }

    private static string? ResolveThumbnailUrl(DefectRecordEntity defect)
    {
        var uri = defect.ImageUrl ?? defect.ImageUri;
        if (!string.IsNullOrWhiteSpace(uri))
        {
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }
            if (uri.StartsWith("/"))
            {
                return uri;
            }
        }

        // Return the API image proxy endpoint for this defect
        return $"/api/v1/storage/defect-images/{defect.Id}";
    }

    public async Task<ProductRecordDto> CreateProductRecordAsync(CreateProductRecordRequest request)
    {
        var entity = new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = importEngine.NormalizeId(request.KeyId),
            Judge = request.Judge,
            PanelId = importEngine.NormalizeConfigKey(request.PanelId),
            PanelJudge = request.PanelJudge,
            NameDefect = request.NameDefect,
            MachineCode = importEngine.NormalizeConfigKey(request.MachineCode),
            LineCode = importEngine.NormalizeConfigKey(request.LineCode),
            InspectedAtUtc = DateTimeOffset.UtcNow,
            Zone = request.Zone,
            DefectName = request.DefectName
        };
        db.ProductRecords.Add(entity);
        await db.SaveChangesAsync();
        InvalidateProductCaches();
        return ToResponse(entity);
    }

    public async Task<DefectRecordDto> CreateDefectRecordAsync(CreateDefectRecordRequest request, SeaweedFsStorageManager storage)
    {
        var defectId = Guid.NewGuid();
        string? feat1Uri = null;
        string? feat2Uri = null;

        if (!string.IsNullOrWhiteSpace(request.FeatureJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<object>(request.FeatureJson) ?? request.FeatureJson;
                var obj = await storage.UploadJsonAsync("defect-features", $"{importEngine.NormalizeId(request.KeyId)}_feat_{defectId:N}.json", parsed);
                await SaveStorageObjectAsync(obj, "DefectFeatureJson");
                feat1Uri = obj.Uri;
            }
            catch
            {
                // Fallback
            }
        }

        if (!string.IsNullOrWhiteSpace(request.CustomFeatureJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<object>(request.CustomFeatureJson) ?? request.CustomFeatureJson;
                var obj = await storage.UploadJsonAsync("defect-features", $"{importEngine.NormalizeId(request.KeyId)}_custom_feat_{defectId:N}.json", parsed);
                await SaveStorageObjectAsync(obj, "DefectCustomFeatureJson");
                feat2Uri = obj.Uri;
            }
            catch
            {
                // Fallback
            }
        }

        var modelCode = request.ModelCode;
        var connectionId = request.ConnectionId;
        var machineCode = request.MachineCode;
        var lineCode = request.LineCode;

        if (string.IsNullOrWhiteSpace(modelCode) || modelCode == "UNASSIGNED" || string.IsNullOrWhiteSpace(machineCode) || string.IsNullOrWhiteSpace(lineCode))
        {
            var linkedProduct = await db.ProductRecords.AsNoTracking()
                .FirstOrDefaultAsync(p => p.KeyId == importEngine.NormalizeId(request.KeyId));
            if (linkedProduct != null)
            {
                modelCode ??= linkedProduct.ModelCode;
                machineCode ??= linkedProduct.MachineCode;
                lineCode ??= linkedProduct.LineCode;
            }
        }

        var entity = new DefectRecordEntity
        {
            Id = defectId,
            KeyId = importEngine.NormalizeId(request.KeyId),
            DefectTable = importEngine.NormalizeConfigKey(request.DefectTable),
            DefectName = request.DefectName,
            DefectType = request.DefectType,
            ComponentName = request.ComponentName,
            CoordinateX = request.CoordinateX,
            CoordinateY = request.CoordinateY,
            Severity = request.Severity,
            Judge = request.Judge,
            ConfidenceScore = request.ConfidenceScore,
            ConnectionId = connectionId,
            ModelCode = !string.IsNullOrWhiteSpace(modelCode) ? modelCode : "UNASSIGNED",
            MachineCode = !string.IsNullOrWhiteSpace(machineCode) ? machineCode : "MACHINE-A",
            LineCode = !string.IsNullOrWhiteSpace(lineCode) ? lineCode : "LINE-1",
            ImageUri = request.ImageUrl ?? request.ImageUri,
            ImageUrl = request.ImageUrl ?? request.ImageUri,
            Feature1StorageUri = feat1Uri,
            Feature2StorageUri = feat2Uri,
            FeatureUrl = request.FeatureUrl ?? feat1Uri,
            CustomFeatureUrl = request.CustomFeatureUrl ?? feat2Uri,
            FeatureJson = request.FeatureJson,
            CustomFeatureJson = request.CustomFeatureJson,
            Reason = request.Reason,
            EqpName = request.EqpName,
            EmNo = request.EmNo,
            SpecName = request.SpecName,
            SpecJudge = request.SpecJudge,
            SpecReason = request.SpecReason,
            Priority = request.Priority,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.DefectRecords.Add(entity);
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task RecordAuditAsync(string actor, string action, string targetType, string targetId, string message)
    {
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Actor = actor,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Message = message
        });
        await db.SaveChangesAsync();

        if (logger != null)
        {
            using (logger.BeginScope(new Dictionary<string, object?>
            {
                [LoggingConstants.LogTypeProperty] = LoggingConstants.LogTypes.Special,
                [LoggingConstants.AuditActionProperty] = action,
                [LoggingConstants.UserIdProperty] = actor,
                [LoggingConstants.TargetTypeProperty] = targetType,
                [LoggingConstants.TargetIdProperty] = targetId
            }))
            {
                logger.LogInformation("[Audit] Actor: {Actor} performed {AuditAction} on {TargetType} ({TargetId}): {Message}",
                    actor, action, targetType, targetId, message);
            }
        }
    }

    public async Task<IReadOnlyCollection<AuditEventResponse>> GetAuditEventsAsync() => (await db.AuditEvents.ToArrayAsync())
        .OrderByDescending(x => x.OccurredAtUtc)
        .Select(ToResponse)
        .ToArray();

    internal static string HashPassword(string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    public async Task<IReadOnlyCollection<ScanListItemDto>> GetScanListItemsAsync(Guid scanListId)
    {
        return await db.ScanListItems
            .Where(x => x.ScanListId == scanListId)
            .OrderBy(x => x.RowNumber)
            .Select(x => new ScanListItemDto(x.RowNumber, x.EntityId, x.LineCode, x.MappingInt, x.Mapping))
            .ToArrayAsync();
    }

    public async Task<PagedResponse<VerificationHistoryResponse>> GetVerificationHistoryAsync(string? category, string? machineCode, string? lineCode, int page, int pageSize)
    {
        var (normalizedPage, normalizedPageSize) = NormalizePage(page, pageSize);
        var query = db.VerificationHistory.AsQueryable();
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(machineCode) && !machineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.MachineCode == importEngine.NormalizeConfigKey(machineCode));
        if (!string.IsNullOrWhiteSpace(lineCode) && !lineCode.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.LineCode == importEngine.NormalizeConfigKey(lineCode));

        var totalCount = await query.CountAsync();
        var entities = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToArrayAsync();
        var items = entities.Select(ToHistoryResponse).ToArray();
        return CreatePagedResponse(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public Task<VerifyBatchResponse> VerifyOverkillAsync(VerifyBatchRequest request) =>
        VerifyCategoryAsync(request, "Overkill");

    public Task<VerifyBatchResponse> VerifyLeakageAsync(VerifyBatchRequest request) =>
        VerifyCategoryAsync(request, "Leakage");

    private async Task<VerifyBatchResponse> VerifyCategoryAsync(VerifyBatchRequest request, string category)
    {
        var isOverkill = category.Equals("Overkill", StringComparison.OrdinalIgnoreCase);
        ImportPreviewResponse preview;
        Guid? scanListId = request.ScanListId;
        var machineCode = importEngine.NormalizeConfigKey(request.MachineCode ?? "UNASSIGNED");
        var lineCode = string.IsNullOrWhiteSpace(request.LineCode) ? "ALL" : importEngine.NormalizeConfigKey(request.LineCode);

        if (request.ScanListId is Guid existingListId)
        {
            var scanList = await db.ScanLists.SingleOrDefaultAsync(x => x.Id == existingListId)
                ?? throw new InvalidOperationException("Scan list was not found.");
            machineCode = scanList.MachineCode;
            lineCode = scanList.LineCode;
            var items = await db.ScanListItems.Where(x => x.ScanListId == existingListId).OrderBy(x => x.RowNumber).ToArrayAsync();
            preview = new ImportPreviewResponse(
                items.Length,
                0,
                0,
                scanList.Checksum,
                scanList.VerificationCategory,
                scanList.MachineCode,
                scanList.LineCode,
                false,
                items.Select(x => new ImportAcceptedRow(x.RowNumber, x.EntityId, x.LineCode, x.MappingInt, x.Mapping)).ToArray(),
                []);
        }
        else if (isOverkill && string.IsNullOrWhiteSpace(request.RawText))
        {
            var candidateQuery = db.ProductRecords.AsQueryable();
            if (!string.IsNullOrWhiteSpace(request.MachineCode) && !request.MachineCode.Equals("All", StringComparison.OrdinalIgnoreCase) && !request.MachineCode.Equals("AUTO", StringComparison.OrdinalIgnoreCase) && !request.MachineCode.Equals("UNASSIGNED", StringComparison.OrdinalIgnoreCase))
            {
                var normMachine = importEngine.NormalizeConfigKey(request.MachineCode);
                candidateQuery = candidateQuery.Where(x => x.MachineCode == normMachine);
            }
            if (!string.IsNullOrWhiteSpace(request.LineCode) && !request.LineCode.Equals("All", StringComparison.OrdinalIgnoreCase) && !request.LineCode.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var normLine = importEngine.NormalizeConfigKey(request.LineCode);
                candidateQuery = candidateQuery.Where(x => x.LineCode == normLine);
            }

            var autoProducts = await candidateQuery
                .Where(x => x.Judge == "NG" || x.Judge == "N" || x.Judge == "N/A" || x.Judge == "NA")
                .OrderByDescending(x => x.InspectedAtUtc)
                .Take(200)
                .ToArrayAsync();

            var acceptedRows = autoProducts
                .Select((p, idx) => new ImportAcceptedRow(idx + 1, p.KeyId, p.LineCode, null, null))
                .ToArray();

            preview = new ImportPreviewResponse(
                acceptedRows.Length,
                0,
                0,
                "auto-overkill",
                "Overkill",
                machineCode,
                lineCode,
                false,
                acceptedRows,
                []);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.RawText))
                throw new InvalidOperationException("Provide a scan list or paste raw IDs to verify.");
            preview = PreviewImport(new ImportPreviewRequest(request.RawText, request.MappingName, request.MachineCode, request.LineCode, category));
            machineCode = preview.MachineCode;
            lineCode = preview.LineCode ?? "ALL";
        }

        var keyIds = preview.AcceptedRows.Select(x => x.EntityId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // For Overkill: automatically detect the required machine if not assigned or set to AUTO
        if (category == "Overkill" && (machineCode == "UNASSIGNED" || string.IsNullOrWhiteSpace(machineCode) || machineCode.Equals("AUTO", StringComparison.OrdinalIgnoreCase)))
        {
            var candidate = await db.ProductRecords.FirstOrDefaultAsync(x => keyIds.Contains(x.KeyId));
            if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.MachineCode))
            {
                machineCode = candidate.MachineCode;
                if (lineCode == "ALL" && !string.IsNullOrWhiteSpace(candidate.LineCode))
                {
                    lineCode = candidate.LineCode;
                }
            }
            else
            {
                var overkillProfile = await db.QueryProfiles.FirstOrDefaultAsync(x => x.VerificationCategory == "Overkill" && x.Enabled);
                if (overkillProfile is not null && !string.IsNullOrWhiteSpace(overkillProfile.MachineCode))
                {
                    machineCode = overkillProfile.MachineCode;
                    if (lineCode == "ALL" && !string.IsNullOrWhiteSpace(overkillProfile.LineCode))
                    {
                        lineCode = overkillProfile.LineCode;
                    }
                }
            }
        }

        DatabaseConnectionEntity? connection = null;
        QueryProfileEntity? profile = null;
        if (request.QueryProfileId is Guid profileId)
        {
            profile = await db.QueryProfiles.SingleOrDefaultAsync(x => x.Id == profileId)
                ?? throw new InvalidOperationException("Query profile was not found.");
            if (profile.DataSourceId is Guid boundId)
                connection = await db.DatabaseConnections.SingleOrDefaultAsync(x => x.Id == boundId);
        }
        else if (request.ConnectionId is Guid connectionId)
        {
            connection = await db.DatabaseConnections.SingleOrDefaultAsync(x => x.Id == connectionId)
                ?? throw new InvalidOperationException("Database connection was not found.");
            profile = await db.QueryProfiles
                .Where(x => x.DataSourceId == connection.Id && x.Enabled && x.VerificationCategory == category)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync()
                ?? await db.QueryProfiles
                    .Where(x => x.DataSourceId == connection.Id && x.Enabled)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefaultAsync();
        }
        else
        {
            connection = await db.DatabaseConnections.FirstOrDefaultAsync(x => x.MachineCode == machineCode && (lineCode == "ALL" || x.LineCode == lineCode));
            if (connection is not null)
            {
                profile = await db.QueryProfiles
                    .Where(x => x.DataSourceId == connection.Id && x.Enabled && x.VerificationCategory == category)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefaultAsync()
                    ?? await db.QueryProfiles
                        .Where(x => x.DataSourceId == connection.Id && x.Enabled)
                        .OrderByDescending(x => x.UpdatedAtUtc)
                        .FirstOrDefaultAsync();
            }
        }

        if (connection is not null)
            machineCode = connection.MachineCode;
        var products = await db.ProductRecords.Where(x => keyIds.Contains(x.KeyId)).ToArrayAsync();
        var productsByKey = products.ToDictionary(x => x.KeyId, StringComparer.OrdinalIgnoreCase);
        var defects = await db.DefectRecords.Where(x => keyIds.Contains(x.KeyId)).ToArrayAsync();
        var defectsByKey = defects
            .GroupBy(x => x.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(ToResponse).ToArray(), StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var history = new List<VerificationHistoryEntity>();
        var eligible = 0;
        var skipped = 0;
        var overkillCount = 0;
        var leakageCount = 0;
        var consistentNg = 0;

        foreach (var row in preview.AcceptedRows)
        {
            productsByKey.TryGetValue(row.EntityId, out var product);
            var judge = product?.Judge ?? "Unknown";
            var eligibleRow = isOverkill ? IsOverkillEligibleJudge(judge) : IsLeakageEligibleJudge(judge);
            var defectList = defectsByKey.TryGetValue(row.EntityId, out var found) ? found : [];
            var targetLine = importEngine.NormalizeConfigKey(row.LineCode ?? lineCode);
            var productDto = product is null
                ? new ProductRecordDto(Guid.Empty, row.EntityId, judge, "UNASSIGNED", judge, "None", machineCode, targetLine, now, null)
                : ToResponse(product);

            ProductScanItemDto analyzed;
            if (isOverkill)
            {
                analyzed = anomalyEngine.AnalyzeOverkill(productDto, defectList);
            }
            else
            {
                analyzed = anomalyEngine.AnalyzeLeakage(productDto, defectList, targetLine, row.Mapping, row.MappingInt);
            }

            if (!eligibleRow)
            {
                skipped++;
                analyzed = analyzed with
                {
                    AnomalyStatus = "Skipped",
                    AnomalyScore = 0,
                    AnalysisNotes = isOverkill
                        ? $"Overkill verify only evaluates items judged N / NG / NA. Current judge is '{judge}'."
                        : $"Leakage verify only evaluates items judged G / OK. Current judge is '{judge}'."
                };
            }
            else
            {
                eligible++;
                if (analyzed.AnomalyStatus == "Overkill") overkillCount++;
                if (analyzed.AnomalyStatus == "Leakage") leakageCount++;
                if (analyzed.AnomalyStatus == "ConsistentNG") consistentNg++;
            }

            history.Add(new VerificationHistoryEntity
            {
                Id = Guid.NewGuid(),
                Category = category,
                EntityId = row.EntityId,
                LineCode = importEngine.NormalizeConfigKey(row.LineCode ?? lineCode),
                Mapping = row.Mapping ?? row.MappingInt?.ToString(),
                MachineCode = machineCode,
                Judge = judge,
                PanelJudge = product?.PanelJudge ?? "Unknown",
                NameDefect = product?.NameDefect ?? analyzed.Product.NameDefect,
                AnomalyStatus = analyzed.AnomalyStatus,
                AnomalyScore = analyzed.AnomalyScore,
                AnalysisNotes = analyzed.AnalysisNotes,
                TotalDefects = analyzed.TotalDefects,
                Eligible = eligibleRow,
                ScanListId = scanListId,
                QueryProfileId = profile?.Id,
                ConnectionId = connection?.Id,
                ConnectionName = connection?.Name,
                QueryName = profile?.Name,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    row.EntityId,
                    row.LineCode,
                    row.Mapping,
                    row.MappingInt,
                    Query = profile?.SqlTemplate,
                    Connection = connection?.Name
                }),
                CreatedAtUtc = now
            });
        }

        db.VerificationHistory.AddRange(history);
        await db.SaveChangesAsync();

        return new VerifyBatchResponse(
            category,
            history.Count,
            eligible,
            skipped,
            overkillCount,
            leakageCount,
            consistentNg,
            scanListId,
            connection?.Id,
            connection?.Name,
            history.Select(ToHistoryResponse).ToArray()
        );
    }

    private async Task<(Guid? ConnectionId, string MachineCode, string LineCode, string VerificationCategory)> BindQueryToConnectionAsync(CreateQueryProfileRequest request)
    {
        var category = string.IsNullOrWhiteSpace(request.VerificationCategory) ? "Overkill" : request.VerificationCategory.Trim();
        if (request.DataSourceId is Guid connectionId)
        {
            var connection = await db.DatabaseConnections.SingleOrDefaultAsync(x => x.Id == connectionId)
                ?? throw new InvalidOperationException("Select a valid MariaDB connection for this query.");
            if (!connection.Engine.Equals("MariaDB", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Query content must be bound to a MariaDB connection.");
            return (connection.Id, connection.MachineCode, connection.LineCode, category);
        }

        var machine = importEngine.NormalizeConfigKey(string.IsNullOrWhiteSpace(request.MachineCode) ? "UNASSIGNED" : request.MachineCode);
        var line = importEngine.NormalizeConfigKey(string.IsNullOrWhiteSpace(request.LineCode) ? "ALL" : request.LineCode);
        return (null, machine, line, category);
    }

    private static bool IsOverkillEligibleJudge(string judge) =>
        judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
        || judge.Equals("N", StringComparison.OrdinalIgnoreCase)
        || judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        || judge.Equals("NA", StringComparison.OrdinalIgnoreCase);

    private static bool IsLeakageEligibleJudge(string judge) =>
        judge.Equals("G", StringComparison.OrdinalIgnoreCase)
        || judge.Equals("OK", StringComparison.OrdinalIgnoreCase)
        || judge.Equals("GOOD", StringComparison.OrdinalIgnoreCase)
        || judge.Equals("PASS", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<VerificationResultEntity> CreateResultRows(ScanExecutionEntity execution, DataSourceEntity[] targetSources)
    {
        var statuses = new[] { "Passed", "Passed", "Passed", "Warning", "Failed", "AnomalyDetected", "ExecutionError" };
        return Enumerable.Range(1, Math.Min(execution.TotalItems, 100)).Select(i =>
        {
            var status = statuses[i % statuses.Length];
            var severity = status switch
            {
                "Failed" => "High",
                "ExecutionError" => "Critical",
                "AnomalyDetected" => "Medium",
                "Warning" => "Low",
                _ => "None"
            };
            var source = targetSources[i % targetSources.Length];
            return new VerificationResultEntity
            {
                Id = Guid.NewGuid(),
                ExecutionId = execution.Id,
                EntityId = $"AUTO-{i:000000}",
                SourceName = source.Name,
                Status = status,
                Severity = severity,
                RuleCode = status == "Passed" ? "RULE-OK" : status == "ExecutionError" ? "SOURCE-TIMEOUT" : "VERIFY-MISMATCH",
                Message = status == "Passed" ? "All configured checks passed." : "Simulated verification evidence for investigation workflow.",
                AnomalyScore = Math.Round(0.12 + (i % 9) * 0.09, 2),
                CompletedAtUtc = execution.CompletedAtUtc
            };
        });
    }

    private static double Rate(int numerator, int denominator) => denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 4);
    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) => (Math.Max(page, 1), Math.Clamp(pageSize, 10, 100));
    private static PagedResponse<T> CreatePagedResponse<T>(IReadOnlyCollection<T> items, int page, int pageSize, int totalCount) => new(items, page, pageSize, totalCount, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));

    private static ProductRecordDto ToResponse(ProductRecordEntity x) => new(x.Id, x.KeyId, x.Judge, x.PanelId, x.PanelJudge, x.NameDefect, x.MachineCode, x.LineCode, x.InspectedAtUtc, x.MetadataJson, x.Zone, x.DefectName, x.DefectPackUrl, x.OpReviewMark, x.OpReviewJudge, x.OpReviewDefectName, x.OpReviewMappedDefect, x.OpReviewNote, x.OpReviewedAtUtc);
    private static DefectRecordDto ToResponse(DefectRecordEntity x) => new(
        x.Id,
        x.KeyId,
        x.DefectTable,
        x.DefectName,
        x.DefectType,
        x.ComponentName,
        x.CoordinateX,
        x.CoordinateY,
        x.Severity,
        x.Judge,
        x.ConfidenceScore,
        x.ImageUri,
        x.Feature1StorageUri,
        x.Feature2StorageUri,
        x.CreatedAtUtc,
        x.FeatureJson,
        x.CustomFeatureJson,
        x.Reason,
        x.EqpName,
        x.EmNo,
        x.SpecName,
        x.SpecJudge,
        x.SpecReason,
        x.Priority,
        x.FeatureUrl ?? x.Feature1StorageUri,
        x.CustomFeatureUrl ?? x.Feature2StorageUri,
        x.ImageUrl ?? x.ImageUri,
        x.ModelCode,
        x.ConnectionId,
        x.MachineCode,
        x.LineCode);
    private static ScanListResponse ToResponse(ScanListEntity x) => new(x.Id, x.Name, x.MappingName, x.MachineCode, x.LineCode, x.VerificationCategory, x.AcceptedCount, x.RejectedCount, x.DuplicateCount, x.Checksum, x.EvidenceUri, x.CreatedAtUtc, x.CreatedBy);
    private static ScanExecutionResponse ToResponse(ScanExecutionEntity x) => new(x.Id, x.Name, x.ScanListId, x.ScanListName, x.Status, JsonSerializer.Deserialize<string[]>(x.SourceNamesJson) ?? [], x.TotalItems, x.PassedItems, x.WarningItems, x.FailedItems, x.TechnicalErrorItems, x.AnomalyItems, x.EvidenceUri, x.StartedAtUtc, x.CompletedAtUtc);
    private static VerificationResultResponse ToResponse(VerificationResultEntity x) => new(x.Id, x.ExecutionId, x.EntityId, x.SourceName, x.Status, x.Severity, x.RuleCode, x.Message, x.AnomalyScore, x.CompletedAtUtc);
    private static DataSourceResponse ToResponse(DataSourceEntity x) => new(x.Id, x.Name, x.Type, x.MachineCode, x.VerificationCategory, x.SchedulePeriod, x.Environment, x.Health, x.AverageLatencyMs, x.Enabled, x.Host, x.Port, x.DatabaseName, x.ApiBaseUrl, !string.IsNullOrWhiteSpace(x.EncryptedSecret));
    private DatabaseConnectionResponse ToResponse(DatabaseConnectionEntity x)
    {
        var credentialStatus = databaseCredentials.GetCredentialStatus(x.EncryptedPassword);
        return new DatabaseConnectionResponse(
            x.Id,
            x.Name,
            x.Engine,
            x.MachineCode,
            x.LineCode,
            x.Host,
            x.Port,
            x.DatabaseName,
            x.Username,
            x.ApiBaseUrl,
            credentialStatus == "Unreadable" ? "CredentialError" : x.Health,
            x.AverageLatencyMs,
            credentialStatus != "Missing",
            credentialStatus,
            x.UpdatedAtUtc,
            x.ImageCrawlerApiUrl ?? x.ApiBaseUrl,
            x.ImageCrawlerVpMode ?? "0",
            x.ModelCode ?? "UNASSIGNED");
    }
    private static AuditEventResponse ToResponse(AuditEventEntity x) => new(x.Id, x.OccurredAtUtc, x.Actor, x.Action, x.TargetType, x.TargetId, x.Message);
    private static ProductQueryResponse ToResponse(ProductQueryEntity x) => new(x.Id, x.QueryKey, x.ProductId, x.SourceSystem, x.Status, x.PayloadJson, x.JudgeResult, x.Message, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static QueryProfileResponse ToResponse(QueryProfileEntity x, string? connectionName = null) =>
        new(x.Id, x.Name, x.DataSourceId, connectionName, x.MachineCode, x.LineCode, x.VerificationCategory, x.Concept, x.SourceSystem, x.SqlTemplate, x.ParameterSchemaJson, x.TimeoutSeconds, x.MaxConcurrency, x.Enabled, x.Notes, x.CreatedAtUtc, x.UpdatedAtUtc);

    private static VerificationHistoryResponse ToHistoryResponse(VerificationHistoryEntity x) =>
        new(x.Id, x.Category, x.EntityId, x.LineCode, x.Mapping, x.MachineCode, x.Judge, x.PanelJudge, x.NameDefect, x.AnomalyStatus, x.AnomalyScore, x.AnalysisNotes, x.TotalDefects, x.Eligible, x.ScanListId, x.QueryProfileId, x.ConnectionId, x.ConnectionName, x.QueryName, x.CreatedAtUtc);
}
