using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Integration;

public sealed class AiVerificationOptions
{
    public const string SectionName = "AiVerification";

    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 15;
    public int MaxDefectsPerProduct { get; set; } = 20;

    /// <summary>VP passed to the image crawler; "0" is the cached thumbnail.</summary>
    public string VpMode { get; set; } = "0";

    /// <summary>A Master job older than this without a result is given up on (marked Unavailable).</summary>
    public int GiveUpAfterMinutes { get; set; } = 60;

    /// <summary>Raw image bytes per Master job. Base64 adds ~33%, and the Master caps a request body at 128 MB (Server:MaxRequestBodyMb).</summary>
    public long MaxBytesPerJob { get; set; } = 48L * 1024 * 1024;

    /// <summary>Master switch for automatic sending. A product is also only sent when its mapping has AutoSubmit on. Needs Enabled.</summary>
    public bool AutoSubmit { get; set; }

    /// <summary>Products picked per tick; each carries up to MaxDefectsPerProduct images, and an Edge serves one image at a time per station.</summary>
    public int AutoSubmitMaxProductsPerTick { get; set; } = 20;

    /// <summary>Only defects that entered AutoVerify within this window are considered, so switching AutoSubmit on does not replay history.</summary>
    public int AutoSubmitLookbackHours { get; set; } = 24;

    /// <summary>Sending pauses this long after the Master could not be reached or refused our credentials.</summary>
    public int AutoSubmitBackoffMinutes { get; set; } = 5;

    /// <summary>A verdict that arrives after GiveUpAfterMinutes is still accepted for this long.</summary>
    public int AcceptLateResultsHours { get; set; } = 24;
}

/// <param name="Deferred">Items the Master could not take right now; nothing was written for them, so they are sent again later.</param>
/// <param name="Transient">The Master was unreachable or refused our credentials. Callers should back off.</param>
public sealed record AiSubmitResult(
    int Submitted, int AlreadyJudged, int NoMapping, int NoImage, string? Message, int Deferred = 0, bool Transient = false);

public sealed record AiAutoSubmitResult(int Picked, int Submitted, int NoImage, int Deferred, bool Transient, string? Message)
{
    public static readonly AiAutoSubmitResult None = new(0, 0, 0, 0, false, null);
}

public sealed record AiDefectResult(
    Guid DefectId, string Status, string? Verdict, double? AnomalyScore, string? DefectClass, string? ClientMode, string? Error);

public sealed record AiProductResult(string KeyId, string Judge, string AiStatus, IReadOnlyList<AiDefectResult> Defects);

/// <summary>One line per product for list screens: the combined status plus how many of its defect images are in each state.</summary>
public sealed record AiProductSummary(
    string KeyId, string AiStatus, int Done, int Ng, int Ok, int Pending, int Unavailable, double? MaxAnomalyScore);

public interface IAiVerificationService
{
    Task<AiSubmitResult> SubmitProductsAsync(IReadOnlyCollection<string> keyIds, bool resubmit, CancellationToken cancellationToken);
    Task<AiAutoSubmitResult> AutoSubmitAsync(CancellationToken cancellationToken);
    Task<int> PollAsync(CancellationToken cancellationToken);
    Task<AiProductResult?> GetProductResultAsync(string keyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiProductSummary>> GetProductSummariesAsync(IReadOnlyCollection<string> keyIds, CancellationToken cancellationToken);
}

public sealed class AiVerificationService : IAiVerificationService
{
    private readonly AutoVerifyDbContext _db;
    private readonly IMasterServerClient _master;
    private readonly IImageCrawlerService _crawler;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AiVerificationOptions _options;
    private readonly ILogger<AiVerificationService> _logger;

    public AiVerificationService(
        AutoVerifyDbContext db, IMasterServerClient master, IImageCrawlerService crawler, IHttpClientFactory httpFactory,
        IOptions<AiVerificationOptions> options, ILogger<AiVerificationService> logger)
    {
        _db = db;
        _master = master;
        _crawler = crawler;
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiSubmitResult> SubmitProductsAsync(IReadOnlyCollection<string> keyIds, bool resubmit, CancellationToken cancellationToken)
    {
        if (!_master.IsConfigured)
            return new AiSubmitResult(0, 0, 0, 0, "MasterServer is not configured; no AI verdict can be requested.");
        var keys = keyIds.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return new AiSubmitResult(0, 0, 0, 0, "No product keys given.");

        var products = await _db.ProductRecords.AsNoTracking().Where(p => keys.Contains(p.KeyId)).ToListAsync(cancellationToken);
        var defects = await _db.DefectRecords.AsNoTracking().Where(d => keys.Contains(d.KeyId)).ToListAsync(cancellationToken);
        var existing = await _db.AiVerdicts.Where(v => keys.Contains(v.KeyId)).ToDictionaryAsync(v => v.DefectId, cancellationToken);
        var mappings = await _db.MasterStationMappings.AsNoTracking().Where(m => m.Enabled).ToListAsync(cancellationToken);

        int alreadyJudged = 0, noMapping = 0, noImage = 0, submitted = 0, deferred = 0;
        string? message = null;
        var transient = false;
        var batch = new List<PendingItem>();
        long batchBytes = 0;
        var maxItems = Math.Max(1, _master.MaxItemsPerJob);
        var maxBytes = Math.Max(1, _options.MaxBytesPerJob);

        // Images are fetched one batch at a time and every batch is saved as soon as the Master answers, so a cancelled
        // or timed-out request cannot lose the job ids of batches the Master already accepted.
        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            var current = batch.ToList();
            batch.Clear();
            batchBytes = 0;
            try
            {
                var jobId = await _master.SubmitJobAsync($"autoverify:{DateTime.UtcNow:yyyyMMddHHmmss}", current.Select(c => c.Item!).ToList(), cancellationToken);
                foreach (var c in current) Stage(c, AiVerdictStatus.Submitted, null, jobId);
                submitted += current.Count;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (IsTransient(ex))
                {
                    // Master down or credentials refused: write nothing, so these defects are picked up again once it is back.
                    message = $"Master unavailable, will retry later: {ex.Message}";
                    deferred += current.Count;
                    transient = true;
                    return;
                }
                // The Master read the job and refused it: retrying would fail the same way, so say so.
                message = $"Master rejected the job: {ex.Message}";
                foreach (var c in current) Stage(c, AiVerdictStatus.Unavailable, message, null);
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        foreach (var product in products)
        {
            if (transient) break;
            var selected = AiDefectSelector.Select(product.Judge, defects.Where(d => d.KeyId == product.KeyId), _options.MaxDefectsPerProduct);
            foreach (var defect in selected)
            {
                if (transient) break;
                existing.TryGetValue(defect.Id, out var prior);
                if (prior != null && (!resubmit || prior.Status == AiVerdictStatus.Submitted))
                {
                    alreadyJudged++;
                    continue;
                }
                var mapping = mappings.FirstOrDefault(m => MappingMatches(m, defect));
                if (mapping == null) { noMapping++; continue; }

                var image = await TryGetImageAsync(defect, cancellationToken);
                var pendingItem = new PendingItem(prior, defect, mapping, image == null ? null : new MasterVerifyItem(defect.Id.ToString("N"), mapping.StationId, mapping.ProductId, image));
                if (image == null)
                {
                    Stage(pendingItem, AiVerdictStatus.Unavailable, "The defect image could not be retrieved.", null);
                    noImage++;
                    continue;
                }

                if (batch.Count > 0 && (batch.Count >= maxItems || batchBytes + image.Length > maxBytes))
                    await FlushAsync();
                batch.Add(pendingItem);
                batchBytes += image.Length;
            }
        }
        if (!transient) await FlushAsync();
        await _db.SaveChangesAsync(cancellationToken);
        return new AiSubmitResult(submitted, alreadyJudged, noMapping, noImage, message, deferred, transient);
    }

    /// <summary>
    /// Temporary conditions worth retrying: no answer, timeouts, 429/5xx, and 403 (wrong IP/key/clock or a missing scope
    /// that an operator fixes on the Master). Any other 4xx means the Master read the job and refused it.
    /// </summary>
    public static bool IsTransient(Exception ex) => ex switch
    {
        TaskCanceledException => true,
        HttpRequestException h => h.StatusCode is null
            or System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.Forbidden
            or >= System.Net.HttpStatusCode.InternalServerError,
        _ => false
    };

    public async Task<AiAutoSubmitResult> AutoSubmitAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoSubmit || !_master.IsConfigured) return AiAutoSubmitResult.None;
        var mappings = await _db.MasterStationMappings.AsNoTracking().Where(m => m.Enabled && m.AutoSubmit).ToListAsync(cancellationToken);
        if (mappings.Count == 0) return AiAutoSubmitResult.None;

        var keys = await PickProductKeysAsync(mappings, cancellationToken);
        if (keys.Count == 0) return AiAutoSubmitResult.None;

        var result = await SubmitProductsAsync(keys, false, cancellationToken);
        return new AiAutoSubmitResult(keys.Count, result.Submitted, result.NoImage, result.Deferred, result.Transient, result.Message);
    }

    // At most this many recent products are examined per tick.
    private const int CandidateScanLimit = 500;

    /// <summary>
    /// Newest products first whose defects entered AutoVerify inside the lookback window, belong to an AutoSubmit mapping and
    /// still have an image the AI has not judged. The defect choice mirrors <see cref="AiDefectSelector"/> so a picked product
    /// always has something to send; a product whose chosen defects all have a row is skipped, never retried.
    /// </summary>
    private async Task<List<string>> PickProductKeysAsync(IReadOnlyList<MasterStationMappingEntity> mappings, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Max(1, _options.AutoSubmitLookbackHours));
        var recent = await _db.DefectRecords.AsNoTracking()
            .Where(d => d.CreatedAtUtc >= cutoff && d.ImageUri != null && d.ImageUri != "")
            .Select(d => new { d.KeyId, d.MachineCode, d.LineCode, d.ModelCode, d.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var candidates = recent
            .Where(d => mappings.Any(m =>
                string.Equals(m.MachineCode, d.MachineCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(m.LineCode, d.LineCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(m.ModelCode, d.ModelCode, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(d => d.KeyId)
            .OrderByDescending(g => g.Max(d => d.CreatedAtUtc))
            .Take(CandidateScanLimit)
            .Select(g => g.Key)
            .ToList();
        if (candidates.Count == 0) return new List<string>();

        var judges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in await _db.ProductRecords.AsNoTracking().Where(p => candidates.Contains(p.KeyId)).Select(p => new { p.KeyId, p.Judge }).ToListAsync(cancellationToken))
            judges.TryAdd(p.KeyId, p.Judge);
        var defectsByKey = (await _db.DefectRecords.AsNoTracking().Where(d => candidates.Contains(d.KeyId))
                .Select(d => new DefectRecordEntity { Id = d.Id, KeyId = d.KeyId, Judge = d.Judge, ImageUri = d.ImageUri })
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.KeyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var judged = (await _db.AiVerdicts.AsNoTracking().Where(v => candidates.Contains(v.KeyId)).Select(v => v.DefectId).ToListAsync(cancellationToken)).ToHashSet();

        var picked = new List<string>();
        var max = Math.Max(1, _options.AutoSubmitMaxProductsPerTick);
        foreach (var key in candidates)
        {
            if (!judges.TryGetValue(key, out var judge) || !defectsByKey.TryGetValue(key, out var defects)) continue;
            if (AiDefectSelector.Select(judge, defects, _options.MaxDefectsPerProduct).All(d => judged.Contains(d.Id))) continue;
            picked.Add(key);
            if (picked.Count >= max) break;
        }
        return picked;
    }

    private sealed record PendingItem(AiVerdictEntity? Prior, DefectRecordEntity Defect, MasterStationMappingEntity Mapping, MasterVerifyItem? Item);

    /// <summary>
    /// Writes the verdict row only once its outcome is known. A row saved as "Submitted" without a job id would be
    /// failed by the poller as "never accepted", so rows are not touched while a batch is still being assembled.
    /// </summary>
    private void Stage(PendingItem p, string status, string? error, string? jobId)
    {
        var row = p.Prior ?? new AiVerdictEntity { Id = Guid.NewGuid(), DefectId = p.Defect.Id };
        row.KeyId = p.Defect.KeyId;
        row.MachineCode = p.Defect.MachineCode; row.LineCode = p.Defect.LineCode; row.ModelCode = p.Defect.ModelCode;
        row.StationId = p.Mapping.StationId; row.ProductId = p.Mapping.ProductId;
        row.Verdict = null; row.AnomalyScore = null; row.DefectClass = null; row.BoxesJson = null;
        row.ModelVersionsJson = null; row.ClientMode = null;
        row.MasterJobId = jobId;
        row.SubmittedAtUtc = DateTimeOffset.UtcNow;
        row.Status = status;
        row.Error = error;
        row.CompletedAtUtc = status == AiVerdictStatus.Submitted ? null : DateTimeOffset.UtcNow;
        if (p.Prior == null) _db.AiVerdicts.Add(row);
    }

    public async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        if (!_master.IsConfigured) return 0;
        var now = DateTimeOffset.UtcNow;
        var lateCutoff = now.AddHours(-Math.Max(1, _options.AcceptLateResultsHours));
        // Rows given up on keep their job id and a TIMEOUT marker, so a verdict the Master delivers late is still collected.
        var open = await _db.AiVerdicts
            .Where(v => v.Status == AiVerdictStatus.Submitted || (v.Status == AiVerdictStatus.Unavailable && v.Error != null && v.Error.StartsWith(TimeoutMarker)))
            .ToListAsync(cancellationToken);
        var completed = 0;
        foreach (var expired in open.Where(v => v.Status == AiVerdictStatus.Unavailable && v.SubmittedAtUtc < lateCutoff).ToList())
        {
            expired.Error = expired.Error![TimeoutMarker.Length..].TrimStart(); // late window closed: stop polling it
            open.Remove(expired);
        }
        foreach (var group in open.GroupBy(v => v.MasterJobId))
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                foreach (var row in group) Fail(row, "Job was never accepted by the Master.");
                completed += group.Count();
                continue;
            }
            try
            {
                var results = (await _master.GetItemsAsync(group.Key, cancellationToken))
                    .ToDictionary(r => r.ExternalItemId, StringComparer.OrdinalIgnoreCase);
                foreach (var row in group)
                {
                    if (!results.TryGetValue(row.DefectId.ToString("N"), out var result)) continue;
                    if (Apply(row, result)) completed++;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                foreach (var row in group) Fail(row, "The Master no longer knows this job (it may have been reset).");
                completed += group.Count();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Master unreachable while polling job {JobId}: {Message}", group.Key, ex.Message);
            }
        }

        // Anything the Master keeps "pending" beyond the limit is abandoned so a dead job cannot hide a missing verdict.
        var cutoff = now.AddMinutes(-Math.Max(1, _options.GiveUpAfterMinutes));
        foreach (var row in open.Where(v => v.Status == AiVerdictStatus.Submitted && v.SubmittedAtUtc < cutoff))
        {
            Fail(row, $"{TimeoutMarker} No result from the Master within {_options.GiveUpAfterMinutes} minutes.");
            completed++;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return completed;
    }

    public async Task<AiProductResult?> GetProductResultAsync(string keyId, CancellationToken cancellationToken)
    {
        var product = await _db.ProductRecords.AsNoTracking().FirstOrDefaultAsync(p => p.KeyId == keyId, cancellationToken);
        if (product == null) return null;
        var rows = await _db.AiVerdicts.AsNoTracking().Where(v => v.KeyId == keyId).ToListAsync(cancellationToken);
        var defects = rows.Select(v => new AiDefectResult(v.DefectId, v.Status, v.Verdict, v.AnomalyScore, v.DefectClass, v.ClientMode, v.Error)).ToList();
        return new AiProductResult(keyId, product.Judge, AiProductStatus.Resolve(product.Judge, rows.Select(v => new AiDefectVerdict(v.Status, v.Verdict)).ToList()), defects);
    }

    public async Task<IReadOnlyList<AiProductSummary>> GetProductSummariesAsync(IReadOnlyCollection<string> keyIds, CancellationToken cancellationToken)
    {
        var keys = keyIds.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return Array.Empty<AiProductSummary>();

        var rows = await _db.AiVerdicts.AsNoTracking().Where(v => keys.Contains(v.KeyId))
            .Select(v => new { v.KeyId, v.Status, v.Verdict, v.AnomalyScore })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return Array.Empty<AiProductSummary>();

        var judges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in await _db.ProductRecords.AsNoTracking().Where(p => keys.Contains(p.KeyId)).Select(p => new { p.KeyId, p.Judge }).ToListAsync(cancellationToken))
            judges.TryAdd(p.KeyId, p.Judge);

        return rows.GroupBy(r => r.KeyId, StringComparer.OrdinalIgnoreCase)
            .Where(g => judges.ContainsKey(g.Key))
            .Select(g =>
            {
                var done = g.Where(r => r.Status == AiVerdictStatus.Done).ToList();
                return new AiProductSummary(
                    g.Key,
                    AiProductStatus.Resolve(judges[g.Key], g.Select(r => new AiDefectVerdict(r.Status, r.Verdict)).ToList()),
                    done.Count,
                    done.Count(r => r.Verdict == "NG"),
                    done.Count(r => r.Verdict == "OK"),
                    g.Count(r => r.Status == AiVerdictStatus.Submitted),
                    g.Count(r => r.Status is AiVerdictStatus.Unavailable or AiVerdictStatus.Rejected),
                    done.Select(r => r.AnomalyScore).Max());
            })
            .ToList();
    }

    private static bool MappingMatches(MasterStationMappingEntity m, DefectRecordEntity d) =>
        string.Equals(m.MachineCode, d.MachineCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(m.LineCode, d.LineCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(m.ModelCode, d.ModelCode, StringComparison.OrdinalIgnoreCase);

    private static bool Apply(AiVerdictEntity row, MasterVerifyItemResult result)
    {
        switch (result.Status)
        {
            case "Done" when result.Verdict is "OK" or "NG":
                row.Status = AiVerdictStatus.Done;
                row.Verdict = result.Verdict;
                row.AnomalyScore = result.AnomalyScore;
                row.DefectClass = result.DefectClass;
                row.BoxesJson = result.BoxesJson;
                row.ModelVersionsJson = result.ModelVersionsJson;
                row.ClientMode = result.ClientMode;
                row.Error = null;
                break;
            case "Rejected":
                row.Status = AiVerdictStatus.Rejected;
                row.Error = result.Error;
                break;
            case "Unavailable":
            case "Done": // Done without a usable verdict is not a verdict
                row.Status = AiVerdictStatus.Unavailable;
                row.Error = result.Error ?? "The Master returned no usable verdict.";
                break;
            default:
                return false; // Pending / Processing
        }
        row.CompletedAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    private const string TimeoutMarker = "TIMEOUT:";

    private static void Fail(AiVerdictEntity row, string error)
    {
        row.Status = AiVerdictStatus.Unavailable;
        row.Error = error;
        row.CompletedAtUtc = DateTimeOffset.UtcNow;
    }

    private async Task<byte[]?> TryGetImageAsync(DefectRecordEntity defect, CancellationToken cancellationToken)
    {
        try
        {
            var crawl = await _crawler.CrawlAndCacheDefectImageAsync(defect.Id, false, null, _options.VpMode, cancellationToken);
            if (!crawl.Success) return null;
            if (crawl.ImageBytes is { Length: > 0 }) return crawl.ImageBytes;
            // A cache hit on an absolute URL returns the URL only.
            if (Uri.TryCreate(crawl.StoredUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var bytes = await _httpFactory.CreateClient().GetByteArrayAsync(uri, cancellationToken);
                return bytes.Length > 0 ? bytes : null;
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning("Image for defect {DefectId} could not be fetched: {Message}", defect.Id, ex.Message);
            return null;
        }
    }
}

/// <summary>Which defect images of a product are worth sending to the AI.</summary>
public static class AiDefectSelector
{
    public static IReadOnlyList<DefectRecordEntity> Select(string productJudge, IEnumerable<DefectRecordEntity> defects, int max)
    {
        var withImage = defects.Where(d => !string.IsNullOrWhiteSpace(d.ImageUri));
        // An NG product is questioned on the defects that made it NG (same filter as the Overkill rule);
        // an OK product is checked on everything recorded against it.
        if (AiProductStatus.IsNg(productJudge)) withImage = withImage.Where(d => AiProductStatus.IsNg(d.Judge));
        return withImage.OrderBy(d => d.Id).Take(Math.Max(1, max)).ToList();
    }
}

public static class AiProductStatus
{
    public static bool IsNg(string? judge) =>
        judge is not null && (judge.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("N", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || judge.Equals("NA", StringComparison.OrdinalIgnoreCase));

    public static string Resolve(string productJudge, IReadOnlyCollection<AiDefectVerdict> verdicts)
    {
        if (verdicts.Any(v => v.Status == AiVerdictStatus.Submitted)) return "Pending";
        return AiVerdictMatrix.Decide(IsNg(productJudge), verdicts).ToString();
    }
}
