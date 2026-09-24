using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class SystemController(AutoVerifyStore store, AutoVerifyDbContext db, SeaweedFsStorageManager storage) : ControllerBase
{
    [HttpGet("audit-events")]
    [ProducesResponseType(typeof(IReadOnlyCollection<AuditEventResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditEvents()
    {
        var events = await store.GetAuditEventsAsync();
        return Ok(events);
    }

    [HttpGet("system/health")]
    [ProducesResponseType(typeof(SystemHealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemHealth()
    {
        var canConnect = await db.Database.CanConnectAsync();
        var storageMode = storage.Mode;
        var response = new SystemHealthResponse(
            canConnect ? "Healthy" : "Degraded",
            DateTimeOffset.UtcNow,
            "AutoVerify.Api",
            "0.3.0-mariadb-storage",
            storageMode
        );
        return Ok(response);
    }
}
