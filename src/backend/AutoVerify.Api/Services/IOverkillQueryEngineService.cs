namespace AutoVerify.Api.Services;

using AutoVerify.Api.Persistence.Entities;

public interface IOverkillQueryEngineService
{
    Task<TestPipelineMappingResponse> TestMappingAsync(
        TestPipelineMappingRequest request,
        CancellationToken cancellationToken = default);

    Task<OverkillPipelineExecutionEntity> ExecutePipelineAsync(
        Guid configId,
        string triggerType = "Scheduled",
        CancellationToken cancellationToken = default);
}
