using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/imports")]
public sealed class ImportsController(AutoVerifyStore store) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ImportPreviewResponse), StatusCodes.Status200OK)]
    public IActionResult PreviewImport([FromBody] ImportPreviewRequest request)
    {
        var preview = store.PreviewImport(request);
        return Ok(preview);
    }
}
