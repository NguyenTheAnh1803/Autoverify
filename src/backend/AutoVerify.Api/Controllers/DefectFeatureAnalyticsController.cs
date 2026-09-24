using AutoVerify.Api.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/storage/defect-preview/sessions/{sessionId:guid}/analytics")]
public sealed class DefectFeatureAnalyticsController(IDefectFeatureAnalyticsService analyticsService) : ControllerBase
{
    [HttpGet("schema-readiness")]
    [ProducesResponseType(typeof(FeatureSchemaReadinessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSchemaReadiness(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await analyticsService.GetSchemaAndReadinessAsync(sessionId, cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }

        return Ok(result);
    }

    [HttpGet("facets")]
    [ProducesResponseType(typeof(SessionScopeFacetsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFacets(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        var facets = await analyticsService.GetScopeFacetsAsync(sessionId, cancellationToken);
        if (facets == null)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }

        return Ok(facets);
    }

    [HttpPost("jobs")]
    [ProducesResponseType(typeof(AnalyticsJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateJob(
        [FromRoute] Guid sessionId,
        [FromBody] CreateAnalyticsJobRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AnalysisType))
        {
            return BadRequest(new { message = "analysisType is required." });
        }

        try
        {
            var username = User.Identity?.Name ?? "system";
            var job = await analyticsService.CreateJobAsync(sessionId, request, username, cancellationToken);
            return Accepted($"api/v1/storage/defect-preview/sessions/{sessionId}/analytics/jobs/{job.Id}", job);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }

    [HttpGet("jobs")]
    [ProducesResponseType(typeof(List<AnalyticsJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListJobs(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        var jobs = await analyticsService.ListJobsAsync(sessionId, cancellationToken);
        return Ok(jobs);
    }

    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType(typeof(AnalyticsJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJob(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await analyticsService.GetJobResultAsync(sessionId, jobId, cancellationToken);
        if (job == null)
        {
            return NotFound(new { message = $"Job '{jobId}' not found for session '{sessionId}'." });
        }

        return Ok(job);
    }

    [HttpGet("jobs/{jobId:guid}/export-csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportJobCsv(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await analyticsService.GetJobResultAsync(sessionId, jobId, cancellationToken);
        if (job == null)
        {
            return NotFound(new { message = $"Job '{jobId}' not found for session '{sessionId}'." });
        }

        if (job.Status != "Completed" || job.Result == null)
        {
            return BadRequest(new { message = "Job is not completed or has no result." });
        }

        if (!string.Equals(job.AnalysisType, "SCRIPT_CONDITION", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = $"Export CSV is currently implemented for SCRIPT_CONDITION analysis (current: {job.AnalysisType})." });
        }

        try
        {
            var jsonText = job.Result is System.Text.Json.JsonElement je ? je.GetRawText() : job.Result.ToString();
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return BadRequest(new { message = "Result payload is empty." });
            }

            var result = System.Text.Json.JsonSerializer.Deserialize<ScriptConditionEngine.ScriptConditionResult>(
                jsonText,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null)
            {
                return BadRequest(new { message = "Failed to deserialize ScriptConditionResult." });
            }

            var sb = new System.Text.StringBuilder();
            var condNames = result.ConditionStats.Select(c => c.ConditionName).ToList();
            var featNames = result.Script.AllRequiredFeatures.ToList();

            // CSV Header
            sb.Append("RowIndex,ObservationId,LineCode,MachineCode,StationCode,FinalResult,GuardFailed,FailedGuardFeature");
            foreach (var cn in condNames)
            {
                sb.Append(',').Append(EscapeCsv(cn));
            }
            foreach (var fn in featNames)
            {
                sb.Append(',').Append(EscapeCsv(fn));
            }
            sb.AppendLine();

            // CSV Rows
            foreach (var ev in result.Evaluations)
            {
                sb.Append(ev.Observation.RowIndex).Append(',');
                sb.Append(EscapeCsv(ev.Observation.ObservationId.ToString())).Append(',');
                sb.Append(EscapeCsv(ev.Observation.LineCode ?? "")).Append(',');
                sb.Append(EscapeCsv(ev.Observation.MachineCode ?? "")).Append(',');
                sb.Append(EscapeCsv(ev.Observation.StationCode ?? "")).Append(',');
                sb.Append(ev.FinalResult ? "NG" : "OK").Append(',');
                sb.Append(ev.GuardFailed ? "YES" : "NO").Append(',');
                sb.Append(EscapeCsv(ev.FailedGuardFeature ?? ""));

                foreach (var cn in condNames)
                {
                    var condVal = ev.ConditionResults.TryGetValue(cn, out var cv) && cv;
                    sb.Append(',').Append(condVal ? "TRUE" : "FALSE");
                }

                foreach (var fn in featNames)
                {
                    var featVal = ev.FeatureValues.TryGetValue(fn, out var fv) ? fv : null;
                    sb.Append(',').Append(featVal.HasValue ? featVal.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
                }

                sb.AppendLine();
            }

            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            var filename = $"script_analysis_{sessionId:N}_{jobId:N}.csv";
            return File(bytes, "text/csv; charset=utf-8", filename);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"CSV generation failed: {ex.Message}" });
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    [HttpPost("jobs/{jobId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelJob(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken)
    {
        var success = await analyticsService.CancelJobAsync(sessionId, jobId, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = $"Job '{jobId}' not found or cannot be cancelled." });
        }

        return Ok(new { success = true, message = $"Job '{jobId}' cancelled." });
    }

    [HttpDelete("jobs/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteJob(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken)
    {
        var deleted = await analyticsService.DeleteJobAsync(sessionId, jobId, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { message = $"Job '{jobId}' not found." });
        }

        return Ok(new { success = true, message = $"Job '{jobId}' deleted." });
    }
}
