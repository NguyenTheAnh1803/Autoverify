using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class VerificationController(AutoVerifyStore store) : ControllerBase
{
    [HttpPost("verify/overkill")]
    [ProducesResponseType(typeof(VerifyBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyOverkill([FromBody] VerifyBatchRequest request)
    {
        try
        {
            var result = await store.VerifyOverkillAsync(request);
            await store.RecordAuditAsync("admin", "Verify.Overkill", "VerificationHistory", result.ScanListId?.ToString() ?? "adhoc", $"Overkill auto-verify: {result.EligibleItems} eligible, {result.SkippedItems} skipped.");
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("verify/leakage")]
    [ProducesResponseType(typeof(VerifyBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyLeakage([FromBody] VerifyBatchRequest request)
    {
        try
        {
            var result = await store.VerifyLeakageAsync(request);
            await store.RecordAuditAsync("admin", "Verify.Leakage", "VerificationHistory", result.ScanListId?.ToString() ?? "adhoc", $"Leakage verify: {result.TotalItems} items.");
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("overkill/analysis")]
    [ProducesResponseType(typeof(OverkillAnalysisResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverkillAnalysis(
        [FromQuery] string? machineCode,
        [FromQuery] string? lineCode,
        [FromQuery] double minNgShare = 0)
    {
        var analysis = await store.GetOverkillAnalysisAsync(machineCode, lineCode, minNgShare);
        return Ok(analysis);
    }

    [HttpGet("verification/overkill-trend")]
    [ProducesResponseType(typeof(OverkillTrendResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverkillTrend(
        [FromQuery] string? machineCode,
        [FromQuery] string? lineCode,
        [FromQuery] string? modelCode,
        [FromQuery] string? defectFamily,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc)
    {
        var trend = await store.GetOverkillTrendAsync(machineCode, lineCode, defectFamily, fromUtc, toUtc, modelCode);
        return Ok(trend);
    }

    [HttpGet("verification/anomaly-thumbnails")]
    [ProducesResponseType(typeof(PagedResponse<AnomalyThumbnailItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnomalyThumbnails(
        [FromQuery] string? category = "All",
        [FromQuery] string? defectFamily = null,
        [FromQuery] string? machineCode = null,
        [FromQuery] string? lineCode = null,
        [FromQuery] string? modelCode = null,
        [FromQuery] string? defectCluster = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        var request = new AnomalyThumbnailQueryRequest(
            category,
            defectFamily,
            machineCode,
            lineCode,
            modelCode,
            defectCluster,
            search,
            fromUtc,
            toUtc,
            page,
            pageSize
        );

        var response = await store.GetAnomalyThumbnailsAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("verification-history")]
    [ProducesResponseType(typeof(PagedResponse<VerificationHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? category,
        [FromQuery] string? machineCode,
        [FromQuery] string? lineCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var history = await store.GetVerificationHistoryAsync(category, machineCode, lineCode, page, pageSize);
        return Ok(history);
    }
}
