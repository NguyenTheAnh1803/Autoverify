using AutoVerify.Api;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Verification;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class AnomalyThumbnailPaginationTests
{
    [Fact]
    public async Task GetAnomalyThumbnailsAsync_StationNg_PaginatesWithoutDroppingOlderProducts()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);
        var now = DateTimeOffset.UtcNow;

        for (var i = 1; i <= 40; i++)
        {
            db.ProductRecords.Add(new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = $"NG-{i:D3}",
                Judge = "NG",
                MachineCode = "AOI-01",
                LineCode = "LINE-01",
                InspectedAtUtc = now.AddMinutes(-i),
            });
            db.ProductRecords.Add(new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = $"OK-{i:D3}",
                Judge = "OK",
                MachineCode = "AOI-01",
                LineCode = "LINE-01",
                InspectedAtUtc = now.AddMinutes(-i),
            });
        }

        await db.SaveChangesAsync();

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var page1 = await store.GetAnomalyThumbnailsAsync(new AnomalyThumbnailQueryRequest(
            Category: "StationNG",
            Page: 1,
            PageSize: 18));
        var page2 = await store.GetAnomalyThumbnailsAsync(new AnomalyThumbnailQueryRequest(
            Category: "StationNG",
            Page: 2,
            PageSize: 18));
        var page3 = await store.GetAnomalyThumbnailsAsync(new AnomalyThumbnailQueryRequest(
            Category: "StationNG",
            Page: 3,
            PageSize: 18));

        Assert.Equal(40, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(18, page1.Items.Count);
        Assert.Equal(18, page2.Items.Count);
        Assert.Equal(4, page3.Items.Count);
        Assert.Equal(40, page1.Items.Count + page2.Items.Count + page3.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.KeyId).Intersect(page2.Items.Select(i => i.KeyId)));
        Assert.All(page1.Items.Concat(page2.Items).Concat(page3.Items), item =>
            Assert.Equal("NG", item.StationJudge, ignoreCase: true));
    }
}
