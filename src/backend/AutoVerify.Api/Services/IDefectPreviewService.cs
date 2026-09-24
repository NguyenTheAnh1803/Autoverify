using AutoVerify.Api;

namespace AutoVerify.Api.Services;

public interface IDefectPreviewService
{
    Task<DefectPreviewSessionDto> CreateSessionAsync(CreateDefectPreviewSessionRequest request, CancellationToken cancellationToken = default);
    Task<DefectPreviewProgressDto?> GetProgressAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<DefectPreviewPagedResponse?> QueryRecordsAsync(Guid sessionId, DefectPreviewQueryParams queryParams, CancellationToken cancellationToken = default);
    Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
