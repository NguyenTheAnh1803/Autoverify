using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Services;

public interface IImageCrawlerService
{
    Task<ImageCrawlResult> CrawlImageAsync(string imagePath, string apiUrl, string method = "GET", string? customVp = null, CancellationToken cancellationToken = default);
    Task<ImageCrawlResult> CrawlAndCacheDefectImageAsync(Guid defectId, bool forceRefresh = false, string? customApiUrl = null, string? customVp = null, CancellationToken cancellationToken = default);
    Task<TestImageCrawlerApiResponse> TestApiAsync(TestImageCrawlerApiRequest request, CancellationToken cancellationToken = default);
    Task<TestDefectCrawlResponse> TestDefectCrawlAsync(Guid defectId, string? customApiUrl = null, string? customVp = null, CancellationToken cancellationToken = default);
    Task<TestPathCrawlResponse> TestPathCrawlAsync(string imagePath, string? customApiUrl = null, string? featureJson = null, string? customVp = null, CancellationToken cancellationToken = default);
    Task<BatchImageCrawlResponse> BatchCrawlDefectImagesAsync(BatchImageCrawlRequest request, CancellationToken cancellationToken = default);
    Task<(string? ApiUrl, string VpMode)> ResolveApiUrlAndVpModeForDefectAsync(DefectRecordEntity defect, CancellationToken cancellationToken = default);
    Task<string?> ResolveApiUrlForDefectAsync(DefectRecordEntity defect, CancellationToken cancellationToken = default);
}

public sealed class ImageCrawlerService : IImageCrawlerService
{
    private const string FallbackAnywherePixApiUrl = "http://10.121.45.43:336/api/image/get";
    private const string FallbackAnywherePixImageRoot = @"\\192.168.200.121\d\defectimage";
    private static readonly Regex LineZoneRegex = new(@"#(?<line>\d+)-(?<zone>[A-Za-z0-9]+)", RegexOptions.Compiled);
    private static readonly Regex VpFolderRegex = new(@"^VP0*(?<vp>\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly HttpClient _httpClient;
    private readonly AutoVerifyDbContext _db;
    private readonly IStorageService _storage;
    private readonly ILogger<ImageCrawlerService> _logger;
    private readonly ImageCrawlerOptions _options;
    private string DefaultApiUrl => string.IsNullOrWhiteSpace(_options.DefaultApiUrl) ? FallbackAnywherePixApiUrl : _options.DefaultApiUrl.Trim();
    private string ImageRoot => string.IsNullOrWhiteSpace(_options.ImageRoot) ? FallbackAnywherePixImageRoot : _options.ImageRoot.TrimEnd('\\', '/');

    public ImageCrawlerService(
        HttpClient httpClient,
        AutoVerifyDbContext db,
        IStorageService storage,
        ILogger<ImageCrawlerService> logger,
        IOptions<ImageCrawlerOptions>? options = null)
    {
        _httpClient = httpClient;
        _db = db;
        _storage = storage;
        _logger = logger;
        _options = options?.Value ?? new ImageCrawlerOptions();

        // Default timeout for PC / machine edge calls
        if (_httpClient.Timeout == TimeSpan.FromSeconds(100))
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    public async Task<ImageCrawlResult> CrawlImageAsync(
        string imagePath,
        string apiUrl,
        string method = "GET",
        string? customVp = null,
        CancellationToken cancellationToken = default)
    {
        return await CrawlImageInternalAsync(imagePath, apiUrl, method, null, customVp, cancellationToken);
    }

    private async Task<ImageCrawlResult> CrawlImageInternalAsync(
        string imagePath,
        string apiUrl,
        string method,
        string? featureJson,
        string? customVp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return new ImageCrawlResult(false, null, null, null, "Image path is empty.", 400);
        }

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            return new ImageCrawlResult(false, null, null, null, "Crawler API URL is empty.", 400);
        }

        if (!_options.AllowExternalUrls && !IsOfflineNetworkUrl(apiUrl))
        {
            return new ImageCrawlResult(
                false,
                null,
                null,
                null,
                "External crawler URLs are disabled. Configure a localhost or private-network API URL, or explicitly enable ImageCrawler:AllowExternalUrls.",
                403);
        }

        var sw = Stopwatch.StartNew();
        var trimmedPath = CombineImagePath(imagePath, null);
        var targetUrl = apiUrl.Trim();
        var originalApiUrl = targetUrl;
        var isProxyApi = targetUrl.Contains(":336") || targetUrl.Contains("/api/image");
        var requestedMode = customVp?.Trim().ToLowerInvariant();
        var requestsVp0 = string.IsNullOrWhiteSpace(requestedMode) || requestedMode == "0" || requestedMode == "auto";
        var requestsBrowseImage = !requestsVp0 && requestedMode != "zip";
        var isCpAoiPath = IsCpAoiImagePath(trimmedPath);

        // VP0 CPAOI thumbnail only: POST /api/image/get with folder/file (or ZIP+entry).
        // VP1-4/original keep search-panels -> panel-images -> get.
        if (isProxyApi && isCpAoiPath && (requestsVp0 || requestedMode == "zip"))
        {
            var cpAoiResult = await TryDownloadViaCpAoiZipServiceAsync(targetUrl, trimmedPath, customVp, cancellationToken);
            if (cpAoiResult != null && cpAoiResult.Success && cpAoiResult.ImageBytes is { Length: > 0 })
            {
                sw.Stop();
                return cpAoiResult with { LatencyMs = (int)sw.ElapsedMilliseconds };
            }

            if (requestedMode == "zip")
            {
                sw.Stop();
                return new ImageCrawlResult(
                    false,
                    null,
                    null,
                    null,
                    "CPAOI ZIP image was not found through /api/image/get.",
                    404,
                    (int)sw.ElapsedMilliseconds);
            }
        }

        // VP1-4/original: search-panels -> panel-images -> get.
        if (isProxyApi && requestsBrowseImage && TryBuildContext(trimmedPath, featureJson, customVp, out var browseContext, out _) && browseContext != null)
        {
            var proxyBaseUrl = ExtractProxyBaseUrl(targetUrl);
            var browseResult = await TryDownloadViaVpBrowseServiceAsync(proxyBaseUrl, browseContext, customVp, cancellationToken);
            if (browseResult != null && browseResult.Success && browseResult.ImageBytes is { Length: > 0 })
            {
                sw.Stop();
                return browseResult with { LatencyMs = (int)sw.ElapsedMilliseconds };
            }

            sw.Stop();
            return new ImageCrawlResult(
                false,
                null,
                null,
                null,
                $"Requested image mode '{customVp}' was not found through folder browse API.",
                404,
                (int)sw.ElapsedMilliseconds);
        }

        if (isProxyApi && requestsBrowseImage)
        {
            sw.Stop();
            return new ImageCrawlResult(false, null, null, null, $"Cannot build folder browse context from image_path for mode '{customVp}'.", 400, (int)sw.ElapsedMilliseconds);
        }

        var useAnywherePixPayload = targetUrl.Contains("/api/image/get", StringComparison.OrdinalIgnoreCase);

        // 1. URL Template Substitution
        if (targetUrl.Contains("{path}", StringComparison.OrdinalIgnoreCase))
        {
            targetUrl = targetUrl.Replace("{path}", Uri.EscapeDataString(trimmedPath), StringComparison.OrdinalIgnoreCase);
        }
        else if (targetUrl.Contains("{image_path}", StringComparison.OrdinalIgnoreCase))
        {
            targetUrl = targetUrl.Replace("{image_path}", Uri.EscapeDataString(trimmedPath), StringComparison.OrdinalIgnoreCase);
        }
        else if (targetUrl.Contains("{imagePath}", StringComparison.OrdinalIgnoreCase))
        {
            targetUrl = targetUrl.Replace("{imagePath}", Uri.EscapeDataString(trimmedPath), StringComparison.OrdinalIgnoreCase);
        }
        else if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var separator = targetUrl.Contains('?') ? "&" : "?";
            targetUrl = $"{targetUrl}{separator}path={Uri.EscapeDataString(trimmedPath)}";
        }

        try
        {
            HttpResponseMessage response;
            if (useAnywherePixPayload || string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var payload = useAnywherePixPayload
                    ? JsonSerializer.Serialize(BuildAnywherePixRequest(trimmedPath, featureJson, customVp, imageRoot: ImageRoot))
                    : JsonSerializer.Serialize(new
                    {
                        path = trimmedPath,
                        image_path = trimmedPath,
                        imagePath = trimmedPath,
                        filePath = trimmedPath
                    });
                using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                LogOutgoingImageApiRequest("AnywherePix.get", request, payload);
                response = await _httpClient.SendAsync(request, cancellationToken);

                // If proxy returned 404 with "chưa đăng ký" for AgentName AnywherePix, auto-retry with AgentName = Line
                if (!response.IsSuccessStatusCode && useAnywherePixPayload)
                {
                    var checkText = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (checkText.Contains("chưa đăng ký", StringComparison.OrdinalIgnoreCase))
                    {
                        var retryPayload = JsonSerializer.Serialize(BuildAnywherePixRequest(trimmedPath, featureJson, customVp, fallbackAgentToLine: true, imageRoot: ImageRoot));
                        using var retryReq = new HttpRequestMessage(HttpMethod.Post, targetUrl)
                        {
                            Content = new StringContent(retryPayload, Encoding.UTF8, "application/json")
                        };
                        retryReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                        retryReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        LogOutgoingImageApiRequest("AnywherePix.get.retryAgent", retryReq, retryPayload);
                        response = await _httpClient.SendAsync(retryReq, cancellationToken);
                    }
                }
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                LogOutgoingImageApiRequest("ImageAPI.GET", request, null);
                response = await _httpClient.SendAsync(request, cancellationToken);

                // If GET was rejected with 405 Method Not Allowed, gracefully fallback to POST JSON
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    var cleanEndpoint = apiUrl.Split('?')[0];
                    var payload = JsonSerializer.Serialize(new { path = trimmedPath, image_path = trimmedPath });
                    using var postReq = new HttpRequestMessage(HttpMethod.Post, cleanEndpoint)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    postReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                    postReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    LogOutgoingImageApiRequest("ImageAPI.POST.fallback405", postReq, payload);
                    response = await _httpClient.SendAsync(postReq, cancellationToken);
                }
            }

            sw.Stop();
            var latency = (int)sw.ElapsedMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var errBody = string.Empty;
                try { errBody = await response.Content.ReadAsStringAsync(cancellationToken); } catch { }
                var status = (int)response.StatusCode;
                var friendlyError = TryExtractErrorMessage(errBody) ?? $"API returned HTTP {status} {response.ReasonPhrase}: {(errBody.Length > 200 ? errBody[..200] + "..." : errBody)}";
                if (isProxyApi && requestsVp0)
                {
                    var backfill = await TryDownloadVp0BackfillAsync(originalApiUrl, trimmedPath, featureJson, friendlyError, cancellationToken);
                    if (backfill is not null) return backfill with { LatencyMs = (int)sw.ElapsedMilliseconds };
                }
                return new ImageCrawlResult(
                    false,
                    null,
                    null,
                    null,
                    friendlyError,
                    status,
                    latency
                );
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;

            // Direct binary image stream
            if (mediaType.StartsWith("image/") || mediaType == "application/octet-stream")
            {
                var rawBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var ctype = mediaType.StartsWith("image/") ? mediaType : DetectImageMimeType(rawBytes, trimmedPath);
                return new ImageCrawlResult(true, rawBytes, ctype, null, null, (int)response.StatusCode, latency);
            }

            // JSON or text with base64 payload
            var textContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var (parsedBytes, detectedCtype) = TryExtractBase64FromResponse(textContent, trimmedPath);
            if (parsedBytes != null && parsedBytes.Length > 0)
            {
                return new ImageCrawlResult(true, parsedBytes, detectedCtype, null, null, (int)response.StatusCode, latency);
            }

            // If JSON contains a direct download URL
            var directUrl = TryExtractUrlFromJson(textContent);
            if (!string.IsNullOrWhiteSpace(directUrl))
            {
                using var followReq = new HttpRequestMessage(HttpMethod.Get, directUrl);
                LogOutgoingImageApiRequest("ImageAPI.followUrl", followReq, null);
                var followResp = await _httpClient.SendAsync(followReq, cancellationToken);
                if (followResp.IsSuccessStatusCode)
                {
                    var followBytes = await followResp.Content.ReadAsByteArrayAsync(cancellationToken);
                    var followType = followResp.Content.Headers.ContentType?.MediaType ?? DetectImageMimeType(followBytes, directUrl);
                    return new ImageCrawlResult(true, followBytes, followType, null, null, (int)response.StatusCode, latency);
                }
            }

            if (isProxyApi && requestsVp0)
            {
                var backfill = await TryDownloadVp0BackfillAsync(
                    originalApiUrl,
                    trimmedPath,
                    featureJson,
                    $"Primary image_path response did not contain a valid image (Content-Type: {mediaType}).",
                    cancellationToken);
                if (backfill is not null) return backfill with { LatencyMs = (int)sw.ElapsedMilliseconds };
            }

            return new ImageCrawlResult(
                false,
                null,
                null,
                null,
                $"Could not parse valid image binary or base64 from response. Content-Type: {mediaType}",
                (int)response.StatusCode,
                latency
            );
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            if (isProxyApi && requestsVp0)
            {
                var backfill = await TryDownloadVp0BackfillAsync(originalApiUrl, trimmedPath, featureJson, ex.Message, cancellationToken);
                if (backfill is not null) return backfill with { LatencyMs = (int)sw.ElapsedMilliseconds };
            }
            return new ImageCrawlResult(false, null, null, null, $"Request timed out connecting to {targetUrl}: {ex.Message}", 408, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (isProxyApi && requestsVp0)
            {
                var backfill = await TryDownloadVp0BackfillAsync(originalApiUrl, trimmedPath, featureJson, ex.Message, cancellationToken);
                if (backfill is not null) return backfill with { LatencyMs = (int)sw.ElapsedMilliseconds };
            }
            return new ImageCrawlResult(false, null, null, null, $"Connection error to {targetUrl}: {ex.Message}", 500, (int)sw.ElapsedMilliseconds);
        }
    }

    private async Task<ImageCrawlResult?> TryDownloadVp0BackfillAsync(
        string apiUrl,
        string imagePath,
        string? featureJson,
        string primaryFailure,
        CancellationToken cancellationToken)
    {
        // Keep the edge API contract for the default image: exact source image_path + VP=0.
        if (!TryBuildContext(imagePath, featureJson, "0", out var context, out var contextError) || context is null)
        {
            _logger.LogWarning("[VP0_Backfill] Primary request failed and browse context could not be built. PrimaryError={PrimaryError}, ContextError={ContextError}, ImagePath={ImagePath}",
                primaryFailure, contextError, imagePath);
            return null;
        }

        _logger.LogWarning("[VP0_Backfill] Primary image_path request failed. Falling back to folder browse API. PrimaryError={PrimaryError}, Line={Line}, Zone={Zone}, PanelId={PanelId}",
            primaryFailure, context.Line, context.Zone, context.PanelId);

        var result = await TryDownloadViaVpBrowseServiceAsync(
            ExtractProxyBaseUrl(apiUrl),
            context,
            "0",
            cancellationToken);

        if (result is { Success: true, ImageBytes.Length: > 0 })
        {
            _logger.LogInformation("[VP0_Backfill] Successfully recovered VP0 via folder browse API. PanelId={PanelId}, Bytes={Bytes}",
                context.PanelId, result.ImageBytes.Length);
            return result;
        }

        _logger.LogWarning("[VP0_Backfill] Folder browse API could not recover VP0. PanelId={PanelId}", context.PanelId);
        return null;
    }

    public async Task<ImageCrawlResult> CrawlAndCacheDefectImageAsync(
        Guid defectId,
        bool forceRefresh = false,
        string? customApiUrl = null,
        string? customVp = null,
        CancellationToken cancellationToken = default)
    {
        var defect = await _db.DefectRecords.FirstOrDefaultAsync(d => d.Id == defectId, cancellationToken);
        if (defect == null)
        {
            return new ImageCrawlResult(false, null, null, null, $"Defect {defectId} not found.", 404);
        }

        var (resolvedApiUrl, _) = await ResolveApiUrlAndVpModeForDefectAsync(defect, cancellationToken);
        var apiUrl = !string.IsNullOrWhiteSpace(customApiUrl) ? customApiUrl : resolvedApiUrl;

        // ImageUri is the immutable source DB image_path. ImageUrl is only a VP0 cache URL
        // and must never be parsed as a machine image source for VP1-4/original requests.
        var imagePath = defect.ImageUri;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return new ImageCrawlResult(false, null, null, null, "Defect record does not have an image_path (ImageUri is empty).", 400);
        }

        var featureJson = defect.FeatureJson ?? defect.CustomFeatureJson;
        if (string.IsNullOrWhiteSpace(featureJson))
        {
            var featureLocation = defect.FeatureUrl ?? defect.Feature1StorageUri;
            if (!string.IsNullOrWhiteSpace(featureLocation))
            {
                featureJson = await _storage.ReadTextAsync(featureLocation, cancellationToken);
            }
        }

        // Default VP is ALWAYS "0" (thumbnail) unless explicitly requested or requested as real VP
        string effectiveVp;
        if (string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase))
        {
            effectiveVp = ResolveOriginalDefectVp(imagePath) ?? "0";
        }
        else if (string.Equals(customVp, "real", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "actual", StringComparison.OrdinalIgnoreCase))
        {
            effectiveVp = ResolveRealDefectVp(imagePath, featureJson);
        }
        else if (!string.IsNullOrWhiteSpace(customVp) && customVp != "auto" && customVp != "all")
        {
            effectiveVp = customVp.Trim();
        }
        else
        {
            effectiveVp = "0";
        }

        // VP0 cache-first: return the stored SeaweedFS/local object before touching the source API.
        if (!forceRefresh && effectiveVp == "0" && !string.IsNullOrWhiteSpace(defect.ImageUrl))
        {
            if (defect.ImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                defect.ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new ImageCrawlResult(true, null, "image/jpeg", defect.ImageUrl, null, 200);
            }

            try
            {
                await using var stored = await _storage.OpenReadStreamAsync(defect.ImageUrl, cancellationToken);
                if (stored?.Stream is not null)
                {
                    using var ms = new MemoryStream();
                    await stored.Stream.CopyToAsync(ms, cancellationToken);
                    var bytes = ms.ToArray();
                    if (bytes.Length > 0)
                    {
                        _logger.LogInformation("[VP_Image] VP0 cache hit from storage. DefectId={DefectId}, ImageUrl={ImageUrl}, Bytes={Bytes}",
                            defect.Id, defect.ImageUrl, bytes.Length);
                        return new ImageCrawlResult(true, bytes, stored.ContentType ?? "image/jpeg", defect.ImageUrl, null, 200);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[VP_Image] VP0 storage cache read failed. Backfilling from source API. DefectId={DefectId}, ImageUrl={ImageUrl}",
                    defect.Id, defect.ImageUrl);
            }
        }

        _logger.LogInformation("[VP_Image] Crawl requested. DefectId={DefectId}, KeyId={KeyId}, VP={VP}, ImagePath={ImagePath}", defect.Id, defect.KeyId, effectiveVp, imagePath);

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            _logger.LogWarning("[VP_Image] No Image Crawler API configured for defect {DefectId} (KeyId={KeyId}).", defect.Id, defect.KeyId);
            return new ImageCrawlResult(
                false,
                null,
                null,
                null,
                $"No Image Crawler API configured for defect's machine/line. Please configure ImageCrawlerApiUrl in Pipeline or Database Connection.",
                400
            );
        }

        // Preserve the semantic original/path request for the folder browse workflow.
        // The resolved numeric VP remains used only for cache naming and display.
        var vpToUse = string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase)
            ? "original"
            : effectiveVp;

        var crawlResult = await CrawlImageInternalAsync(
            imagePath,
            apiUrl,
            apiUrl.Contains("/api/image/get", StringComparison.OrdinalIgnoreCase) ? "POST" : "GET",
            featureJson,
            vpToUse,
            cancellationToken);
        if (!crawlResult.Success || crawlResult.ImageBytes == null || crawlResult.ImageBytes.Length == 0)
        {
            _logger.LogWarning("[VP_Image] Crawl failed for DefectId={DefectId}, VP={VP}: {ErrorMessage}", defect.Id, effectiveVp, crawlResult.ErrorMessage);
            return crawlResult;
        }

        // VP1-4/original are strictly on-demand proxy responses. Do not upload them
        // to SeaweedFS and do not update DefectRecord.ImageUrl.
        if (effectiveVp != "0" || string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[VP_Image] On-demand image returned without persistence. DefectId={DefectId}, Mode={Mode}, VP={VP}, Bytes={Bytes}",
                defect.Id, customVp, effectiveVp, crawlResult.ImageBytes.Length);
            return crawlResult with { StoredUrl = null };
        }

        // VP0 only: save crawled image to Storage / SeaweedFS and update ImageUrl.
        string storedUrl;
        try
        {
            var ext = (crawlResult.ContentType?.Contains("png") == true) ? "png" : "jpg";
            var isOriginalRequest = string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase);
            var cacheVariant = isOriginalRequest ? "original" : effectiveVp;
            var fileName = cacheVariant == "0"
                ? $"{defect.Id:N}.{ext}"
                : isOriginalRequest
                    ? $"{defect.Id:N}_original.{ext}"
                    : $"{defect.Id:N}_vp{cacheVariant}.{ext}";

            if (_storage != null && _storage.Mode != "Offline")
            {
                var shardedPath = StoragePathSharder.GetShardedDefectPath(defect.KeyId, fileName);
                using var ms = new MemoryStream(crawlResult.ImageBytes);
                var storedObj = await _storage.UploadImageAsync("defect-images", shardedPath, ms, crawlResult.ContentType, cancellationToken);
                storedUrl = storedObj.Uri;
            }
            else
            {
                storedUrl = cacheVariant == "0"
                    ? $"/api/v1/storage/defect-images/{defect.Id}"
                    : $"/api/v1/storage/defect-images/{defect.Id}?vp={cacheVariant}";
            }

            if (effectiveVp == "0")
            {
                defect.ImageUrl = storedUrl;
                defect.ImageUri ??= imagePath;
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("[VP_Image] Crawl succeeded for DefectId={DefectId}, VP={VP}: {Bytes} bytes in {Latency}ms. Saved to {StoredUrl}",
                defect.Id, effectiveVp, crawlResult.ImageBytes.Length, crawlResult.LatencyMs, storedUrl);

            return crawlResult with { StoredUrl = storedUrl };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload crawled defect image to SeaweedFS: {Message}", ex.Message);
            var isOriginalRequest = string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase);
            var cacheVariant = isOriginalRequest ? "original" : effectiveVp;
            storedUrl = cacheVariant == "0"
                ? $"/api/v1/storage/defect-images/{defect.Id}"
                : $"/api/v1/storage/defect-images/{defect.Id}?vp={cacheVariant}";
            if (effectiveVp == "0")
            {
                defect.ImageUrl = storedUrl;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return crawlResult with { StoredUrl = storedUrl };
        }
    }

    public async Task<TestImageCrawlerApiResponse> TestApiAsync(TestImageCrawlerApiRequest request, CancellationToken cancellationToken = default)
    {
        var result = await CrawlImageAsync(request.ImagePath, request.ApiUrl, request.Method, customVp: null, cancellationToken: cancellationToken);
        var base64Preview = result.ImageBytes != null && result.ImageBytes.Length > 0
            ? (result.ImageBytes.Length <= 1024 * 1024
                ? $"data:{result.ContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(result.ImageBytes)}"
                : $"data:{result.ContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(result.ImageBytes.Take(1024).ToArray())}...")
            : null;

        return new TestImageCrawlerApiResponse(
            result.Success,
            result.StatusCode,
            result.ContentType,
            result.ImageBytes?.LongLength ?? 0,
            base64Preview,
            result.ErrorMessage,
            result.LatencyMs
        );
    }

    public async Task<TestDefectCrawlResponse> TestDefectCrawlAsync(
        Guid defectId,
        string? customApiUrl = null,
        string? customVp = null,
        CancellationToken cancellationToken = default)
    {
        var defect = await _db.DefectRecords.AsNoTracking().FirstOrDefaultAsync(d => d.Id == defectId, cancellationToken);
        if (defect == null)
        {
            return new TestDefectCrawlResponse(
                false, defectId, "Unknown", "", null, "", null, 404, null, 0, null, $"Defect with ID '{defectId}' not found in database.", 0);
        }

        var (resolvedApiUrl, resolvedVpMode) = await ResolveApiUrlAndVpModeForDefectAsync(defect, cancellationToken);
        var apiUrl = !string.IsNullOrWhiteSpace(customApiUrl) ? customApiUrl.Trim() : resolvedApiUrl;
        apiUrl ??= DefaultApiUrl;

        var vpToUse = !string.IsNullOrWhiteSpace(customVp)
            ? customVp
            : (string.Equals(resolvedVpMode, "auto", StringComparison.OrdinalIgnoreCase) ? null : resolvedVpMode);

        var featureJson = defect.FeatureJson ?? defect.CustomFeatureJson;
        if (string.IsNullOrWhiteSpace(featureJson))
        {
            var featureLoc = defect.FeatureUrl ?? defect.Feature1StorageUri;
            if (!string.IsNullOrWhiteSpace(featureLoc))
            {
                featureJson = await _storage.ReadTextAsync(featureLoc, cancellationToken);
            }
        }

        var imagePath = defect.ImageUri ?? defect.ImageUrl ?? string.Empty;
        var requestPayload = BuildAnywherePixRequest(imagePath, featureJson, vpToUse, imageRoot: ImageRoot);

        var crawlResult = await CrawlImageInternalAsync(imagePath, apiUrl, "POST", featureJson, vpToUse, cancellationToken);

        var base64Preview = crawlResult.ImageBytes != null && crawlResult.ImageBytes.Length > 0
            ? $"data:{crawlResult.ContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(crawlResult.ImageBytes)}"
            : null;

        return new TestDefectCrawlResponse(
            crawlResult.Success,
            defect.Id,
            defect.DefectName,
            defect.KeyId,
            imagePath,
            apiUrl,
            requestPayload,
            crawlResult.StatusCode,
            crawlResult.ContentType,
            crawlResult.ImageBytes?.LongLength ?? 0,
            base64Preview,
            crawlResult.ErrorMessage,
            crawlResult.LatencyMs
        );
    }

    public async Task<TestPathCrawlResponse> TestPathCrawlAsync(
        string imagePath,
        string? customApiUrl = null,
        string? featureJson = null,
        string? customVp = null,
        CancellationToken cancellationToken = default)
    {
        var apiUrl = !string.IsNullOrWhiteSpace(customApiUrl) ? customApiUrl.Trim() : DefaultApiUrl;
        var requestPayload = BuildAnywherePixRequest(imagePath, featureJson, customVp, imageRoot: ImageRoot);

        var crawlResult = await CrawlImageInternalAsync(imagePath, apiUrl, "POST", featureJson, customVp, cancellationToken);

        var base64Preview = crawlResult.ImageBytes != null && crawlResult.ImageBytes.Length > 0
            ? $"data:{crawlResult.ContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(crawlResult.ImageBytes)}"
            : null;

        return new TestPathCrawlResponse(
            crawlResult.Success,
            imagePath,
            apiUrl,
            requestPayload,
            crawlResult.StatusCode,
            crawlResult.ContentType,
            crawlResult.ImageBytes?.LongLength ?? 0,
            base64Preview,
            crawlResult.ErrorMessage,
            crawlResult.LatencyMs
        );
    }

    public async Task<BatchImageCrawlResponse> BatchCrawlDefectImagesAsync(
        BatchImageCrawlRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _db.DefectRecords.AsQueryable();

        if (request.DefectIds != null && request.DefectIds.Count > 0)
        {
            query = query.Where(d => request.DefectIds.Contains(d.Id));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.KeyId))
            {
                query = query.Where(d => d.KeyId == request.KeyId);
            }
            // Only crawl defects judged NG / N (never crawl OK defects in automated batch)
            query = query.Where(d => d.Judge == "NG" || d.Judge == "N" || d.Judge == "N/A" || d.Judge == "NA");
        }

        if (!request.ForceRefresh)
        {
            query = query.Where(d => string.IsNullOrEmpty(d.ImageUrl) || d.ImageUrl.StartsWith("/api/v1/"));
        }

        query = query.Where(d => d.ImageUri != null && d.ImageUri != "");

        var maxLimit = Math.Clamp(request.Limit, 1, Math.Max(1, _options.BatchMaxItems));
        var targetDefects = await query.Take(maxLimit).ToListAsync(cancellationToken);

        int crawled = 0;
        int failed = 0;
        var messages = new List<string>();

        foreach (var def in targetDefects)
        {
            try
            {
                var res = await CrawlAndCacheDefectImageAsync(def.Id, request.ForceRefresh, customApiUrl: null, customVp: "0", cancellationToken: cancellationToken);
                if (res.Success)
                {
                    crawled++;
                    messages.Add($"✓ Defect {def.Id:N} ({def.DefectName}): Crawled {res.ImageBytes?.Length ?? 0} bytes from PC API.");
                }
                else
                {
                    failed++;
                    messages.Add($"✗ Defect {def.Id:N} ({def.DefectName}): {res.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"✗ Defect {def.Id:N}: {ex.Message}");
            }
        }

        return new BatchImageCrawlResponse(
            Success: failed == 0 || crawled > 0,
            TotalRequested: targetDefects.Count,
            CrawledCount: crawled,
            FailedCount: failed,
            Messages: messages
        );
    }

    public async Task<string?> ResolveApiUrlForDefectAsync(DefectRecordEntity defect, CancellationToken cancellationToken = default)
    {
        var (url, _) = await ResolveApiUrlAndVpModeForDefectAsync(defect, cancellationToken);
        return url;
    }

    public async Task<(string? ApiUrl, string VpMode)> ResolveApiUrlAndVpModeForDefectAsync(DefectRecordEntity defect, CancellationToken cancellationToken = default)
    {
        DatabaseConnectionEntity? conn = null;
        if (defect.ConnectionId.HasValue && defect.ConnectionId.Value != Guid.Empty)
        {
            conn = await _db.DatabaseConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == defect.ConnectionId.Value, cancellationToken);
        }

        var product = await _db.ProductRecords.AsNoTracking()
            .FirstOrDefaultAsync(p => p.KeyId == defect.KeyId, cancellationToken);

        var machine = !string.IsNullOrWhiteSpace(defect.MachineCode) && defect.MachineCode != "MACHINE-A"
            ? defect.MachineCode
            : (product?.MachineCode ?? defect.MachineCode);
        var line = !string.IsNullOrWhiteSpace(defect.LineCode) && defect.LineCode != "LINE-1"
            ? defect.LineCode
            : (product?.LineCode ?? defect.LineCode);

        OverkillPipelineConfigEntity? pipeline = null;

        if (!string.IsNullOrWhiteSpace(machine) || !string.IsNullOrWhiteSpace(line))
        {
            pipeline = await _db.OverkillPipelineConfigs.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.MachineCode == machine && p.LineCode == line,
                    cancellationToken);

            var vpMode = pipeline?.ImageCrawlerVpMode ?? "0";

            if (!string.IsNullOrWhiteSpace(pipeline?.ImageCrawlerApiUrl))
            {
                var url = pipeline.ImageCrawlerApiUrl.Trim();
                if (url.Contains("{host}", StringComparison.OrdinalIgnoreCase) && pipeline.DatabaseConnectionId != Guid.Empty)
                {
                    conn = await _db.DatabaseConnections.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == pipeline.DatabaseConnectionId, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(conn?.Host))
                    {
                        url = url.Replace("{host}", conn.Host.Trim(), StringComparison.OrdinalIgnoreCase);
                    }
                }
                return (url, vpMode);
            }

            if (pipeline != null && pipeline.DatabaseConnectionId != Guid.Empty)
            {
                conn = await _db.DatabaseConnections.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == pipeline.DatabaseConnectionId, cancellationToken);
            }
        }

        if (conn == null && (!string.IsNullOrWhiteSpace(machine) || !string.IsNullOrWhiteSpace(line)))
        {
            conn = await _db.DatabaseConnections.AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.MachineCode == machine && c.LineCode == line,
                    cancellationToken);
        }

        var resolvedVp = pipeline?.ImageCrawlerVpMode ?? conn?.ImageCrawlerVpMode ?? "0";

        if (conn != null)
        {
            var rawApiUrl = !string.IsNullOrWhiteSpace(conn.ImageCrawlerApiUrl)
                ? conn.ImageCrawlerApiUrl.Trim()
                : conn.ApiBaseUrl?.Trim();

            if (!string.IsNullOrWhiteSpace(rawApiUrl))
            {
                if (rawApiUrl.Contains("{host}", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conn.Host))
                {
                    rawApiUrl = rawApiUrl.Replace("{host}", conn.Host.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                return (rawApiUrl, resolvedVp);
            }

            // If connection has host and port, suggest default crawler format
            if (!string.IsNullOrWhiteSpace(conn.Host) && conn.Host != "127.0.0.1" && conn.Host != "localhost")
            {
                return (DefaultApiUrl, resolvedVp);
            }
        }

        return (DefaultApiUrl, resolvedVp);
    }

    public static string ExtractProxyBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return FallbackAnywherePixApiUrl.Replace("/api/image/get", "");
        var idx = url.IndexOf("/api/image", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            return url[..idx].TrimEnd('/');
        }
        return url.TrimEnd('/');
    }

    private static bool IsOfflineNetworkUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(uri.Host, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast;
    }

    // =========================================================================
    // Dedicated CPAOI Image Service (ZIP Defect Thumbnail Extraction)
    // =========================================================================
    public static bool IsCpAoiImagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/');
        return normalized.IndexOf("CP-AOI", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("CP_AOI", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("CP AOI", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("CPAOI", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool TryParseCpAoiZipPath(
        string rawPath,
        out string? zipFilePath,
        out string? zipEntry,
        out string lineName)
    {
        zipFilePath = null;
        zipEntry = null;
        lineName = "301_B";

        if (string.IsNullOrWhiteSpace(rawPath)) return false;

        var normalized = CombineImagePath(rawPath, null).Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2) return false;

        var lastIsFile = IsImageFileName(parts[^1]);
        zipEntry = lastIsFile ? parts[^1] : null;
        var zipFolderName = lastIsFile ? parts[^2] : parts[^1];

        foreach (var part in parts)
        {
            var match = Regex.Match(part, @"(?<station>\d{3})[-_](?<lane>[A-Za-z])");
            if (match.Success)
            {
                lineName = $"{match.Groups["station"].Value}_{match.Groups["lane"].Value.ToUpperInvariant()}";
                break;
            }
        }

        var parentFolder = lastIsFile
            ? string.Join("/", parts, 0, parts.Length - 1)
            : string.Join("/", parts);
        var relZipPath = $"{parentFolder}/{zipFolderName}.zip";

        if (relZipPath.StartsWith("D:", StringComparison.OrdinalIgnoreCase) ||
            relZipPath.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
            relZipPath.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
        {
            zipFilePath = relZipPath.Replace('/', '\\');
        }
        else
        {
            const string uncRoot = @"\\192.168.200.121\d\defectimage";
            zipFilePath = uncRoot + "\\" + relZipPath.Replace('/', '\\');
        }

        return true;
    }

    public static List<string> BuildCandidateZipPaths(string zipFilePath, string rawPath, string lineName)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(zipFilePath))
        {
            AddCandidate(candidates, zipFilePath);
        }

        if (!string.IsNullOrWhiteSpace(zipFilePath))
        {
            var normalized = zipFilePath.Replace('/', '\\');

            // 1. Swap \d\defectimage vs \defectimage vs \DefectImage vs \d\DefectImage
            AddCandidate(candidates, normalized.Replace(@"\d\defectimage\", @"\defectimage\"));
            AddCandidate(candidates, normalized.Replace(@"\defectimage\", @"\d\defectimage\"));
            AddCandidate(candidates, normalized.Replace(@"\defectimage\", @"\DefectImage\"));
            AddCandidate(candidates, normalized.Replace(@"\d\defectimage\", @"\d\DefectImage\"));

            // 2. Fallback without nested folder (e.g. .../VP01/Folder.zip instead of .../VP01/Folder/Folder.zip)
            var parts = CombineImagePath(rawPath, null).Replace('\\', '/').Trim('/').Split('/');
            if (parts.Length >= 2)
            {
                var lastIsFile = IsImageFileName(parts[^1]);
                var zipFolderName = lastIsFile ? parts[^2] : parts[^1];
                var singleZipPath = normalized.Replace($@"\{zipFolderName}\{zipFolderName}.zip", $@"\{zipFolderName}.zip");
                AddCandidate(candidates, singleZipPath);
                AddCandidate(candidates, singleZipPath.Replace(@"\d\defectimage\", @"\defectimage\"));
                AddCandidate(candidates, singleZipPath.Replace(@"\defectimage\", @"\d\defectimage\"));
            }

            // 3. Local drive paths D:\...
            int idx = normalized.IndexOf(@"\defectimage\", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = normalized.IndexOf(@"\DefectImage\", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var sub = normalized[idx..];
                AddCandidate(candidates, @"D:" + sub);
                AddCandidate(candidates, @"D:" + sub.Replace(@"\defectimage\", @"\DefectImage\"));
            }
        }

        // Direct image paths (in case image is stored loose)
        foreach (var directPath in BuildCandidateDirectImagePaths(rawPath, lineName))
        {
            AddCandidate(candidates, directPath);
        }

        AddCpAoiMachineNameVariants(candidates);
        return candidates;
    }

    public static List<string> BuildCandidateDirectImagePaths(string rawPath, string lineName)
    {
        var candidates = new List<string>();
        if (string.IsNullOrWhiteSpace(rawPath)) return candidates;

        var normalizedRaw = rawPath.Trim().Replace('/', '\\').Trim('\\');
        var directPath = normalizedRaw;

        if (!directPath.StartsWith("\\\\") && !Regex.IsMatch(directPath, @"^[A-Za-z]:\\"))
        {
            const string uncRoot = @"\\192.168.200.121\d\defectimage";
            directPath = uncRoot + "\\" + normalizedRaw;
        }

        AddCandidate(candidates, directPath);
        AddCandidate(candidates, directPath.Replace(@"\d\defectimage\", @"\defectimage\"));
        AddCandidate(candidates, directPath.Replace(@"\defectimage\", @"\d\defectimage\"));

        var idxSource = directPath.Replace('/', '\\');
        int idx = idxSource.IndexOf(@"\defectimage\", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = idxSource.IndexOf(@"\DefectImage\", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var sub = idxSource[idx..];
            AddCandidate(candidates, @"D:" + sub);
            AddCandidate(candidates, @"D:" + sub.Replace(@"\defectimage\", @"\DefectImage\"));
        }

        AddCpAoiMachineNameVariants(candidates);
        return candidates;
    }

    private static void AddCpAoiMachineNameVariants(List<string> candidates)
    {
        if (candidates == null || candidates.Count == 0) return;
        var originals = candidates.ToList();
        foreach (var path in originals)
        {
            AddCandidate(candidates, Regex.Replace(path, @"CP\s+AOI", "CP-AOI", RegexOptions.IgnoreCase));
            AddCandidate(candidates, Regex.Replace(path, @"CP-AOI", "CP AOI", RegexOptions.IgnoreCase));
            AddCandidate(candidates, Regex.Replace(path, @"CP_AOI", "CP-AOI", RegexOptions.IgnoreCase));
            AddCandidate(candidates, Regex.Replace(path, @"CP-AOI", "CP_AOI", RegexOptions.IgnoreCase));
        }
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }

    private static bool IsImageFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProxyLineToStation(string lineName)
    {
        if (!string.IsNullOrWhiteSpace(lineName))
        {
            var match = Regex.Match(lineName.Trim(), @"^(?<station>\d{3})");
            if (match.Success)
            {
                return match.Groups["station"].Value;
            }
        }
        return "301";
    }

    private async Task<ImageCrawlResult?> TryDownloadViaCpAoiZipServiceAsync(
        string targetUrl,
        string rawPath,
        string? customVp,
        CancellationToken cancellationToken)
    {
        if (!TryParseCpAoiZipPath(rawPath, out var zipFilePath, out var zipEntry, out var lineName))
        {
            return null;
        }

        var proxyGetUrl = targetUrl.Contains("/api/image/get", StringComparison.OrdinalIgnoreCase)
            ? targetUrl
            : $"{ExtractProxyBaseUrl(targetUrl)}/api/image/get";

        var proxyLine = NormalizeProxyLineToStation(lineName);
        var vp = string.Equals(customVp, "zip", StringComparison.OrdinalIgnoreCase)
            ? ResolveOriginalDefectVp(rawPath) ?? "0"
            : string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase)
            ? ResolveOriginalDefectVp(rawPath) ?? "0"
            : !string.IsNullOrWhiteSpace(customVp) && customVp != "auto" && customVp != "all" ? customVp.Trim() : "0";

        var laneMatch = Regex.Match(lineName, @"[_-](?<lane>[A-Za-z])$");
        var laneZone = laneMatch.Success ? laneMatch.Groups["lane"].Value.ToUpperInvariant() : "A";
        var zonesToTry = BuildZonesToTry(laneZone);

        var candidatePaths = BuildCandidateZipPaths(zipFilePath!, rawPath, lineName);

        _logger.LogInformation("[CPAOI_Image] Starting CPAOI ZIP crawl. Line={Line}, ZipEntry={ZipEntry}, CandidateCount={Count}",
            proxyLine, zipEntry, candidatePaths.Count);

        foreach (var zone in zonesToTry)
        {
            foreach (var currentPath in candidatePaths)
            {
                try
                {
                    bool isDirect = IsImageFilePath(currentPath);
                    var payload = JsonSerializer.Serialize(new
                    {
                        AgentName = proxyLine,
                        Line = proxyLine,
                        Zone = zone,
                        VP = vp,
                        FilePath = currentPath,
                        ImagePath = rawPath,
                        image_path = rawPath,
                        FileName = zipEntry,
                        ZipEntry = isDirect ? null : zipEntry
                    });

                    using var request = new HttpRequestMessage(HttpMethod.Post, proxyGetUrl)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    LogOutgoingImageApiRequest("CPAOI.image.get", request, payload);
                    using var response = await _httpClient.SendAsync(request, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (bytes.Length > 0)
                        {
                            var ctype = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                            _logger.LogInformation("[CPAOI_Image] Successfully retrieved CPAOI thumbnail ({Bytes:N0} bytes) from Path={Path}, Zone={Zone}, VP={VP}",
                                bytes.Length, currentPath, zone, vp);
                            return new ImageCrawlResult(true, bytes, ctype, null, null, 200, 0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[CPAOI_Image] Candidate path {Path} failed: {Message}", currentPath, ex.Message);
                }
            }
        }

        return null;
    }

    public static bool TryBuildContext(
        string rawImagePath,
        string? featureJson,
        string? customVp,
        out VpImageBrowseContext? context,
        out string? error)
    {
        context = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawImagePath))
        {
            error = "image_path is required.";
            return false;
        }

        var normalized = rawImagePath.Trim().Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 5)
        {
            error = $"image_path invalid format: {rawImagePath}";
            return false;
        }

        if (!int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day))
        {
            error = $"Could not parse date from image_path: {rawImagePath}";
            return false;
        }

        var machinePart = parts.FirstOrDefault(IsCpAoiMachinePart) ?? "";
        var stationMatch = Regex.Match(machinePart, @"(?<station>\d{3})[-_](?<lane>[A-Za-z])");
        var station = stationMatch.Success ? stationMatch.Groups["station"].Value : "";
        var lane = stationMatch.Success ? stationMatch.Groups["lane"].Value.ToUpperInvariant() : "A";

        var machineIndex = Array.FindIndex(parts, IsCpAoiMachinePart);
        var panelId = machineIndex >= 0 && machineIndex + 1 < parts.Length ? parts[machineIndex + 1] : null;
        if (string.IsNullOrWhiteSpace(panelId))
        {
            error = $"Could not parse PanelId from image_path: {rawImagePath}";
            return false;
        }

        var vpFolder = parts.FirstOrDefault(x => Regex.IsMatch(x ?? "", @"^VP\d+$", RegexOptions.IgnoreCase)) ?? "";
        string vp = "0";
        if (string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase))
        {
            vp = ResolveOriginalDefectVp(rawImagePath) ?? "0";
        }
        else if (string.Equals(customVp, "real", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "actual", StringComparison.OrdinalIgnoreCase))
        {
            vp = ResolveRealDefectVp(rawImagePath, featureJson);
        }
        else if (!string.IsNullOrWhiteSpace(customVp) && customVp != "auto" && customVp != "all")
        {
            vp = customVp.Trim().TrimStart('0');
            if (string.IsNullOrWhiteSpace(vp)) vp = "0";
        }
        else if (!string.IsNullOrWhiteSpace(vpFolder))
        {
            var vpMatch = Regex.Match(vpFolder, @"^VP(?<vp>\d+)$", RegexOptions.IgnoreCase);
            if (vpMatch.Success)
            {
                vp = vpMatch.Groups["vp"].Value.TrimStart('0');
                if (string.IsNullOrWhiteSpace(vp)) vp = "0";
            }
        }
        else
        {
            var vpFromFeature = TryExtractVpId(featureJson);
            if (!string.IsNullOrWhiteSpace(vpFromFeature))
            {
                vp = vpFromFeature.Trim().TrimStart('0');
                if (string.IsNullOrWhiteSpace(vp)) vp = "0";
            }
        }

        var targetFileName = parts.LastOrDefault() ?? "";

        context = new VpImageBrowseContext(
            rawImagePath,
            panelId,
            string.IsNullOrWhiteSpace(station) ? "301" : station,
            string.IsNullOrWhiteSpace(station) ? null : $"{station}_{lane}",
            lane,
            vp,
            new DateTime(year, month, day),
            targetFileName
        );

        return true;
    }

    private static bool IsCpAoiMachinePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Regex.IsMatch(value, @"CP\s*-?\s*AOI", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"#\d{3}[-_][A-Za-z]", RegexOptions.IgnoreCase);
    }

    private static string[] BuildZonesToTry(string preferredZone)
    {
        var zones = new List<string>();
        void Add(string z)
        {
            if (!string.IsNullOrWhiteSpace(z) && !zones.Any(x => string.Equals(x, z.Trim(), StringComparison.OrdinalIgnoreCase)))
                zones.Add(z.Trim().ToUpperInvariant());
        }

        Add(preferredZone);
        Add("A");
        Add("B");
        Add("C");
        Add("D");
        return zones.ToArray();
    }

    private static string[] BuildVpsToTry(string contextVp, string? vpMode)
    {
        var vps = new List<string>();
        void Add(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            var clean = v.Trim().TrimStart('0');
            if (string.IsNullOrWhiteSpace(clean)) clean = "0";
            if (!vps.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                vps.Add(clean);
        }

        var mode = vpMode?.Trim().ToLowerInvariant();

        if (mode is "original" or "path")
        {
            // Find the original image folder at exactly the VP encoded in image_path.
            Add(contextVp);
        }
        else if (!string.IsNullOrWhiteSpace(mode) && mode != "all" && mode != "auto" && int.TryParse(mode, out var specificVp))
        {
            // A specifically requested VP must resolve exactly; silently returning VP0 is misleading.
            Add(specificVp.ToString());
        }
        else
        {
            // Default: Always try VP 0 (thumbnail) first, then context VP, then other VPs
            Add("0");
            Add(contextVp);
            Add("1");
            Add("2");
            Add("3");
            Add("4");
        }

        return vps.ToArray();
    }

    private async Task<ImageCrawlResult?> TryDownloadViaVpBrowseServiceAsync(
        string proxyBaseUrl,
        VpImageBrowseContext context,
        string? vpMode,
        CancellationToken cancellationToken)
    {
        var searchUrl = $"{proxyBaseUrl}/api/image/search-panels";
        var zonesToTry = BuildZonesToTry(context.Zone);
        var vpsToTry = BuildVpsToTry(context.Vp, vpMode);

        foreach (var zone in zonesToTry)
        {
            foreach (var vp in vpsToTry)
            {
                try
                {
                    var searchPayload = JsonSerializer.Serialize(new
                    {
                        Line = context.Line,
                        Zone = zone,
                        VP = vp,
                        PanelId = context.PanelId,
                        DateTime = context.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                        ImageFilter = "*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp",
                        ImagePath = context.RawImagePath,
                        image_path = context.RawImagePath,
                        FilePath = context.RawImagePath,
                        FileName = context.TargetFileName
                    });

                    _logger.LogInformation("[VP_Image] Calling proxy search-panels: Line={Line}, Zone={Zone}, VP={VP}, PanelId={PanelId}, Date={Date:yyyy-MM-dd}, ImagePath={ImagePath}",
                        context.Line, zone, vp, context.PanelId, context.Date, context.RawImagePath);

                    using var searchReq = new HttpRequestMessage(HttpMethod.Post, searchUrl)
                    {
                        Content = new StringContent(searchPayload, Encoding.UTF8, "application/json")
                    };
                    LogOutgoingImageApiRequest("VP.browse.search-panels", searchReq, searchPayload);
                    using var searchResp = await _httpClient.SendAsync(searchReq, cancellationToken);
                    if (!searchResp.IsSuccessStatusCode) continue;

                    var searchJson = await searchResp.Content.ReadAsStringAsync(cancellationToken);
                    using var searchDoc = JsonDocument.Parse(searchJson);
                    var root = searchDoc.RootElement;

                    if (!root.TryGetProperty("folders", out var foldersElem) || foldersElem.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var folders = foldersElem.EnumerateArray().ToList();
                    if (folders.Count == 0) continue;

                    _logger.LogInformation("[VP_Image] search-panels found {Count} folder(s) for PanelId={PanelId} in Zone={Zone}, VP={VP}", folders.Count, context.PanelId, zone, vp);

                    // Iterate folders (e.g. OrgBlk, CTQ)
                    foreach (var folder in folders)
                    {
                        if (!folder.TryGetProperty("folderPath", out var fpElem)) continue;
                        var folderPath = fpElem.GetString();
                        if (string.IsNullOrWhiteSpace(folderPath)) continue;

                        var panelImagesUrl = $"{proxyBaseUrl}/api/image/panel-images";
                        var listPayload = JsonSerializer.Serialize(new
                        {
                            AgentName = context.Line,
                            Line = context.Line,
                            Zone = zone,
                            VP = vp,
                            FolderPath = folderPath,
                            Filter = "*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp",
                            Page = 1,
                            PageSize = 500,
                            SortBy = "name",
                            SortDesc = false,
                            ImagePath = context.RawImagePath,
                            image_path = context.RawImagePath,
                            FileName = context.TargetFileName
                        });

                        using var listReq = new HttpRequestMessage(HttpMethod.Post, panelImagesUrl)
                        {
                            Content = new StringContent(listPayload, Encoding.UTF8, "application/json")
                        };
                        LogOutgoingImageApiRequest("VP.browse.panel-images", listReq, listPayload);
                        using var listResp = await _httpClient.SendAsync(listReq, cancellationToken);
                        if (!listResp.IsSuccessStatusCode) continue;

                        var listJson = await listResp.Content.ReadAsStringAsync(cancellationToken);
                        using var listDoc = JsonDocument.Parse(listJson);
                        var listRoot = listDoc.RootElement;

                        JsonElement imagesElem;
                        if (listRoot.TryGetProperty("Data", out var dataElem) && dataElem.TryGetProperty("Images", out var imgs1))
                        {
                            imagesElem = imgs1;
                        }
                        else if (listRoot.TryGetProperty("images", out var imgs2))
                        {
                            imagesElem = imgs2;
                        }
                        else
                        {
                            continue;
                        }

                        if (imagesElem.ValueKind != JsonValueKind.Array) continue;
                        var imageList = imagesElem.EnumerateArray().ToList();
                        if (imageList.Count == 0) continue;

                        // Match target filename if possible
                        var targetImage = imageList[0];
                        if (!string.IsNullOrWhiteSpace(context.TargetFileName))
                        {
                            foreach (var img in imageList)
                            {
                                if (img.TryGetProperty("FileName", out var fnElem))
                                {
                                    var fn = fnElem.GetString();
                                    if (!string.IsNullOrWhiteSpace(fn) &&
                                        (string.Equals(fn, context.TargetFileName, StringComparison.OrdinalIgnoreCase) ||
                                         context.TargetFileName.Contains(fn, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        targetImage = img;
                                        break;
                                    }
                                }
                            }
                        }

                        if (targetImage.TryGetProperty("FilePath", out var finalFilePathElem))
                        {
                            var filePath = finalFilePathElem.GetString();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                var getUrl = $"{proxyBaseUrl}/api/image/get";
                                var getPayload = JsonSerializer.Serialize(new
                                {
                                    AgentName = context.Line,
                                    Line = context.Line,
                                    Zone = zone,
                                    VP = vp,
                                    FilePath = filePath,
                                    ImagePath = context.RawImagePath,
                                    image_path = context.RawImagePath,
                                    FileName = context.TargetFileName
                                });

                                using var getReq = new HttpRequestMessage(HttpMethod.Post, getUrl)
                                {
                                    Content = new StringContent(getPayload, Encoding.UTF8, "application/json")
                                };
                                LogOutgoingImageApiRequest("VP.browse.image.get", getReq, getPayload);
                                using var getResp = await _httpClient.SendAsync(getReq, cancellationToken);
                                if (getResp.IsSuccessStatusCode)
                                {
                                    var bytes = await getResp.Content.ReadAsByteArrayAsync(cancellationToken);
                                    if (bytes.Length > 0)
                                    {
                                        var ctype = getResp.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                                        _logger.LogInformation("[VP_Image] Successfully downloaded image via proxy: {FilePath} (VP={VP}, {Bytes:N0} bytes, {ContentType})",
                                            filePath, vp, bytes.Length, ctype);
                                        return new ImageCrawlResult(true, bytes, ctype, null, null, 200, 0);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "VpImageBrowseService attempt failed for zone {Zone}, vp {VP}", zone, vp);
                }
            }
        }

        return null;
    }

    public static string CombineImagePath(string? folderOrPath, string? imageName)
    {
        var pathCombo = (folderOrPath ?? string.Empty).Trim();
        var file = (imageName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(file)
            && IsImageFileName(file)
            && !IsImageFileName(pathCombo)
            && !pathCombo.EndsWith(file, StringComparison.OrdinalIgnoreCase))
        {
            var slash = pathCombo.Contains('\\') && !pathCombo.Contains('/') ? "\\" : "/";
            pathCombo = pathCombo.EndsWith('/') || pathCombo.EndsWith('\\')
                ? pathCombo + file
                : pathCombo + slash + file;
        }

        return pathCombo.Trim().TrimEnd('/', '\\');
    }

    public static bool IsImageFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path.Replace('/', '\\'));
        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".tif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".tiff", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase);
    }

    public static object BuildAnywherePixRequest(
        string imagePath,
        string? featureJson,
        string? customVp = null,
        bool fallbackAgentToLine = false,
        string? imageRoot = null)
    {
        var effectiveImageRoot = string.IsNullOrWhiteSpace(imageRoot)
            ? FallbackAnywherePixImageRoot
            : imageRoot.TrimEnd('\\', '/');
        var combined = CombineImagePath(imagePath, null);
        var normalized = combined.Replace('/', '\\').TrimStart('\\');
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var lastSegment = segments.LastOrDefault() ?? string.Empty;
        var lastIsFile = IsImageFileName(lastSegment);
        var sourceFileName = lastIsFile ? lastSegment : string.Empty;
        var requestsZip = string.Equals(customVp, "zip", StringComparison.OrdinalIgnoreCase);
        var requestsVp0 = string.IsNullOrWhiteSpace(customVp) ||
                          string.Equals(customVp, "0", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(customVp, "auto", StringComparison.OrdinalIgnoreCase);
        var archiveFolder = lastIsFile
            ? (segments.Length > 1 ? segments[^2] : Path.GetFileNameWithoutExtension(sourceFileName))
            : lastSegment;

        string filePath;
        if (requestsZip || requestsVp0)
        {
            var relativeSegments = segments.ToList();
            if (lastIsFile && relativeSegments.Count > 0)
            {
                relativeSegments.RemoveAt(relativeSegments.Count - 1);
            }
            relativeSegments.Add($"{archiveFolder}.zip");
            var relativeZipPath = string.Join('\\', relativeSegments);
            filePath = requestsVp0
                ? relativeZipPath
                : $"{effectiveImageRoot}\\{relativeZipPath}";
        }
        else
        {
            // VP0 primary request uses the exact JPG path stored by the source database.
            filePath = imagePath.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase) ||
                       Regex.IsMatch(imagePath, @"^[A-Za-z]:[\\/]")
                ? imagePath.Trim().Replace('/', '\\')
                : $"{effectiveImageRoot}\\{normalized}";
        }

        var line = string.Empty;
        var zone = string.Empty;
        foreach (var segment in segments)
        {
            var match = LineZoneRegex.Match(segment);
            if (!match.Success) continue;
            line = match.Groups["line"].Value;
            zone = match.Groups["zone"].Value;
            break;
        }

        // Default image contract: send the exact JPG path and mark VP as 0.
        string vp;
        if (requestsZip)
        {
            vp = ResolveOriginalDefectVp(imagePath) ?? "0";
        }
        else if (requestsVp0)
        {
            vp = "0";
        }
        else if (string.Equals(customVp, "original", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "path", StringComparison.OrdinalIgnoreCase))
        {
            vp = ResolveOriginalDefectVp(imagePath) ?? "0";
        }
        else if (string.Equals(customVp, "real", StringComparison.OrdinalIgnoreCase) || string.Equals(customVp, "actual", StringComparison.OrdinalIgnoreCase))
        {
            vp = ResolveRealDefectVp(imagePath, featureJson);
        }
        else if (!string.IsNullOrWhiteSpace(customVp) && customVp != "auto" && customVp != "all")
        {
            vp = customVp.Trim();
        }
        else
        {
            vp = "0";
        }

        var agentName = (fallbackAgentToLine && !string.IsNullOrWhiteSpace(line)) ? line : "AnywherePix";

        if (requestsZip || requestsVp0)
        {
            return new
            {
                AgentName = agentName,
                Line = line,
                Zone = zone,
                VP = vp,
                FilePath = filePath,
                ZipEntry = sourceFileName
            };
        }

        return new
        {
            AgentName = agentName,
            Line = line,
            Zone = zone,
            VP = vp,
            FilePath = filePath
        };
    }

    private void LogOutgoingImageApiRequest(string flow, HttpRequestMessage request, string? body)
    {
        var headers = new StringBuilder();
        foreach (var header in request.Headers)
        {
            headers.Append(header.Key).Append(": ").Append(string.Join(", ", header.Value)).Append("; ");
        }

        if (request.Content?.Headers is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers.Append(header.Key).Append(": ").Append(string.Join(", ", header.Value)).Append("; ");
            }
        }

        _logger.LogInformation(
            "[ImageAPI_Request] Flow={Flow} Method={Method} Url={Url} Headers={Headers} Body={Body}",
            flow,
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            headers.ToString().Trim(),
            body ?? string.Empty);
    }

    private static string? TryExtractErrorMessage(string errBody)
    {
        if (string.IsNullOrWhiteSpace(errBody)) return null;
        try
        {
            using var doc = JsonDocument.Parse(errBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("Message", out var m1) && m1.ValueKind == JsonValueKind.String)
            {
                return m1.GetString();
            }
            if (root.TryGetProperty("message", out var m2) && m2.ValueKind == JsonValueKind.String)
            {
                return m2.GetString();
            }
        }
        catch { }
        return null;
    }

    public static string ResolveRealDefectVp(string? imagePath, string? featureJson)
    {
        // 1. Check feature JSON G010_VpID
        var vpFromFeature = TryExtractVpId(featureJson);
        if (!string.IsNullOrWhiteSpace(vpFromFeature))
        {
            var clean = vpFromFeature.Trim().TrimStart('0');
            if (!string.IsNullOrWhiteSpace(clean)) return clean;
        }

        // 2. Check image path for VP folder (/VP02/ or \VP02\) or file prefix (VP02_)
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var folderMatch = Regex.Match(imagePath, @"[\\/]VP0*(?<vp>\d+)[\\/]", RegexOptions.IgnoreCase);
            if (folderMatch.Success)
            {
                var clean = folderMatch.Groups["vp"].Value.TrimStart('0');
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }

            var fileMatch = Regex.Match(imagePath, @"VP0*(?<vp>\d+)[_.-]", RegexOptions.IgnoreCase);
            if (fileMatch.Success)
            {
                var clean = fileMatch.Groups["vp"].Value.TrimStart('0');
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }
        }

        return "1";
    }

    public static string? ResolveOriginalDefectVp(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;

        // The original defect VP is defined by the source image_path itself.
        var folderMatch = Regex.Match(imagePath, @"[\\/]VP0*(?<vp>\d+)[\\/]", RegexOptions.IgnoreCase);
        if (folderMatch.Success)
        {
            var clean = folderMatch.Groups["vp"].Value.TrimStart('0');
            return string.IsNullOrWhiteSpace(clean) ? "0" : clean;
        }

        var fileMatch = Regex.Match(imagePath, @"(?:^|[\\/])VP0*(?<vp>\d+)[_.-]", RegexOptions.IgnoreCase);
        if (fileMatch.Success)
        {
            var clean = fileMatch.Groups["vp"].Value.TrimStart('0');
            return string.IsNullOrWhiteSpace(clean) ? "0" : clean;
        }

        return null;
    }

    private static string? TryExtractVpId(string? featureJson)
    {
        if (string.IsNullOrWhiteSpace(featureJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(featureJson);
            return FindJsonValue(document.RootElement, "G010_VpID");
        }
        catch
        {
            return null;
        }
    }

    private static string? FindJsonValue(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => property.Value.ToString()
                    };
                }

                var nestedValue = FindJsonValue(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nestedValue)) return nestedValue;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedValue = FindJsonValue(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nestedValue)) return nestedValue;
            }
        }

        return null;
    }

    private static (byte[]? Bytes, string ContentType) TryExtractBase64FromResponse(string text, string path)
    {
        var clean = text.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return (null, "image/jpeg");

        // Direct data URI string (e.g. data:image/png;base64,iVBORw0KGgo...)
        if (clean.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && clean.Contains(";base64,"))
        {
            var commaIdx = clean.IndexOf(',');
            var meta = clean[..commaIdx];
            var base64Part = clean[(commaIdx + 1)..];
            var ctype = meta.Split(';')[0].Replace("data:", "");
            try
            {
                var bytes = Convert.FromBase64String(base64Part.Trim());
                return (bytes, ctype);
            }
            catch { }
        }

        // JSON response
        if (clean.StartsWith('{') && clean.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(clean);
                var root = doc.RootElement;
                var propertyCandidates = new[]
                {
                    "data", "base64", "image", "imageBase64", "image_base64", "thumbnail",
                    "thumbnailBase64", "rawBytes", "bytes", "content"
                };

                foreach (var prop in propertyCandidates)
                {
                    if (root.TryGetProperty(prop, out var elem) && elem.ValueKind == JsonValueKind.String)
                    {
                        var strVal = elem.GetString();
                        if (!string.IsNullOrWhiteSpace(strVal))
                        {
                            if (strVal.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && strVal.Contains(";base64,"))
                            {
                                var commaIdx = strVal.IndexOf(',');
                                var meta = strVal[..commaIdx];
                                var base64Part = strVal[(commaIdx + 1)..];
                                var ctype = meta.Split(';')[0].Replace("data:", "");
                                var bytes = Convert.FromBase64String(base64Part.Trim());
                                return (bytes, ctype);
                            }

                            try
                            {
                                var bytes = Convert.FromBase64String(strVal.Trim());
                                var ctype = "image/jpeg";
                                if (root.TryGetProperty("contentType", out var ctElem) && ctElem.ValueKind == JsonValueKind.String)
                                {
                                    ctype = ctElem.GetString() ?? "image/jpeg";
                                }
                                else
                                {
                                    ctype = DetectImageMimeType(bytes, path);
                                }
                                return (bytes, ctype);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        // Try raw base64 string directly
        try
        {
            var rawBytes = Convert.FromBase64String(clean);
            if (rawBytes.Length > 16)
            {
                return (rawBytes, DetectImageMimeType(rawBytes, path));
            }
        }
        catch { }

        return (null, "image/jpeg");
    }

    private static string? TryExtractUrlFromJson(string text)
    {
        var clean = text.Trim();
        if (!clean.StartsWith('{') || !clean.EndsWith('}')) return null;

        try
        {
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;
            var urlProps = new[] { "url", "imageUrl", "image_url", "link", "downloadUrl" };
            foreach (var prop in urlProps)
            {
                if (root.TryGetProperty(prop, out var elem) && elem.ValueKind == JsonValueKind.String)
                {
                    var u = elem.GetString();
                    if (!string.IsNullOrWhiteSpace(u) && (u.StartsWith("http://") || u.StartsWith("https://")))
                    {
                        return u;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static string DetectImageMimeType(byte[] bytes, string pathOrName)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D) return "image/bmp";
        if (bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46) return "image/webp";

        var ext = Path.GetExtension(pathOrName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }
}
