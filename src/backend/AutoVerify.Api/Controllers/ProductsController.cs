using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController(AutoVerifyStore store) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProductRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? search,
        [FromQuery] string? judge,
        [FromQuery] string? panelId,
        [FromQuery] string? machineCode,
        [FromQuery] string? lineCode,
        [FromQuery] string? zone,
        [FromQuery] string? defectName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var products = await store.GetProductsAsync(search, judge, panelId, machineCode, lineCode, page, pageSize, zone, defectName);
        return Ok(products);
    }

    [HttpGet("filter-options")]
    [ProducesResponseType(typeof(ProductFilterOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilterOptions([FromQuery] string? machineCode, [FromQuery] string? lineCode)
    {
        return Ok(await store.GetProductFilterOptionsAsync(machineCode, lineCode));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductRecordDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRecordRequest request)
    {
        var product = await store.CreateProductRecordAsync(request);
        await store.RecordAuditAsync("admin", "ProductRecord.Create", "ProductRecord", product.KeyId, $"Created product record '{product.KeyId}' with Judge '{product.Judge}'.");
        return Created($"/api/v1/products?search={product.KeyId}", product);
    }

    [HttpPost("scan")]
    [ProducesResponseType(typeof(MultiTableScanResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> MultiTableScan([FromBody] MultiTableScanRequest request)
    {
        var response = await store.ScanProductsMultiTableAsync(request);
        await store.RecordAuditAsync("admin", "Product.MultiTableScan", "ProductRecords", $"{response.ScannedProductsCount} items", $"Scanned {response.ScannedProductsCount} products with {response.TotalDefectsFound} defects found across multiple defect tables.");
        return Ok(response);
    }

    [HttpGet("overkill-candidates")]
    [ProducesResponseType(typeof(IReadOnlyCollection<OverkillCandidateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverkillCandidates([FromQuery] string? machineCode, [FromQuery] string? lineCode)
    {
        var candidates = await store.GetOverkillCandidatesAsync(machineCode, lineCode);
        return Ok(candidates);
    }

    [HttpGet("{keyId}/defects")]
    [ProducesResponseType(typeof(IReadOnlyCollection<DefectRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProductDefects(string keyId)
    {
        var defects = await store.GetProductDefectsAsync(keyId);
        return Ok(defects);
    }
}
