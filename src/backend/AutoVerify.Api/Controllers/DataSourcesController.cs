using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/data-sources")]
public sealed class DataSourcesController(AutoVerifyStore store) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<DataSourceResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDataSources(
        [FromQuery] string? machineCode,
        [FromQuery] string? verificationCategory,
        [FromQuery] string? schedulePeriod)
    {
        var sources = await store.GetDataSourcesAsync(machineCode, verificationCategory, schedulePeriod);
        return Ok(sources);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DataSourceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDataSource([FromBody] CreateDataSourceRequest request)
    {
        try
        {
            var source = await store.CreateDataSourceAsync(request);
            await store.RecordAuditAsync("admin", "DataSource.Create", "DataSource", source.Id.ToString(), $"Created protected source '{source.Name}'.");
            return Created($"/api/v1/data-sources/{source.Id}", source);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{sourceId:guid}/test")]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestDataSource(Guid sourceId)
    {
        var result = await store.TestDataSourceAsync(sourceId);
        return result is null ? NotFound() : Ok(result);
    }
}
