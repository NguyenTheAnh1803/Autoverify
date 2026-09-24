using AutoVerify.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/storage/defect-preview/sessions")]
public sealed class DefectPreviewController(IDefectPreviewService previewService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(DefectPreviewSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSession(
        [FromBody] CreateDefectPreviewSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EffectiveUri))
        {
            return BadRequest(new { message = "storageUri is required." });
        }

        if (!string.IsNullOrWhiteSpace(request.Format) && !DefectDataFormats.IsValidFormat(request.Format))
        {
            return BadRequest(new { message = $"Invalid defect data format '{request.Format}'. Supported formats: {string.Join(", ", DefectDataFormats.AllCanonical)}." });
        }

        try
        {
            var session = await previewService.CreateSessionAsync(request, cancellationToken);
            return Ok(session);
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

    [HttpGet("{sessionId:guid}/progress")]
    [ProducesResponseType(typeof(DefectPreviewProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgress(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        var progress = await previewService.GetProgressAsync(sessionId, cancellationToken);
        if (progress == null)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }

        return Ok(progress);
    }

    [HttpGet("{sessionId:guid}/rows")]
    [ProducesResponseType(typeof(DefectPreviewPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QueryRows(
        [FromRoute] Guid sessionId,
        [FromQuery] DefectPreviewQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        var result = await previewService.QueryRecordsAsync(sessionId, queryParams, cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }

        return Ok(result);
    }

    [HttpDelete("{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSession(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
    {
        var deleted = await previewService.DeleteSessionAsync(sessionId, cancellationToken);
        if (!deleted)
        {
            return NotFound(new { success = false, message = $"Session '{sessionId}' not found." });
        }

        return Ok(new { success = true, message = $"Session '{sessionId}' deleted successfully." });
    }
}
