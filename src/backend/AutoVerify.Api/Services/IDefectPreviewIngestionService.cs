namespace AutoVerify.Api.Services;

public interface IDefectPreviewIngestionService
{
    void QueueIngestion(Guid sessionId);
    bool CancelIngestion(Guid sessionId);
    Task IngestSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
