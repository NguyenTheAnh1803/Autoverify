using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Integration;

/// <summary>
/// Sends new products to the Master (when AiVerification:AutoSubmit is on) and polls it for verdicts.
/// Does nothing unless AiVerification:Enabled is true.
/// </summary>
public sealed class AiVerificationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AiVerificationOptions> options,
    AiDispatchState state,
    ILogger<AiVerificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("AI verification polling is disabled (AiVerification:Enabled=false).");
            return;
        }
        if (options.Value.AutoSubmit)
            logger.LogInformation("AI auto-submit is on: up to {Max} product(s) per tick, defects from the last {Hours}h, only for mappings with AutoSubmit.",
                options.Value.AutoSubmitMaxProductsPerTick, options.Value.AutoSubmitLookbackHours);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PollIntervalSeconds)));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ai = scope.ServiceProvider.GetRequiredService<IAiVerificationService>();

                if (options.Value.AutoSubmit)
                    await AutoSubmitAsync(ai, stoppingToken);

                var completed = await ai.PollAsync(stoppingToken);
                if (completed > 0) logger.LogInformation("Collected {Count} AI verdict(s) from the Master.", completed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI verification tick failed.");
            }
        }
    }

    private async Task AutoSubmitAsync(IAiVerificationService ai, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (state.IsPaused(now)) return;

        var result = await ai.AutoSubmitAsync(cancellationToken);
        var backoff = TimeSpan.FromMinutes(Math.Max(1, options.Value.AutoSubmitBackoffMinutes));
        state.Record(result, now, backoff);

        if (result.Transient)
            logger.LogWarning("AI auto-submit paused for {Minutes} min: {Message}", backoff.TotalMinutes, result.Message);
        else if (result.Picked > 0)
            logger.LogInformation("AI auto-submit: picked {Picked} product(s), submitted {Submitted} defect(s), no image for {NoImage}.",
                result.Picked, result.Submitted, result.NoImage);
    }
}
