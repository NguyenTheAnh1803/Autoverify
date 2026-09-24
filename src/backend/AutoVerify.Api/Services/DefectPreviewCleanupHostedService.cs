using AutoVerify.Api.Analytics;
using AutoVerify.Api.Logging;
using AutoVerify.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Services;

public sealed class DefectPreviewCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IDefectPreviewIngestionService ingestionService,
    IDefectFeatureAnalyticsService analyticsService,
    IOptions<DefectPreviewOptions> options,
    ILogger<DefectPreviewCleanupHostedService> logger) : BackgroundService
{
    private readonly DefectPreviewOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var autoScope = logger.BeginAutoScope("DefectPreviewCleanup");
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CleanupIntervalMinutes));
        logger.LogInformation("DefectPreviewCleanupHostedService started with interval: {Interval}", interval);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CleanupExpiredSessionsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occurred in DefectPreviewCleanupHostedService loop");
        }
    }

    private async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

            var now = DateTimeOffset.UtcNow;
            var expiredSessions = await db.DefectPreviewSessions
                .Where(s => s.ExpiresAtUtc <= now)
                .ToListAsync(cancellationToken);

            if (expiredSessions.Count > 0)
            {
                logger.LogInformation("Found {Count} expired defect preview sessions to clean up", expiredSessions.Count);

                foreach (var session in expiredSessions)
                {
                    ingestionService.CancelIngestion(session.Id);
                    analyticsService.CancelJobsForSession(session.Id);
                }

                db.DefectPreviewSessions.RemoveRange(expiredSessions);
            }

            var expiredJobs = await db.DefectAnalyticsJobs
                .Where(j => j.ExpiresAtUtc <= now)
                .ToListAsync(cancellationToken);

            if (expiredJobs.Count > 0)
            {
                logger.LogInformation("Found {Count} expired defect analytics jobs to clean up", expiredJobs.Count);
                db.DefectAnalyticsJobs.RemoveRange(expiredJobs);
            }

            if (expiredSessions.Count > 0 || expiredJobs.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignored on shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while cleaning up expired defect preview sessions");
        }
    }
}
