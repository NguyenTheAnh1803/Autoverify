using AutoVerify.Api;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Verification;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class LineComparisonTests
{
    [Fact]
    public async Task GetOverkillTrendAsync_ComputesLineComparisonAcrossLinesWithOkAndNgProducts()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);
        var now = DateTimeOffset.UtcNow;

        // Line 1: 100 products running through (80 OK, 20 NG -> 20% NG rate)
        for (int i = 1; i <= 80; i++)
        {
            db.ProductRecords.Add(new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = $"L1-OK-{i}",
                Judge = "OK",
                PanelId = $"P-L1-{i}",
                PanelJudge = "OK",
                NameDefect = "None",
                MachineCode = "MACHINE-A",
                LineCode = "LINE-1",
                InspectedAtUtc = now.AddMinutes(-i)
            });
        }
        for (int i = 1; i <= 20; i++)
        {
            var keyId = $"L1-NG-{i}";
            db.ProductRecords.Add(new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = keyId,
                Judge = "NG",
                PanelId = $"P-L1-NG-{i}",
                PanelJudge = "NG",
                NameDefect = "Bridge",
                MachineCode = "MACHINE-A",
                LineCode = "LINE-1",
                InspectedAtUtc = now.AddMinutes(-i)
            });
        }

        // Line 2: 200 products running through (190 OK, 10 NG -> 5% NG rate)
        for (int i = 1; i <= 190; i++)
        {
            db.ProductRecords.Add(new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = $"L2-OK-{i}",
                Judge = "G",
                PanelId = $"P-L2-{i}",
                PanelJudge = "G",
                NameDefect = "None",
                MachineCode = "MACHINE-B",
                LineCode = "LINE-2",
                InspectedAtUtc = now.AddMinutes(-i)
            });
        }
        for (int i = 1; i <= 10; i++)
        {
            var keyId = $"L2-NG-{i}";
            db.ProductRecords.Add(new ProductRecordEntity
            {
                Id = Guid.NewGuid(),
                KeyId = keyId,
                Judge = "N",
                PanelId = $"P-L2-NG-{i}",
                PanelJudge = "N",
                NameDefect = "Crack",
                MachineCode = "MACHINE-B",
                LineCode = "LINE-2",
                InspectedAtUtc = now.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var result = await store.GetOverkillTrendAsync("ALL", "ALL", "ALL");

        Assert.NotNull(result);
        Assert.Equal(300, result.Summary.TotalInspected); // 100 on Line 1 + 200 on Line 2
        Assert.Equal(30, result.Summary.StationNgCount);   // 20 on Line 1 + 10 on Line 2

        Assert.NotNull(result.LineComparison);
        Assert.Equal(2, result.LineComparison.Count);

        // Sorted by StationNgRate descending -> LINE-1 (20%) should be first, then LINE-2 (5%)
        var line1 = result.LineComparison.First(l => l.LineCode == "LINE-1");
        Assert.Equal(100, line1.TotalInspected);
        Assert.Equal(80, line1.OkCount);
        Assert.Equal(20, line1.StationNgCount);
        Assert.Equal(0.20, line1.StationNgRate); // 20% NG rate

        var line2 = result.LineComparison.First(l => l.LineCode == "LINE-2");
        Assert.Equal(200, line2.TotalInspected);
        Assert.Equal(190, line2.OkCount);
        Assert.Equal(10, line2.StationNgCount);
        Assert.Equal(0.05, line2.StationNgRate); // 5% NG rate

        // Recommendations should highlight Line 1 as having highest NG rate
        Assert.Contains(result.Recommendations, r => r.Contains("LINE-1") && r.Contains("highest station NG rate"));
    }

    [Fact]
    public async Task OpReviewMark_OverridesClassifierCategoryInTrendAndThumbnails()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);
        db.ProductRecords.AddRange(
            new ProductRecordEntity
            {
                Id = Guid.NewGuid(), KeyId = "OP-OVERKILL", Judge = "NG", PanelId = "P1", PanelJudge = "NG",
                NameDefect = "Dent", MachineCode = "M1", LineCode = "L1", OpReviewMark = "Overkill", OpReviewedAtUtc = DateTimeOffset.UtcNow, InspectedAtUtc = DateTimeOffset.UtcNow
            },
            new ProductRecordEntity
            {
                Id = Guid.NewGuid(), KeyId = "OP-REAL", Judge = "NG", PanelId = "P2", PanelJudge = "NG",
                NameDefect = "Crack", MachineCode = "M1", LineCode = "L1", OpReviewMark = "Real Defect", InspectedAtUtc = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var store = new AutoVerifyStore(db, null!, new AnomalyVerificationService(), new QueryEngineService(), new ImportValidationService());
        var trend = await store.GetOverkillTrendAsync("M1", "L1", "ALL");

        Assert.Equal(1, trend.Summary.OverkillCount);
        Assert.Equal(1, trend.Summary.RealNgCount);
        Assert.Equal(1, Assert.Single(trend.TrendPoints).OverkillCount);
        Assert.Equal(1, Assert.Single(trend.TrendPoints).RealNgCount);

        var thumbnails = await store.GetAnomalyThumbnailsAsync(new AnomalyThumbnailQueryRequest(Category: "All", MachineCode: "M1", LineCode: "L1"));
        Assert.Contains(thumbnails.Items, item => item.KeyId == "OP-OVERKILL" && item.AnomalyCategory == "Overkill" && item.OpReviewMark == "Overkill");
        Assert.Contains(thumbnails.Items, item => item.KeyId == "OP-REAL" && item.AnomalyCategory == "RealNG" && item.OpReviewMark == "Real Defect");
        var line = Assert.Single(trend.LineComparison);
        Assert.Equal(2, line.OpMappedCount);
        Assert.Equal(1.0, line.OpMappedRate);
    }
}
