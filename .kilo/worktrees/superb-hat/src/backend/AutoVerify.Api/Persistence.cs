using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public sealed class AutoVerifyDbContext(DbContextOptions<AutoVerifyDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<DataSourceEntity> DataSources => Set<DataSourceEntity>();
    public DbSet<ScanListEntity> ScanLists => Set<ScanListEntity>();
    public DbSet<ScanExecutionEntity> ScanExecutions => Set<ScanExecutionEntity>();
    public DbSet<VerificationResultEntity> VerificationResults => Set<VerificationResultEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<StorageObjectEntity> StorageObjects => Set<StorageObjectEntity>();
    public DbSet<ProductQueryEntity> ProductQueries => Set<ProductQueryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureCommonEntityLengths(modelBuilder);

        modelBuilder.Entity<AppUserEntity>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<DataSourceEntity>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<ScanListEntity>().HasIndex(x => x.Checksum);
        modelBuilder.Entity<ScanExecutionEntity>().HasIndex(x => x.StartedAtUtc);
        modelBuilder.Entity<VerificationResultEntity>().HasIndex(x => x.ExecutionId);
        modelBuilder.Entity<AuditEventEntity>().HasIndex(x => x.OccurredAtUtc);
        modelBuilder.Entity<StorageObjectEntity>().HasIndex(x => x.CreatedAtUtc);
        modelBuilder.Entity<ProductQueryEntity>().HasIndex(x => x.Status);
        modelBuilder.Entity<ProductQueryEntity>().HasIndex(x => x.QueryKey);
    }

    private static void ConfigureCommonEntityLengths(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUserEntity>().Property(x => x.Username).HasMaxLength(128);
        modelBuilder.Entity<AppUserEntity>().Property(x => x.DisplayName).HasMaxLength(256);
        modelBuilder.Entity<AppUserEntity>().Property(x => x.PasswordHash).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Type).HasMaxLength(64);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Environment).HasMaxLength(64);
        modelBuilder.Entity<DataSourceEntity>().Property(x => x.Health).HasMaxLength(64);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.MappingName).HasMaxLength(128);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.Checksum).HasMaxLength(128);
        modelBuilder.Entity<ScanListEntity>().Property(x => x.EvidenceUri).HasColumnType("text");
        modelBuilder.Entity<ScanListEntity>().Property(x => x.CreatedBy).HasMaxLength(128);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.Name).HasMaxLength(256);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.ScanListName).HasMaxLength(256);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.Status).HasMaxLength(64);
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.SourceNamesJson).HasColumnType("longtext");
        modelBuilder.Entity<ScanExecutionEntity>().Property(x => x.EvidenceUri).HasColumnType("text");
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.EntityId).HasMaxLength(128);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.SourceName).HasMaxLength(256);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.Status).HasMaxLength(64);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.Severity).HasMaxLength(64);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.RuleCode).HasMaxLength(128);
        modelBuilder.Entity<VerificationResultEntity>().Property(x => x.Message).HasColumnType("text");
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.Actor).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.Action).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.TargetType).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.TargetId).HasMaxLength(128);
        modelBuilder.Entity<AuditEventEntity>().Property(x => x.Message).HasColumnType("text");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Backend).HasMaxLength(64);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Bucket).HasMaxLength(256);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.ObjectName).HasMaxLength(512);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.ContentType).HasMaxLength(128);
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Uri).HasColumnType("text");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Path).HasColumnType("text");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.MetadataJson).HasColumnType("longtext");
        modelBuilder.Entity<StorageObjectEntity>().Property(x => x.Purpose).HasMaxLength(128);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.QueryKey).HasMaxLength(256);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.ProductId).HasMaxLength(128);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.SourceSystem).HasMaxLength(128);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.Status).HasMaxLength(32);
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.PayloadJson).HasColumnType("longtext");
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.JudgeResult).HasColumnType("longtext");
        modelBuilder.Entity<ProductQueryEntity>().Property(x => x.Message).HasColumnType("longtext");
    }
}

public sealed class AutoVerifyStore(AutoVerifyDbContext db)
{
    public async Task<bool> ValidateCredentialsAsync(string username, string password)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == username && x.IsActive);
        return user is not null && user.PasswordHash == HashPassword(password);
    }

    public ImportPreviewResponse PreviewImport(ImportPreviewRequest request)
    {
        var rows = ParseRows(request.RawText);
        var accepted = new List<ImportAcceptedRow>();
        var rejected = new List<ImportRejectedRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateCount = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var value = NormalizeId(rows[i]);
            if (string.IsNullOrWhiteSpace(value))
            {
                rejected.Add(new ImportRejectedRow(i + 1, rows[i], "ID is empty after normalization."));
                continue;
            }

            if (value.Length > 64)
            {
                rejected.Add(new ImportRejectedRow(i + 1, rows[i], "ID exceeds the maximum supported length of 64 characters."));
                continue;
            }

            if (!seen.Add(value))
            {
                duplicateCount++;
                rejected.Add(new ImportRejectedRow(i + 1, rows[i], "Duplicate ID after normalization."));
                continue;
            }

            accepted.Add(new ImportAcceptedRow(i + 1, value));
        }

        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', accepted.Select(x => x.EntityId))))).ToLowerInvariant();
        return new ImportPreviewResponse(accepted.Count, rejected.Count, duplicateCount, checksum, accepted.Take(50).ToArray(), rejected.Take(50).ToArray());
    }

    public async Task<ScanListResponse> CreateScanListAsync(CreateScanListRequest request, StoredObjectResponse? importEvidence)
    {
        var preview = PreviewImport(new ImportPreviewRequest(request.RawText, request.MappingName));
        var entity = new ScanListEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            MappingName = request.MappingName,
            AcceptedCount = preview.AcceptedCount,
            RejectedCount = preview.RejectedCount,
            DuplicateCount = preview.DuplicateCount,
            Checksum = preview.Checksum,
            EvidenceUri = importEvidence?.Uri,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = "admin"
        };

        db.ScanLists.Add(entity);
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
            ExecutionId = executionId,
            request.Name,
            ScanList = scanList.Name,
            Sources = sourceNames,
            total,
            passed,
            warnings,
            failed,
            technicalErrors,
            anomalyCount,
            startedAt,
            completedAt
        });

        db.StorageObjects.Add(new StorageObjectEntity
        {
            Id = evidence.Id,
            Backend = evidence.Backend,
            Bucket = evidence.Bucket,
            ObjectName = evidence.ObjectName,
            ContentType = evidence.ContentType,
            SizeBytes = evidence.SizeBytes,
            Uri = evidence.Uri,
            Path = evidence.Uri,
            MetadataJson = JsonSerializer.Serialize(evidence),
            Purpose = "ScanExecutionEvidence",
            CreatedAtUtc = evidence.CreatedAtUtc
        });

        var entity = new ScanExecutionEntity
        {
            Id = executionId,
            Name = request.Name,
            ScanListId = scanList.Id,
            ScanListName = scanList.Name,
            Status = "Completed",
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

        db.ScanExecutions.Add(entity);
        db.VerificationResults.AddRange(CreateResultRows(entity, targetSources));
        await db.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<IReadOnlyCollection<ScanExecutionResponse>> GetScanExecutionsAsync() => (await db.ScanExecutions.ToArrayAsync())
        .OrderByDescending(x => x.StartedAtUtc)
        .Select(ToResponse)
        .ToArray();

    public async Task<IReadOnlyCollection<VerificationResultResponse>> GetResultsAsync(Guid executionId) => (await db.VerificationResults
        .Where(x => x.ExecutionId == executionId)
        .ToArrayAsync())
        .OrderByDescending(x => x.CompletedAtUtc)
        .Select(ToResponse)
        .ToArray();

    public async Task<IReadOnlyCollection<DataSourceResponse>> GetDataSourcesAsync() => await db.DataSources
        .OrderBy(x => x.Name)
        .Select(x => ToResponse(x))
        .ToArrayAsync();

    public async Task<DataSourceResponse?> GetDataSourceAsync(Guid sourceId) => await db.DataSources
        .Where(x => x.Id == sourceId)
        .Select(x => ToResponse(x))
        .SingleOrDefaultAsync();

    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync()
    {
        var executions = await db.ScanExecutions.ToArrayAsync();
        var total = executions.Sum(x => x.TotalItems);
        var passed = executions.Sum(x => x.PassedItems);
        var failed = executions.Sum(x => x.FailedItems);
        var technicalErrors = executions.Sum(x => x.TechnicalErrorItems);
        var anomalies = executions.Sum(x => x.AnomalyItems);
        var healthySources = await db.DataSources.CountAsync(x => x.Health == "Healthy");
        var totalSources = await db.DataSources.CountAsync();

        return new DashboardSummaryResponse(executions.Length, total, Rate(passed, total), Rate(failed, total), Rate(technicalErrors, total), Rate(anomalies, total), healthySources, totalSources, executions.MaxBy(x => x.StartedAtUtc)?.StartedAtUtc);
    }

    public async Task<IReadOnlyCollection<TrendBucketResponse>> GetTrendBucketsAsync()
    {
        var executions = (await db.ScanExecutions.ToArrayAsync()).OrderBy(x => x.StartedAtUtc).ToArray();
        if (executions.Length == 0)
        {
            return [];
        }

        return executions
            .GroupBy(x => x.StartedAtUtc.UtcDateTime.Date)
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var items = group.ToArray();
                var total = items.Sum(x => x.TotalItems);
                return new TrendBucketResponse(group.Key, total, Rate(items.Sum(x => x.PassedItems), total), Rate(items.Sum(x => x.FailedItems), total), Rate(items.Sum(x => x.TechnicalErrorItems), total), Rate(items.Sum(x => x.AnomalyItems), total));
            })
            .ToArray();
    }

    public async Task SaveStorageObjectAsync(StoredObjectResponse storedObject, string purpose)
    {
        db.StorageObjects.Add(new StorageObjectEntity
        {
            Id = storedObject.Id,
            Backend = storedObject.Backend,
            Bucket = storedObject.Bucket,
            ObjectName = storedObject.ObjectName,
            ContentType = storedObject.ContentType,
            SizeBytes = storedObject.SizeBytes,
            Uri = storedObject.Uri,
            Path = storedObject.Uri,
            MetadataJson = JsonSerializer.Serialize(storedObject),
            Purpose = purpose,
            CreatedAtUtc = storedObject.CreatedAtUtc
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
            QueryKey = request.QueryKey,
            ProductId = request.ProductId,
            SourceSystem = string.IsNullOrWhiteSpace(request.SourceSystem) ? "Manual" : request.SourceSystem,
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
            query = query.Where(x => x.Status == status);
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
        if (entity is null)
        {
            return null;
        }

        entity.Status = status;
        entity.JudgeResult = judgeResult;
        entity.Message = message;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
    }

    public async Task<IReadOnlyCollection<AuditEventResponse>> GetAuditEventsAsync() => (await db.AuditEvents.ToArrayAsync())
        .OrderByDescending(x => x.OccurredAtUtc)
        .Select(ToResponse)
        .ToArray();

    internal static string HashPassword(string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

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
                Message = status == "Passed" ? "All configured checks passed." : "Simulated MVP verification evidence for investigation workflow.",
                AnomalyScore = Math.Round(0.12 + (i % 9) * 0.09, 2),
                CompletedAtUtc = execution.CompletedAtUtc
            };
        });
    }

    private static double Rate(int numerator, int denominator) => denominator == 0 ? 0 : Math.Round((double)numerator / denominator, 4);
    private static List<string> ParseRows(string rawText) => rawText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split(['\n', ',', ';', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    private static string NormalizeId(string value) => value.Trim().ToUpperInvariant();
    private static ScanListResponse ToResponse(ScanListEntity x) => new(x.Id, x.Name, x.MappingName, x.AcceptedCount, x.RejectedCount, x.DuplicateCount, x.Checksum, x.EvidenceUri, x.CreatedAtUtc, x.CreatedBy);
    private static ScanExecutionResponse ToResponse(ScanExecutionEntity x) => new(x.Id, x.Name, x.ScanListId, x.ScanListName, x.Status, JsonSerializer.Deserialize<string[]>(x.SourceNamesJson) ?? [], x.TotalItems, x.PassedItems, x.WarningItems, x.FailedItems, x.TechnicalErrorItems, x.AnomalyItems, x.EvidenceUri, x.StartedAtUtc, x.CompletedAtUtc);
    private static VerificationResultResponse ToResponse(VerificationResultEntity x) => new(x.Id, x.ExecutionId, x.EntityId, x.SourceName, x.Status, x.Severity, x.RuleCode, x.Message, x.AnomalyScore, x.CompletedAtUtc);
    private static DataSourceResponse ToResponse(DataSourceEntity x) => new(x.Id, x.Name, x.Type, x.Environment, x.Health, x.AverageLatencyMs, x.Enabled);
    private static AuditEventResponse ToResponse(AuditEventEntity x) => new(x.Id, x.OccurredAtUtc, x.Actor, x.Action, x.TargetType, x.TargetId, x.Message);
    private static ProductQueryResponse ToResponse(ProductQueryEntity x) => new(x.Id, x.QueryKey, x.ProductId, x.SourceSystem, x.Status, x.PayloadJson, x.JudgeResult, x.Message, x.CreatedAtUtc, x.UpdatedAtUtc);
}

public static class ProductQueryStatuses
{
    public const string Pending = "Pending";
    public const string Doing = "Doing";
    public const string Judge = "Judge";

    public static bool IsValid(string status) => status is Pending or Doing or Judge;
}

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AutoVerifyDbContext db)
    {
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AppUserEntity { Id = Guid.NewGuid(), Username = "admin", DisplayName = "Administrator", PasswordHash = AutoVerifyStore.HashPassword("admin!"), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        }

        if (!await db.DataSources.AnyAsync())
        {
            db.DataSources.AddRange(
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999655"), Name = "Core Customer MariaDB", Type = "MariaDB", Environment = "Production", Health = "Healthy", AverageLatencyMs = 42, Enabled = true },
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999656"), Name = "Billing MariaDB", Type = "MariaDB", Environment = "Production", Health = "Warning", AverageLatencyMs = 119, Enabled = true },
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999657"), Name = "Partner Verification API", Type = "REST API", Environment = "Production", Health = "Healthy", AverageLatencyMs = 87, Enabled = true },
                new DataSourceEntity { Id = Guid.Parse("0198d917-6f5c-7000-b2f0-982dd2999658"), Name = "Legacy Orders MariaDB", Type = "MariaDB", Environment = "Staging", Health = "Healthy", AverageLatencyMs = 66, Enabled = false });
        }

        await db.SaveChangesAsync();

        if (!await db.ScanLists.AnyAsync())
        {
            var store = new AutoVerifyStore(db);
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

        await db.SaveChangesAsync();
    }
}

public class AppUserEntity
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class DataSourceEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Health { get; set; } = string.Empty;
    public int AverageLatencyMs { get; set; }
    public bool Enabled { get; set; }
}

public class ScanListEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DuplicateCount { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string? EvidenceUri { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class ScanExecutionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ScanListId { get; set; }
    public string ScanListName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceNamesJson { get; set; } = "[]";
    public int TotalItems { get; set; }
    public int PassedItems { get; set; }
    public int WarningItems { get; set; }
    public int FailedItems { get; set; }
    public int TechnicalErrorItems { get; set; }
    public int AnomalyItems { get; set; }
    public string? EvidenceUri { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

public class VerificationResultEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double AnomalyScore { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

public class AuditEventEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class StorageObjectEntity
{
    public Guid Id { get; set; }
    public string Backend { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Uri { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class ProductQueryEntity
{
    public Guid Id { get; set; }
    public string QueryKey { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string Status { get; set; } = ProductQueryStatuses.Pending;
    public string PayloadJson { get; set; } = string.Empty;
    public string? JudgeResult { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
