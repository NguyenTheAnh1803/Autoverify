using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(AutoVerifyStore store) : ControllerBase
{
    [HttpGet("production-report")]
    [ProducesResponseType(typeof(DashboardProductionReportResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProductionReport([FromQuery] int days = 14)
    {
        var report = await store.GetProductionDashboardReportAsync(days);
        return Ok(report);
    }
}
