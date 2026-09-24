using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/query-profiles")]
public sealed class QueryProfilesController(AutoVerifyStore store) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<QueryProfileResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueryProfiles(
        [FromQuery] string? machineCode,
        [FromQuery] string? lineCode,
        [FromQuery] string? concept,
        [FromQuery] string? sourceSystem,
        [FromQuery] Guid? connectionId,
        [FromQuery] string? verificationCategory)
    {
        var profiles = await store.GetQueryProfilesAsync(machineCode, lineCode, concept, sourceSystem, connectionId, verificationCategory);
        return Ok(profiles);
    }

    [HttpPost]
    [ProducesResponseType(typeof(QueryProfileResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateQueryProfile([FromBody] CreateQueryProfileRequest request)
    {
        try
        {
            var profile = await store.CreateQueryProfileAsync(request);
            await store.RecordAuditAsync("admin", "QueryProfile.Create", "QueryProfile", profile.Id.ToString(), $"Created query profile '{profile.Name}'.");
            return Created($"/api/v1/query-profiles/{profile.Id}", profile);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{profileId:guid}")]
    [ProducesResponseType(typeof(QueryProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateQueryProfile(Guid profileId, [FromBody] CreateQueryProfileRequest request)
    {
        try
        {
            var profile = await store.UpdateQueryProfileAsync(profileId, request);
            if (profile is null) return NotFound();
            await store.RecordAuditAsync("admin", "QueryProfile.Update", "QueryProfile", profile.Id.ToString(), $"Updated query profile '{profile.Name}'.");
            return Ok(profile);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{profileId:guid}/render")]
    [ProducesResponseType(typeof(RenderedQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenderQueryProfile(Guid profileId, [FromBody] RenderQueryProfileRequest request)
    {
        var rendered = await store.RenderQueryProfileAsync(profileId, request.Parameters);
        return rendered is null ? NotFound() : Ok(rendered);
    }

    [HttpPost("{profileId:guid}/test")]
    [ProducesResponseType(typeof(QueryTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestQueryProfile(Guid profileId, [FromBody] RenderQueryProfileRequest request)
    {
        try
        {
            var result = await store.TestQueryProfileAsync(profileId, request.Parameters);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
