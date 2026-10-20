using Microsoft.Extensions.Options;

namespace AutoVerify.Api.Integration;

/// <summary>Polls the Master for verdicts of submitted defect images. Does nothing unless AiVerification:Enabled is true.</summary>
public sealed class AiVerificationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AiVerificationOptions> options,
    ILogger<AiVerificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("AI verification polling is disabled (AiVerification:Enabled=false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PollIntervalSeconds)));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var completed = await scope.ServiceProvider.GetRequiredService<IAiVerificationService>().PollAsync(stoppingToken);
                if (completed > 0) logger.LogInformation("Collected {Count} AI verdict(s) from the Master.", completed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI verification poll failed.");
            }
        }
    }
}
