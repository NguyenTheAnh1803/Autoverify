using System.Collections.Concurrent;
using AutoVerify.Api.Logging;
using AutoVerify.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Services;

public sealed class OverkillQuerySchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OverkillQuerySchedulerHostedService> logger,
    IOptions<BackgroundJobOptions> options) : BackgroundService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RunLocks = new();
    private readonly TimeSpan tickInterval = TimeSpan.FromSeconds(Math.Max(5, options.Value.OverkillSchedulerIntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var autoScope = logger.BeginAutoScope("OverkillQueryScheduler");
        logger.LogInformation("Overkill automated query scheduler started with interval {Interval}", tickInterval);

        using var timer = new PeriodicTimer(tickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExecuteDuePipelinesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in Overkill query scheduler loop");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Overkill automated query scheduler stopped");
    }

    private async Task CheckAndExecuteDuePipelinesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();

        var nowUtc = DateTimeOffset.UtcNow;
        var dueConfigs = await db.OverkillPipelineConfigs
            .AsNoTracking()
            .Where(x => x.Enabled && (x.NextRunAtUtc == null || x.NextRunAtUtc <= nowUtc))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        if (dueConfigs.Count == 0)
        {
            return;
        }

        logger.LogInformation("Overkill scheduler discovered {Count} pipeline(s) due for automated execution", dueConfigs.Count);

        foreach (var cfg in dueConfigs)
        {
            var configId = cfg.Id;
            var configName = cfg.Name;
            var semaphore = RunLocks.GetOrAdd(configId, _ => new SemaphoreSlim(1, 1));

            if (!await semaphore.WaitAsync(0, cancellationToken))
            {
                logger.LogWarning("Overkill pipeline '{PipelineName}' ({ConfigId}) is already executing, skipping this tick", configName, configId);
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var execScope = scopeFactory.CreateScope();
                    var engine = execScope.ServiceProvider.GetRequiredService<IOverkillQueryEngineService>();
                    await engine.ExecutePipelineAsync(configId, "Scheduled", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error occurred during scheduled execution of pipeline '{PipelineName}' ({ConfigId})", configName, configId);
                }
                finally
                {
                    semaphore.Release();
                }
            }, CancellationToken.None);
        }
    }
}
