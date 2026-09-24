using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/scan-executions")]
public sealed class ScanExecutionsController(AutoVerifyStore store, SeaweedFsStorageManager storage) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ScanExecutionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScanExecutions([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var executions = await store.GetScanExecutionsAsync(page, pageSize);
        return Ok(executions);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScanExecutionResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateScanExecution([FromBody] CreateScanExecutionRequest request)
    {
        var execution = await store.CreateScanExecutionAsync(request, storage);
        await store.RecordAuditAsync("admin", "ScanExecution.Create", "ScanExecution", execution.Id.ToString(), $"Started scan '{execution.Name}'.");
        return Created($"/api/v1/scan-executions/{execution.Id}", execution);
    }

    [HttpGet("{executionId:guid}/results")]
    [ProducesResponseType(typeof(PagedResponse<VerificationResultResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetResults(Guid executionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var results = await store.GetResultsAsync(executionId, page, pageSize);
        return Ok(results);
    }
}
