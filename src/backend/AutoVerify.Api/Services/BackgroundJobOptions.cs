namespace AutoVerify.Api.Services;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public int OverkillSchedulerIntervalSeconds { get; set; } = 60;
}
