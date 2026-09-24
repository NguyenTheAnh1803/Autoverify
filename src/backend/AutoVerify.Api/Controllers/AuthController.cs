using System.Security.Cryptography;
using AutoVerify.Api.Logging;
using AutoVerify.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    AutoVerifyStore store,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!await store.ValidateCredentialsAsync(request.Username, request.Password))
        {
            logger.LogSpecial(
                LogLevel.Warning,
                "Authentication failed for user '{Username}' from IP {ClientIp}",
                request.Username,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return Unauthorized();
        }

        await store.RecordAuditAsync("admin", "Auth.Login", "User", "admin", "Temporary administrator login succeeded.");
        logger.LogUserEvent("admin", "Auth.Login", "User '{Username}' authenticated successfully", request.Username);

        var response = new LoginResponse(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            new UserProfile("admin", "Administrator", ["Administrator"])
        );
        return Ok(response);
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfile), StatusCodes.Status200OK)]
    public IActionResult GetCurrentUser()
    {
        return Ok(new UserProfile("admin", "Administrator", ["Administrator"]));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await store.RecordAuditAsync("admin", "Auth.Logout", "User", "admin", "User session ended.");
        logger.LogUserEvent("admin", "Auth.Logout", "User session ended successfully");
        return NoContent();
    }
}
