using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/scan-lists")]
public sealed class ScanListsController(AutoVerifyStore store, SeaweedFsStorageManager storage) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<ScanListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScanLists()
    {
        var scanLists = await store.GetScanListsAsync();
        return Ok(scanLists);
    }

    [HttpGet("{scanListId:guid}/items")]
    [ProducesResponseType(typeof(IReadOnlyCollection<ScanListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScanListItems(Guid scanListId)
    {
        var items = await store.GetScanListItemsAsync(scanListId);
        return Ok(items);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ScanListResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateScanList([FromBody] CreateScanListRequest request)
    {
        var importPreview = store.PreviewImport(new ImportPreviewRequest(
            request.RawText,
            request.MappingName,
            request.MachineCode,
            request.LineCode,
            request.VerificationCategory
        ));

        var evidence = new
        {
            request.Name,
            request.MappingName,
            request.MachineCode,
            request.LineCode,
            request.VerificationCategory,
            importPreview.AcceptedCount,
            importPreview.RejectedCount,
            importPreview.DuplicateCount,
            importPreview.Checksum,
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

        var storedObject = await storage.UploadJsonAsync("scan-imports", $"{Guid.NewGuid():N}.json", evidence);
        await store.SaveStorageObjectAsync(storedObject, "ScanImportEvidence");
        var scanList = await store.CreateScanListAsync(request, storedObject);
        await store.RecordAuditAsync("admin", "ScanList.Create", "ScanList", scanList.Id.ToString(), $"Created {request.VerificationCategory} scan list '{scanList.Name}'.");
        return Created($"/api/v1/scan-lists/{scanList.Id}", scanList);
    }
}
