using AutoVerify.Api.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

public sealed record SubmitAiVerificationRequest(IReadOnlyCollection<string> KeyIds, bool Resubmit = false);

public sealed record UpsertStationMappingRequest(string MachineCode, string LineCode, string ModelCode, string StationId, string ProductId, bool Enabled = true);

[ApiController]
[Route("api/v1/ai-verification")]
public sealed class AiVerificationController(IAiVerificationService ai, AutoVerifyDbContext db) : ControllerBase
{
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitAiVerificationRequest request, CancellationToken cancellationToken)
    {
        if (request.KeyIds is null || request.KeyIds.Count == 0) return BadRequest(new { error = "KEY_IDS_REQUIRED" });
        if (request.KeyIds.Count > 500) return BadRequest(new { error = "TOO_MANY_KEYS", max = 500 });
        return Ok(await ai.SubmitProductsAsync(request.KeyIds, request.Resubmit, cancellationToken));
    }

    [HttpPost("poll")]
    public async Task<IActionResult> Poll(CancellationToken cancellationToken) =>
        Ok(new { completed = await ai.PollAsync(cancellationToken) });

    [HttpGet("products/{keyId}")]
    public async Task<IActionResult> GetProduct(string keyId, CancellationToken cancellationToken)
    {
        var result = await ai.GetProductResultAsync(keyId, cancellationToken);
        return result is null ? NotFound(new { error = "PRODUCT_NOT_FOUND" }) : Ok(result);
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings(CancellationToken cancellationToken) =>
        Ok(await db.MasterStationMappings.AsNoTracking().OrderBy(m => m.MachineCode).ThenBy(m => m.LineCode).ThenBy(m => m.ModelCode).ToListAsync(cancellationToken));

    [HttpPut("mappings")]
    public async Task<IActionResult> UpsertMapping([FromBody] UpsertStationMappingRequest request, CancellationToken cancellationToken)
    {
        if (new[] { request.MachineCode, request.LineCode, request.ModelCode, request.StationId, request.ProductId }.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { error = "ALL_FIELDS_REQUIRED" });

        var entity = await db.MasterStationMappings.FirstOrDefaultAsync(m =>
            m.MachineCode == request.MachineCode && m.LineCode == request.LineCode && m.ModelCode == request.ModelCode, cancellationToken);
        if (entity == null)
        {
            entity = new Persistence.Entities.MasterStationMappingEntity
            {
                Id = Guid.NewGuid(), MachineCode = request.MachineCode.Trim(), LineCode = request.LineCode.Trim(), ModelCode = request.ModelCode.Trim()
            };
            db.MasterStationMappings.Add(entity);
        }
        entity.StationId = request.StationId.Trim();
        entity.ProductId = request.ProductId.Trim();
        entity.Enabled = request.Enabled;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("mappings/{id:guid}")]
    public async Task<IActionResult> DeleteMapping(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await db.MasterStationMappings.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? NotFound() : NoContent();
    }
}
