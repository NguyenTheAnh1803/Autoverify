using AutoVerify.Api.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/analytics/production-features")]
public sealed class ProductionFeatureAnalyticsController(ProductionFeatureAnalyticsService analytics) : ControllerBase
{
    [HttpGet("facets")]
    [ProducesResponseType(typeof(ProductionFeatureJobDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacets(CancellationToken cancellationToken) =>
        Ok(await analytics.GetFacetsAsync(cancellationToken));

    [HttpPost("jobs")]
    [ProducesResponseType(typeof(ProductionFeatureJobDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartJob(
        [FromQuery] string? machineCode,
        [FromQuery] string? lineCode,
        [FromQuery] string period = "1w",
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] string? defectName = null,
        [FromQuery] string? judge = null,
        CancellationToken cancellationToken = default)
    {
        var job = await analytics.StartJobAsync(machineCode, lineCode, period, fromUtc, toUtc, defectName, judge, cancellationToken);
        return Accepted($"/api/v1/analytics/production-features/jobs/{job.JobId}", job);
    }

    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(ProductionFeatureJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJob(Guid jobId)
    {
        var job = analytics.GetJob(jobId);
        return job is null ? NotFound(new { error = "Feature load job was not found." }) : Ok(job);
    }

    [HttpPost("jobs/{jobId:guid}/analyze")]
    [ProducesResponseType(typeof(ProductionFeatureAnalysisResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult AnalyzeJob(
        Guid jobId,
        [FromQuery] string analysisType = "SPC",
        [FromQuery] string? featureName = null,
        [FromQuery] string? featureNames = null)
    {
        try
        {
            var names = string.IsNullOrWhiteSpace(featureNames)
                ? []
                : featureNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Ok(analytics.Analyze(jobId, analysisType, featureName, names));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("jobs/{jobId:guid}/cancel")]
    [ProducesResponseType(typeof(ProductionFeatureJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult CancelJob(Guid jobId)
    {
        if (!analytics.CancelJob(jobId)) return NotFound(new { error = "Feature load job was not found." });
        return Ok(analytics.GetJob(jobId));
    }
}
