using AutoVerify.Api.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

public sealed record SubmitAiVerificationRequest(IReadOnlyCollection<string> KeyIds, bool Resubmit = false);

public sealed record UpsertStationMappingRequest(string MachineCode, string LineCode, string ModelCode, string StationId, string ProductId, bool Enabled = true, bool AutoSubmit = false);

[ApiController]
[Route("api/v1/ai-verification")]
public sealed class AiVerificationController(IAiVerificationService ai, AutoVerifyDbContext db) : ControllerBase
{
    // Master limits: verify_items.StationId is VARCHAR(64), ProductId VARCHAR(128).
    private const int MaxStationIdLength = 64;
    private const int MaxProductIdLength = 128;

    private static bool IsMasterId(string? value, int maxLength)
    {
        var v = value?.Trim();
        return !string.IsNullOrEmpty(v) && v.Length <= maxLength && v.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
    }

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
        // The Master rejects a whole job when any item has an unsafe id, so refuse a mapping it would never accept.
        if (!IsMasterId(request.StationId, MaxStationIdLength))
            return BadRequest(new { error = "INVALID_STATION_ID", message = $"StationId must be 1-{MaxStationIdLength} characters: letters, digits, '-', '_' or '.'." });
        if (!IsMasterId(request.ProductId, MaxProductIdLength))
            return BadRequest(new { error = "INVALID_PRODUCT_ID", message = $"ProductId must be 1-{MaxProductIdLength} characters: letters, digits, '-', '_' or '.'." });

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
        entity.AutoSubmit = request.AutoSubmit;
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
