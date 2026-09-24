using AutoVerify.Api.Persistence;
using AutoVerify.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/defects")]
public sealed class DefectsController(AutoVerifyStore store, AutoVerifyDbContext db, IStorageService storage) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(DefectRecordDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDefect([FromBody] CreateDefectRecordRequest request)
    {
        var defect = await store.CreateDefectRecordAsync(request, (SeaweedFsStorageManager)storage);
        await store.RecordAuditAsync("admin", "DefectRecord.Create", "DefectRecord", defect.KeyId, $"Created defect in '{defect.DefectTable}' for Key ID '{defect.KeyId}' (Feature JSON saved to SeaweedFS/Storage).");
        return Created($"/api/v1/products/{defect.KeyId}/defects", defect);
    }

    [HttpGet("{id:guid}/feature-content")]
    public async Task<IActionResult> GetDefectFeatureContent(Guid id, [FromQuery] string? type = "feature")
    {
        var defect = await db.DefectRecords.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (defect is null)
        {
            return NotFound(new { error = "Defect not found." });
        }
        var isCustom = string.Equals(type, "custom", StringComparison.OrdinalIgnoreCase);
        var inDbJson = isCustom ? defect.CustomFeatureJson : defect.FeatureJson;
        if (!string.IsNullOrWhiteSpace(inDbJson))
        {
            return Content(inDbJson, "application/json");
        }
        var url = isCustom ? (defect.CustomFeatureUrl ?? defect.Feature2StorageUri) : (defect.FeatureUrl ?? defect.Feature1StorageUri);
        if (string.IsNullOrWhiteSpace(url))
        {
            return NotFound(new { error = "No feature content or storage URL available for this defect." });
        }
        var content = await storage.ReadTextAsync(url);
        if (content is null)
        {
            return NotFound(new { error = $"Could not retrieve feature content from storage location: {url}" });
        }

        return Content(content, "application/json");
    }
}
