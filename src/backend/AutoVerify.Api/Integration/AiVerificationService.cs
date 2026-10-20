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
}

public sealed record AiSubmitResult(int Submitted, int AlreadyJudged, int NoMapping, int NoImage, string? Message);

public sealed record AiDefectResult(
    Guid DefectId, string Status, string? Verdict, double? AnomalyScore, string? DefectClass, string? ClientMode, string? Error);

public sealed record AiProductResult(string KeyId, string Judge, string AiStatus, IReadOnlyList<AiDefectResult> Defects);

public interface IAiVerificationService
{
    Task<AiSubmitResult> SubmitProductsAsync(IReadOnlyCollection<string> keyIds, bool resubmit, CancellationToken cancellationToken);
    Task<int> PollAsync(CancellationToken cancellationToken);
    Task<AiProductResult?> GetProductResultAsync(string keyId, CancellationToken cancellationToken);
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

        int alreadyJudged = 0, noMapping = 0, noImage = 0;
        var pending = new List<(AiVerdictEntity Row, MasterVerifyItem Item)>();

        foreach (var product in products)
        {
            var selected = AiDefectSelector.Select(product.Judge, defects.Where(d => d.KeyId == product.KeyId), _options.MaxDefectsPerProduct);
            foreach (var defect in selected)
            {
                if (existing.TryGetValue(defect.Id, out var prior) && (!resubmit || prior.Status == AiVerdictStatus.Submitted))
                {
                    alreadyJudged++;
                    continue;
                }
                var mapping = mappings.FirstOrDefault(m => MappingMatches(m, defect));
                if (mapping == null) { noMapping++; continue; }

                var image = await TryGetImageAsync(defect, cancellationToken);
                var row = prior ?? new AiVerdictEntity { Id = Guid.NewGuid(), DefectId = defect.Id };
                row.KeyId = defect.KeyId;
                row.MachineCode = defect.MachineCode; row.LineCode = defect.LineCode; row.ModelCode = defect.ModelCode;
                row.StationId = mapping.StationId; row.ProductId = mapping.ProductId;
                row.Verdict = null; row.AnomalyScore = null; row.DefectClass = null; row.BoxesJson = null;
                row.ModelVersionsJson = null; row.ClientMode = null; row.MasterJobId = null;
                row.SubmittedAtUtc = DateTimeOffset.UtcNow; row.CompletedAtUtc = null;
                if (prior == null) _db.AiVerdicts.Add(row);

                if (image == null)
                {
                    row.Status = AiVerdictStatus.Unavailable;
                    row.Error = "The defect image could not be retrieved.";
                    row.CompletedAtUtc = DateTimeOffset.UtcNow;
                    noImage++;
                    continue;
                }
                row.Status = AiVerdictStatus.Submitted;
                row.Error = null;
                pending.Add((row, new MasterVerifyItem(defect.Id.ToString("N"), mapping.StationId, mapping.ProductId, image)));
            }
        }

        var submitted = 0;
        string? message = null;
        foreach (var chunk in pending.Chunk(Math.Max(1, _master.MaxItemsPerJob)))
        {
            try
            {
                var jobId = await _master.SubmitJobAsync($"autoverify:{DateTime.UtcNow:yyyyMMddHHmmss}", chunk.Select(c => c.Item).ToList(), cancellationToken);
                foreach (var (row, _) in chunk) row.MasterJobId = jobId;
                submitted += chunk.Length;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The Master never saw these: say so instead of leaving them "Submitted" forever.
                message = $"Master rejected or could not receive a job: {ex.Message}";
                foreach (var (row, _) in chunk)
                {
                    row.Status = AiVerdictStatus.Unavailable;
                    row.Error = message;
                    row.CompletedAtUtc = DateTimeOffset.UtcNow;
                }
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new AiSubmitResult(submitted, alreadyJudged, noMapping, noImage, message);
    }

    public async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        if (!_master.IsConfigured) return 0;
        var open = await _db.AiVerdicts.Where(v => v.Status == AiVerdictStatus.Submitted).ToListAsync(cancellationToken);
        var completed = 0;
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
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, _options.GiveUpAfterMinutes));
        foreach (var row in open.Where(v => v.Status == AiVerdictStatus.Submitted && v.SubmittedAtUtc < cutoff))
        {
            Fail(row, $"No result from the Master within {_options.GiveUpAfterMinutes} minutes.");
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
