using AutoVerify.Api.Persistence;
using AutoVerify.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoVerify.Api.Controllers;

[ApiController]
[Route("api/v1/image-crawler")]
public sealed class ImageCrawlerController(
    IImageCrawlerService crawlerService,
    AutoVerifyDbContext db) : ControllerBase
{
    [HttpPost("crawl")]
    [ProducesResponseType(typeof(CrawlDefectImageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CrawlDefectImage(
        [FromBody] CrawlDefectImageRequest request,
        CancellationToken cancellationToken)
    {
        var defect = await db.DefectRecords.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DefectId, cancellationToken);
        if (defect == null)
        {
            return NotFound(new { error = $"Defect with ID {request.DefectId} not found." });
        }

        var result = await crawlerService.CrawlAndCacheDefectImageAsync(
            request.DefectId,
            request.ForceRefresh,
            request.CustomApiUrl,
            request.CustomVp,
            cancellationToken);

        var resp = new CrawlDefectImageResponse(
            result.Success,
            request.DefectId,
            defect.ImageUri,
            result.StoredUrl ?? (string.Equals(request.CustomVp, "0", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(request.CustomVp)
                ? defect.ImageUrl
                : null),
            result.ContentType,
            result.ImageBytes?.LongLength ?? 0,
            result.ErrorMessage,
            result.LatencyMs
        );

        if (!result.Success)
        {
            return BadRequest(resp);
        }

        return Ok(resp);
    }

    [HttpPost("test")]
    [ProducesResponseType(typeof(TestImageCrawlerApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestApi(
        [FromBody] TestImageCrawlerApiRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApiUrl))
        {
            return BadRequest(new { error = "API URL is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return BadRequest(new { error = "Sample image path is required." });
        }

        var result = await crawlerService.TestApiAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("batch-crawl")]
    [ProducesResponseType(typeof(BatchImageCrawlResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchCrawl(
        [FromBody] BatchImageCrawlRequest request,
        CancellationToken cancellationToken)
    {
        var result = await crawlerService.BatchCrawlDefectImagesAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("test-defect")]
    [ProducesResponseType(typeof(TestDefectCrawlResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestDefectCrawl(
        [FromBody] TestDefectCrawlRequest request,
        CancellationToken cancellationToken)
    {
        var result = await crawlerService.TestDefectCrawlAsync(request.DefectId, request.CustomApiUrl, request.CustomVp, cancellationToken);
        return Ok(result);
    }

    [HttpPost("test-path")]
    [ProducesResponseType(typeof(TestPathCrawlResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestPathCrawl(
        [FromBody] TestPathCrawlRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return BadRequest(new { error = "ImagePath is required." });
        }

        var result = await crawlerService.TestPathCrawlAsync(request.ImagePath, request.ApiUrl, request.FeatureJson, request.Vp, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sample-defects")]
    [ProducesResponseType(typeof(IReadOnlyCollection<SampleDefectForTestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSampleDefects(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var maxLimit = Math.Clamp(limit, 1, 50);
        var samples = await db.DefectRecords
            .AsNoTracking()
            .Where(d => d.ImageUri != null && d.ImageUri != "" && (d.Judge == "NG" || d.Judge == "N" || d.Judge == "N/A" || d.Judge == "NA"))
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(maxLimit)
            .Select(d => new SampleDefectForTestDto(
                d.Id,
                d.KeyId,
                d.DefectName,
                d.DefectTable,
                d.ImageUri,
                d.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return Ok(samples);
    }

    [HttpGet("preview/{defectId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewDefectImage(
        Guid defectId,
        [FromQuery] bool refresh = false,
        [FromQuery] string? vp = "0",
        CancellationToken cancellationToken = default)
    {
        var targetVp = string.IsNullOrWhiteSpace(vp) ? "0" : vp.Trim();
        var result = await crawlerService.CrawlAndCacheDefectImageAsync(defectId, refresh, customApiUrl: null, customVp: targetVp, cancellationToken: cancellationToken);
        if (result.Success && result.ImageBytes != null && result.ImageBytes.Length > 0)
        {
            return File(result.ImageBytes, result.ContentType ?? "image/jpeg");
        }

        return NotFound(new { error = result.ErrorMessage ?? "Image could not be retrieved from PC API." });
    }
}
