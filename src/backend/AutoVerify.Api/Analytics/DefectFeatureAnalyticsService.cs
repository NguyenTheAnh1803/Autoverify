using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Analytics;

public sealed class DefectFeatureAnalyticsService : IDefectFeatureAnalyticsService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefectFeatureAnalyticsService> _logger;
    private readonly Channel<Guid> _jobChannel;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeJobTokens = new();
    private readonly ConcurrentDictionary<Guid, HashSet<Guid>> _sessionJobs = new();
    private readonly SemaphoreSlim _concurrencySemaphore = new(2, 2);
    private readonly CancellationTokenSource _serviceCts = new();
    private readonly Task _backgroundProcessorTask;

    public DefectFeatureAnalyticsService(
        IServiceScopeFactory scopeFactory,
        ILogger<DefectFeatureAnalyticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _jobChannel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        _backgroundProcessorTask = Task.Run(ProcessJobsLoopAsync);
    }

    public async Task<FeatureSchemaReadinessResponse?> GetSchemaAndReadinessAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var session = await db.DefectPreviewSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        var totalRecords = session.TotalRowsCount > 0 ? session.TotalRowsCount : session.LoadedRowsCount;
        if (totalRecords == 0)
        {
            totalRecords = await db.DefectPreviewRecords
                .AsNoTracking()
                .Where(r => r.SessionId == sessionId)
                .CountAsync(cancellationToken);
        }

        var isNgOnly = session.NgCount > 0 && session.OkCount == 0;

        // Fetch feature statistics
        var featureStats = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId)
            .GroupBy(f => f.FeatureName)
            .Select(g => new
            {
                Name = g.Key,
                Count = g.Count(),
                Min = g.Min(x => x.FeatureValue),
                Max = g.Max(x => x.FeatureValue),
                Sum = g.Sum(x => x.FeatureValue),
                SumSq = g.Sum(x => x.FeatureValue * x.FeatureValue)
            })
            .ToListAsync(cancellationToken);

        var summaries = new List<FeatureSummaryDto>(featureStats.Count);
        foreach (var stat in featureStats)
        {
            var count = stat.Count;
            var missingCount = Math.Max(0, totalRecords - count);
            var missingRatio = totalRecords > 0 ? (double)missingCount / totalRecords : 0.0;
            var mean = count > 0 ? stat.Sum / count : 0.0;

            var variance = count > 1 ? (stat.SumSq - (stat.Sum * stat.Sum / count)) / (count - 1) : 0.0;
            if (variance < 0) variance = 0.0;
            var stdDev = Math.Sqrt(variance);

            summaries.Add(new FeatureSummaryDto(
                stat.Name,
                count,
                missingCount,
                missingRatio,
                stat.Min,
                stat.Max,
                mean,
                stdDev));
        }

        // Check availability of metadata fields for MSA / DOE / TimeSeries
        var sampleRecords = await db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .Take(100)
            .Select(r => new { r.ComponentName, r.EqpName, r.EmNo, r.InspectedAtUtc })
            .ToListAsync(cancellationToken);

        var hasComponentNames = sampleRecords.Any(r => !string.IsNullOrWhiteSpace(r.ComponentName));
        var hasEqpNames = sampleRecords.Any(r => !string.IsNullOrWhiteSpace(r.EqpName));
        var hasTimestamps = sampleRecords.Any(r => r.InspectedAtUtc.HasValue);

        var readiness = EvaluateReadiness(summaries, totalRecords, isNgOnly, hasComponentNames, hasEqpNames, hasTimestamps);

        return new FeatureSchemaReadinessResponse(
            sessionId,
            totalRecords,
            isNgOnly,
            summaries.OrderBy(s => s.Name).ToList(),
            readiness
        );
    }

    private static Dictionary<string, AnalysisReadinessDto> EvaluateReadiness(
        List<FeatureSummaryDto> features,
        int totalRecords,
        bool isNgOnly,
        bool hasComponentNames,
        bool hasEqpNames,
        bool hasTimestamps = true)
    {
        var result = new Dictionary<string, AnalysisReadinessDto>(StringComparer.OrdinalIgnoreCase);
        // Diagnostic log
        // Evaluated features count: {features.Count}, TotalRecords: {totalRecords}, IsNgOnly: {isNgOnly}

        // 1. SPC Readiness
        var spcBlockers = new List<string>();
        var spcWarnings = new List<string>();
        var spcRecs = new List<string>();

        if (features.Count == 0)
        {
            spcBlockers.Add("No numeric features found in dataset.");
        }
        else if (totalRecords < 2)
        {
            spcBlockers.Add($"Insufficient sample size (total records = {totalRecords}). SPC requires at least 2 observations.");
        }

        if (totalRecords is >= 2 and < 20)
        {
            spcWarnings.Add($"Sample size is small ({totalRecords} observations). ASTM recommends >= 20 observations for stable control limits.");
        }

        if (isNgOnly)
        {
            spcWarnings.Add("Dataset consists exclusively of NG defects. Normal distribution assumption is violated due to tail truncation; process capability (Cp/Cpk) is invalid.");
        }

        foreach (var f in features.Where(f => f.MissingRatio < 0.20 && f.StdDev > 1e-9))
        {
            spcRecs.Add(f.Name);
        }

        result["SPC"] = new AnalysisReadinessDto(
            "SPC",
            spcBlockers.Count == 0,
            spcBlockers.Count == 0 ? (isNgOnly ? 0.7 : 1.0) : 0.0,
            spcRecs,
            spcBlockers,
            spcWarnings
        );

        // 2. EWMA Readiness
        var ewmaBlockers = new List<string>();
        var ewmaWarnings = new List<string>();
        if (features.Count == 0) ewmaBlockers.Add("No numeric features found.");
        if (totalRecords < 10) ewmaBlockers.Add($"Sample size ({totalRecords}) too small for time-series EWMA drift detection (minimum 10).");
        if (!hasTimestamps) ewmaWarnings.Add("No InspectedAtUtc timestamps detected in records. Time-series sequence defaults to ingestion row index.");
        if (totalRecords is >= 10 and < 30) ewmaWarnings.Add("Recommended sample size for EWMA is >= 30 observations.");

        result["EWMA"] = new AnalysisReadinessDto(
            "EWMA",
            ewmaBlockers.Count == 0,
            ewmaBlockers.Count == 0 ? 1.0 : 0.0,
            spcRecs,
            ewmaBlockers,
            ewmaWarnings
        );

        // 3. CUSUM Readiness
        var cusumBlockers = new List<string>();
        var cusumWarnings = new List<string>();
        if (features.Count == 0) cusumBlockers.Add("No numeric features found.");
        if (totalRecords < 15) cusumBlockers.Add($"Sample size ({totalRecords}) too small for CUSUM shift detection (minimum 15).");
        if (!hasTimestamps) cusumWarnings.Add("No InspectedAtUtc timestamps detected in records. Time-series sequence defaults to ingestion row index.");

        result["CUSUM"] = new AnalysisReadinessDto(
            "CUSUM",
            cusumBlockers.Count == 0,
            cusumBlockers.Count == 0 ? 1.0 : 0.0,
            spcRecs,
            cusumBlockers,
            cusumWarnings
        );

        // 4. PCA / Hotelling T2 Readiness
        var pcaBlockers = new List<string>();
        var pcaWarnings = new List<string>();
        var nonZeroFeatures = features.Where(f => f.StdDev > 1e-9 && f.MissingRatio < 0.25).Select(f => f.Name).ToList();

        if (nonZeroFeatures.Count < 2)
        {
            pcaBlockers.Add($"PCA requires at least 2 distinct numeric features with non-zero variance (found {nonZeroFeatures.Count}).");
        }

        if (totalRecords < nonZeroFeatures.Count)
        {
            pcaBlockers.Add($"Sample size ({totalRecords}) must be greater than number of features ({nonZeroFeatures.Count}).");
        }

        if (totalRecords < nonZeroFeatures.Count * 3)
        {
            pcaWarnings.Add($"Sample size ({totalRecords}) is less than 3x number of features ({nonZeroFeatures.Count}). Stability of covariance estimates may be compromised.");
        }

        result["PCA"] = new AnalysisReadinessDto(
            "PCA",
            pcaBlockers.Count == 0,
            pcaBlockers.Count == 0 ? 1.0 : 0.0,
            nonZeroFeatures,
            pcaBlockers,
            pcaWarnings
        );

        // 5. MSA (Gage R&R) Readiness
        var msaBlockers = new List<string>();
        var msaWarnings = new List<string>();
        if (features.Count == 0) msaBlockers.Add("No numeric feature available as gage measurement.");
        if (!hasComponentNames) msaWarnings.Add("No ComponentName values detected in records. A discrete feature must be mapped to 'Part'.");
        if (!hasEqpNames) msaWarnings.Add("No EqpName/EmNo detected. A discrete feature or equipment identifier must be mapped to 'Operator'.");

        result["MSA"] = new AnalysisReadinessDto(
            "MSA",
            features.Count > 0,
            hasComponentNames && hasEqpNames ? 0.9 : 0.5,
            spcRecs,
            msaBlockers,
            msaWarnings
        );

        // 6. DOE Readiness
        var doeBlockers = new List<string>();
        var doeWarnings = new List<string>();
        if (features.Count < 2) doeBlockers.Add("DOE requires at least 1 response variable and 1 factor feature.");
        if (totalRecords < 4) doeBlockers.Add($"DOE requires at least 4 runs (found {totalRecords}).");

        result["DOE"] = new AnalysisReadinessDto(
            "DOE",
            doeBlockers.Count == 0,
            doeBlockers.Count == 0 ? 0.9 : 0.0,
            spcRecs,
            doeBlockers,
            doeWarnings
        );

        // 7. Feature Relationship Readiness
        var relBlockers = new List<string>();
        var relWarnings = new List<string>();
        if (features.Count < 2) relBlockers.Add($"Feature Relationship requires at least 2 features (found {features.Count}).");
        if (totalRecords < 3) relBlockers.Add($"Feature Relationship requires at least 3 samples (found {totalRecords}).");
        if (features.Count > 50) relWarnings.Add($"Large feature set ({features.Count} features) will compute {features.Count * (features.Count - 1) / 2} pairs.");

        result["FEATURE_RELATIONSHIP"] = new AnalysisReadinessDto(
            "FEATURE_RELATIONSHIP",
            relBlockers.Count == 0,
            relBlockers.Count == 0 ? 1.0 : 0.0,
            spcRecs,
            relBlockers,
            relWarnings
        );

        // 8. K-means Readiness
        var kmBlockers = new List<string>();
        var kmWarnings = new List<string>();
        if (features.Count == 0) kmBlockers.Add("No numeric features found.");
        if (totalRecords < 3) kmBlockers.Add($"K-means requires at least 3 observations (found {totalRecords}).");
        if (totalRecords < 10) kmWarnings.Add("Small sample size (<10) may yield unrepresentative cluster centers.");

        result["KMEANS"] = new AnalysisReadinessDto(
            "KMEANS",
            kmBlockers.Count == 0,
            kmBlockers.Count == 0 ? 1.0 : 0.0,
            spcRecs,
            kmBlockers,
            kmWarnings
        );

        // 9. GMM Readiness
        var gmmBlockers = new List<string>();
        var gmmWarnings = new List<string>();
        if (features.Count == 0) gmmBlockers.Add("No numeric features found.");
        if (totalRecords < 4) gmmBlockers.Add($"GMM requires at least 4 observations (found {totalRecords}).");
        if (totalRecords < 20) gmmWarnings.Add("Recommended sample size for GMM is >= 20 observations for stable variance estimation.");

        result["GMM"] = new AnalysisReadinessDto(
            "GMM",
            gmmBlockers.Count == 0,
            gmmBlockers.Count == 0 ? 0.9 : 0.0,
            spcRecs,
            gmmBlockers,
            gmmWarnings
        );

        // 10. Isolation Forest Readiness
        var ifBlockers = new List<string>();
        var ifWarnings = new List<string>();
        if (features.Count == 0) ifBlockers.Add("No numeric features found.");
        if (totalRecords < 8) ifBlockers.Add($"Isolation Forest requires at least 8 observations (found {totalRecords}).");
        if (totalRecords < 30) ifWarnings.Add("Isolation Forest accuracy improves significantly with >= 30 observations.");

        result["ISOLATION_FOREST"] = new AnalysisReadinessDto(
            "ISOLATION_FOREST",
            ifBlockers.Count == 0,
            ifBlockers.Count == 0 ? 1.0 : 0.0,
            spcRecs,
            ifBlockers,
            ifWarnings
        );

        return result;
    }

    public async Task<SessionScopeFacetsResponse?> GetScopeFacetsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var session = await db.DefectPreviewSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        var recordsQuery = db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId);

        var totalRecords = await recordsQuery.CountAsync(cancellationToken);

        var rawLineFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.LineCode))
            .GroupBy(r => r.LineCode!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var lineFacets = rawLineFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawMachineFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.MachineCode))
            .GroupBy(r => r.MachineCode!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var machineFacets = rawMachineFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawStationFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.StationCode))
            .GroupBy(r => r.StationCode!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var stationFacets = rawStationFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawRuleFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.ClassifierRuleCode))
            .GroupBy(r => r.ClassifierRuleCode!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var ruleFacets = rawRuleFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawVersionFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.ClassifierRuleVersion))
            .GroupBy(r => r.ClassifierRuleVersion!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var versionFacets = rawVersionFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawFamilyFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.DefectFamily))
            .GroupBy(r => r.DefectFamily!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var familyFacets = rawFamilyFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawClassFacets = await recordsQuery
            .Where(r => !string.IsNullOrEmpty(r.DefectClass))
            .GroupBy(r => r.DefectClass!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var classFacets = rawClassFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        var rawFeatureFacets = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId)
            .GroupBy(f => f.FeatureName)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var featureFacets = rawFeatureFacets.OrderBy(x => x.Key).Select(x => new FacetItem(x.Key, x.Count)).ToList();

        return new SessionScopeFacetsResponse(
            sessionId,
            totalRecords,
            lineFacets,
            machineFacets,
            stationFacets,
            ruleFacets,
            versionFacets,
            familyFacets,
            classFacets,
            featureFacets
        );
    }

    private static string ComputeRequestHash(Guid sessionId, string analysisType, CreateAnalyticsJobRequest req)
    {
        var scope = req.ScopeFilter;
        var featuresSorted = (req.FeatureNames ?? (req.FeatureName != null ? [req.FeatureName] : []))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        var featuresStr = string.Join(",", featuresSorted);
        var scopeStr = scope != null
            ? $"{scope.ProductKey}:{scope.LineCode}:{scope.MachineCode}:{scope.StationCode}:{scope.ClassifierRuleCode}:{scope.ClassifierRuleVersion}:{scope.DefectFamily}:{scope.DefectClass ?? scope.DefectType}:{scope.FromUtc?.ToUnixTimeSeconds() ?? scope.TimeFromUtc?.ToUnixTimeSeconds()}:{scope.ToUtc?.ToUnixTimeSeconds() ?? scope.TimeToUtc?.ToUnixTimeSeconds()}"
            : $"{req.ProductKey}:{req.LineCode}:{req.MachineCode}:{req.StationCode}:{req.ClassifierRuleCode}:{req.ClassifierRuleVersion}:{req.DefectFamily}:{req.DefectType}:{req.TimeFromUtc?.ToUnixTimeSeconds()}:{req.TimeToUtc?.ToUnixTimeSeconds()}";
        var algoParamsStr = $"{req.K}:{req.ComponentsCount}:{req.CovarianceType}:{req.TreesCount}:{req.SubsampleSize}:{req.Contamination}:{req.Seed}:{req.MissingValuePolicy}:{req.ScalingPolicy}:{req.MaxScatterPoints}:{req.AllowRuleVersionMixing}:{req.ScriptText ?? ""}";
        var payload = $"{sessionId}:{analysisType}:{featuresStr}:{req.PartField}:{req.OperatorField}:{req.ResponseFeature}:{string.Join(",", req.FactorFeatures ?? [])}:{scopeStr}:{algoParamsStr}:{JsonSerializer.Serialize(req.Parameters)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public async Task<AnalyticsJobDto> CreateJobAsync(
        Guid sessionId,
        CreateAnalyticsJobRequest request,
        string username = "system",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var session = await db.DefectPreviewSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new KeyNotFoundException($"Defect preview session {sessionId} was not found.");
        }

        var normalizedType = request.AnalysisType?.Trim().ToUpperInvariant() ?? "SPC";
        var ttl = Math.Clamp(request.TtlMinutes, 5, 1440);
        var maxDataPoints = Math.Clamp(request.MaxDataPoints, 10, 50000);
        request.MaxDataPoints = maxDataPoints;
        request.TtlMinutes = ttl;

        // Hash request parameters for caching
        var requestHash = ComputeRequestHash(sessionId, normalizedType, request);

        // Check if an existing completed job is still valid
        var existingJob = await db.DefectAnalyticsJobs
            .AsNoTracking()
            .Where(j => j.SessionId == sessionId && j.RequestHash == requestHash)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingJob != null && existingJob.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            if (existingJob.Status is "Completed" or "Queued" or "Computing")
            {
                _logger.LogInformation("Reusing existing analytics job {JobId} for session {SessionId} (Status: {Status})",
                    existingJob.Id, sessionId, existingJob.Status);
                return MapJobEntityToDto(existingJob);
            }
        }

        var jobEntity = new DefectAnalyticsJobEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            AnalysisType = normalizedType,
            Status = "Queued",
            ProgressPercent = 0,
            RequestHash = requestHash,
            ConfigurationJson = JsonSerializer.Serialize(request),
            TtlMinutes = ttl,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(ttl),
            CreatedBy = string.IsNullOrWhiteSpace(username) ? "system" : username
        };

        db.DefectAnalyticsJobs.Add(jobEntity);
        await db.SaveChangesAsync(cancellationToken);

        _sessionJobs.AddOrUpdate(
            sessionId,
            _ => new HashSet<Guid> { jobEntity.Id },
            (_, set) => { lock (set) { set.Add(jobEntity.Id); return set; } });

        await _jobChannel.Writer.WriteAsync(jobEntity.Id, cancellationToken);

        return MapJobEntityToDto(jobEntity);
    }

    public async Task<AnalyticsJobDto?> GetJobStatusAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var job = await db.DefectAnalyticsJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.SessionId == sessionId && j.Id == jobId, cancellationToken);

        return job == null ? null : MapJobEntityToDto(job);
    }

    public async Task<AnalyticsJobDto?> GetJobResultAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var job = await db.DefectAnalyticsJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.SessionId == sessionId && j.Id == jobId, cancellationToken);

        return job == null ? null : MapJobEntityToDto(job, includeResult: true);
    }

    public async Task<List<AnalyticsJobDto>> ListJobsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var jobs = await db.DefectAnalyticsJobs
            .AsNoTracking()
            .Where(j => j.SessionId == sessionId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return jobs.Select(j => MapJobEntityToDto(j)).ToList();
    }

    public async Task<bool> CancelJobAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_activeJobTokens.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var job = await db.DefectAnalyticsJobs
            .FirstOrDefaultAsync(j => j.SessionId == sessionId && j.Id == jobId, cancellationToken);

        if (job == null) return false;

        if (job.Status is "Queued" or "Computing")
        {
            job.Status = "Cancelled";
            job.ErrorMessage = "Cancelled by user.";
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> DeleteJobAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await CancelJobAsync(sessionId, jobId, cancellationToken);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var job = await db.DefectAnalyticsJobs
            .FirstOrDefaultAsync(j => j.SessionId == sessionId && j.Id == jobId, cancellationToken);

        if (job == null) return false;

        db.DefectAnalyticsJobs.Remove(job);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public void CancelJobsForSession(Guid sessionId)
    {
        if (_sessionJobs.TryRemove(sessionId, out var jobIds))
        {
            lock (jobIds)
            {
                foreach (var jobId in jobIds)
                {
                    if (_activeJobTokens.TryRemove(jobId, out var cts))
                    {
                        try { cts.Cancel(); cts.Dispose(); } catch { }
                    }
                }
            }
        }
    }

    private async Task ProcessJobsLoopAsync()
    {
        // Startup recovery: handle stranded jobs
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

            var strandedComputing = await db.DefectAnalyticsJobs
                .Where(j => j.Status == "Computing")
                .ToListAsync(_serviceCts.Token);

            if (strandedComputing.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var job in strandedComputing)
                {
                    job.Status = "Failed";
                    job.ErrorMessage = "Service was restarted while job was computing.";
                    job.CompletedAtUtc = now;
                }
                await db.SaveChangesAsync(_serviceCts.Token);
                _logger.LogWarning("Marked {Count} stranded computing analytics jobs as Failed on startup", strandedComputing.Count);
            }

            var queuedJobs = await db.DefectAnalyticsJobs
                .Where(j => j.Status == "Queued")
                .OrderBy(j => j.CreatedAtUtc)
                .Select(j => j.Id)
                .ToListAsync(_serviceCts.Token);

            foreach (var qId in queuedJobs)
            {
                _jobChannel.Writer.TryWrite(qId);
            }

            if (queuedJobs.Count > 0)
            {
                _logger.LogInformation("Re-enqueued {Count} queued analytics jobs on startup", queuedJobs.Count);
            }
        }
        catch (OperationCanceledException) when (_serviceCts.Token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during analytics startup job recovery");
        }

        while (!_serviceCts.Token.IsCancellationRequested)
        {
            try
            {
                var jobId = await _jobChannel.Reader.ReadAsync(_serviceCts.Token);
                await _concurrencySemaphore.WaitAsync(_serviceCts.Token);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteJobAsync(jobId);
                    }
                    finally
                    {
                        _concurrencySemaphore.Release();
                    }
                });
            }
            catch (OperationCanceledException) when (_serviceCts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in analytics job processor loop");
            }
        }
    }

    private async Task ExecuteJobAsync(Guid jobId)
    {
        using var cts = new CancellationTokenSource();
        _activeJobTokens[jobId] = cts;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

            var job = await db.DefectAnalyticsJobs
                .FirstOrDefaultAsync(j => j.Id == jobId, cts.Token);

            if (job == null || job.Status is "Cancelled" or "Completed")
            {
                return;
            }

            job.Status = "Computing";
            job.StartedAtUtc = DateTimeOffset.UtcNow;
            job.ProgressPercent = 15;
            await db.SaveChangesAsync(cts.Token);

            var session = await db.DefectPreviewSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == job.SessionId, cts.Token);

            if (session == null)
            {
                job.Status = "Failed";
                job.ErrorMessage = "Parent defect preview session no longer exists.";
                job.CompletedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            var request = JsonSerializer.Deserialize<CreateAnalyticsJobRequest>(job.ConfigurationJson)
                ?? throw new InvalidOperationException("Failed to deserialize job configuration.");

            var isNgOnly = session.NgCount > 0 && session.OkCount == 0;
            object resultData;
            List<string> warnings = new();

            switch (job.AnalysisType.ToUpperInvariant())
            {
                case "SPC":
                    resultData = await ExecuteSpcJobAsync(db, job.SessionId, request, isNgOnly, cts.Token);
                    break;
                case "EWMA":
                    resultData = await ExecuteEwmaJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "CUSUM":
                    resultData = await ExecuteCusumJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "PCA":
                    resultData = await ExecutePcaJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "MSA":
                    resultData = await ExecuteMsaJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "DOE":
                    resultData = await ExecuteDoeJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "FEATURE_RELATIONSHIP":
                    resultData = await ExecuteFeatureRelationshipJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "KMEANS":
                    resultData = await ExecuteKMeansJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "GMM":
                    resultData = await ExecuteGmmJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "ISOLATION_FOREST":
                    resultData = await ExecuteIsolationForestJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                case "SCRIPT_CONDITION":
                    resultData = await ExecuteScriptConditionJobAsync(db, job.SessionId, request, cts.Token);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported analysis type '{job.AnalysisType}'.");
            }

            job.ProgressPercent = 90;
            await db.SaveChangesAsync(cts.Token);

            job.ResultJson = JsonSerializer.Serialize(resultData);
            job.WarningsJson = warnings.Count > 0 ? JsonSerializer.Serialize(warnings) : null;
            job.Status = "Completed";
            job.ProgressPercent = 100;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.ErrorMessage = null;

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Job {JobId} was cancelled during execution", jobId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
                var job = await db.DefectAnalyticsJobs.FirstOrDefaultAsync(j => j.Id == jobId);
                if (job != null && job.Status != "Cancelled")
                {
                    job.Status = "Cancelled";
                    job.ErrorMessage = "Job cancelled.";
                    job.CompletedAtUtc = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute analytics job {JobId}", jobId);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
                var job = await db.DefectAnalyticsJobs.FirstOrDefaultAsync(j => j.Id == jobId);
                if (job != null)
                {
                    job.Status = "Failed";
                    job.ErrorMessage = ex.Message;
                    job.CompletedAtUtc = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch { }
        }
        finally
        {
            _activeJobTokens.TryRemove(jobId, out _);
        }
    }

    private static async Task<SpcResult> ExecuteSpcJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        bool isNgOnly,
        CancellationToken ct)
    {
        var featName = req.FeatureName ?? req.FeatureNames?.FirstOrDefault()
            ?? throw new ArgumentException("A target featureName must be specified for SPC analysis.");

        // Check scope filtering and rule-version mixing
        var recordsQuery = db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId);

        var scope = req.ScopeFilter;
        var lineFilter = scope?.LineCode ?? req.LineCode;
        var machineFilter = scope?.MachineCode ?? req.MachineCode;
        var stationFilter = scope?.StationCode ?? req.StationCode;
        var ruleCodeFilter = scope?.ClassifierRuleCode ?? req.ClassifierRuleCode;
        var ruleVersionFilter = scope?.ClassifierRuleVersion ?? req.ClassifierRuleVersion;
        var familyFilter = scope?.DefectFamily ?? req.DefectFamily;
        var classFilter = scope?.DefectClass ?? scope?.DefectType ?? req.DefectType;
        var fromUtc = scope?.FromUtc ?? scope?.TimeFromUtc ?? req.TimeFromUtc;
        var toUtc = scope?.ToUtc ?? scope?.TimeToUtc ?? req.TimeToUtc;

        if (!string.IsNullOrWhiteSpace(lineFilter))
            recordsQuery = recordsQuery.Where(r => r.LineCode == lineFilter);
        if (!string.IsNullOrWhiteSpace(machineFilter))
            recordsQuery = recordsQuery.Where(r => r.MachineCode == machineFilter);
        if (!string.IsNullOrWhiteSpace(stationFilter))
            recordsQuery = recordsQuery.Where(r => r.StationCode == stationFilter);
        if (!string.IsNullOrWhiteSpace(ruleCodeFilter))
            recordsQuery = recordsQuery.Where(r => r.ClassifierRuleCode == ruleCodeFilter);
        if (!string.IsNullOrWhiteSpace(ruleVersionFilter))
            recordsQuery = recordsQuery.Where(r => r.ClassifierRuleVersion == ruleVersionFilter);
        if (!string.IsNullOrWhiteSpace(familyFilter))
            recordsQuery = recordsQuery.Where(r => r.DefectFamily == familyFilter);
        if (!string.IsNullOrWhiteSpace(classFilter))
            recordsQuery = recordsQuery.Where(r => r.DefectClass == classFilter);
        if (fromUtc.HasValue)
            recordsQuery = recordsQuery.Where(r => r.InspectedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            recordsQuery = recordsQuery.Where(r => r.InspectedAtUtc <= toUtc.Value);

        var matchedRecords = await recordsQuery
            .OrderBy(r => r.InspectedAtUtc)
            .ThenBy(r => r.RowIndex)
            .Select(r => new
            {
                r.RowIndex,
                r.ObservationId,
                r.LineCode,
                r.MachineCode,
                r.StationCode,
                r.ClassifierRuleCode,
                r.ClassifierRuleVersion,
                r.DefectFamily,
                r.DefectClass,
                r.ComponentName,
                r.EqpName
            })
            .Take(req.MaxDataPoints)
            .ToListAsync(ct);

        if (matchedRecords.Count < 2)
        {
            throw new InvalidOperationException($"Insufficient data points matching scope for SPC analysis (found {matchedRecords.Count}, required >= 2).");
        }

        // Rule version mixing safeguard
        var distinctVersions = matchedRecords
            .Where(r => !string.IsNullOrEmpty(r.ClassifierRuleVersion))
            .Select(r => r.ClassifierRuleVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctVersions.Count > 1 && req.AllowRuleVersionMixing != true)
        {
            throw new InvalidOperationException(
                $"Mixed classifier rule versions detected in SPC sample (versions: [{string.Join(", ", distinctVersions)}]). SPC requires homogeneous classification thresholds or explicit allow_rule_version_mixing option.");
        }

        var matchedRowIndices = matchedRecords.Select(r => r.RowIndex).ToHashSet();
        var recordMap = matchedRecords.ToDictionary(r => r.RowIndex);

        var data = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && f.FeatureName == featName && matchedRowIndices.Contains(f.RowIndex))
            .OrderBy(f => f.RowIndex)
            .Select(f => new { f.RowIndex, f.FeatureValue })
            .ToListAsync(ct);

        if (data.Count < 2)
        {
            throw new InvalidOperationException($"Insufficient data points for feature '{featName}' within scope (found {data.Count}).");
        }

        var subgroupIdField = req.SubgroupIdField;
        var inputTuples = data.Select(d =>
        {
            string? subId = null;
            ObservationReference? obsRef = null;
            if (recordMap.TryGetValue(d.RowIndex, out var rec))
            {
                obsRef = new ObservationReference(
                    rec.ObservationId,
                    rec.RowIndex,
                    rec.LineCode,
                    rec.MachineCode,
                    rec.StationCode,
                    rec.ClassifierRuleCode,
                    rec.ClassifierRuleVersion,
                    rec.DefectFamily,
                    rec.DefectClass
                );
                if (!string.IsNullOrWhiteSpace(subgroupIdField))
                {
                    subId = rec.ComponentName ?? rec.EqpName ?? "G0";
                }
            }
            return (d.RowIndex, d.FeatureValue, subId, obsRef);
        }).ToList();

        var chartType = GetParamString(req.Parameters, "ChartType") ?? "IMR";
        var lsl = GetParamDouble(req.Parameters, "Lsl");
        var usl = GetParamDouble(req.Parameters, "Usl");
        var target = GetParamDouble(req.Parameters, "Target");

        return SpcEngine.Compute(inputTuples, featName, chartType, lsl, usl, target, isNgOnly);
    }

    private static async Task<EwmaResult> ExecuteEwmaJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var featName = req.FeatureName ?? req.FeatureNames?.FirstOrDefault()
            ?? throw new ArgumentException("A target featureName must be specified for EWMA analysis.");

        var data = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && f.FeatureName == featName)
            .OrderBy(f => f.RowIndex)
            .Take(req.MaxDataPoints)
            .Select(f => ValueTuple.Create(f.RowIndex, f.FeatureValue))
            .ToListAsync(ct);

        if (data.Count < 2)
        {
            throw new InvalidOperationException($"Insufficient data points for feature '{featName}' (found {data.Count}).");
        }

        var lambda = GetParamDouble(req.Parameters, "Lambda") ?? 0.2;
        var lFactor = GetParamDouble(req.Parameters, "LFactor") ?? 3.0;
        var target = GetParamDouble(req.Parameters, "Target");
        var sigma = GetParamDouble(req.Parameters, "Sigma");

        return TimeSeriesEngine.ComputeEwma(data, featName, lambda, lFactor, target, sigma);
    }

    private static async Task<CusumResult> ExecuteCusumJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var featName = req.FeatureName ?? req.FeatureNames?.FirstOrDefault()
            ?? throw new ArgumentException("A target featureName must be specified for CUSUM analysis.");

        var data = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && f.FeatureName == featName)
            .OrderBy(f => f.RowIndex)
            .Take(req.MaxDataPoints)
            .Select(f => ValueTuple.Create(f.RowIndex, f.FeatureValue))
            .ToListAsync(ct);

        if (data.Count < 2)
        {
            throw new InvalidOperationException($"Insufficient data points for feature '{featName}' (found {data.Count}).");
        }

        var target = GetParamDouble(req.Parameters, "Target");
        var sigma = GetParamDouble(req.Parameters, "Sigma");
        var kParam = GetParamDouble(req.Parameters, "KParam") ?? 0.5;
        var hParam = GetParamDouble(req.Parameters, "HParam") ?? 5.0;

        return TimeSeriesEngine.ComputeCusum(data, featName, target, sigma, kParam, hParam);
    }

    private static async Task<PcaResult> ExecutePcaJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var featNames = req.FeatureNames;
        if (featNames == null || featNames.Count < 2)
        {
            // Auto-detect top numeric features
            featNames = await db.DefectPreviewFeatures
                .AsNoTracking()
                .Where(f => f.SessionId == sessionId)
                .GroupBy(f => f.FeatureName)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(10)
                .ToListAsync(ct);
        }

        if (featNames.Count < 2)
        {
            throw new InvalidOperationException("At least 2 numeric features are required for PCA analysis.");
        }

        // Fetch features
        var featureRows = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && featNames.Contains(f.FeatureName))
            .Select(f => new { f.RowIndex, f.FeatureName, f.FeatureValue })
            .ToListAsync(ct);

        var groupedByRow = featureRows
            .GroupBy(r => r.RowIndex)
            .Where(g => g.Count() >= featNames.Count) // Only rows with all features present
            .Take(req.MaxDataPoints)
            .ToList();

        var rows = new List<(int RowIndex, double[] Values)>(groupedByRow.Count);
        foreach (var grp in groupedByRow)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in grp)
            {
                dict[item.FeatureName] = item.FeatureValue;
            }
            var vals = new double[featNames.Count];
            for (var j = 0; j < featNames.Count; j++)
            {
                vals[j] = dict.GetValueOrDefault(featNames[j], 0.0);
            }
            rows.Add((grp.Key, vals));
        }

        if (rows.Count < 3)
        {
            throw new InvalidOperationException($"Insufficient complete rows with all selected features present (found {rows.Count}, required >= 3).");
        }

        var numComponents = (int?)GetParamDouble(req.Parameters, "NumComponents");
        var confLevel = GetParamDouble(req.Parameters, "ConfidenceLevel") ?? 0.95;

        return PcaEngine.Compute(rows, featNames, numComponents, confLevel);
    }

    private static async Task<MsaResult> ExecuteMsaJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var featName = req.FeatureName ?? req.FeatureNames?.FirstOrDefault()
            ?? throw new ArgumentException("A target feature measurement must be specified for MSA (Gage R&R).");

        // Determine Part and Operator source
        var partField = string.IsNullOrWhiteSpace(req.PartField) ? "ComponentName" : req.PartField;
        var operField = string.IsNullOrWhiteSpace(req.OperatorField) ? "EqpName" : req.OperatorField;

        var features = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && f.FeatureName == featName)
            .Select(f => new { f.RowIndex, f.FeatureValue })
            .ToListAsync(ct);

        var records = await db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .Select(r => new { r.RowIndex, r.ComponentName, r.EqpName, r.EmNo, r.KeyId })
            .ToListAsync(ct);

        var recDict = records.ToDictionary(r => r.RowIndex);

        var msaData = new List<(string PartId, string OperatorId, double Value)>(features.Count);
        foreach (var f in features)
        {
            if (recDict.TryGetValue(f.RowIndex, out var rec))
            {
                var partVal = partField switch
                {
                    "KeyId" => rec.KeyId,
                    "EmNo" => rec.EmNo,
                    _ => rec.ComponentName
                } ?? "P0";

                var operVal = operField switch
                {
                    "EmNo" => rec.EmNo,
                    "KeyId" => rec.KeyId,
                    _ => rec.EqpName
                } ?? "Op0";

                msaData.Add((partVal, operVal, f.FeatureValue));
            }
        }

        var tol = GetParamDouble(req.Parameters, "Tolerance");
        return MsaEngine.Compute(msaData, featName, tol);
    }

    private static async Task<DoeResult> ExecuteDoeJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var responseFeature = req.ResponseFeature ?? req.FeatureName
            ?? throw new ArgumentException("A response variable must be specified for DOE.");

        var factorNames = req.FactorFeatures;
        if (factorNames == null || factorNames.Count == 0)
        {
            throw new ArgumentException("At least one factor feature must be specified for DOE.");
        }

        var allFeaturesNeeded = new HashSet<string>(factorNames) { responseFeature };

        var features = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && allFeaturesNeeded.Contains(f.FeatureName))
            .Select(f => new { f.RowIndex, f.FeatureName, f.FeatureValue })
            .ToListAsync(ct);

        var grouped = features
            .GroupBy(f => f.RowIndex)
            .Where(g => g.Any(x => x.FeatureName == responseFeature))
            .ToList();

        var doeData = new List<(int RowIndex, double Response, Dictionary<string, double> Factors)>(grouped.Count);
        foreach (var g in grouped)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in g)
            {
                dict[item.FeatureName] = item.FeatureValue;
            }
            if (!dict.TryGetValue(responseFeature, out var resp))
            {
                continue;
            }

            var factorVals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var fn in factorNames)
            {
                if (dict.TryGetValue(fn, out var fv))
                {
                    factorVals[fn] = fv;
                }
            }

            if (factorVals.Count == factorNames.Count)
            {
                doeData.Add((g.Key, resp, factorVals));
            }
        }

        var incInteractions = GetParamBool(req.Parameters, "IncludeInteractions") ?? true;
        return DoeEngine.Compute(doeData, responseFeature, factorNames, incInteractions);
    }

    private static double? GetParamDouble(Dictionary<string, object>? dict, string key)
    {
        if (dict == null) return null;
        foreach (var kvp in dict)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value is JsonElement je && je.TryGetDouble(out var d)) return d;
                if (double.TryParse(kvp.Value?.ToString(), out var parsed)) return parsed;
            }
        }
        return null;
    }

    private static string? GetParamString(Dictionary<string, object>? dict, string key)
    {
        if (dict == null) return null;
        foreach (var kvp in dict)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value?.ToString();
            }
        }
        return null;
    }

    private static bool? GetParamBool(Dictionary<string, object>? dict, string key)
    {
        if (dict == null) return null;
        foreach (var kvp in dict)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value is JsonElement je && (je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False))
                    return je.GetBoolean();
                if (bool.TryParse(kvp.Value?.ToString(), out var b)) return b;
            }
        }
        return null;
    }

    private sealed record PreparedObservation(
        int RowIndex,
        double?[] RawValues,
        double[] PreparedValues,
        ObservationReference Observation
    );

    private static async Task<(List<string> Features, List<PreparedObservation> Observations, List<string> Warnings)> PrepareDatasetAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var featNames = req.FeatureNames?.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (featNames == null || featNames.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(req.FeatureName))
            {
                featNames = [req.FeatureName];
            }
            else
            {
                featNames = await db.DefectPreviewFeatures
                    .AsNoTracking()
                    .Where(f => f.SessionId == sessionId)
                    .GroupBy(f => f.FeatureName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .Take(10)
                    .ToListAsync(ct);
            }
        }

        if (featNames.Count == 0)
        {
            throw new InvalidOperationException("No features specified or found in preview session.");
        }

        var recordsQuery = db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId);

        var scope = req.ScopeFilter;
        var lineFilter = scope?.LineCode ?? req.LineCode;
        var machineFilter = scope?.MachineCode ?? req.MachineCode;
        var stationFilter = scope?.StationCode ?? req.StationCode;
        var ruleCodeFilter = scope?.ClassifierRuleCode ?? req.ClassifierRuleCode;
        var ruleVersionFilter = scope?.ClassifierRuleVersion ?? req.ClassifierRuleVersion;
        var familyFilter = scope?.DefectFamily ?? req.DefectFamily;
        var classFilter = scope?.DefectClass ?? scope?.DefectType ?? req.DefectType;
        var fromUtc = scope?.FromUtc ?? scope?.TimeFromUtc ?? req.TimeFromUtc;
        var toUtc = scope?.ToUtc ?? scope?.TimeToUtc ?? req.TimeToUtc;

        if (!string.IsNullOrWhiteSpace(lineFilter))
            recordsQuery = recordsQuery.Where(r => r.LineCode == lineFilter);
        if (!string.IsNullOrWhiteSpace(machineFilter))
            recordsQuery = recordsQuery.Where(r => r.MachineCode == machineFilter);
        if (!string.IsNullOrWhiteSpace(stationFilter))
            recordsQuery = recordsQuery.Where(r => r.StationCode == stationFilter);
        if (!string.IsNullOrWhiteSpace(ruleCodeFilter))
            recordsQuery = recordsQuery.Where(r => r.ClassifierRuleCode == ruleCodeFilter);
        if (!string.IsNullOrWhiteSpace(ruleVersionFilter))
            recordsQuery = recordsQuery.Where(r => r.ClassifierRuleVersion == ruleVersionFilter);
        if (!string.IsNullOrWhiteSpace(familyFilter))
            recordsQuery = recordsQuery.Where(r => r.DefectFamily == familyFilter);
        if (!string.IsNullOrWhiteSpace(classFilter))
            recordsQuery = recordsQuery.Where(r => r.DefectClass == classFilter);
        if (fromUtc.HasValue)
            recordsQuery = recordsQuery.Where(r => r.InspectedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            recordsQuery = recordsQuery.Where(r => r.InspectedAtUtc <= toUtc.Value);

        var records = await recordsQuery
            .OrderBy(r => r.InspectedAtUtc)
            .ThenBy(r => r.RowIndex)
            .ThenBy(r => r.ObservationId)
            .Take(req.MaxDataPoints)
            .Select(r => new
            {
                r.RowIndex,
                r.ObservationId,
                r.LineCode,
                r.MachineCode,
                r.StationCode,
                r.ClassifierRuleCode,
                r.ClassifierRuleVersion,
                r.DefectFamily,
                r.DefectClass
            })
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            throw new InvalidOperationException("No observations match the specified scope filters.");
        }

        var rowIndices = records.Select(r => r.RowIndex).ToHashSet();

        var featureRows = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && rowIndices.Contains(f.RowIndex) && featNames.Contains(f.FeatureName))
            .OrderBy(f => f.Id)
            .Select(f => new { f.RowIndex, f.FeatureName, f.FeatureValue })
            .ToListAsync(ct);

        var featureMapByRow = new Dictionary<int, Dictionary<string, double>>();
        foreach (var fr in featureRows)
        {
            if (!featureMapByRow.TryGetValue(fr.RowIndex, out var dict))
            {
                dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                featureMapByRow[fr.RowIndex] = dict;
            }
            dict[fr.FeatureName] = fr.FeatureValue;
        }

        var rawObs = new List<(int RowIndex, double?[] Values, ObservationReference Obs)>(records.Count);
        foreach (var rec in records)
        {
            var obsRef = new ObservationReference(
                rec.ObservationId,
                rec.RowIndex,
                rec.LineCode,
                rec.MachineCode,
                rec.StationCode,
                rec.ClassifierRuleCode,
                rec.ClassifierRuleVersion,
                rec.DefectFamily,
                rec.DefectClass
            );

            featureMapByRow.TryGetValue(rec.RowIndex, out var fDict);
            var vals = new double?[featNames.Count];
            for (var j = 0; j < featNames.Count; j++)
            {
                if (fDict != null && fDict.TryGetValue(featNames[j], out var v))
                {
                    vals[j] = v;
                }
                else
                {
                    vals[j] = null;
                }
            }
            rawObs.Add((rec.RowIndex, vals, obsRef));
        }

        var policy = (req.MissingValuePolicy ?? "drop_row").Trim().ToLowerInvariant();
        var p = featNames.Count;

        var featureMeans = new double[p];
        for (var j = 0; j < p; j++)
        {
            var sum = 0.0;
            var cnt = 0;
            for (var i = 0; i < rawObs.Count; i++)
            {
                var v = rawObs[i].Values[j];
                if (v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value))
                {
                    sum += v.Value;
                    cnt++;
                }
            }
            featureMeans[j] = cnt > 0 ? sum / cnt : 0.0;
        }

        var prepared = new List<PreparedObservation>();
        foreach (var item in rawObs)
        {
            var hasMissing = item.Values.Any(v => !v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value));
            if (hasMissing && (policy is "drop_row" or "completecase" or "rejectmissing"))
            {
                continue;
            }

            var cleanVals = new double[p];
            for (var j = 0; j < p; j++)
            {
                var v = item.Values[j];
                if (v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value))
                {
                    cleanVals[j] = v.Value;
                }
                else
                {
                    cleanVals[j] = policy == "zero_impute" ? 0.0 : featureMeans[j];
                }
            }

            prepared.Add(new PreparedObservation(item.RowIndex, item.Values, cleanVals, item.Obs));
        }

        if (prepared.Count < 2 && (policy is "drop_row" or "completecase" or "rejectmissing") && rawObs.Count >= 2)
        {
            warnings.Add($"Drop-row policy eliminated {rawObs.Count - prepared.Count} rows due to missing feature values.");
        }

        return (featNames, prepared, warnings);
    }

    private static async Task<FeatureRelationshipResult> ExecuteFeatureRelationshipJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var (features, obs, _) = await PrepareDatasetAsync(db, sessionId, req, ct);
        if (features.Count < 2)
        {
            throw new InvalidOperationException("At least 2 features are required for Feature Relationship analysis.");
        }

        var maxScatter = req.MaxScatterPoints ?? 500;
        var rows = obs.Select(o => (o.RowIndex, o.RawValues, (ObservationReference?)o.Observation)).ToList();
        return FeatureRelationshipEngine.Compute(rows, features, maxScatter);
    }

    private static async Task<KMeansResult> ExecuteKMeansJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var (features, obs, _) = await PrepareDatasetAsync(db, sessionId, req, ct);
        if (obs.Count < 3)
        {
            throw new InvalidOperationException($"Insufficient complete samples for K-means clustering (found {obs.Count}, required >= 3).");
        }

        var k = req.K ?? (int?)GetParamDouble(req.Parameters, "k") ?? 3;
        var maxIter = (int?)GetParamDouble(req.Parameters, "max_iter") ?? 100;
        var nInit = (int?)GetParamDouble(req.Parameters, "n_init") ?? 5;
        var seed = req.Seed ?? (int?)GetParamDouble(req.Parameters, "seed");

        var rows = obs.Select(o => (o.RowIndex, o.PreparedValues, (ObservationReference?)o.Observation)).ToList();
        return KMeansEngine.Compute(rows, features, k, maxIter, nInit, seed);
    }

    private static async Task<GaussianMixtureResult> ExecuteGmmJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var (features, obs, _) = await PrepareDatasetAsync(db, sessionId, req, ct);
        if (obs.Count < 4)
        {
            throw new InvalidOperationException($"Insufficient complete samples for GMM (found {obs.Count}, required >= 4).");
        }

        var k = req.ComponentsCount ?? req.K ?? (int?)GetParamDouble(req.Parameters, "n_components") ?? 3;
        var covType = req.CovarianceType ?? GetParamString(req.Parameters, "covariance_type") ?? "diagonal";
        var maxIter = (int?)GetParamDouble(req.Parameters, "max_iter") ?? 100;
        var tol = GetParamDouble(req.Parameters, "tol") ?? 1e-4;
        var seed = req.Seed ?? (int?)GetParamDouble(req.Parameters, "seed");

        var rows = obs.Select(o => (o.RowIndex, o.PreparedValues, (ObservationReference?)o.Observation)).ToList();
        return GaussianMixtureEngine.Compute(rows, features, k, covType, maxIter, tol, seed);
    }

    private static async Task<IsolationForestResult> ExecuteIsolationForestJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var (features, obs, _) = await PrepareDatasetAsync(db, sessionId, req, ct);
        if (obs.Count < 8)
        {
            throw new InvalidOperationException($"Insufficient complete samples for Isolation Forest (found {obs.Count}, required >= 8).");
        }

        var nTrees = req.TreesCount ?? (int?)GetParamDouble(req.Parameters, "n_estimators") ?? 100;
        var maxSamples = req.SubsampleSize ?? (int?)GetParamDouble(req.Parameters, "max_samples");
        var contam = req.Contamination ?? GetParamDouble(req.Parameters, "contamination") ?? 0.05;
        var seed = req.Seed ?? (int?)GetParamDouble(req.Parameters, "seed");

        var rows = obs.Select(o => (o.RowIndex, o.PreparedValues, (ObservationReference?)o.Observation)).ToList();
        return IsolationForestEngine.Compute(rows, features, nTrees, maxSamples, contam, seed);
    }

    private static async Task<ScriptConditionEngine.ScriptConditionResult> ExecuteScriptConditionJobAsync(
        AutoVerifyDbContext db,
        Guid sessionId,
        CreateAnalyticsJobRequest req,
        CancellationToken ct)
    {
        var scriptText = req.ScriptText;
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            throw new ArgumentException("ScriptText is required for SCRIPT_CONDITION analysis.");
        }

        if (scriptText.Length > 50_000)
        {
            throw new ArgumentException("ScriptText exceeds maximum allowed length of 50,000 characters.");
        }

        // Parse the script to extract required features
        var parsedScript = ScriptConditionEngine.Parse(scriptText);
        var requiredFeatures = parsedScript.AllRequiredFeatures;

        if (requiredFeatures.Count == 0)
        {
            throw new InvalidOperationException("The script does not reference any recognizable feature variables (e.g. G020_PxlArea, MLClass01).");
        }

        // Apply scope filters to records query
        var recordsQuery = db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId);

        var scope = req.ScopeFilter;
        var lineFilter = scope?.LineCode ?? req.LineCode;
        var machineFilter = scope?.MachineCode ?? req.MachineCode;
        var stationFilter = scope?.StationCode ?? req.StationCode;
        var ruleCodeFilter = scope?.ClassifierRuleCode ?? req.ClassifierRuleCode;
        var ruleVersionFilter = scope?.ClassifierRuleVersion ?? req.ClassifierRuleVersion;
        var familyFilter = scope?.DefectFamily ?? req.DefectFamily;
        var classFilter = scope?.DefectClass ?? scope?.DefectType ?? req.DefectType;
        var fromUtc = scope?.FromUtc ?? scope?.TimeFromUtc ?? req.TimeFromUtc;
        var toUtc = scope?.ToUtc ?? scope?.TimeToUtc ?? req.TimeToUtc;

        if (!string.IsNullOrWhiteSpace(lineFilter))
            recordsQuery = recordsQuery.Where(r => r.LineCode == lineFilter);
        if (!string.IsNullOrWhiteSpace(machineFilter))
            recordsQuery = recordsQuery.Where(r => r.MachineCode == machineFilter);
        if (!string.IsNullOrWhiteSpace(stationFilter))
            recordsQuery = recordsQuery.Where(r => r.StationCode == stationFilter);
        if (!string.IsNullOrWhiteSpace(ruleCodeFilter))
            recordsQuery = recordsQuery.Where(r => r.ClassifierRuleCode == ruleCodeFilter);
        if (!string.IsNullOrWhiteSpace(ruleVersionFilter))
            recordsQuery = recordsQuery.Where(r => r.ClassifierRuleVersion == ruleVersionFilter);
        if (!string.IsNullOrWhiteSpace(familyFilter))
            recordsQuery = recordsQuery.Where(r => r.DefectFamily == familyFilter);
        if (!string.IsNullOrWhiteSpace(classFilter))
            recordsQuery = recordsQuery.Where(r => r.DefectClass == classFilter);
        if (fromUtc.HasValue)
            recordsQuery = recordsQuery.Where(r => r.InspectedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            recordsQuery = recordsQuery.Where(r => r.InspectedAtUtc <= toUtc.Value);

        var records = await recordsQuery
            .OrderBy(r => r.InspectedAtUtc)
            .ThenBy(r => r.RowIndex)
            .Take(req.MaxDataPoints)
            .Select(r => new
            {
                r.RowIndex,
                r.ObservationId,
                r.LineCode,
                r.MachineCode,
                r.StationCode,
                r.ClassifierRuleCode,
                r.ClassifierRuleVersion,
                r.DefectFamily,
                r.DefectClass
            })
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            throw new InvalidOperationException("No observations match the specified scope filters for script evaluation.");
        }

        var rowIndices = records.Select(r => r.RowIndex).ToHashSet();

        // Load only required features for these rows (not all features)
        var featureRows = await db.DefectPreviewFeatures
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId && rowIndices.Contains(f.RowIndex) && requiredFeatures.Contains(f.FeatureName))
            .OrderBy(f => f.Id)
            .Select(f => new { f.RowIndex, f.FeatureName, f.FeatureValue })
            .ToListAsync(ct);

        var featureMapByRow = new Dictionary<int, Dictionary<string, double>>();
        foreach (var fr in featureRows)
        {
            if (!featureMapByRow.TryGetValue(fr.RowIndex, out var dict))
            {
                dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                featureMapByRow[fr.RowIndex] = dict;
            }
            dict[fr.FeatureName] = fr.FeatureValue;
        }

        // Build observation list for engine
        var observations = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>(records.Count);
        foreach (var rec in records)
        {
            var obsRef = new ObservationReference(
                rec.ObservationId,
                rec.RowIndex,
                rec.LineCode,
                rec.MachineCode,
                rec.StationCode,
                rec.ClassifierRuleCode,
                rec.ClassifierRuleVersion,
                rec.DefectFamily,
                rec.DefectClass
            );

            featureMapByRow.TryGetValue(rec.RowIndex, out var fDict);
            observations.Add((rec.RowIndex, fDict ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), obsRef));
        }

        // Use imperative evaluation for correct conditional reassignment handling
        var result = ScriptConditionEngine.EvaluateImperative(scriptText, parsedScript, observations);

        // Clamp per-row evaluations to reduce response size
        var maxEvalRows = Math.Clamp(req.MaxEvaluationRows ?? 500, 10, 2000);
        if (result.Evaluations.Count > maxEvalRows)
        {
            return result with { Evaluations = result.Evaluations.Take(maxEvalRows).ToList() };
        }

        return result;
    }

    private static AnalyticsJobDto MapJobEntityToDto(DefectAnalyticsJobEntity job, bool includeResult = false)
    {
        object? result = null;
        if (includeResult && !string.IsNullOrWhiteSpace(job.ResultJson))
        {
            try
            {
                result = JsonSerializer.Deserialize<JsonElement>(job.ResultJson);
            }
            catch
            {
                result = job.ResultJson;
            }
        }

        List<string>? warnings = null;
        if (!string.IsNullOrWhiteSpace(job.WarningsJson))
        {
            try
            {
                warnings = JsonSerializer.Deserialize<List<string>>(job.WarningsJson);
            }
            catch { }
        }

        return new AnalyticsJobDto(
            job.Id,
            job.SessionId,
            job.AnalysisType,
            job.Status,
            job.ProgressPercent,
            job.RequestHash,
            job.CreatedAtUtc,
            job.StartedAtUtc,
            job.CompletedAtUtc,
            job.ExpiresAtUtc,
            job.ErrorMessage,
            warnings,
            result
        );
    }

    public void Dispose()
    {
        _serviceCts.Cancel();
        _serviceCts.Dispose();
        _concurrencySemaphore.Dispose();
    }
}
