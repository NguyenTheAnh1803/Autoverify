using Microsoft.AspNetCore.Mvc;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/product-queries")]
public sealed class ProductQueriesController(AutoVerifyStore store) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<ProductQueryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProductQueries([FromQuery] string? status)
    {
        var queries = await store.GetProductQueriesAsync(status);
        return Ok(queries);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductQueryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateProductQuery([FromBody] CreateProductQueryRequest request)
    {
        var query = await store.CreateProductQueryAsync(request);
        await store.RecordAuditAsync("admin", "ProductQuery.Create", "ProductQuery", query.Id.ToString(), $"Created product query '{query.QueryKey}'.");
        return Created($"/api/v1/product-queries/{query.Id}", query);
    }

    [HttpPost("{queryId:guid}/status")]
    [ProducesResponseType(typeof(ProductQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid queryId, [FromBody] UpdateProductQueryStatusRequest request)
    {
        var query = await store.UpdateProductQueryStatusAsync(queryId, request.Status, request.JudgeResult, request.Message);
        return query is null ? NotFound() : Ok(query);
    }
}
