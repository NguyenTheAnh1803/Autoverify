using AutoVerify.Api.Integration;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class AiVerificationServiceTests
{
    private sealed class FakeMaster : IMasterServerClient
    {
        public bool IsConfigured { get; set; } = true;
        public int MaxItemsPerJob { get; set; } = 50;
        public bool RejectSubmit { get; set; }
        public List<IReadOnlyList<MasterVerifyItem>> Jobs { get; } = new();
        public Func<MasterVerifyItem, MasterVerifyItemResult>? Answer { get; set; }

        public Task<string> SubmitJobAsync(string? sourceRef, IReadOnlyList<MasterVerifyItem> items, CancellationToken ct)
        {
            if (RejectSubmit) throw new HttpRequestException("403 ACCESS_DENIED");
            Jobs.Add(items);
            return Task.FromResult($"job{Jobs.Count}");
        }

        public Task<MasterJobSummary> GetJobAsync(string jobId, CancellationToken ct) =>
            Task.FromResult(new MasterJobSummary(jobId, "Completed", 0, 0, 0, 0));

        public Task<IReadOnlyList<MasterVerifyItemResult>> GetItemsAsync(string jobId, CancellationToken ct)
        {
            var items = Jobs[int.Parse(jobId[3..]) - 1];
            return Task.FromResult<IReadOnlyList<MasterVerifyItemResult>>(items.Select(i => Answer!(i)).ToList());
        }

        public Task<MasterConnectionStatus> TestConnectionAsync(CancellationToken ct) =>
            Task.FromResult(new MasterConnectionStatus(true, true, "ok"));
    }

    private sealed class FakeCrawler : IImageCrawlerService
    {
        public HashSet<Guid> Missing { get; } = new();

        public Task<ImageCrawlResult> CrawlAndCacheDefectImageAsync(Guid defectId, bool forceRefresh = false, string? customApiUrl = null, string? customVp = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Missing.Contains(defectId)
                ? new ImageCrawlResult(false, null, null, null, "missing", 404)
                : new ImageCrawlResult(true, new byte[] { 9, 9 }, "image/jpeg", null, null));

        public Task<ImageCrawlResult> CrawlImageAsync(string imagePath, string apiUrl, string method = "GET", string? customVp = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TestImageCrawlerApiResponse> TestApiAsync(TestImageCrawlerApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TestDefectCrawlResponse> TestDefectCrawlAsync(Guid defectId, string? customApiUrl = null, string? customVp = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TestPathCrawlResponse> TestPathCrawlAsync(string imagePath, string? customApiUrl = null, string? featureJson = null, string? customVp = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BatchImageCrawlResponse> BatchCrawlDefectImagesAsync(BatchImageCrawlRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(string? ApiUrl, string VpMode)> ResolveApiUrlAndVpModeForDefectAsync(DefectRecordEntity defect, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> ResolveApiUrlForDefectAsync(DefectRecordEntity defect, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    private static (AutoVerifyDbContext Db, AiVerificationService Service, FakeMaster Master, FakeCrawler Crawler) Create()
    {
        var db = new AutoVerifyDbContext(new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var master = new FakeMaster();
        var crawler = new FakeCrawler();
        var service = new AiVerificationService(db, master, crawler, new NoHttp(), Options.Create(new AiVerificationOptions()), NullLogger<AiVerificationService>.Instance);
        return (db, service, master, crawler);
    }

    private static DefectRecordEntity Defect(string key, string judge = "NG", string? image = "img.png") => new()
    {
        Id = Guid.NewGuid(), KeyId = key, Judge = judge, ImageUri = image,
        MachineCode = "M1", LineCode = "LINE-1", ModelCode = "D992"
    };

    private static void Seed(AutoVerifyDbContext db, string key, string productJudge, params DefectRecordEntity[] defects)
    {
        db.ProductRecords.Add(new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = key, Judge = productJudge, MachineCode = "M1", LineCode = "LINE-1", ModelCode = "D992" });
        db.DefectRecords.AddRange(defects);
        db.MasterStationMappings.Add(new MasterStationMappingEntity
        {
            Id = Guid.NewGuid(), MachineCode = "M1", LineCode = "LINE-1", ModelCode = "D992", StationId = "L01", ProductId = "P-A", Enabled = true
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task SubmitThenPoll_StoresVerdictsAndResolvesOverkillConfirmed()
    {
        var (db, service, master, _) = Create();
        var d1 = Defect("K1"); var d2 = Defect("K1");
        Seed(db, "K1", "NG", d1, d2);
        master.Answer = i => new MasterVerifyItemResult(i.ExternalItemId, "Done", "OK", 0.1, null, "[]", "{}", "Production", null);

        var submit = await service.SubmitProductsAsync(["K1"], false, default);
        Assert.Equal(2, submit.Submitted);
        var job = Assert.Single(master.Jobs);
        Assert.All(job, i => { Assert.Equal("L01", i.StationId); Assert.Equal("P-A", i.ProductId); Assert.Equal(new byte[] { 9, 9 }, i.ImageBuffer); });
        Assert.Equal("Pending", (await service.GetProductResultAsync("K1", default))!.AiStatus);

        Assert.Equal(2, await service.PollAsync(default));
        var result = (await service.GetProductResultAsync("K1", default))!;
        Assert.Equal("OverkillConfirmed", result.AiStatus);
        Assert.All(result.Defects, d => Assert.Equal("Done", d.Status));
    }

    [Fact]
    public async Task NgProduct_OnlySendsNgDefects_OkProductSendsAll()
    {
        var (db, service, master, _) = Create();
        Seed(db, "KNG", "NG", Defect("KNG", "NG"), Defect("KNG", "OK"));
        await service.SubmitProductsAsync(["KNG"], false, default);
        Assert.Single(master.Jobs[0]);

        var (db2, service2, master2, _) = Create();
        Seed(db2, "KOK", "OK", Defect("KOK", "NG"), Defect("KOK", "OK"));
        await service2.SubmitProductsAsync(["KOK"], false, default);
        Assert.Equal(2, master2.Jobs[0].Count);
    }

    [Fact]
    public async Task DefectWithoutMapping_IsNotSubmittedAndNotInvented()
    {
        var (db, service, master, _) = Create();
        db.ProductRecords.Add(new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = "K2", Judge = "NG" });
        db.DefectRecords.Add(Defect("K2"));
        await db.SaveChangesAsync();

        var submit = await service.SubmitProductsAsync(["K2"], false, default);

        Assert.Equal(1, submit.NoMapping);
        Assert.Empty(master.Jobs);
        Assert.Empty(db.AiVerdicts);
    }

    [Fact]
    public async Task MissingImage_IsRecordedAsUnavailable_AndNeverBecomesOk()
    {
        var (db, service, master, crawler) = Create();
        var d = Defect("K3");
        Seed(db, "K3", "NG", d);
        crawler.Missing.Add(d.Id);

        var submit = await service.SubmitProductsAsync(["K3"], false, default);

        Assert.Equal(1, submit.NoImage);
        Assert.Empty(master.Jobs);
        Assert.Equal("AiUnavailable", (await service.GetProductResultAsync("K3", default))!.AiStatus);
    }

    [Fact]
    public async Task MasterRejectingTheJob_MarksItemsUnavailableWithTheReason()
    {
        var (db, service, master, _) = Create();
        Seed(db, "K4", "NG", Defect("K4"));
        master.RejectSubmit = true;

        var submit = await service.SubmitProductsAsync(["K4"], false, default);

        Assert.Equal(0, submit.Submitted);
        Assert.Contains("ACCESS_DENIED", submit.Message);
        var row = Assert.Single(db.AiVerdicts);
        Assert.Equal(AiVerdictStatus.Unavailable, row.Status);
        Assert.Null(row.Verdict);
    }

    [Fact]
    public async Task SimulatorRejectedByMaster_StaysRejectedWithNoVerdict()
    {
        var (db, service, master, _) = Create();
        Seed(db, "K5", "NG", Defect("K5"));
        master.Answer = i => new MasterVerifyItemResult(i.ExternalItemId, "Rejected", null, null, null, null, null, null, "ClientMode is Simulator");

        await service.SubmitProductsAsync(["K5"], false, default);
        await service.PollAsync(default);

        var row = Assert.Single(db.AiVerdicts);
        Assert.Equal(AiVerdictStatus.Rejected, row.Status);
        Assert.Null(row.Verdict);
        Assert.Equal("AiUnavailable", (await service.GetProductResultAsync("K5", default))!.AiStatus);
    }

    [Fact]
    public async Task AlreadyJudgedDefects_AreSkippedUnlessResubmitted()
    {
        var (db, service, master, _) = Create();
        Seed(db, "K6", "NG", Defect("K6"));
        master.Answer = i => new MasterVerifyItemResult(i.ExternalItemId, "Done", "NG", 0.9, "short", "[]", "{}", "Production", null);
        await service.SubmitProductsAsync(["K6"], false, default);
        await service.PollAsync(default);

        var again = await service.SubmitProductsAsync(["K6"], false, default);
        Assert.Equal(1, again.AlreadyJudged);
        Assert.Single(master.Jobs);

        var forced = await service.SubmitProductsAsync(["K6"], true, default);
        Assert.Equal(1, forced.Submitted);
        Assert.Equal(2, master.Jobs.Count);
        Assert.Single(db.AiVerdicts);
    }

    [Fact]
    public async Task UnconfiguredMaster_SubmitsNothingAndSaysSo()
    {
        var (db, service, master, _) = Create();
        Seed(db, "K7", "NG", Defect("K7"));
        master.IsConfigured = false;

        var submit = await service.SubmitProductsAsync(["K7"], false, default);

        Assert.Equal(0, submit.Submitted);
        Assert.NotNull(submit.Message);
        Assert.Empty(db.AiVerdicts);
    }
}
