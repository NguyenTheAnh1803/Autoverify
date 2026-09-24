using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/database-connections")]
public sealed class DatabaseConnectionsController(AutoVerifyStore store) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<DatabaseConnectionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDatabaseConnections(
        [FromQuery] string? search,
        [FromQuery] string? engine,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var connections = await store.GetDatabaseConnectionsAsync(search, engine, page, pageSize);
        return Ok(connections);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DatabaseConnectionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDatabaseConnection([FromBody] CreateDatabaseConnectionRequest request)
    {
        try
        {
            var connection = await store.CreateDatabaseConnectionAsync(request);
            await store.RecordAuditAsync("admin", "DatabaseConnection.Create", "DatabaseConnection", connection.Id.ToString(), $"Created encrypted MariaDB connection '{connection.Name}'.");
            return Created($"/api/v1/database-connections/{connection.Id}", connection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{connectionId:guid}")]
    [ProducesResponseType(typeof(DatabaseConnectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDatabaseConnection(Guid connectionId, [FromBody] CreateDatabaseConnectionRequest request)
    {
        try
        {
            var connection = await store.UpdateDatabaseConnectionAsync(connectionId, request);
            if (connection is null) return NotFound();
            await store.RecordAuditAsync("admin", "DatabaseConnection.Update", "DatabaseConnection", connection.Id.ToString(), $"Updated connection '{connection.Name}'.");
            return Ok(connection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{connectionId:guid}/test")]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestDatabaseConnection(Guid connectionId)
    {
        var result = await store.TestDatabaseConnectionAsync(connectionId);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("batch-provision")]
    [ProducesResponseType(typeof(BatchProvisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchProvision(
        [FromBody] BatchProvisionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await store.BatchProvisionAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("scan-directory-paths")]
    [ProducesResponseType(typeof(ScanDirectoryPathsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ScanDirectoryPaths(
        [FromBody] ScanDirectoryPathsRequest request)
    {
        var response = await store.ScanDirectoryPathsAsync(request);
        return Ok(response);
    }

    [HttpPost("batch-delete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchDelete(
        [FromBody] BatchDeleteConnectionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var count = await store.BatchDeleteConnectionsAsync(request.ConnectionIds, request.DeleteLinkedPipelines, cancellationToken);
        return Ok(new { deletedCount = count, message = $"Successfully deleted {count} database connection(s)." });
    }
}
