using System.Collections.Concurrent;
using System.Text.Json;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Services;

public sealed class DefectPreviewIngestionService : IDefectPreviewIngestionService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DefectPreviewIngestionService> _logger;
    private readonly DefectPreviewOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeIngestions = new();

    public DefectPreviewIngestionService(
        IServiceScopeFactory scopeFactory,
        IOptions<DefectPreviewOptions> options,
        ILogger<DefectPreviewIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        var maxConcurrency = Math.Max(1, _options.MaxConcurrentIngestions);
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public void QueueIngestion(Guid sessionId)
    {
        var cts = new CancellationTokenSource();
        _activeIngestions[sessionId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await IngestSessionAsync(sessionId, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled background exception ingesting defect preview session {SessionId}", sessionId);
            }
            finally
            {
                if (_activeIngestions.TryRemove(sessionId, out var removedCts))
                {
                    removedCts.Dispose();
                }
            }
        });
    }

    public bool CancelIngestion(Guid sessionId)
    {
        if (_activeIngestions.TryRemove(sessionId, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public async Task IngestSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Defect preview ingestion waiting for execution slot: {SessionId}", sessionId);
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            _logger.LogInformation("Defect preview ingestion starting: {SessionId}", sessionId);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
            var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

            var session = await db.DefectPreviewSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
            if (session == null)
            {
                _logger.LogWarning("Defect preview session not found: {SessionId}", sessionId);
                return;
            }

            if (session.Status is "Ready" or "Failed" or "Expired")
            {
                _logger.LogInformation("Defect preview session already in final status '{Status}': {SessionId}", session.Status, sessionId);
                return;
            }

            session.Status = "Ingesting";
            session.ProcessingStage = "Connecting";
            session.StartedAtUtc = DateTimeOffset.UtcNow;
            session.LastAccessedAtUtc = DateTimeOffset.UtcNow;
            session.ErrorMessage = null;
            await db.SaveChangesAsync(cancellationToken);

            StorageStreamResult? storageResult = null;
            try
            {
                storageResult = await storageService.OpenReadStreamAsync(session.StorageUri, cancellationToken);
                if (storageResult == null)
                {
                    session.Status = "Failed";
                    session.ProcessingStage = "Failed";
                    session.ErrorMessage = $"Failed to open read stream from storage URI: {session.StorageUri}";
                    session.CompletedAtUtc = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(CancellationToken.None);
                    return;
                }

                var batchSize = _options.BatchInsertSize > 0 ? _options.BatchInsertSize : 1000;
                var batch = new List<DefectPreviewRecordEntity>(batchSize);
                var featuresBatch = new List<DefectPreviewFeatureEntity>(batchSize * 4);
                var totalProcessed = 0;
                var skippedOtherKeys = 0;
                var ngCount = 0;
                var okCount = 0;
                var severityMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var maxRowsLimit = session.MaxRows > 0 ? session.MaxRows : _options.MaxRowsLimit;
                var metadata = new StreamMetadata(
                    SourceUri: session.StorageUri,
                    ContentType: storageResult.ContentType,
                    ContentEncoding: storageResult.ContentEncoding,
                    ContentLength: storageResult.ContentLength,
                    FormatHint: session.Format
                );

                var parser = DefectStreamParser.ParseDefectsAsync(
                    storageResult.Stream,
                    metadata,
                    onFormatResolved: format =>
                    {
                        session.DetectedFormat = format;
                    },
                    onStageChanged: stage =>
                    {
                        session.ProcessingStage = stage;
                    },
                    onWarning: warning =>
                    {
                        session.WarningMessage = warning;
                        session.ProcessingStage = "Completed (Recovered)";
                    },
                    maxSnippetLength: _options.FeatureSnippetMaxLength,
                    maxRowsLimit: maxRowsLimit,
                    zipSafeguards: null,
                    enableRecovery: _options.EnableSafeJsonRecovery,
                    ct: cancellationToken);

                await foreach (var record in parser.WithCancellation(cancellationToken))
                {
                    if (totalProcessed >= maxRowsLimit)
                    {
                        _logger.LogInformation("Defect preview reached max rows limit {MaxRows}: {SessionId}", maxRowsLimit, sessionId);
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(session.KeyId))
                    {
                        if (!string.IsNullOrWhiteSpace(record.KeyId) &&
                            !string.Equals(record.KeyId.Trim(), session.KeyId.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            skippedOtherKeys++;
                            continue;
                        }

                        record.KeyId = session.KeyId.Trim();
                    }

                    totalProcessed++;
                    record.SessionId = sessionId;
                    record.RowIndex = totalProcessed;

                    if (string.Equals(record.Judge, "NG", StringComparison.OrdinalIgnoreCase))
                    {
                        ngCount++;
                    }
                    else if (string.Equals(record.Judge, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        okCount++;
                    }

                    var sev = string.IsNullOrWhiteSpace(record.Severity) ? "Unknown" : record.Severity;
                    severityMap[sev] = severityMap.GetValueOrDefault(sev, 0) + 1;

                    if (record.ExtractedFeatures.Count > 0)
                    {
                        foreach (var feature in record.ExtractedFeatures)
                        {
                            feature.SessionId = sessionId;
                            feature.RowIndex = totalProcessed;
                            feature.ObservationId = record.ObservationId;
                            featuresBatch.Add(feature);
                        }
                    }

                    batch.Add(record);

                    if (batch.Count >= batchSize)
                    {
                        await db.DefectPreviewRecords.AddRangeAsync(batch, cancellationToken);
                        if (featuresBatch.Count > 0)
                        {
                            await db.DefectPreviewFeatures.AddRangeAsync(featuresBatch, cancellationToken);
                            featuresBatch.Clear();
                        }
                        session.ProcessedRecords = totalProcessed;
                        session.LoadedRowsCount = totalProcessed;
                        session.LastAccessedAtUtc = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        db.ChangeTracker.Clear();
                        db.Attach(session);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    await db.DefectPreviewRecords.AddRangeAsync(batch, cancellationToken);
                    batch.Clear();
                }

                if (featuresBatch.Count > 0)
                {
                    await db.DefectPreviewFeatures.AddRangeAsync(featuresBatch, cancellationToken);
                    featuresBatch.Clear();
                }

                session.Status = "Ready";
                session.ProcessingStage = "Completed";
                session.ProcessedRecords = totalProcessed;
                session.LoadedRowsCount = totalProcessed;
                session.TotalRowsCount = totalProcessed;
                session.CompletedAtUtc = DateTimeOffset.UtcNow;
                session.LastAccessedAtUtc = DateTimeOffset.UtcNow;

                var ttl = session.TtlMinutes > 0 ? session.TtlMinutes : _options.DefaultTtlMinutes;
                session.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(ttl);

                session.NgCount = ngCount;
                session.OkCount = okCount;
                session.SeverityBreakdownJson = JsonSerializer.Serialize(severityMap);
                session.ErrorMessage = null;

                if (skippedOtherKeys > 0)
                {
                    session.WarningMessage = $"Skipped {skippedOtherKeys} record(s) belonging to a different Key ID. Preview contains only '{session.KeyId}'.";
                }

                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();

                _logger.LogInformation(
                    "Defect preview ingestion completed successfully: {SessionId}, total records: {Total}, skipped other keys: {SkippedOtherKeys}",
                    sessionId,
                    totalProcessed,
                    skippedOtherKeys);
            }
            catch (DefectParseException ex)
            {
                _logger.LogWarning(ex, "Defect preview stream parse failed: {SessionId}, {Message}", sessionId, ex.Message);

                // Clean up any partially staged records/features for atomic consistency
                try
                {
                    var existingRecords = await db.DefectPreviewRecords.Where(r => r.SessionId == sessionId).ToListAsync(CancellationToken.None);
                    if (existingRecords.Count > 0)
                    {
                        db.DefectPreviewRecords.RemoveRange(existingRecords);
                    }
                    var existingFeatures = await db.DefectPreviewFeatures.Where(f => f.SessionId == sessionId).ToListAsync(CancellationToken.None);
                    if (existingFeatures.Count > 0)
                    {
                        db.DefectPreviewFeatures.RemoveRange(existingFeatures);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Failed to cleanup partially staged records for session {SessionId}", sessionId);
                }

                session.Status = "Failed";
                session.ProcessingStage = ex.Stage;
                session.ErrorMessage = ex.Message;
                session.ProcessedRecords = 0;
                session.LoadedRowsCount = 0;
                session.TotalRowsCount = 0;
                session.CompletedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Defect preview ingestion cancelled: {SessionId}", sessionId);
                try
                {
                    var existingRecords = await db.DefectPreviewRecords.Where(r => r.SessionId == sessionId).ToListAsync(CancellationToken.None);
                    if (existingRecords.Count > 0)
                    {
                        db.DefectPreviewRecords.RemoveRange(existingRecords);
                    }
                    var existingFeatures = await db.DefectPreviewFeatures.Where(f => f.SessionId == sessionId).ToListAsync(CancellationToken.None);
                    if (existingFeatures.Count > 0)
                    {
                        db.DefectPreviewFeatures.RemoveRange(existingFeatures);
                    }
                }
                catch { }

                session.Status = "Failed";
                session.ProcessingStage = "Cancelled";
                session.ErrorMessage = "Ingestion cancelled.";
                session.ProcessedRecords = 0;
                session.LoadedRowsCount = 0;
                session.TotalRowsCount = 0;
                session.CompletedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Defect preview ingestion failed: {SessionId}", sessionId);
                try
                {
                    var existingRecords = await db.DefectPreviewRecords.Where(r => r.SessionId == sessionId).ToListAsync(CancellationToken.None);
                    if (existingRecords.Count > 0)
                    {
                        db.DefectPreviewRecords.RemoveRange(existingRecords);
                    }
                    var existingFeatures = await db.DefectPreviewFeatures.Where(f => f.SessionId == sessionId).ToListAsync(CancellationToken.None);
                    if (existingFeatures.Count > 0)
                    {
                        db.DefectPreviewFeatures.RemoveRange(existingFeatures);
                    }
                }
                catch { }

                session.Status = "Failed";
                session.ProcessingStage = "Failed";
                session.ErrorMessage = "An unexpected error occurred while decoding and parsing defect stream data.";
                session.ProcessedRecords = 0;
                session.LoadedRowsCount = 0;
                session.TotalRowsCount = 0;
                session.CompletedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            finally
            {
                if (storageResult != null)
                {
                    await storageResult.DisposeAsync();
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
