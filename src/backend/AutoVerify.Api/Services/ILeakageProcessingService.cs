namespace AutoVerify.Api.Services;

public interface ILeakageProcessingService
{
    Task<LeakageBatchDto> EnqueuePasteAsync(
        EnqueuePasteRequest request,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<LeakageBatchDto> EnqueueCsvAsync(
        Stream fileStream,
        string fileName,
        string? batchName,
        string? defaultLineCode,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<bool> StartProcessingAsync(
        ProcessLeakageQueueRequest request,
        CancellationToken cancellationToken = default);

    LeakageQueueProgressDto GetProgress();

    Task<int> RetryItemsAsync(
        RetryQueueItemsRequest request,
        CancellationToken cancellationToken = default);

    Task<int> ClearQueueAsync(
        ClearQueueRequest request,
        CancellationToken cancellationToken = default);

    Task<LeakageQueueItemDetailDto?> GetItemDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<LeakageBatchDto>> GetBatchesAsync(
        CancellationToken cancellationToken = default);

    Task<PagedResponse<LeakageQueueItemDto>> GetItemsAsync(
        Guid? batchId,
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
