using System.Text.Json;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/storage")]
public sealed class StorageController(
    AutoVerifyStore store,
    AutoVerifyDbContext db,
    SeaweedFsStorageManager storage,
    IImageCrawlerService imageCrawler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<StoredObjectResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStorageObjects()
    {
        var objects = await store.GetStorageObjectsAsync();
        return Ok(objects);
    }

    [HttpGet("config")]
    [ProducesResponseType(typeof(SeaweedFsConfigDto), StatusCodes.Status200OK)]
    public IActionResult GetConfig()
    {
        return Ok(storage.GetConfig());
    }

    [HttpPost("config")]
    [ProducesResponseType(typeof(SeaweedFsConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateSeaweedFsConfigRequest request, CancellationToken cancellationToken)
    {
        var updated = await storage.UpdateConfigAsync(request, cancellationToken);
        await store.RecordAuditAsync("admin", "Storage.UpdateConfig", "SeaweedFs", "config", $"Updated SeaweedFS configuration (Filer: {updated.FilerUrl}, Master: {updated.MasterUrl}).");
        return Ok(updated);
    }

    [HttpPost("test-connection")]
    [ProducesResponseType(typeof(StorageTestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestConnection([FromBody] StorageTestConnectionRequest? request, CancellationToken cancellationToken)
    {
        var result = await storage.TestConnectionAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sharding-info")]
    [ProducesResponseType(typeof(StorageShardingInfoResponse), StatusCodes.Status200OK)]
    public IActionResult GetShardingInfo([FromQuery] string? key)
    {
        var sampleKey = string.IsNullOrWhiteSpace(key) ? "PROD-2026-0904" : key.Trim();
        var sampleShardedPath = StoragePathSharder.GetShardedDefectPath(sampleKey, "defect_01_feature.json.gz");
        var sampleBundlePath = StoragePathSharder.GetDefectBundlePath(sampleKey);
        return Ok(new StorageShardingInfoResponse(
            Strategy: "4-Tier MD5 Hex Hierarchy (/XX/YY/ZZ/WW/)",
            Levels: 4,
            MaxEntriesPerDirectory: 256,
            SampleKey: sampleKey,
            SampleShardedPath: sampleShardedPath,
            SampleBundlePath: sampleBundlePath
        ));
    }

    [HttpGet("defect-file/{defectId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDefectFile(
        Guid defectId,
        [FromQuery] string? fileType,
        [FromQuery] string? vp,
        CancellationToken cancellationToken)
    {
        var targetType = (fileType ?? "feature").ToLowerInvariant();
        var defect = await db.DefectRecords.AsNoTracking().FirstOrDefaultAsync(d => d.Id == defectId, cancellationToken);
        if (defect is null)
        {
            return NotFound(new { error = $"Defect with ID {defectId} not found." });
        }

        Response.Headers.CacheControl = "public, max-age=86400";

        if (targetType == "feature")
        {
            if (!string.IsNullOrWhiteSpace(defect.FeatureJson))
            {
                return Content(defect.FeatureJson, "application/json");
            }

            var targetUri = defect.FeatureUrl ?? defect.Feature1StorageUri;
            if (!string.IsNullOrWhiteSpace(targetUri))
            {
                if (targetUri.Contains('#'))
                {
                    var parts = targetUri.Split('#', 2);
                    if (parts[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        var extracted = await storage.ExtractZipEntryAsync(parts[0], parts[1], cancellationToken);
                        if (extracted.HasValue)
                        {
                            return File(extracted.Value.Data, extracted.Value.ContentType);
                        }
                    }
                    else
                    {
                        var fragmentText = await storage.ReadTextAsync(targetUri, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(fragmentText))
                        {
                            return Content(fragmentText, "application/json");
                        }
                    }
                }
                else if (targetUri.Contains(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extracted = await storage.ExtractZipEntryAsync(targetUri, $"{defect.Id:N}_feature.json", cancellationToken);
                    if (extracted.HasValue)
                    {
                        return File(extracted.Value.Data, extracted.Value.ContentType);
                    }
                }

                var featureText = await storage.ReadTextAsync(targetUri, cancellationToken);
                if (!string.IsNullOrWhiteSpace(featureText))
                {
                    return Content(featureText, "application/json");
                }
            }

            // Fallback: Check if product has a unified DefectPackUrl
            var product = await db.ProductRecords.AsNoTracking().FirstOrDefaultAsync(p => p.KeyId == defect.KeyId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(product?.DefectPackUrl))
            {
                var packText = await storage.ReadTextAsync($"{product.DefectPackUrl}#{defect.Id:N}", cancellationToken);
                if (!string.IsNullOrWhiteSpace(packText))
                {
                    return Content(packText, "application/json");
                }
            }

            return NotFound(new { error = "Feature JSON not found or not yet offloaded for this defect." });
        }

        if (targetType is "custom" or "customfeature")
        {
            if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
            {
                return Content(defect.CustomFeatureJson, "application/json");
            }

            var targetUri = defect.CustomFeatureUrl ?? defect.Feature2StorageUri;
            if (!string.IsNullOrWhiteSpace(targetUri))
            {
                if (targetUri.Contains('#'))
                {
                    var parts = targetUri.Split('#', 2);
                    if (parts[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        var extracted = await storage.ExtractZipEntryAsync(parts[0], parts[1], cancellationToken);
                        if (extracted.HasValue)
                        {
                            return File(extracted.Value.Data, extracted.Value.ContentType);
                        }
                    }
                    else
                    {
                        var fragmentText = await storage.ReadTextAsync(targetUri, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(fragmentText))
                        {
                            return Content(fragmentText, "application/json");
                        }
                    }
                }
                else if (targetUri.Contains(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extracted = await storage.ExtractZipEntryAsync(targetUri, $"{defect.Id:N}_custom_feature.json", cancellationToken);
                    if (extracted.HasValue)
                    {
                        return File(extracted.Value.Data, extracted.Value.ContentType);
                    }
                }

                var customText = await storage.ReadTextAsync(targetUri, cancellationToken);
                if (!string.IsNullOrWhiteSpace(customText))
                {
                    return Content(customText, "application/json");
                }
            }

            // Fallback: Check if product has a unified DefectPackUrl
            var product = await db.ProductRecords.AsNoTracking().FirstOrDefaultAsync(p => p.KeyId == defect.KeyId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(product?.DefectPackUrl))
            {
                var packText = await storage.ReadTextAsync($"{product.DefectPackUrl}#{defect.Id:N}_custom", cancellationToken);
                if (!string.IsNullOrWhiteSpace(packText))
                {
                    return Content(packText, "application/json");
                }
            }

            return NotFound(new { error = "Custom Feature JSON not found for this defect." });
        }

        if (targetType == "image")
        {
            var requestedVp = string.IsNullOrWhiteSpace(vp) ? "0" : vp.Trim();
            var isVp0 = requestedVp == "0";
            // ImageUrl is the VP0 cache only. Never return it for VP1-4/original.
            var targetUri = isVp0 ? defect.ImageUrl : null;
            if (!string.IsNullOrWhiteSpace(targetUri))
            {
                if (targetUri.Contains('#'))
                {
                    var parts = targetUri.Split('#', 2);
                    var extracted = await storage.ExtractZipEntryAsync(parts[0], parts[1], cancellationToken);
                    if (extracted.HasValue)
                    {
                        return File(extracted.Value.Data, extracted.Value.ContentType);
                    }
                }
                else if (targetUri.Contains(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extracted = await storage.ExtractZipEntryAsync(targetUri, $"{defect.Id:N}_img", cancellationToken);
                    if (extracted.HasValue)
                    {
                        return File(extracted.Value.Data, extracted.Value.ContentType);
                    }
                }
                else if (targetUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || targetUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(targetUri);
                }
                else if (System.IO.File.Exists(targetUri))
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(targetUri, cancellationToken);
                    var ext = Path.GetExtension(targetUri).ToLowerInvariant();
                    var ctype = ext switch { ".png" => "image/png", ".bmp" => "image/bmp", _ => "image/jpeg" };
                    return File(bytes, ctype);
                }
            }

            // On-Demand Crawl from Machine/PC Image API via ImageCrawlerService
            if (!string.IsNullOrWhiteSpace(defect.ImageUri))
            {
                var crawlResult = await imageCrawler.CrawlAndCacheDefectImageAsync(
                    defect.Id,
                    forceRefresh: false,
                    customApiUrl: null,
                    customVp: requestedVp,
                    cancellationToken: cancellationToken);
                if (crawlResult.Success && crawlResult.ImageBytes is { Length: > 0 })
                {
                    return File(crawlResult.ImageBytes, crawlResult.ContentType ?? "image/jpeg");
                }
            }

            return NotFound(new { error = "Defect image not found or PC Image Crawler API unreachable." });
        }

        return BadRequest(new { error = $"Invalid fileType: {fileType}. Supported types: feature, custom, image." });
    }

    [HttpGet("defect-images/{defectId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetDefectImage(
        Guid defectId,
        [FromQuery] string? vp,
        CancellationToken cancellationToken)
    {
        return GetDefectFile(defectId, "image", vp, cancellationToken);
    }

    [HttpGet("defects/preview")]
    [ProducesResponseType(typeof(IReadOnlyCollection<DefectStoragePreviewItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewDefects(
        [FromQuery] string? keyword,
        [FromQuery] string? filter,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var maxLimit = Math.Clamp(limit, 1, 100);

        var query = db.DefectRecords.AsNoTracking().AsQueryable();

        if (string.Equals(filter, "inline_only", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(d => (d.FeatureJson != null && d.FeatureJson != "") || (d.CustomFeatureJson != null && d.CustomFeatureJson != ""));
        }
        else if (string.Equals(filter, "offloaded_only", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(d => (d.FeatureUrl != null && d.FeatureUrl != "") || (d.CustomFeatureUrl != null && d.CustomFeatureUrl != ""));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(d =>
                d.KeyId.Contains(kw) ||
                d.DefectName.Contains(kw) ||
                d.Judge.Contains(kw) ||
                d.DefectTable.Contains(kw) ||
                (d.ComponentName != null && d.ComponentName.Contains(kw)));
        }

        var defects = await query
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(maxLimit)
            .ToListAsync(cancellationToken);

        var keyIds = defects.Select(d => d.KeyId).Distinct().ToList();
        var products = await db.ProductRecords.AsNoTracking()
            .Where(p => keyIds.Contains(p.KeyId))
            .ToListAsync(cancellationToken);
        var productMap = products
            .GroupBy(p => p.KeyId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = defects.Select(d =>
        {
            productMap.TryGetValue(d.KeyId, out var prod);
            var hasInline = !string.IsNullOrEmpty(d.FeatureJson) || !string.IsNullOrEmpty(d.CustomFeatureJson);
            var bytes = (d.FeatureJson?.Length ?? 0) + (d.CustomFeatureJson?.Length ?? 0);
            return new DefectStoragePreviewItem(
                Id: d.Id,
                KeyId: d.KeyId,
                ProductId: prod?.Id,
                ProductName: prod?.NameDefect ?? prod?.DefectName ?? prod?.PanelId ?? prod?.MachineCode,
                DefectName: d.DefectName,
                DefectTable: d.DefectTable,
                Judge: d.Judge,
                Severity: d.Severity,
                HasInlineFeatureJson: hasInline,
                FeatureJsonBytes: bytes,
                FeatureUrl: d.FeatureUrl,
                CustomFeatureUrl: d.CustomFeatureUrl,
                CreatedAtUtc: d.CreatedAtUtc
            );
        }).ToList();

        return Ok(result);
    }

    [HttpPost("defects/{defectId:guid}/migrate-single")]
    [ProducesResponseType(typeof(MigrateSingleDefectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MigrateSingleDefect(Guid defectId, CancellationToken cancellationToken)
    {
        var defect = await db.DefectRecords.FirstOrDefaultAsync(d => d.Id == defectId, cancellationToken);
        if (defect is null)
        {
            return NotFound(new { error = $"Defect with ID {defectId} not found." });
        }

        var result = await MigrateDefectRecordAsync(defect, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (result.Success)
        {
            await store.RecordAuditAsync("admin", "Defect.MigrateToStorage", "DefectRecord", defect.KeyId, $"Migrated defect {defect.Id} to SeaweedFS ({result.BytesOffloaded} bytes).");
        }

        return Ok(result);
    }

    [HttpPost("defects/migrate-batch")]
    [ProducesResponseType(typeof(MigrateBatchDefectsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MigrateBatchDefects([FromBody] MigrateDefectsRequest? request, CancellationToken cancellationToken)
    {
        var maxLimit = Math.Clamp(request?.Limit ?? 100, 1, 500);
        List<DefectRecordEntity> defectsToMigrate;

        if (request?.DefectIds != null && request.DefectIds.Count > 0)
        {
            defectsToMigrate = await db.DefectRecords
                .Where(d => request.DefectIds.Contains(d.Id))
                .Take(maxLimit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            defectsToMigrate = await db.DefectRecords
                .Where(d => (d.FeatureJson != null && d.FeatureJson != "") || (d.CustomFeatureJson != null && d.CustomFeatureJson != ""))
                .OrderByDescending(d => d.CreatedAtUtc)
                .Take(maxLimit)
                .ToListAsync(cancellationToken);
        }

        var details = new List<MigrateSingleDefectResponse>();
        long totalBytes = 0;
        var successCount = 0;
        var failedCount = 0;

        foreach (var defect in defectsToMigrate)
        {
            var res = await MigrateDefectRecordAsync(defect, cancellationToken);
            details.Add(res);
            if (res.Success)
            {
                successCount++;
                totalBytes += res.BytesOffloaded;
            }
            else
            {
                failedCount++;
            }
        }

        if (successCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await store.RecordAuditAsync("admin", "Defect.BatchMigrateToStorage", "DefectRecord", "batch", $"Batch migrated {successCount} defects to SeaweedFS ({totalBytes} bytes).");
        }

        return Ok(new MigrateBatchDefectsResponse(
            TotalProcessed: defectsToMigrate.Count,
            SuccessCount: successCount,
            FailedCount: failedCount,
            BytesOffloaded: totalBytes,
            Details: details
        ));
    }

    private async Task<MigrateSingleDefectResponse> MigrateDefectRecordAsync(DefectRecordEntity defect, CancellationToken cancellationToken)
    {
        var hasFeat = !string.IsNullOrWhiteSpace(defect.FeatureJson);
        var hasCustom = !string.IsNullOrWhiteSpace(defect.CustomFeatureJson);

        if (!hasFeat && !hasCustom)
        {
            if (!string.IsNullOrWhiteSpace(defect.FeatureUrl) || !string.IsNullOrWhiteSpace(defect.CustomFeatureUrl))
            {
                return new MigrateSingleDefectResponse(
                    DefectId: defect.Id,
                    KeyId: defect.KeyId,
                    Success: true,
                    FeatureUrl: defect.FeatureUrl,
                    CustomFeatureUrl: defect.CustomFeatureUrl,
                    BytesOffloaded: 0,
                    Message: "Defect already offloaded to SeaweedFS."
                );
            }

            return new MigrateSingleDefectResponse(
                DefectId: defect.Id,
                KeyId: defect.KeyId,
                Success: false,
                FeatureUrl: null,
                CustomFeatureUrl: null,
                BytesOffloaded: 0,
                Message: "Defect contains no feature JSON data to migrate."
            );
        }

        long bytesOffloaded = 0;
        var bucket = storage.Options.DefaultBucket;

        try
        {
            if (hasFeat)
            {
                var objPath = StoragePathSharder.GetShardedDefectPath(defect.KeyId, $"{defect.Id:N}_feature.json");
                var stored = await storage.UploadJsonGzipAsync(bucket, objPath, defect.FeatureJson!, cancellationToken);
                await store.SaveStorageObjectAsync(stored, "DefectFeatureJson");
                defect.FeatureUrl = stored.Uri;
                defect.Feature1StorageUri = stored.Uri;
                bytesOffloaded += System.Text.Encoding.UTF8.GetByteCount(defect.FeatureJson!);
                defect.FeatureJson = null;
            }

            if (hasCustom)
            {
                var customPath = StoragePathSharder.GetShardedDefectPath(defect.KeyId, $"{defect.Id:N}_custom_feature.json");
                var stored2 = await storage.UploadJsonGzipAsync(bucket, customPath, defect.CustomFeatureJson!, cancellationToken);
                await store.SaveStorageObjectAsync(stored2, "DefectCustomFeatureJson");
                defect.CustomFeatureUrl = stored2.Uri;
                defect.Feature2StorageUri = stored2.Uri;
                bytesOffloaded += System.Text.Encoding.UTF8.GetByteCount(defect.CustomFeatureJson!);
                defect.CustomFeatureJson = null;
            }

            return new MigrateSingleDefectResponse(
                DefectId: defect.Id,
                KeyId: defect.KeyId,
                Success: true,
                FeatureUrl: defect.FeatureUrl,
                CustomFeatureUrl: defect.CustomFeatureUrl,
                BytesOffloaded: bytesOffloaded,
                Message: "Successfully offloaded feature JSON to SeaweedFS."
            );
        }
        catch (Exception ex)
        {
            return new MigrateSingleDefectResponse(
                DefectId: defect.Id,
                KeyId: defect.KeyId,
                Success: false,
                FeatureUrl: defect.FeatureUrl,
                CustomFeatureUrl: defect.CustomFeatureUrl,
                BytesOffloaded: 0,
                Message: $"Migration failed: {ex.Message}"
            );
        }
    }

    [HttpPost("defects/migrate-bundles")]
    [ProducesResponseType(typeof(MigrateBundlesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MigrateDefectBundles([FromBody] MigrateDefectsRequest? request, CancellationToken cancellationToken)
    {
        var maxLimit = Math.Clamp(request?.Limit ?? 200, 1, 1000);
        List<DefectRecordEntity> defectsToMigrate;

        if (request?.DefectIds != null && request.DefectIds.Count > 0)
        {
            defectsToMigrate = await db.DefectRecords
                .Where(d => request.DefectIds.Contains(d.Id))
                .Take(maxLimit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            defectsToMigrate = await db.DefectRecords
                .Where(d => (d.FeatureJson != null && d.FeatureJson != "") || (d.CustomFeatureJson != null && d.CustomFeatureJson != ""))
                .OrderByDescending(d => d.CreatedAtUtc)
                .Take(maxLimit)
                .ToListAsync(cancellationToken);
        }

        var groups = defectsToMigrate.GroupBy(d => d.KeyId).ToList();
        var bundleItems = new List<MigrateBundleItemResponse>();
        long totalOriginal = 0;
        long totalCompressed = 0;
        int successBundles = 0;
        int failedBundles = 0;

        foreach (var group in groups)
        {
            var keyId = group.Key;
            var groupDefects = group.ToList();

            try
            {
                var bundleResult = await storage.UploadProductDefectBundleAsync(keyId, groupDefects, null, cancellationToken);
                totalOriginal += bundleResult.OriginalBytes;
                totalCompressed += bundleResult.CompressedBytes;
                successBundles++;

                foreach (var defect in groupDefects)
                {
                    if (bundleResult.DefectEntries.TryGetValue(defect.Id, out var entryInfo))
                    {
                        if (entryInfo.FeatureUrl != null) defect.FeatureUrl = entryInfo.FeatureUrl;
                        if (entryInfo.CustomFeatureUrl != null) defect.CustomFeatureUrl = entryInfo.CustomFeatureUrl;
                        defect.Feature1StorageUri = bundleResult.BundleUri;
                        defect.Feature2StorageUri = bundleResult.BundleUri;
                        if (entryInfo.ImageUrl != null) defect.ImageUrl = entryInfo.ImageUrl;
                        defect.FeatureJson = null;
                        defect.CustomFeatureJson = null;
                    }
                }

                await store.SaveStorageObjectAsync(new StoredObjectResponse(
                    Guid.NewGuid(),
                    storage.Mode,
                    storage.Options.DefaultBucket ?? "autoverify",
                    bundleResult.ShardedPath,
                    "application/zip",
                    bundleResult.CompressedBytes,
                    bundleResult.BundleUri,
                    DateTimeOffset.UtcNow
                ), "ProductDefectBundle");

                bundleItems.Add(new MigrateBundleItemResponse(
                    KeyId: keyId,
                    DefectsCount: groupDefects.Count,
                    Success: true,
                    BundleUrl: bundleResult.BundleUri,
                    OriginalBytes: bundleResult.OriginalBytes,
                    CompressedBytes: bundleResult.CompressedBytes,
                    CompressionRatio: bundleResult.CompressionRatio,
                    Message: $"Successfully packed into sharded bundle ({bundleResult.CompressionRatio}% compressed)."
                ));
            }
            catch (Exception ex)
            {
                failedBundles++;
                bundleItems.Add(new MigrateBundleItemResponse(
                    KeyId: keyId,
                    DefectsCount: groupDefects.Count,
                    Success: false,
                    BundleUrl: null,
                    OriginalBytes: 0,
                    CompressedBytes: 0,
                    CompressionRatio: 0,
                    Message: $"Bundle packaging failed: {ex.Message}"
                ));
            }
        }

        if (successBundles > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await store.RecordAuditAsync("admin", "Defect.MigrateBundles", "DefectBundle", "batch",
                $"Packaged {defectsToMigrate.Count} defects across {groups.Count} products into compressed sharded archives ({totalOriginal} -> {totalCompressed} bytes).");
        }

        var overallRatio = totalOriginal > 0 ? Math.Round((1.0 - ((double)totalCompressed / totalOriginal)) * 100, 2) : 0;
        return Ok(new MigrateBundlesResponse(
            TotalKeyIdsProcessed: groups.Count,
            TotalDefectsProcessed: defectsToMigrate.Count,
            SuccessCount: successBundles,
            FailedCount: failedBundles,
            TotalOriginalBytes: totalOriginal,
            TotalCompressedBytes: totalCompressed,
            OverallCompressionRatio: overallRatio,
            Bundles: bundleItems
        ));
    }

    [HttpGet("product-defect-pack/{keyOrId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductDefectPack(string keyOrId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        var isGuid = Guid.TryParse(keyOrId, out var parsedGuid);

        var product = await db.ProductRecords.AsNoTracking()
            .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.KeyId == keyOrId || p.PanelId == keyOrId, cancellationToken);

        if (product != null && !string.IsNullOrWhiteSpace(product.DefectPackUrl))
        {
            var packJson = await storage.ReadDefectPackJsonAsync(product.DefectPackUrl, cancellationToken);
            if (!string.IsNullOrWhiteSpace(packJson))
            {
                return Content(packJson, "application/json");
            }
        }

        var effectiveKey = product?.KeyId ?? keyOrId;
        var defects = await db.DefectRecords.AsNoTracking()
            .Where(d => d.KeyId == effectiveKey)
            .ToListAsync(cancellationToken);

        if (defects.Count == 0 && product == null)
        {
            return NotFound(new { error = $"Product or defects not found for identifier '{keyOrId}'." });
        }

        var fkParsed = long.TryParse(effectiveKey, out var parsedFk) ? (long?)parsedFk : null;
        var packObj = new
        {
            productId = effectiveKey,
            keyId = effectiveKey,
            fkId = fkParsed,
            totalDefects = defects.Count,
            generatedAt = DateTime.UtcNow,
            defects = defects.Select(d => new
            {
                id = d.Id,
                defectName = d.DefectName,
                defectType = d.DefectType,
                componentName = d.ComponentName,
                coordinateX = d.CoordinateX,
                coordinateY = d.CoordinateY,
                severity = d.Severity,
                judge = d.Judge,
                confidenceScore = d.ConfidenceScore,
                imageUri = d.ImageUri,
                imageUrl = d.ImageUrl,
                reason = d.Reason,
                eqpName = d.EqpName,
                emNo = d.EmNo,
                specName = d.SpecName,
                specJudge = d.SpecJudge,
                specReason = d.SpecReason,
                priority = d.Priority,
                feature = !string.IsNullOrWhiteSpace(d.FeatureJson) ? JsonSerializer.Deserialize<object>(d.FeatureJson) : null,
                customFeature = !string.IsNullOrWhiteSpace(d.CustomFeatureJson) ? JsonSerializer.Deserialize<object>(d.CustomFeatureJson) : null
            })
        };

        return Ok(packObj);
    }

    [HttpPost("defects/migrate-packs")]
    [ProducesResponseType(typeof(MigratePacksResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MigrateDefectPacks([FromBody] MigrateDefectsRequest? request, CancellationToken cancellationToken)
    {
        var maxLimit = Math.Clamp(request?.Limit ?? 200, 1, 2000);
        List<DefectRecordEntity> defectsToMigrate;

        if (request?.DefectIds != null && request.DefectIds.Count > 0)
        {
            defectsToMigrate = await db.DefectRecords
                .Where(d => request.DefectIds.Contains(d.Id))
                .Take(maxLimit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            defectsToMigrate = await db.DefectRecords
                .Where(d => (d.FeatureJson != null && d.FeatureJson != "") || (d.CustomFeatureJson != null && d.CustomFeatureJson != ""))
                .OrderByDescending(d => d.CreatedAtUtc)
                .Take(maxLimit)
                .ToListAsync(cancellationToken);
        }

        var groups = defectsToMigrate.GroupBy(d => d.KeyId).ToList();
        var packItems = new List<MigratePackItemResponse>();
        int successPacks = 0;
        int failedPacks = 0;

        foreach (var group in groups)
        {
            var keyId = group.Key;
            var groupDefects = group.ToList();

            try
            {
                var packUrl = await storage.UploadProductDefectPackAsync(keyId, groupDefects, cancellationToken);
                successPacks++;

                var product = await db.ProductRecords.FirstOrDefaultAsync(p => p.KeyId == keyId, cancellationToken);
                if (product != null)
                {
                    product.DefectPackUrl = packUrl;
                }

                foreach (var defect in groupDefects)
                {
                    defect.FeatureUrl = $"{packUrl}#{defect.Id:N}";
                    if (!string.IsNullOrWhiteSpace(defect.CustomFeatureJson))
                    {
                        defect.CustomFeatureUrl = $"{packUrl}#{defect.Id:N}_custom";
                    }
                    defect.FeatureJson = null;
                    defect.CustomFeatureJson = null;
                }

                packItems.Add(new MigratePackItemResponse(
                    KeyId: keyId,
                    DefectsCount: groupDefects.Count,
                    Success: true,
                    PackUrl: packUrl,
                    Message: $"Successfully packed {groupDefects.Count} defects into unified JSON pack at {packUrl}."
                ));
            }
            catch (Exception ex)
            {
                failedPacks++;
                packItems.Add(new MigratePackItemResponse(
                    KeyId: keyId,
                    DefectsCount: groupDefects.Count,
                    Success: false,
                    PackUrl: null,
                    Message: $"Failed to consolidate defect pack: {ex.Message}"
                ));
            }
        }

        if (successPacks > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await store.RecordAuditAsync("admin", "Defect.MigratePacks", "DefectPack", "batch",
                $"Consolidated {defectsToMigrate.Count} defects across {groups.Count} products into unified JSON packs.");
        }

        return Ok(new MigratePacksResponse(
            TotalKeyIdsProcessed: groups.Count,
            TotalDefectsProcessed: defectsToMigrate.Count,
            SuccessCount: successPacks,
            FailedCount: failedPacks,
            Packs: packItems
        ));
    }

    [HttpPost("json")]
    [ProducesResponseType(typeof(StoredObjectResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadJson([FromBody] UploadJsonRequest request)
    {
        var storedObject = await storage.UploadJsonAsync(request.Bucket, request.ObjectName, request.Payload);
        await store.SaveStorageObjectAsync(storedObject, "JsonPayload");
        return Created($"/api/v1/storage/{storedObject.Id}", storedObject);
    }

    [HttpPost("images")]
    [ProducesResponseType(typeof(StoredObjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadImage([FromForm] IFormFile? file, [FromForm] string? bucket)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Image file is required.");
        }

        await using var stream = file.OpenReadStream();
        var targetBucket = bucket ?? "images";
        var storedObject = await storage.UploadImageAsync(targetBucket, file.FileName, stream, file.ContentType);
        await store.SaveStorageObjectAsync(storedObject, "Image");
        return Created($"/api/v1/storage/{storedObject.Id}", storedObject);
    }
}
