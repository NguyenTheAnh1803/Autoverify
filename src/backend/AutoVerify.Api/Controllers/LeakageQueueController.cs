using AutoVerify.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/leakage-queue")]
public sealed class LeakageQueueController(ILeakageProcessingService processingService) : ControllerBase
{
    [HttpPost("paste")]
    [ProducesResponseType(typeof(LeakageBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnqueuePaste([FromBody] EnqueuePasteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { message = "Paste text cannot be empty." });
        }

        var username = User.Identity?.Name ?? "admin";
        var batch = await processingService.EnqueuePasteAsync(request, username, cancellationToken);
        return Ok(batch);
    }

    [HttpPost("upload-csv")]
    [ProducesResponseType(typeof(LeakageBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnqueueCsv(
        [FromForm] IFormFile? file,
        [FromForm] string? batchName,
        [FromForm] string? defaultLineCode,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "A non-empty CSV file must be provided." });
        }

        var username = User.Identity?.Name ?? "admin";
        await using var stream = file.OpenReadStream();
        var batch = await processingService.EnqueueCsvAsync(stream, file.FileName, batchName, defaultLineCode, username, cancellationToken);
        return Ok(batch);
    }

    [HttpGet("batches")]
    [ProducesResponseType(typeof(IReadOnlyCollection<LeakageBatchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBatches([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) limit = 50;
        if (limit > 500) limit = 500;

        var batches = await processingService.GetBatchesAsync(cancellationToken);
        if (batches.Count > limit)
        {
            batches = batches.Take(limit).ToList();
        }

        return Ok(batches);
    }

    [HttpGet("items")]
    [ProducesResponseType(typeof(PagedResponse<LeakageQueueItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetItems(
        [FromQuery] Guid? batchId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        var response = await processingService.GetItemsAsync(batchId, status, search, page, pageSize, cancellationToken);
        return Ok(response);
    }

    [HttpGet("items/{id:guid}/details")]
    [ProducesResponseType(typeof(LeakageQueueItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItemDetails(Guid id, CancellationToken cancellationToken)
    {
        var details = await processingService.GetItemDetailAsync(id, cancellationToken);
        if (details == null)
        {
            return NotFound(new { message = $"Leakage queue item {id} was not found." });
        }

        return Ok(details);
    }

    [HttpPost("process")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcessQueue([FromBody] ProcessLeakageQueueRequest? request, CancellationToken cancellationToken)
    {
        request ??= new ProcessLeakageQueueRequest(null, null, null);
        var started = await processingService.StartProcessingAsync(request, cancellationToken);
        if (!started)
        {
            return Conflict(new { message = "Queue processing is already active. Check progress status." });
        }

        return Ok(new { message = "Processing started.", isRunning = true });
    }

    [HttpGet("progress")]
    [ProducesResponseType(typeof(LeakageQueueProgressDto), StatusCodes.Status200OK)]
    public IActionResult GetProgress()
    {
        var progress = processingService.GetProgress();
        return Ok(progress);
    }

    [HttpPost("retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RetryItems([FromBody] RetryQueueItemsRequest request, CancellationToken cancellationToken)
    {
        var resetCount = await processingService.RetryItemsAsync(request, cancellationToken);
        return Ok(new { resetCount, message = $"Reset {resetCount} item(s) to Pending." });
    }

    [HttpDelete("clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearQueue([FromBody] ClearQueueRequest request, CancellationToken cancellationToken)
    {
        var removedCount = await processingService.ClearQueueAsync(request, cancellationToken);
        return Ok(new { removedCount, message = $"Cleared {removedCount} queue item(s)." });
    }
}
