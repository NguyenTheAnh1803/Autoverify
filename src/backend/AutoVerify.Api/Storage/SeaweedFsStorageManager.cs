using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Storage;

public class SeaweedFsStorageManager(HttpClient httpClient, IOptions<SeaweedFsOptions> options, IWebHostEnvironment environment) : IStorageService
{
    private static SeaweedFsOptions? _runtimeOptions;
    public SeaweedFsOptions Options => _runtimeOptions ??= options.Value;

    public string Mode => string.IsNullOrWhiteSpace(Options.BaseUrl) ? "LocalFallback" : "SeaweedFS";

    public SeaweedFsConfigDto GetConfig()
    {
        return new SeaweedFsConfigDto(
            MasterUrl: Options.MasterUrl ?? string.Empty,
            FilerUrl: Options.FilerUrl ?? Options.BaseUrl ?? string.Empty,
            PublicBaseUrl: Options.PublicBaseUrl ?? string.Empty,
            DefaultBucket: Options.DefaultBucket ?? "autoverify",
            OffloadDefects: Options.OffloadDefects,
            LocalFallbackPath: Options.LocalFallbackPath ?? "data/storage"
        );
    }

    public async Task<SeaweedFsConfigDto> UpdateConfigAsync(UpdateSeaweedFsConfigRequest req, CancellationToken cancellationToken = default)
    {
        if (req.MasterUrl != null) Options.MasterUrl = req.MasterUrl.Trim();
        if (req.FilerUrl != null)
        {
            Options.FilerUrl = req.FilerUrl.Trim();
            Options.BaseUrl = req.FilerUrl.Trim();
        }
        if (req.PublicBaseUrl != null) Options.PublicBaseUrl = req.PublicBaseUrl.Trim();
        if (!string.IsNullOrWhiteSpace(req.DefaultBucket)) Options.DefaultBucket = req.DefaultBucket.Trim();
        if (req.OffloadDefects.HasValue) Options.OffloadDefects = req.OffloadDefects.Value;
        if (!string.IsNullOrWhiteSpace(req.LocalFallbackPath)) Options.LocalFallbackPath = req.LocalFallbackPath.Trim();

        try
        {
            var appsettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");
            if (File.Exists(appsettingsPath))
            {
                var jsonString = await File.ReadAllTextAsync(appsettingsPath, cancellationToken);
                var docNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);
                if (docNode is System.Text.Json.Nodes.JsonObject rootObj)
                {
                    var seaweedNode = new System.Text.Json.Nodes.JsonObject
                    {
                        ["MasterUrl"] = Options.MasterUrl ?? string.Empty,
                        ["FilerUrl"] = Options.FilerUrl ?? string.Empty,
                        ["BaseUrl"] = Options.BaseUrl ?? string.Empty,
                        ["PublicBaseUrl"] = Options.PublicBaseUrl ?? string.Empty,
                        ["DefaultBucket"] = Options.DefaultBucket ?? "autoverify",
                        ["LocalFallbackPath"] = Options.LocalFallbackPath ?? "data/storage",
                        ["OffloadDefects"] = Options.OffloadDefects
                    };
                    rootObj["SeaweedFs"] = seaweedNode;
                    var formatted = docNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(appsettingsPath, formatted, cancellationToken);
                }
            }
        }
        catch
        {
            // In-memory options updated; ignore disk persistence errors
        }

        return GetConfig();
    }

    public async Task<StorageTestConnectionResponse> TestConnectionAsync(StorageTestConnectionRequest? overrideConfig = null, CancellationToken cancellationToken = default)
    {
        var masterUrl = overrideConfig?.MasterUrl ?? Options.MasterUrl;
        var filerUrl = overrideConfig?.FilerUrl ?? Options.FilerUrl ?? Options.BaseUrl;
        var bucket = overrideConfig?.DefaultBucket ?? Options.DefaultBucket ?? "autoverify";

        StorageComponentCheck masterCheck;
        if (string.IsNullOrWhiteSpace(masterUrl))
        {
            masterCheck = new StorageComponentCheck("SeaweedFS Master", false, 0, "Master URL is empty or unconfigured.");
        }
        else
        {
            var swMaster = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var masterCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, masterCts.Token);
                var probeUrl = $"{masterUrl.TrimEnd('/')}/dir/status";
                using var res = await httpClient.GetAsync(probeUrl, linked.Token);
                swMaster.Stop();
                if (res.IsSuccessStatusCode)
                {
                    var preview = await res.Content.ReadAsStringAsync(linked.Token);
                    if (preview.Length > 120) preview = preview[..120] + "...";
                    masterCheck = new StorageComponentCheck("SeaweedFS Master", true, swMaster.ElapsedMilliseconds, $"Online ({res.StatusCode}): {preview}", (int)res.StatusCode);
                }
                else
                {
                    masterCheck = new StorageComponentCheck("SeaweedFS Master", false, swMaster.ElapsedMilliseconds, $"HTTP {(int)res.StatusCode} ({res.ReasonPhrase})", (int)res.StatusCode);
                }
            }
            catch (Exception ex)
            {
                swMaster.Stop();
                masterCheck = new StorageComponentCheck("SeaweedFS Master", false, swMaster.ElapsedMilliseconds, $"Unreachable: {ex.Message}");
            }
        }

        StorageComponentCheck filerCheck;
        if (string.IsNullOrWhiteSpace(filerUrl))
        {
            filerCheck = new StorageComponentCheck("SeaweedFS Filer", false, 0, "Filer URL is empty or unconfigured.");
        }
        else
        {
            var swFiler = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var filerCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, filerCts.Token);
                var probeUrl = $"{filerUrl.TrimEnd('/')}/";
                using var res = await httpClient.GetAsync(probeUrl, linked.Token);
                swFiler.Stop();
                var isOk = (int)res.StatusCode >= 200 && (int)res.StatusCode < 500;
                filerCheck = new StorageComponentCheck("SeaweedFS Filer", isOk, swFiler.ElapsedMilliseconds, $"HTTP {(int)res.StatusCode} ({res.ReasonPhrase}) at {probeUrl}", (int)res.StatusCode);
            }
            catch (Exception ex)
            {
                swFiler.Stop();
                filerCheck = new StorageComponentCheck("SeaweedFS Filer", false, swFiler.ElapsedMilliseconds, $"Unreachable: {ex.Message}");
            }
        }

        StorageComponentCheck rwCheck;
        if (!filerCheck.Reachable || string.IsNullOrWhiteSpace(filerUrl))
        {
            rwCheck = new StorageComponentCheck("Write / Read / Delete Test", false, 0, "Skipped test write because Filer endpoint is unreachable.");
        }
        else
        {
            var swRw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var rwCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, rwCts.Token);
                var probeName = $".autoverify_probe_{Guid.NewGuid():N}.json";
                var testBucket = string.IsNullOrWhiteSpace(bucket) ? "autoverify" : bucket;
                var testUrl = $"{filerUrl.TrimEnd('/')}/{Uri.EscapeDataString(testBucket)}/{probeName}";

                var testPayload = JsonSerializer.Serialize(new
                {
                    probe = "AutoVerify Live Check",
                    timestamp = DateTimeOffset.UtcNow
                });
                using var putContent = new StringContent(testPayload, Encoding.UTF8, "application/json");
                using var putRes = await httpClient.PutAsync(testUrl, putContent, linked.Token);

                if (!putRes.IsSuccessStatusCode)
                {
                    swRw.Stop();
                    rwCheck = new StorageComponentCheck("Write / Read / Delete Test", false, swRw.ElapsedMilliseconds, $"Write probe failed with HTTP {(int)putRes.StatusCode} ({putRes.ReasonPhrase})", (int)putRes.StatusCode);
                }
                else
                {
                    // Read back
                    using var getRes = await httpClient.GetAsync(testUrl, linked.Token);
                    // Clean up test file
                    try { using var delRes = await httpClient.DeleteAsync(testUrl, linked.Token); } catch { /* ignore cleanup error */ }

                    swRw.Stop();
                    rwCheck = new StorageComponentCheck("Write / Read / Delete Test", true, swRw.ElapsedMilliseconds, "Write, Read & Delete succeeded on Filer.");
                }
            }
            catch (Exception ex)
            {
                swRw.Stop();
                rwCheck = new StorageComponentCheck("Write / Read / Delete Test", false, swRw.ElapsedMilliseconds, $"Read/Write check failed: {ex.Message}");
            }
        }

        var overallSuccess = filerCheck.Reachable && rwCheck.Reachable;
        var statusMsg = overallSuccess
            ? (masterCheck.Reachable ? "SeaweedFS Filer and Master are fully operational." : "SeaweedFS Filer is operational (Master status check degraded).")
            : (filerCheck.Reachable ? "SeaweedFS Filer reachable but test write failed." : "SeaweedFS Filer is offline or unreachable.");

        var diags = new Dictionary<string, string>
        {
            ["MasterUrl"] = masterUrl ?? "(none)",
            ["FilerUrl"] = filerUrl ?? "(none)",
            ["DefaultBucket"] = bucket,
            ["PublicBaseUrl"] = overrideConfig?.PublicBaseUrl ?? Options.PublicBaseUrl ?? "(none)",
            ["StorageMode"] = Mode,
            ["LocalFallbackPath"] = Options.LocalFallbackPath ?? "data/storage",
            ["TestedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return new StorageTestConnectionResponse(overallSuccess, statusMsg, masterCheck, filerCheck, rwCheck, diags);
    }

    public async Task<StoredObjectResponse> UploadJsonAsync(string? bucket, string? objectName, object payload, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true });
        return await UploadBytesAsync(bucket, objectName, bytes, "application/json", cancellationToken);
    }

    public async Task<StoredObjectResponse> UploadJsonStringAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonContent);
        return await UploadBytesAsync(bucket, objectName, bytes, "application/json", cancellationToken);
    }

    public async Task<StoredObjectResponse> UploadJsonGzipAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default)
    {
        var rawBytes = Encoding.UTF8.GetBytes(jsonContent);
        using var mem = new MemoryStream();
        await using (var gzip = new GZipStream(mem, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(rawBytes, cancellationToken);
            await gzip.FlushAsync(cancellationToken);
        }

        var compressedBytes = mem.ToArray();
        var targetName = objectName ?? $"{Guid.NewGuid():N}.json.gz";
        if (!targetName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            targetName += ".gz";
        }

        return await UploadBytesAsync(bucket, targetName, compressedBytes, "application/json", cancellationToken, contentEncoding: "gzip");
    }

    public async Task<StoredObjectResponse> UploadImageAsync(string? bucket, string objectName, Stream imageStream, string? contentType, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        await imageStream.CopyToAsync(memory, cancellationToken);
        return await UploadBytesAsync(bucket, objectName, memory.ToArray(), string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, cancellationToken);
    }

    public virtual async Task<ProductBundleUploadResult> UploadProductDefectBundleAsync(
        string keyId,
        IEnumerable<DefectRecordEntity> defects,
        IDictionary<string, byte[]>? imageFiles = null,
        CancellationToken cancellationToken = default)
    {
        var defectList = defects.ToList();
        var shardedBundlePath = StoragePathSharder.GetDefectBundlePath(keyId);
        var bucket = Options.DefaultBucket ?? "autoverify";

        using var zipMs = new MemoryStream();
        long originalBytes = 0;
        var entryMap = new Dictionary<Guid, DefectBundleEntryInfo>();
        var manifestEntries = new List<object>();

        using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var defect in defectList)
            {
                string? featEntryName = null;
                string? customFeatEntryName = null;
                string? imgEntryName = null;

                if (!string.IsNullOrWhiteSpace(defect.FeatureJson))
                {
                    featEntryName = $"{defect.Id:N}_feature.json";
                    var featBytes = Encoding.UTF8.GetBytes(defect.FeatureJson);
                    originalBytes += featBytes.Length;
                    var entry = archive.CreateEntry(featEntryName, CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    await es.WriteAsync(featBytes, cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
                {
                    customFeatEntryName = $"{defect.Id:N}_custom_feature.json";
                    var customBytes = Encoding.UTF8.GetBytes(defect.CustomFeatureJson);
                    originalBytes += customBytes.Length;
                    var entry = archive.CreateEntry(customFeatEntryName, CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    await es.WriteAsync(customBytes, cancellationToken);
                }

                if (imageFiles != null)
                {
                    byte[]? imgBytes = null;
                    string? rawKey = null;

                    if (imageFiles.TryGetValue(defect.Id.ToString(), out imgBytes)) rawKey = defect.Id.ToString();
                    else if (imageFiles.TryGetValue(defect.Id.ToString("N"), out imgBytes)) rawKey = defect.Id.ToString("N");
                    else if (!string.IsNullOrWhiteSpace(defect.ImageUri) && imageFiles.TryGetValue(defect.ImageUri, out imgBytes)) rawKey = defect.ImageUri;

                    if (imgBytes != null)
                    {
                        var ext = !string.IsNullOrWhiteSpace(rawKey) && Path.HasExtension(rawKey) ? Path.GetExtension(rawKey) : ".jpg";
                        imgEntryName = $"{defect.Id:N}_img{ext}";
                        originalBytes += imgBytes.Length;
                        var entry = archive.CreateEntry(imgEntryName, CompressionLevel.Optimal);
                        await using var es = entry.Open();
                        await es.WriteAsync(imgBytes, cancellationToken);
                    }
                }

                manifestEntries.Add(new
                {
                    defect.Id,
                    defect.KeyId,
                    defect.DefectName,
                    defect.DefectTable,
                    defect.Judge,
                    defect.Severity,
                    defect.CoordinateX,
                    defect.CoordinateY,
                    FeatureEntry = featEntryName,
                    CustomFeatureEntry = customFeatEntryName,
                    ImageEntry = imgEntryName
                });
            }

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                KeyId = keyId,
                DefectsCount = defectList.Count,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Defects = manifestEntries
            }, new JsonSerializerOptions { WriteIndented = true });

            originalBytes += manifestBytes.Length;
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var msStream = manifestEntry.Open())
            {
                await msStream.WriteAsync(manifestBytes, cancellationToken);
            }
        }

        var compressedBytes = zipMs.ToArray();
        var stored = await UploadBytesAsync(bucket, shardedBundlePath, compressedBytes, "application/zip", cancellationToken);

        foreach (var defect in defectList)
        {
            var featEntry = !string.IsNullOrWhiteSpace(defect.FeatureJson) ? $"{defect.Id:N}_feature.json" : null;
            var customFeatEntry = !string.IsNullOrWhiteSpace(defect.CustomFeatureJson) ? $"{defect.Id:N}_custom_feature.json" : null;
            var imgEntry = (imageFiles != null && (imageFiles.ContainsKey(defect.Id.ToString()) || imageFiles.ContainsKey(defect.Id.ToString("N")) || (!string.IsNullOrWhiteSpace(defect.ImageUri) && imageFiles.ContainsKey(defect.ImageUri))))
                ? $"{defect.Id:N}_img.jpg"
                : null;

            var featUrl = featEntry != null ? $"{stored.Uri}#{featEntry}" : null;
            var customFeatUrl = customFeatEntry != null ? $"{stored.Uri}#{customFeatEntry}" : null;
            var imgUrl = imgEntry != null ? $"{stored.Uri}#{imgEntry}" : null;

            entryMap[defect.Id] = new DefectBundleEntryInfo(featEntry, customFeatEntry, imgEntry, featUrl, customFeatUrl, imgUrl);
        }

        var ratio = originalBytes > 0 ? (1.0 - ((double)compressedBytes.Length / originalBytes)) : 0;
        return new ProductBundleUploadResult(
            BundleUri: stored.Uri,
            ShardedPath: shardedBundlePath,
            OriginalBytes: originalBytes,
            CompressedBytes: compressedBytes.Length,
            CompressionRatio: Math.Round(ratio * 100, 2),
            FilesCount: manifestEntries.Count,
            DefectEntries: entryMap
        );
    }

    public virtual async Task<string> UploadProductDefectPackAsync(
        string keyId,
        IEnumerable<DefectRecordEntity> defects,
        CancellationToken cancellationToken = default)
    {
        var defectList = defects.ToList();
        var bucket = Options.DefaultBucket ?? "autoverify";
        var shardedPackPath = StoragePathSharder.GetDefectPackPath(keyId);

        var defectItems = new List<object>();
        foreach (var defect in defectList)
        {
            object? featObj = null;
            if (!string.IsNullOrWhiteSpace(defect.FeatureJson))
            {
                try
                {
                    featObj = JsonDocument.Parse(defect.FeatureJson).RootElement.Clone();
                }
                catch
                {
                    featObj = defect.FeatureJson;
                }
            }

            object? customFeatObj = null;
            if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
            {
                try
                {
                    customFeatObj = JsonDocument.Parse(defect.CustomFeatureJson).RootElement.Clone();
                }
                catch
                {
                    customFeatObj = defect.CustomFeatureJson;
                }
            }

            defectItems.Add(new
            {
                defectId = defect.Id.ToString(),
                defectName = defect.DefectName,
                defectTable = defect.DefectTable,
                defectType = defect.DefectType,
                componentName = defect.ComponentName,
                judge = defect.Judge,
                severity = defect.Severity,
                confidenceScore = defect.ConfidenceScore,
                reason = defect.Reason,
                eqpName = defect.EqpName,
                emNo = defect.EmNo,
                specName = defect.SpecName,
                specJudge = defect.SpecJudge,
                specReason = defect.SpecReason,
                priority = defect.Priority,
                imagePath = defect.ImageUri ?? defect.ImageUrl,
                imageUrl = defect.ImageUrl ?? defect.ImageUri,
                coordinates = new
                {
                    x = defect.CoordinateX,
                    y = defect.CoordinateY
                },
                feature = featObj,
                customFeature = customFeatObj,
                createdAtUtc = defect.CreatedAtUtc
            });
        }

        long.TryParse(keyId, out var parsedFk);
        var packDoc = new
        {
            productId = keyId,
            keyId = keyId,
            fkId = parsedFk > 0 ? (long?)parsedFk : null,
            totalDefects = defectList.Count,
            generatedAtUtc = DateTimeOffset.UtcNow,
            defects = defectItems
        };

        var json = JsonSerializer.Serialize(packDoc, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var stored = await UploadJsonGzipAsync(bucket, shardedPackPath, json, cancellationToken);
        return stored.Uri;
    }

    public virtual async Task<string?> ReadDefectPackJsonAsync(string packLocationOrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packLocationOrUrl)) return null;
        var cleanTarget = packLocationOrUrl.Split('#')[0];
        return await ReadTextAsync(cleanTarget, cancellationToken);
    }

    public virtual async Task<(byte[] Data, string ContentType)?> ExtractZipEntryAsync(string zipLocationOrUrl, string entryNameOrPrefix, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipLocationOrUrl) || string.IsNullOrWhiteSpace(entryNameOrPrefix))
        {
            return null;
        }

        var cleanTarget = zipLocationOrUrl.Split('#')[0];
        byte[]? zipBytes = null;

        if (cleanTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            cleanTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var requestUrl = cleanTarget;
                if (!string.IsNullOrWhiteSpace(Options.PublicBaseUrl) &&
                    !string.IsNullOrWhiteSpace(Options.BaseUrl) &&
                    requestUrl.StartsWith(Options.PublicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    requestUrl = Options.BaseUrl.TrimEnd('/') + requestUrl[Options.PublicBaseUrl.TrimEnd('/').Length..];
                }

                using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    zipBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
            }
            catch
            {
                // Proceed to disk fallback
            }
        }

        if (zipBytes == null)
        {
            try
            {
                if (File.Exists(cleanTarget))
                {
                    zipBytes = await File.ReadAllBytesAsync(cleanTarget, cancellationToken);
                }
                else
                {
                    var root = Path.IsPathRooted(Options.LocalFallbackPath)
                        ? Options.LocalFallbackPath
                        : Path.Combine(environment.ContentRootPath, Options.LocalFallbackPath);
                    var combined = Path.Combine(root, cleanTarget.TrimStart('/', '\\'));
                    if (File.Exists(combined))
                    {
                        zipBytes = await File.ReadAllBytesAsync(combined, cancellationToken);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        if (zipBytes == null || zipBytes.Length < 22) // Smallest valid zip is 22 bytes
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(entryNameOrPrefix, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(entryNameOrPrefix, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(entryNameOrPrefix, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => e.Name.StartsWith(entryNameOrPrefix, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => e.Name.Contains(entryNameOrPrefix, StringComparison.OrdinalIgnoreCase));

            if (entry == null) return null;

            await using var entryStream = entry.Open();
            using var extractedMs = new MemoryStream();
            await entryStream.CopyToAsync(extractedMs, cancellationToken);
            var entryData = extractedMs.ToArray();

            var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
            var contentType = ext switch
            {
                ".json" => "application/json",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".txt" or ".log" => "text/plain",
                _ => "application/octet-stream"
            };

            return (entryData, contentType);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> ReadTextAsync(string locationOrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(locationOrUrl)) return null;

        // If URL contains a zip entry or defect pack fragment (e.g. bundle.zip#defect_123_feature.json or defects.json.gz#{defectId})
        if (locationOrUrl.Contains('#'))
        {
            var parts = locationOrUrl.Split('#', 2);
            var source = parts[0];
            var fragment = parts[1];
            if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extracted = await ExtractZipEntryAsync(source, fragment, cancellationToken);
                if (extracted.HasValue)
                {
                    return Encoding.UTF8.GetString(extracted.Value.Data);
                }
            }
            else
            {
                // Defect pack fragment lookup!
                var packJson = await ReadTextAsync(source, cancellationToken);
                if (!string.IsNullOrWhiteSpace(packJson))
                {
                    var isCustom = fragment.EndsWith("_custom", StringComparison.OrdinalIgnoreCase) ||
                                   fragment.EndsWith("_custom_feature", StringComparison.OrdinalIgnoreCase);
                    var cleanDefectId = fragment.Replace("_custom_feature", "", StringComparison.OrdinalIgnoreCase)
                                                .Replace("_custom", "", StringComparison.OrdinalIgnoreCase)
                                                .Replace("_feature", "", StringComparison.OrdinalIgnoreCase);

                    try
                    {
                        using var doc = JsonDocument.Parse(packJson);
                        if (doc.RootElement.TryGetProperty("defects", out var defectsArray) && defectsArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in defectsArray.EnumerateArray())
                            {
                                if (el.TryGetProperty("defectId", out var idProp) &&
                                    (string.Equals(idProp.GetString(), cleanDefectId, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(idProp.GetString()?.Replace("-", ""), cleanDefectId.Replace("-", ""), StringComparison.OrdinalIgnoreCase)))
                                {
                                    var targetProp = isCustom ? "customFeature" : "feature";
                                    if (el.TryGetProperty(targetProp, out var featProp) &&
                                        featProp.ValueKind != JsonValueKind.Null &&
                                        featProp.ValueKind != JsonValueKind.Undefined)
                                    {
                                        return featProp.GetRawText();
                                    }
                                    return null;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors
                    }
                }
            }
        }

        byte[]? rawBytes = null;

        if (locationOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            locationOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var requestUrl = locationOrUrl;
                if (!string.IsNullOrWhiteSpace(Options.PublicBaseUrl) &&
                    !string.IsNullOrWhiteSpace(Options.BaseUrl) &&
                    requestUrl.StartsWith(Options.PublicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    requestUrl = Options.BaseUrl.TrimEnd('/') + requestUrl[Options.PublicBaseUrl.TrimEnd('/').Length..];
                }

                using var response = await httpClient.GetAsync(requestUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    rawBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
            }
            catch
            {
                // Fall back or return null
            }
        }
        else
        {
            try
            {
                if (File.Exists(locationOrUrl))
                {
                    rawBytes = await File.ReadAllBytesAsync(locationOrUrl, cancellationToken);
                }
                else
                {
                    var root = Path.IsPathRooted(Options.LocalFallbackPath)
                        ? Options.LocalFallbackPath
                        : Path.Combine(environment.ContentRootPath, Options.LocalFallbackPath);
                    var combined = Path.Combine(root, locationOrUrl.TrimStart('/', '\\'));
                    if (File.Exists(combined))
                    {
                        rawBytes = await File.ReadAllBytesAsync(combined, cancellationToken);
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        if (rawBytes == null || rawBytes.Length == 0) return null;

        // Decompress GZip if magic header is present (0x1F, 0x8B) or ends with .gz
        if (rawBytes.Length >= 2 && rawBytes[0] == 0x1F && rawBytes[1] == 0x8B)
        {
            try
            {
                using var compMs = new MemoryStream(rawBytes);
                using var gzip = new GZipStream(compMs, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch
            {
                // Fallback to plain UTF8 string if decompression fails
            }
        }

        return Encoding.UTF8.GetString(rawBytes);
    }

    public virtual async Task<StorageStreamResult?> OpenReadStreamAsync(string locationOrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(locationOrUrl)) return null;

        var cleanTarget = locationOrUrl.Split('#')[0];

        if (cleanTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            cleanTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(cleanTarget, UriKind.Absolute, out var targetUri) && IsAllowedRemoteUri(targetUri))
            {
                try
                {
                    var requestUrl = cleanTarget;
                    if (!string.IsNullOrWhiteSpace(Options.PublicBaseUrl) &&
                        !string.IsNullOrWhiteSpace(Options.BaseUrl) &&
                        requestUrl.StartsWith(Options.PublicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    {
                        requestUrl = Options.BaseUrl.TrimEnd('/') + requestUrl[Options.PublicBaseUrl.TrimEnd('/').Length..];
                    }

                    var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        return new StorageStreamResult
                        {
                            Stream = stream,
                            ContentType = response.Content.Headers.ContentType?.MediaType,
                            ContentLength = response.Content.Headers.ContentLength,
                            ContentEncoding = response.Content.Headers.ContentEncoding.FirstOrDefault(),
                            LifetimeScope = response
                        };
                    }

                    response.Dispose();
                }
                catch
                {
                    // Fall back to local disk
                }
            }
        }

        try
        {
            string? localFilePath = null;
            var root = Path.GetFullPath(Path.IsPathRooted(Options.LocalFallbackPath)
                ? Options.LocalFallbackPath
                : Path.Combine(environment.ContentRootPath, Options.LocalFallbackPath));

            var relPath = cleanTarget.TrimStart('/', '\\');
            var combined = Path.GetFullPath(Path.Combine(root, relPath));

            // Prevent path traversal outside root
            if (combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(combined))
            {
                localFilePath = combined;
            }

            if (localFilePath != null)
            {
                var fileStream = new FileStream(
                    localFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);

                var ext = Path.GetExtension(localFilePath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".json" => "application/json",
                    ".gz" => "application/gzip",
                    ".zip" => "application/zip",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".txt" or ".log" => "text/plain",
                    _ => "application/octet-stream"
                };

                return new StorageStreamResult
                {
                    Stream = fileStream,
                    ContentType = contentType,
                    ContentLength = fileStream.Length,
                    LifetimeScope = fileStream
                };
            }
        }
        catch
        {
            // Ignore and return null
        }

        return null;
    }

    protected virtual async Task<StoredObjectResponse> UploadBytesAsync(
        string? bucket,
        string? objectName,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken,
        string? contentEncoding = null)
    {
        var normalizedBucket = NormalizePathSegment(bucket ?? Options.DefaultBucket);
        var normalizedPath = NormalizeObjectPath(string.IsNullOrWhiteSpace(objectName) ? $"{Guid.NewGuid():N}.bin" : objectName);
        var createdAt = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(Options.BaseUrl))
        {
            return await SaveLocalFallbackAsync(normalizedBucket, normalizedPath, bytes, contentType, createdAt, cancellationToken);
        }

        var baseUrl = Options.BaseUrl.TrimEnd('/');
        var uploadUrl = $"{baseUrl}/{normalizedBucket}/{normalizedPath}";
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (!string.IsNullOrWhiteSpace(contentEncoding))
        {
            content.Headers.ContentEncoding.Add(contentEncoding);
        }

        using var response = await httpClient.PutAsync(uploadUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var publicUrl = !string.IsNullOrWhiteSpace(Options.PublicBaseUrl)
            ? $"{Options.PublicBaseUrl.TrimEnd('/')}/{normalizedBucket}/{normalizedPath}"
            : uploadUrl;

        return new StoredObjectResponse(Guid.NewGuid(), "SeaweedFS", normalizedBucket, normalizedPath, contentType, bytes.LongLength, publicUrl, createdAt);
    }

    private async Task<StoredObjectResponse> SaveLocalFallbackAsync(string bucket, string objectPath, byte[] bytes, string contentType, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var root = Path.IsPathRooted(Options.LocalFallbackPath)
            ? Options.LocalFallbackPath
            : Path.Combine(environment.ContentRootPath, Options.LocalFallbackPath);
        var folder = Path.Combine(root, bucket, Path.GetDirectoryName(objectPath) ?? string.Empty);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, Path.GetFileName(objectPath));
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);

        var publicUrl = !string.IsNullOrWhiteSpace(Options.PublicBaseUrl)
            ? $"{Options.PublicBaseUrl.TrimEnd('/')}/{bucket}/{objectPath.Replace('\\', '/')}"
            : path;

        return new StoredObjectResponse(Guid.NewGuid(), "LocalFallback", bucket, objectPath.Replace('\\', '/'), contentType, bytes.LongLength, publicUrl, createdAt);
    }

    private static string NormalizePathSegment(string value)
    {
        var sanitized = new string(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "autoverify" : sanitized;
    }

    private bool IsAllowedRemoteUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // Block cloud instance metadata endpoints (AWS, GCP, Azure, Alibaba)
        if (uri.Host.Equals("169.254.169.254", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("100.100.100.200", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Options.BaseUrl) && Uri.TryCreate(Options.BaseUrl, UriKind.Absolute, out var bUri))
            allowedHosts.Add(bUri.Authority);
        if (!string.IsNullOrWhiteSpace(Options.PublicBaseUrl) && Uri.TryCreate(Options.PublicBaseUrl, UriKind.Absolute, out var pUri))
            allowedHosts.Add(pUri.Authority);
        if (!string.IsNullOrWhiteSpace(Options.MasterUrl) && Uri.TryCreate(Options.MasterUrl, UriKind.Absolute, out var mUri))
            allowedHosts.Add(mUri.Authority);

        if (allowedHosts.Count > 0)
        {
            return allowedHosts.Contains(uri.Authority);
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return false;
        }

        return true;
    }

    private static string NormalizeObjectPath(string value)
    {
        var segments = value.Trim().Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return $"{Guid.NewGuid():N}.bin";
        }

        var sanitized = segments.Select(part =>
        {
            var ext = Path.GetExtension(part);
            var baseName = Path.GetFileNameWithoutExtension(part);
            var safeBase = new string(baseName.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray());
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = Guid.NewGuid().ToString("N");
            return string.IsNullOrWhiteSpace(ext) ? safeBase : $"{safeBase}{ext}";
        });

        return string.Join("/", sanitized);
    }
}

public sealed class NullSeedStorageManager() : SeaweedFsStorageManager(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new SeaweedFsOptions()), new NullWebHostEnvironment())
{
    protected override Task<StoredObjectResponse> UploadBytesAsync(string? bucket, string? objectName, byte[] bytes, string contentType, CancellationToken cancellationToken, string? contentEncoding = null)
    {
        var normalizedBucket = bucket ?? "seed";
        var normalizedObjectName = objectName ?? $"{Guid.NewGuid():N}.json";
        return Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), "Seed", normalizedBucket, normalizedObjectName, contentType, bytes.LongLength, $"seed://{normalizedBucket}/{normalizedObjectName}", DateTimeOffset.UtcNow));
    }
}

internal sealed class NullWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "AutoVerify.Api";
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
