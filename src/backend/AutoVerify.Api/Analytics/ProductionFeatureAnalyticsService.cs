using System.Collections.Concurrent;
using System.Text.Json;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Services;
using AutoVerify.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Analytics;

public sealed record ProductionFeaturePoint(
    DateTimeOffset InspectedAtUtc,
    double Value,
    string DefectId,
    string KeyId,
    string DefectName,
    string Judge);

public sealed record ProductionFeatureSeries(
    string FeatureName,
    int SampleCount,
    double Min,
    double Max,
    double Mean,
    IReadOnlyList<ProductionFeaturePoint> Points);

public sealed record ProductionFeatureAnalysisResult(
    Guid JobId,
    string AnalysisType,
    string FeatureName,
    IReadOnlyList<string> FeatureNames,
    SpcResult? Spc,
    EwmaResult? Ewma,
    CusumResult? Cusum,
    PcaResult? Pca);

public sealed record ProductionFeatureJobDto(
    Guid JobId,
    string Status,
    string Period,
    string? MachineCode,
    string? LineCode,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int TotalDefects,
    int ProcessedDefects,
    int LoadedPacks,
    int FeatureValueCount,
    int FeaturesWithValues,
    double Progress,
    string Message,
    IReadOnlyCollection<string> Machines,
    IReadOnlyCollection<string> Lines,
    IReadOnlyCollection<string> DefectNames,
    IReadOnlyCollection<string> Judges,
    IReadOnlyList<ProductionFeatureSeries> Series);

public sealed class ProductionFeatureAnalyticsService : IDisposable
{
    private const int ProductBatchSize = 24;
    private const int MaxConcurrentPackReads = 8;
    private const int BatchPauseMs = 80;
    private const int MaxPointsPerFeature = 1800;
    private const int ChartPointBudget = 400;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductionFeatureAnalyticsService> _logger;
    private readonly ConcurrentDictionary<Guid, FeatureJob> _jobs = new();
    private readonly CancellationTokenSource _serviceCts = new();

    public ProductionFeatureAnalyticsService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductionFeatureAnalyticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ProductionFeatureJobDto> GetFacetsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
        var machines = await db.ProductRecords.AsNoTracking()
            .Select(p => p.MachineCode)
            .Where(v => v != "")
            .Distinct()
            .OrderBy(v => v)
            .ToArrayAsync(cancellationToken);
        var lines = await db.ProductRecords.AsNoTracking()
            .Select(p => p.LineCode)
            .Where(v => v != "")
            .Distinct()
            .OrderBy(v => v)
            .ToArrayAsync(cancellationToken);
        var defectNames = await db.DefectRecords.AsNoTracking()
            .Select(d => d.DefectName)
            .Where(v => v != "" && v != "None")
            .Distinct()
            .OrderBy(v => v)
            .Take(200)
            .ToArrayAsync(cancellationToken);
        var judges = await db.DefectRecords.AsNoTracking()
            .Select(d => d.Judge)
            .Where(v => v != "")
            .Distinct()
            .OrderBy(v => v)
            .ToArrayAsync(cancellationToken);

        return new ProductionFeatureJobDto(
            Guid.Empty, "Idle", "1w", null, null,
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
            0, 0, 0, 0, 0, 0, "Select machine, line, defect, judge, and period, then start loading.",
            machines, lines, defectNames, judges, []);
    }

    public async Task<ProductionFeatureJobDto> StartJobAsync(
        string? machineCode,
        string? lineCode,
        string? period,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? defectName = null,
        string? judge = null,
        CancellationToken cancellationToken = default)
    {
        var (windowFrom, windowTo, normalizedPeriod) = ResolveWindow(period, fromUtc, toUtc);
        var machineFilter = Normalize(machineCode);
        var lineFilter = Normalize(lineCode);
        var defectNameFilter = Normalize(defectName);
        var judgeFilter = Normalize(judge);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
        var facets = await GetFacetsAsync(cancellationToken);

        var query =
            from defect in db.DefectRecords.AsNoTracking()
            join product in db.ProductRecords.AsNoTracking() on defect.KeyId equals product.KeyId
            where defect.CreatedAtUtc >= windowFrom && defect.CreatedAtUtc <= windowTo
            select new { defect, product };

        if (machineFilter is not null)
            query = query.Where(x => x.product.MachineCode == machineFilter);
        if (lineFilter is not null)
            query = query.Where(x => x.product.LineCode == lineFilter);
        if (defectNameFilter is not null)
            query = query.Where(x => x.defect.DefectName.ToLower() == defectNameFilter.ToLower());
        if (judgeFilter is not null)
            query = query.Where(x => x.defect.Judge.ToLower() == judgeFilter.ToLower());

        var workItems = await query
            .OrderBy(x => x.defect.CreatedAtUtc)
            .Select(x => new DefectWorkItem(
                x.defect.Id,
                x.defect.KeyId,
                x.defect.DefectName,
                x.defect.Judge,
                x.defect.CreatedAtUtc,
                x.defect.FeatureJson,
                x.defect.CustomFeatureJson,
                x.defect.FeatureUrl,
                x.defect.CustomFeatureUrl,
                x.product.DefectPackUrl,
                x.product.MachineCode,
                x.product.LineCode))
            .ToArrayAsync(cancellationToken);

        var job = new FeatureJob
        {
            Id = Guid.NewGuid(),
            Status = workItems.Length == 0 ? "Completed" : "Queued",
            Period = normalizedPeriod,
            MachineCode = machineFilter,
            LineCode = lineFilter,
            DefectName = defectNameFilter,
            Judge = judgeFilter,
            FromUtc = windowFrom,
            ToUtc = windowTo,
            TotalDefects = workItems.Length,
            Message = workItems.Length == 0
                ? "No production defects matched the selected scope."
                : $"Queued {workItems.Length:N0} defects for paced SeaweedFS extraction.",
            Machines = facets.Machines,
            Lines = facets.Lines,
            DefectNames = facets.DefectNames,
            Judges = facets.Judges
        };
        _jobs[job.Id] = job;

        if (workItems.Length > 0)
        {
            job.Worker = Task.Run(() => ProcessJobAsync(job, workItems, _serviceCts.Token), CancellationToken.None);
        }

        return Snapshot(job);
    }

    public ProductionFeatureJobDto? GetJob(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? Snapshot(job) : null;

    public ProductionFeatureAnalysisResult Analyze(Guid jobId, string analysisType, string? featureName, IReadOnlyList<string>? featureNames)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            throw new KeyNotFoundException("Feature load job was not found.");

        var series = job.SnapshotSeries();
        if (series.Count == 0)
            throw new InvalidOperationException("No feature values have been extracted yet. Wait for the load job to produce series.");

        var type = string.IsNullOrWhiteSpace(analysisType) ? "SPC" : analysisType.Trim().ToUpperInvariant();
        var selectedNames = (featureNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var primaryName = !string.IsNullOrWhiteSpace(featureName)
            ? featureName.Trim()
            : selectedNames.FirstOrDefault() ?? series[0].FeatureName;
        var primary = series.FirstOrDefault(s => s.FeatureName.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Feature '{primaryName}' is not in the loaded production series.");

        if (type is "PCA")
        {
            if (selectedNames.Count < 2)
                selectedNames = series.Take(4).Select(s => s.FeatureName).ToList();
            var pcaSeries = selectedNames
                .Select(name => series.FirstOrDefault(s => s.FeatureName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Where(s => s is not null)
                .Cast<ProductionFeatureSeries>()
                .ToArray();
            if (pcaSeries.Length < 2)
                throw new InvalidOperationException("PCA needs at least two loaded production features.");

            var aligned = AlignSeries(pcaSeries);
            var pca = PcaEngine.Compute(aligned, pcaSeries.Select(s => s.FeatureName).ToArray());
            return new ProductionFeatureAnalysisResult(job.Id, "PCA", pcaSeries[0].FeatureName, pcaSeries.Select(s => s.FeatureName).ToArray(), null, null, null, pca);
        }

        var points = primary.Points.Select((p, index) => (
            RowIndex: index + 1,
            p.Value,
            SubgroupId: (string?)null,
            Observation: (ObservationReference?)new ObservationReference(
                Guid.TryParse(p.DefectId, out var defectId) ? defectId : Guid.Empty,
                index + 1,
                p.DefectId,
                p.KeyId,
                Judge: p.Judge,
                InspectedAtUtc: p.InspectedAtUtc)
        )).ToArray();

        return type switch
        {
            "EWMA" => new ProductionFeatureAnalysisResult(job.Id, "EWMA", primary.FeatureName, [primary.FeatureName], null, TimeSeriesEngine.ComputeEwma(points.Select(p => (p.Item1, p.Value)).ToArray(), primary.FeatureName), null, null),
            "CUSUM" => new ProductionFeatureAnalysisResult(job.Id, "CUSUM", primary.FeatureName, [primary.FeatureName], null, null, TimeSeriesEngine.ComputeCusum(points.Select(p => (p.Item1, p.Value)).ToArray(), primary.FeatureName), null),
            _ => new ProductionFeatureAnalysisResult(job.Id, "SPC", primary.FeatureName, [primary.FeatureName], SpcEngine.Compute(points, primary.FeatureName, "IMR", isNgOnly: true), null, null, null)
        };
    }

    private static List<(int RowIndex, double[] Values)> AlignSeries(IReadOnlyList<ProductionFeatureSeries> series)
    {
        var keys = series[0].Points.Select(p => p.DefectId).ToArray();
        var maps = series.Select(s => s.Points.GroupBy(p => p.DefectId).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase)).ToArray();
        var rows = new List<(int RowIndex, double[] Values)>();
        for (var i = 0; i < keys.Length; i++)
        {
            var values = new double[series.Count];
            var complete = true;
            for (var j = 0; j < series.Count; j++)
            {
                if (!maps[j].TryGetValue(keys[i], out var value))
                {
                    complete = false;
                    break;
                }
                values[j] = value;
            }
            if (complete) rows.Add((i + 1, values));
        }
        return rows;
    }

    public bool CancelJob(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return false;
        job.CancelCts.Cancel();
        job.Status = "Cancelled";
        job.Message = "Loading cancelled. Chart keeps values already extracted.";
        return true;
    }

    private async Task ProcessJobAsync(FeatureJob job, DefectWorkItem[] items, CancellationToken serviceToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, job.CancelCts.Token);
        var ct = linked.Token;
        job.Status = "Running";
        job.Message = "Extracting numeric features in paced product batches.";

        try
        {
            var groups = items
                .GroupBy(x => x.PackUrl ?? x.KeyId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var ioGate = new SemaphoreSlim(MaxConcurrentPackReads, MaxConcurrentPackReads);

            for (var offset = 0; offset < groups.Length && !ct.IsCancellationRequested; offset += ProductBatchSize)
            {
                var batch = groups.Skip(offset).Take(ProductBatchSize).ToArray();
                var batchTasks = batch.Select(group => ProcessProductGroupAsync(job, group.ToArray(), ioGate, ct));
                await Task.WhenAll(batchTasks);
                job.LoadedPacks += batch.Length;
                job.Message = $"Loaded {job.ProcessedDefects:N0}/{job.TotalDefects:N0} defects across {job.LoadedPacks:N0} product packs.";
                await Task.Delay(BatchPauseMs, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                job.Status = "Completed";
                job.Message = $"Completed {job.ProcessedDefects:N0} defects, {job.FeatureValueCount:N0} feature values, {job.SnapshotSeries().Count} named series.";
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = "Cancelled";
            job.Message = "Loading cancelled. Chart keeps values already extracted.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Production feature job {JobId} failed after {Processed} defects", job.Id, job.ProcessedDefects);
            job.Status = "Failed";
            job.Message = $"Stopped after {job.ProcessedDefects:N0} defects: {ex.Message}";
        }
    }

    private async Task ProcessProductGroupAsync(
        FeatureJob job,
        DefectWorkItem[] group,
        SemaphoreSlim ioGate,
        CancellationToken cancellationToken)
    {
        await ioGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            JsonDocument? pack = null;
            var packUrl = group.Select(x => x.PackUrl).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(packUrl))
            {
                var packJson = await storage.ReadDefectPackJsonAsync(packUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(packJson))
                {
                    try { pack = JsonDocument.Parse(packJson); }
                    catch { pack = null; }
                }
            }

            foreach (var item in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesDefectOptions(item.DefectName, item.Judge, job.DefectName, job.Judge))
                {
                    Interlocked.Increment(ref job.ProcessedDefects);
                    continue;
                }

                var json = ResolveFeatureJson(item, pack);
                if (string.IsNullOrWhiteSpace(json) && (!string.IsNullOrWhiteSpace(item.FeatureUrl) || !string.IsNullOrWhiteSpace(item.CustomFeatureUrl)))
                {
                    var remote = !string.IsNullOrWhiteSpace(item.FeatureUrl) ? await storage.ReadTextAsync(item.FeatureUrl, cancellationToken) : null;
                    var remoteCustom = !string.IsNullOrWhiteSpace(item.CustomFeatureUrl) ? await storage.ReadTextAsync(item.CustomFeatureUrl, cancellationToken) : null;
                    json = MergeJson(remote, remoteCustom);
                }

                foreach (var (name, value) in DefectStreamParser.ExtractNumericFeaturesFromJson(json))
                {
                    job.AddPoint(new ProductionFeaturePoint(item.CreatedAtUtc, value, item.Id.ToString(), item.KeyId, item.DefectName, item.Judge), name);
                }

                Interlocked.Increment(ref job.ProcessedDefects);
            }

            pack?.Dispose();
        }
        finally
        {
            ioGate.Release();
        }
    }

    private static string? ResolveFeatureJson(DefectWorkItem item, JsonDocument? pack)
    {
        if (!string.IsNullOrWhiteSpace(item.FeatureJson)) return MergeJson(item.FeatureJson, item.CustomFeatureJson);
        if (!string.IsNullOrWhiteSpace(item.CustomFeatureJson)) return item.CustomFeatureJson;
        if (pack is null) return null;
        if (!pack.RootElement.TryGetProperty("defects", out var defects) || defects.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var el in defects.EnumerateArray())
        {
            var idText = el.TryGetProperty("defectId", out var idProp) ? idProp.GetString()
                : el.TryGetProperty("id", out var legacyId) ? legacyId.GetString()
                : null;
            if (!Guid.TryParse(idText, out var parsed) || parsed != item.Id) continue;
            var feature = el.TryGetProperty("feature", out var featureEl) ? featureEl.GetRawText()
                : el.TryGetProperty("featureJson", out var featureJsonEl) ? featureJsonEl.GetRawText()
                : null;
            var custom = el.TryGetProperty("customFeature", out var customEl) ? customEl.GetRawText()
                : el.TryGetProperty("customFeatureJson", out var customJsonEl) ? customJsonEl.GetRawText()
                : null;
            return MergeJson(feature, custom);
        }

        return null;
    }

    private static string? MergeJson(string? primary, string? secondary)
    {
        if (string.IsNullOrWhiteSpace(primary)) return string.IsNullOrWhiteSpace(secondary) ? null : secondary;
        if (string.IsNullOrWhiteSpace(secondary)) return primary;
        return $"{{\"feature\":{EnsureJsonValue(primary)},\"customFeature\":{EnsureJsonValue(secondary)}}}";
    }

    private static string EnsureJsonValue(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[') ? trimmed : JsonSerializer.Serialize(trimmed);
    }

    private ProductionFeatureJobDto Snapshot(FeatureJob job)
    {
        var series = job.SnapshotSeries();
        return new ProductionFeatureJobDto(
            job.Id,
            job.Status,
            job.Period,
            job.MachineCode,
            job.LineCode,
            job.FromUtc,
            job.ToUtc,
            job.TotalDefects,
            job.ProcessedDefects,
            job.LoadedPacks,
            job.FeatureValueCount,
            series.Count,
            job.TotalDefects == 0 ? 1 : Math.Clamp(job.ProcessedDefects / (double)job.TotalDefects, 0, 1),
            job.Message,
            job.Machines,
            job.Lines,
            job.DefectNames,
            job.Judges,
            series);
    }

    private static (DateTimeOffset From, DateTimeOffset To, string Period) ResolveWindow(string? period, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var to = toUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var normalized = string.IsNullOrWhiteSpace(period) ? "1w" : period.Trim().ToLowerInvariant();
        DateTimeOffset from = normalized switch
        {
            "1d" or "day" => to.AddDays(-1),
            "1m" or "month" => to.AddDays(-30),
            "custom" when fromUtc.HasValue => fromUtc.Value.ToUniversalTime(),
            _ => to.AddDays(-7)
        };
        if (fromUtc.HasValue && toUtc.HasValue) normalized = "custom";
        if (from > to) (from, to) = (to, from);
        return (from, to, normalized is "1d" or "1w" or "1m" or "custom" ? normalized : "1w");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static bool MatchesDefectOptions(string? defectName, string? judge, string? nameFilter, string? judgeFilter)
    {
        if (nameFilter is not null && !string.Equals(defectName?.Trim(), nameFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (judgeFilter is not null && !string.Equals(judge?.Trim(), judgeFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public void Dispose()
    {
        _serviceCts.Cancel();
        _serviceCts.Dispose();
        foreach (var job in _jobs.Values) job.CancelCts.Cancel();
    }

    private sealed record DefectWorkItem(
        Guid Id,
        string KeyId,
        string DefectName,
        string Judge,
        DateTimeOffset CreatedAtUtc,
        string? FeatureJson,
        string? CustomFeatureJson,
        string? FeatureUrl,
        string? CustomFeatureUrl,
        string? PackUrl,
        string MachineCode,
        string LineCode);

    private sealed class FeatureJob
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, List<ProductionFeaturePoint>> _series = new(StringComparer.OrdinalIgnoreCase);

        public Guid Id { get; init; }
        public string Status { get; set; } = "Queued";
        public string Period { get; init; } = "1w";
        public string? MachineCode { get; init; }
        public string? LineCode { get; init; }
        public string? DefectName { get; init; }
        public string? Judge { get; init; }
        public DateTimeOffset FromUtc { get; init; }
        public DateTimeOffset ToUtc { get; init; }
        public int TotalDefects { get; init; }
        public int ProcessedDefects;
        public int LoadedPacks { get; set; }
        public int FeatureValueCount { get; private set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyCollection<string> Machines { get; init; } = [];
        public IReadOnlyCollection<string> Lines { get; init; } = [];
        public IReadOnlyCollection<string> DefectNames { get; init; } = [];
        public IReadOnlyCollection<string> Judges { get; init; } = [];
        public CancellationTokenSource CancelCts { get; } = new();
        public Task? Worker { get; set; }

        public void AddPoint(ProductionFeaturePoint point, string featureName)
        {
            lock (_sync)
            {
                if (!_series.TryGetValue(featureName, out var points))
                {
                    points = [];
                    _series[featureName] = points;
                }

                if (points.Count >= MaxPointsPerFeature)
                {
                    var stride = Math.Max(2, points.Count / ChartPointBudget);
                    var sampled = points.Where((_, index) => index % stride == 0).ToList();
                    points.Clear();
                    points.AddRange(sampled);
                }

                points.Add(point);
                FeatureValueCount++;
            }
        }

        public IReadOnlyList<ProductionFeatureSeries> SnapshotSeries()
        {
            lock (_sync)
            {
                return _series
                    .Select(kvp =>
                    {
                        var values = kvp.Value;
                        var stride = values.Count <= ChartPointBudget ? 1 : (int)Math.Ceiling(values.Count / (double)ChartPointBudget);
                        var chartPoints = stride == 1
                            ? values.ToArray()
                            : values.Where((_, index) => index % stride == 0).ToArray();
                        return new ProductionFeatureSeries(
                            kvp.Key,
                            values.Count,
                            values.Count == 0 ? 0 : values.Min(p => p.Value),
                            values.Count == 0 ? 0 : values.Max(p => p.Value),
                            values.Count == 0 ? 0 : values.Average(p => p.Value),
                            chartPoints);
                    })
                    .OrderByDescending(s => s.SampleCount)
                    .ThenBy(s => s.FeatureName)
                    .ToArray();
            }
        }
    }
}
