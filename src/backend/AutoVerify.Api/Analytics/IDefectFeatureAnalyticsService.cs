namespace AutoVerify.Api.Analytics;

public interface IDefectFeatureAnalyticsService
{
    Task<FeatureSchemaReadinessResponse?> GetSchemaAndReadinessAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<SessionScopeFacetsResponse?> GetScopeFacetsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<AnalyticsJobDto> CreateJobAsync(Guid sessionId, CreateAnalyticsJobRequest request, string username = "system", CancellationToken cancellationToken = default);
    Task<List<AnalyticsJobDto>> ListJobsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<AnalyticsJobDto?> GetJobStatusAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default);
    Task<AnalyticsJobDto?> GetJobResultAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> CancelJobAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> DeleteJobAsync(Guid sessionId, Guid jobId, CancellationToken cancellationToken = default);
    void CancelJobsForSession(Guid sessionId);
}
