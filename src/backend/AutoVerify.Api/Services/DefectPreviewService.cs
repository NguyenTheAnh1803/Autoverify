using System.Text.Json;
using AutoVerify.Api.Analytics;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Services;

public sealed class DefectPreviewService : IDefectPreviewService
{
    private readonly AutoVerifyDbContext _db;
    private readonly IDefectPreviewIngestionService _ingestionService;
    private readonly IDefectFeatureAnalyticsService _analyticsService;
    private readonly DefectPreviewOptions _options;
    private readonly ILogger<DefectPreviewService> _logger;

    public DefectPreviewService(
        AutoVerifyDbContext db,
        IDefectPreviewIngestionService ingestionService,
        IDefectFeatureAnalyticsService analyticsService,
        IOptions<DefectPreviewOptions> options,
        ILogger<DefectPreviewService> logger)
    {
        _db = db;
        _ingestionService = ingestionService;
        _analyticsService = analyticsService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DefectPreviewSessionDto> CreateSessionAsync(CreateDefectPreviewSessionRequest request, CancellationToken cancellationToken = default)
    {
        var effectiveUri = request.EffectiveUri;
        if (string.IsNullOrWhiteSpace(effectiveUri))
        {
            throw new ArgumentException("StorageUri or SourceUri is required.", nameof(request.StorageUri));
        }

        var canonicalFormat = DefectDataFormats.Auto;
        if (!string.IsNullOrWhiteSpace(request.Format))
        {
            if (!DefectDataFormats.TryNormalize(request.Format, out var normalized))
            {
                throw new ArgumentException(
                    $"Invalid defect data format '{request.Format}'. Supported formats: {string.Join(", ", DefectDataFormats.AllCanonical)}.",
                    nameof(request.Format));
            }
            canonicalFormat = normalized;
        }

        var ttl = request.TtlMinutes.HasValue && request.TtlMinutes.Value > 0
            ? Math.Clamp(request.TtlMinutes.Value, 5, 1440)
            : _options.DefaultTtlMinutes;

        var maxRows = request.MaxRows.HasValue && request.MaxRows.Value > 0
            ? Math.Clamp(request.MaxRows.Value, 1, _options.MaxRowsLimit)
            : _options.MaxRowsLimit;

        var session = new DefectPreviewSessionEntity
        {
            Id = Guid.NewGuid(),
            StorageUri = effectiveUri.Trim(),
            KeyId = request.KeyId?.Trim() ?? string.Empty,
            Format = canonicalFormat,
            DetectedFormat = canonicalFormat != DefectDataFormats.Auto ? canonicalFormat : null,
            ProcessingStage = "Queued",
            Status = "Pending",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(ttl),
            TtlMinutes = ttl,
            MaxRows = maxRows,
            LastAccessedAtUtc = DateTimeOffset.UtcNow
        };

        _db.DefectPreviewSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        _ingestionService.QueueIngestion(session.Id);

        return new DefectPreviewSessionDto(
            SessionId: session.Id,
            Status: session.Status,
            SourceUri: session.StorageUri,
            CreatedAtUtc: session.CreatedAtUtc,
            ExpiresAtUtc: session.ExpiresAtUtc,
            KeyId: session.KeyId,
            ProcessedRecords: session.ProcessedRecords,
            TotalRecords: session.TotalRowsCount,
            DetectedFormat: session.DetectedFormat,
            ProcessingStage: session.ProcessingStage,
            WarningMessage: session.WarningMessage
        );
    }

    public async Task<DefectPreviewProgressDto?> GetProgressAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _db.DefectPreviewSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        Dictionary<string, int>? breakdown = null;
        if (!string.IsNullOrWhiteSpace(session.SeverityBreakdownJson))
        {
            try
            {
                breakdown = JsonSerializer.Deserialize<Dictionary<string, int>>(session.SeverityBreakdownJson);
            }
            catch
            {
                // Fallback to null
            }
        }

        var filterSummary = new DefectPreviewFilterSummaryDto(
            Total: session.ProcessedRecords,
            NgCount: session.NgCount,
            OkCount: session.OkCount,
            SeverityBreakdown: breakdown ?? new Dictionary<string, int>()
        );

        double percentComplete;
        if (session.Status == "Ready")
        {
            percentComplete = 100.0;
        }
        else if (session.TotalRowsCount > 0)
        {
            percentComplete = Math.Round((double)session.ProcessedRecords / session.TotalRowsCount * 100.0, 1);
        }
        else
        {
            percentComplete = 0.0;
        }

        return new DefectPreviewProgressDto(
            SessionId: session.Id,
            Status: session.Status,
            ProcessedRecords: session.ProcessedRecords,
            TotalRecords: session.TotalRowsCount,
            PercentComplete: percentComplete,
            FilterSummary: filterSummary,
            ErrorMessage: session.ErrorMessage,
            DetectedFormat: session.DetectedFormat,
            ProcessingStage: session.ProcessingStage,
            WarningMessage: session.WarningMessage
        );
    }

    public async Task<DefectPreviewPagedResponse?> QueryRecordsAsync(Guid sessionId, DefectPreviewQueryParams queryParams, CancellationToken cancellationToken = default)
    {
        var sessionExists = await _db.DefectPreviewSessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId, cancellationToken);

        if (!sessionExists)
        {
            return null;
        }

        var page = Math.Max(1, queryParams.Page);
        var pageSize = Math.Clamp(queryParams.PageSize, 1, _options.MaxPageSize);

        var query = _db.DefectPreviewRecords
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
        {
            var kw = queryParams.Keyword.Trim();
            query = query.Where(r =>
                r.DefectName.Contains(kw) ||
                (r.ComponentName != null && r.ComponentName.Contains(kw)) ||
                (r.DefectId != null && r.DefectId.Contains(kw)));
        }

        if (!string.IsNullOrWhiteSpace(queryParams.Judge) && !queryParams.Judge.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var judge = queryParams.Judge.Trim();
            query = query.Where(r => r.Judge == judge);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.Severity) && !queryParams.Severity.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var sev = queryParams.Severity.Trim();
            query = query.Where(r => r.Severity == sev);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.DefectTable) && !queryParams.DefectTable.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var table = queryParams.DefectTable.Trim();
            query = query.Where(r => r.DefectTable == table);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.LineCode))
        {
            var line = queryParams.LineCode.Trim();
            query = query.Where(r => r.LineCode == line);
        }
        if (!string.IsNullOrWhiteSpace(queryParams.MachineCode))
        {
            var machine = queryParams.MachineCode.Trim();
            query = query.Where(r => r.MachineCode == machine);
        }
        if (!string.IsNullOrWhiteSpace(queryParams.StationCode))
        {
            var station = queryParams.StationCode.Trim();
            query = query.Where(r => r.StationCode == station);
        }
        if (!string.IsNullOrWhiteSpace(queryParams.ClassifierRuleCode))
        {
            var rule = queryParams.ClassifierRuleCode.Trim();
            query = query.Where(r => r.ClassifierRuleCode == rule);
        }
        if (!string.IsNullOrWhiteSpace(queryParams.ClassifierRuleVersion))
        {
            var ver = queryParams.ClassifierRuleVersion.Trim();
            query = query.Where(r => r.ClassifierRuleVersion == ver);
        }
        if (!string.IsNullOrWhiteSpace(queryParams.DefectFamily))
        {
            var family = queryParams.DefectFamily.Trim();
            query = query.Where(r => r.DefectFamily == family);
        }
        if (queryParams.ObservationId.HasValue)
        {
            var obsId = queryParams.ObservationId.Value;
            query = query.Where(r => r.ObservationId == obsId);
        }

        var isDesc = string.Equals(queryParams.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = queryParams.SortBy?.Trim().ToLowerInvariant();

        query = (sortBy, isDesc) switch
        {
            ("keyid", true) => query.OrderByDescending(r => r.KeyId).ThenBy(r => r.RowIndex),
            ("keyid", false) => query.OrderBy(r => r.KeyId).ThenBy(r => r.RowIndex),

            ("defectid", true) => query.OrderByDescending(r => r.DefectId).ThenBy(r => r.RowIndex),
            ("defectid", false) => query.OrderBy(r => r.DefectId).ThenBy(r => r.RowIndex),

            ("defecttype", true) => query.OrderByDescending(r => r.DefectType).ThenBy(r => r.RowIndex),
            ("defecttype", false) => query.OrderBy(r => r.DefectType).ThenBy(r => r.RowIndex),

            ("defectname", true) => query.OrderByDescending(r => r.DefectName).ThenBy(r => r.RowIndex),
            ("defectname", false) => query.OrderBy(r => r.DefectName).ThenBy(r => r.RowIndex),

            ("judge", true) => query.OrderByDescending(r => r.Judge).ThenBy(r => r.RowIndex),
            ("judge", false) => query.OrderBy(r => r.Judge).ThenBy(r => r.RowIndex),

            ("severity", true) => query.OrderByDescending(r => r.Severity).ThenBy(r => r.RowIndex),
            ("severity", false) => query.OrderBy(r => r.Severity).ThenBy(r => r.RowIndex),

            ("coordinatex", true) => query.OrderByDescending(r => r.CoordinateX).ThenBy(r => r.RowIndex),
            ("coordinatex", false) => query.OrderBy(r => r.CoordinateX).ThenBy(r => r.RowIndex),

            ("coordinatey", true) => query.OrderByDescending(r => r.CoordinateY).ThenBy(r => r.RowIndex),
            ("coordinatey", false) => query.OrderBy(r => r.CoordinateY).ThenBy(r => r.RowIndex),

            ("componentname", true) => query.OrderByDescending(r => r.ComponentName).ThenBy(r => r.RowIndex),
            ("componentname", false) => query.OrderBy(r => r.ComponentName).ThenBy(r => r.RowIndex),

            ("defecttable", true) => query.OrderByDescending(r => r.DefectTable).ThenBy(r => r.RowIndex),
            ("defecttable", false) => query.OrderBy(r => r.DefectTable).ThenBy(r => r.RowIndex),

            ("confidencescore", true) => query.OrderByDescending(r => r.ConfidenceScore).ThenBy(r => r.RowIndex),
            ("confidencescore", false) => query.OrderBy(r => r.ConfidenceScore).ThenBy(r => r.RowIndex),

            ("inspectedatutc", true) => query.OrderByDescending(r => r.InspectedAtUtc).ThenBy(r => r.RowIndex),
            ("inspectedatutc", false) => query.OrderBy(r => r.InspectedAtUtc).ThenBy(r => r.RowIndex),

            ("id", true) => query.OrderByDescending(r => r.Id),
            ("id", false) => query.OrderBy(r => r.Id),

            ("rowindex", true) => query.OrderByDescending(r => r.RowIndex),
            _ => query.OrderBy(r => r.RowIndex)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new DefectPreviewRecordDto(
                r.Id,
                r.RowIndex,
                r.DefectId,
                r.KeyId,
                r.DefectName,
                r.DefectTable,
                r.DefectType,
                r.ComponentName,
                r.CoordinateX,
                r.CoordinateY,
                r.Severity,
                r.Judge,
                r.ConfidenceScore,
                r.HasFeatureJson,
                r.FeatureJsonSnippet,
                r.RawFeatureUri,
                r.RawImageUri,
                r.InspectedAtUtc,
                r.ObservationId,
                r.LineCode,
                r.MachineCode,
                r.StationCode,
                r.ClassifierRuleCode,
                r.ClassifierRuleName,
                r.ClassifierRuleVersion,
                r.DefectFamily,
                r.DefectClass
            ))
            .ToListAsync(cancellationToken);

        return new DefectPreviewPagedResponse(
            Items: items,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages
        );
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _ingestionService.CancelIngestion(sessionId);
        _analyticsService.CancelJobsForSession(sessionId);

        var session = await _db.DefectPreviewSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session == null)
        {
            return false;
        }

        _db.DefectPreviewSessions.Remove(session);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted defect preview session and staged records: {SessionId}", sessionId);
        return true;
    }
}
