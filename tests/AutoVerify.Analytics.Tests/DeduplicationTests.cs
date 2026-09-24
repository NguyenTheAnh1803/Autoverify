using AutoVerify.Api;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class DeduplicationTests
{
    [Fact]
    public async Task GetAnomalyThumbnailsAsync_DuplicateProductsInDb_ReturnsDistinctThumbnailsPerKeyId()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        // Intentionally insert 2 duplicate product rows with the same KeyId
        var keyId = "PROD_DUPLICATE_001";
        db.ProductRecords.Add(new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            Judge = "NG",
            MachineCode = "AOI-01",
            LineCode = "LINE-01",
            InspectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        db.ProductRecords.Add(new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            Judge = "NG",
            MachineCode = "AOI-01",
            LineCode = "LINE-01",
            InspectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        db.DefectRecords.Add(new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            DefectName = "SolderBridge",
            DefectTable = "aoi_defect"
        });

        await db.SaveChangesAsync();

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var result = await store.GetAnomalyThumbnailsAsync(new AnomalyThumbnailQueryRequest(Category: "All"));

        // Must only return 1 item for this product, NOT 2 duplicates
        Assert.Single(result.Items);
        Assert.Equal(keyId, result.Items.First().KeyId);
    }

    [Fact]
    public async Task Deduplication_AlreadySavedProducts_AreFilteredOutCorrectly()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var existingKey = "SAVED_BOARD_100";
        db.ProductRecords.Add(new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = existingKey,
            PanelId = existingKey,
            Judge = "NG",
            MachineCode = "AOI-01",
            LineCode = "LINE-01",
            InspectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        // Simulate query returning both the existing board and a new board
        var candidateKeyIds = new List<string> { "SAVED_BOARD_100", "NEW_BOARD_200" };
        var candidateIds = new List<string> { "SAVED_BOARD_100", "NEW_BOARD_200" };
        var candidateFks = new List<string> { "SAVED_BOARD_100", "NEW_BOARD_200" };

        var alreadySavedEntities = await db.ProductRecords
            .AsNoTracking()
            .Where(p => candidateKeyIds.Contains(p.KeyId) || 
                        candidateIds.Contains(p.PanelId) || 
                        candidateFks.Contains(p.KeyId) ||
                        (p.PanelId != null && candidateFks.Contains(p.PanelId)))
            .Select(p => new { p.KeyId, p.PanelId })
            .ToListAsync();

        var alreadySavedKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in alreadySavedEntities)
        {
            if (!string.IsNullOrWhiteSpace(p.KeyId)) alreadySavedKeySet.Add(p.KeyId.Trim());
            if (!string.IsNullOrWhiteSpace(p.PanelId)) alreadySavedKeySet.Add(p.PanelId.Trim());
        }

        var scannedList = new[]
        {
            new { KeyId = "SAVED_BOARD_100", Id = "SAVED_BOARD_100", FkId = "SAVED_BOARD_100" },
            new { KeyId = "NEW_BOARD_200", Id = "NEW_BOARD_200", FkId = "NEW_BOARD_200" }
        };

        var newOnly = scannedList
            .Where(p => !alreadySavedKeySet.Contains(p.KeyId) && 
                        !alreadySavedKeySet.Contains(p.Id) && 
                        !alreadySavedKeySet.Contains(p.FkId))
            .ToList();

        Assert.Single(newOnly);
        Assert.Equal("NEW_BOARD_200", newOnly[0].KeyId);
    }
}
