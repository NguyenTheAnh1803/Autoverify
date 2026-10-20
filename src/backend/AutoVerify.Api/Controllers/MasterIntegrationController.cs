using AutoVerify.Api.Integration;
using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/master")]
public sealed class MasterIntegrationController(IMasterServerClient master) : ControllerBase
{
    [HttpPost("test-connection")]
    [ProducesResponseType(typeof(MasterConnectionStatus), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestConnection(CancellationToken cancellationToken) =>
        Ok(await master.TestConnectionAsync(cancellationToken));
}
