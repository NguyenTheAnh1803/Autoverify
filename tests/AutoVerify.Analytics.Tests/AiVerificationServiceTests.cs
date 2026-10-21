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
        public Exception? SubmitError { get; set; }
        public Action<int>? OnSubmit { get; set; }
        public List<IReadOnlyList<MasterVerifyItem>> Jobs { get; } = new();
        public Func<MasterVerifyItem, MasterVerifyItemResult>? Answer { get; set; }

        public Task<string> SubmitJobAsync(string? sourceRef, IReadOnlyList<MasterVerifyItem> items, CancellationToken ct)
        {
            if (SubmitError != null) throw SubmitError;
            if (RejectSubmit) throw new HttpRequestException("403 ACCESS_DENIED");
            OnSubmit?.Invoke(Jobs.Count + 1);
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

    private static (AutoVerifyDbContext Db, AiVerificationService Service, FakeMaster Master, FakeCrawler Crawler) Create(AiVerificationOptions? options = null)
    {
        var db = new AutoVerifyDbContext(new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var master = new FakeMaster();
        var crawler = new FakeCrawler();
        var service = new AiVerificationService(db, master, crawler, new NoHttp(), Options.Create(options ?? new AiVerificationOptions()), NullLogger<AiVerificationService>.Instance);
        return (db, service, master, crawler);
    }

    private static DefectRecordEntity Defect(string key, string judge = "NG", string? image = "img.png") => new()
    {
        Id = Guid.NewGuid(), KeyId = key, Judge = judge, ImageUri = image,
        MachineCode = "M1", LineCode = "LINE-1", ModelCode = "D992", CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static void AddProduct(AutoVerifyDbContext db, string key, string productJudge, params DefectRecordEntity[] defects)
    {
        db.ProductRecords.Add(new ProductRecordEntity { Id = Guid.NewGuid(), KeyId = key, Judge = productJudge, MachineCode = "M1", LineCode = "LINE-1", ModelCode = "D992" });
        db.DefectRecords.AddRange(defects);
        db.SaveChanges();
    }

    private static void AddMapping(AutoVerifyDbContext db, bool autoSubmit)
    {
        db.MasterStationMappings.Add(new MasterStationMappingEntity
        {
            Id = Guid.NewGuid(), MachineCode = "M1", LineCode = "LINE-1", ModelCode = "D992", StationId = "L01", ProductId = "P-A",
            Enabled = true, AutoSubmit = autoSubmit
        });
        db.SaveChanges();
    }

    private static AiVerificationOptions AutoOptions(int maxProducts = 20) => new() { AutoSubmit = true, AutoSubmitMaxProductsPerTick = maxProducts };

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
    public async Task Submit_SplitsJobsByItemCount()
    {
        var (db, service, master, _) = Create();
        master.MaxItemsPerJob = 2;
        Seed(db, "KC", "OK", Enumerable.Range(0, 5).Select(_ => Defect("KC", "OK")).ToArray());

        var submit = await service.SubmitProductsAsync(["KC"], false, default);

        Assert.Equal(5, submit.Submitted);
        Assert.Equal(new[] { 2, 2, 1 }, master.Jobs.Select(j => j.Count).ToArray());
        Assert.All(db.AiVerdicts, v => Assert.False(string.IsNullOrEmpty(v.MasterJobId)));
    }

    [Fact]
    public async Task Submit_SplitsJobsByImageBytes()
    {
        // Each fake image is 2 bytes; a 4-byte budget fits two per job.
        var (db, service, master, _) = Create(new AiVerificationOptions { MaxBytesPerJob = 4 });
        Seed(db, "KB", "OK", Enumerable.Range(0, 5).Select(_ => Defect("KB", "OK")).ToArray());

        await service.SubmitProductsAsync(["KB"], false, default);

        Assert.Equal(new[] { 2, 2, 1 }, master.Jobs.Select(j => j.Count).ToArray());
    }

    [Fact]
    public async Task Submit_KeepsJobIdsOfAcceptedJobs_WhenRequestIsCancelledMidway()
    {
        var (db, service, master, _) = Create();
        master.MaxItemsPerJob = 2;
        Seed(db, "KX", "OK", Enumerable.Range(0, 4).Select(_ => Defect("KX", "OK")).ToArray());
        using var cts = new CancellationTokenSource();
        master.OnSubmit = n => { if (n == 2) { cts.Cancel(); throw new TaskCanceledException(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SubmitProductsAsync(["KX"], false, cts.Token));

        var rows = await db.AiVerdicts.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => { Assert.Equal("job1", r.MasterJobId); Assert.Equal(AiVerdictStatus.Submitted, r.Status); });
    }

    [Fact]
    public async Task AutoSubmit_Disabled_SendsNothing()
    {
        var (db, service, master, _) = Create(new AiVerificationOptions { AutoSubmit = false });
        AddProduct(db, "A1", "NG", Defect("A1"));
        AddMapping(db, autoSubmit: true);

        var result = await service.AutoSubmitAsync(default);

        Assert.Equal(0, result.Picked);
        Assert.Empty(master.Jobs);
    }

    [Fact]
    public async Task AutoSubmit_MappingWithoutFlag_SendsNothing()
    {
        var (db, service, master, _) = Create(AutoOptions());
        AddProduct(db, "A2", "NG", Defect("A2"));
        AddMapping(db, autoSubmit: false);

        await service.AutoSubmitAsync(default);

        Assert.Empty(master.Jobs);
        Assert.Empty(db.AiVerdicts);
    }

    [Fact]
    public async Task AutoSubmit_PicksNgAndOkProductsWithImages()
    {
        var (db, service, master, _) = Create(AutoOptions());
        AddProduct(db, "NG1", "NG", Defect("NG1", "NG"), Defect("NG1", "OK"));
        AddProduct(db, "OK1", "OK", Defect("OK1", "OK"), Defect("OK1", "NG"));
        AddMapping(db, autoSubmit: true);

        var result = await service.AutoSubmitAsync(default);

        Assert.Equal(2, result.Picked);
        // NG product: only its NG defect (1). OK product: everything recorded against it (2).
        Assert.Equal(3, master.Jobs.SelectMany(j => j).Count());
        Assert.Equal(3, db.AiVerdicts.Count());
    }

    [Fact]
    public async Task AutoSubmit_SkipsOldDefectsNoImagesAndJudgedProducts()
    {
        var (db, service, master, _) = Create(AutoOptions());
        var old = Defect("OLD"); old.CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-3);
        AddProduct(db, "OLD", "NG", old);
        AddProduct(db, "NOIMG", "NG", Defect("NOIMG", image: null));
        AddProduct(db, "NGOKONLY", "NG", Defect("NGOKONLY", "OK")); // NG product is only asked about its NG defects
        var judged = Defect("JUDGED");
        AddProduct(db, "JUDGED", "NG", judged);
        db.AiVerdicts.Add(new AiVerdictEntity { Id = Guid.NewGuid(), DefectId = judged.Id, KeyId = "JUDGED", Status = AiVerdictStatus.Done, Verdict = "NG", SubmittedAtUtc = DateTimeOffset.UtcNow });
        db.SaveChanges();
        AddMapping(db, autoSubmit: true);

        var result = await service.AutoSubmitAsync(default);

        Assert.Equal(0, result.Picked);
        Assert.Empty(master.Jobs);
    }

    [Fact]
    public async Task AutoSubmit_RespectsMaxProductsPerTick_NewestFirst()
    {
        var (db, service, master, _) = Create(AutoOptions(maxProducts: 2));
        for (var i = 0; i < 5; i++)
        {
            var d = Defect($"P{i}");
            d.CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10 + i); // P4 is the newest
            AddProduct(db, $"P{i}", "NG", d);
        }
        AddMapping(db, autoSubmit: true);

        var first = await service.AutoSubmitAsync(default);
        Assert.Equal(2, first.Picked);
        Assert.Equal(new[] { "P4", "P3" }, db.AiVerdicts.AsEnumerable().Select(v => v.KeyId).OrderByDescending(k => k).ToArray());

        // The rest follow on later ticks and nothing is sent twice.
        await service.AutoSubmitAsync(default);
        await service.AutoSubmitAsync(default);
        var again = await service.AutoSubmitAsync(default);
        Assert.Equal(0, again.Picked);
        Assert.Equal(5, master.Jobs.SelectMany(j => j).Count());
    }

    [Fact]
    public async Task AutoSubmit_MasterDown_WritesNothingAndRetriesWhenBack()
    {
        var (db, service, master, _) = Create(AutoOptions());
        AddProduct(db, "T1", "NG", Defect("T1"), Defect("T1"));
        AddMapping(db, autoSubmit: true);
        master.SubmitError = new HttpRequestException("connection refused");

        var down = await service.AutoSubmitAsync(default);

        Assert.True(down.Transient);
        Assert.Equal(2, down.Deferred);
        Assert.Equal(0, down.Submitted);
        Assert.Empty(db.AiVerdicts);

        master.SubmitError = null;
        var back = await service.AutoSubmitAsync(default);
        Assert.False(back.Transient);
        Assert.Equal(2, back.Submitted);
        Assert.Equal(2, db.AiVerdicts.Count(v => v.Status == AiVerdictStatus.Submitted));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Forbidden, true)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, true)]
    [InlineData(System.Net.HttpStatusCode.BadGateway, true)]
    [InlineData(System.Net.HttpStatusCode.BadRequest, false)]
    [InlineData(System.Net.HttpStatusCode.RequestEntityTooLarge, false)]
    public void IsTransient_ClassifiesMasterErrors(System.Net.HttpStatusCode status, bool expected) =>
        Assert.Equal(expected, AiVerificationService.IsTransient(new HttpRequestException("x", null, status)));

    [Fact]
    public async Task AutoSubmit_ClientError_MarksUnavailableAndDoesNotRetry()
    {
        var (db, service, master, _) = Create(AutoOptions());
        AddProduct(db, "C1", "NG", Defect("C1"));
        AddMapping(db, autoSubmit: true);
        master.SubmitError = new HttpRequestException("400", null, System.Net.HttpStatusCode.BadRequest);

        await service.AutoSubmitAsync(default);
        var again = await service.AutoSubmitAsync(default);

        Assert.Equal(AiVerdictStatus.Unavailable, Assert.Single(db.AiVerdicts).Status);
        Assert.Equal(0, again.Picked);
    }

    [Fact]
    public async Task Poll_AcceptsAVerdictThatArrivesAfterGivingUp_ButNotAfterTheLateWindow()
    {
        var (db, service, master, _) = Create(new AiVerificationOptions { GiveUpAfterMinutes = 60, AcceptLateResultsHours = 24 });
        Seed(db, "L1", "NG", Defect("L1"));
        Seed(db, "L2", "NG", Defect("L2"));
        master.Answer = i => new MasterVerifyItemResult(i.ExternalItemId, "Processing", null, null, null, null, null, null, null);
        await service.SubmitProductsAsync(["L1"], false, default);
        await service.SubmitProductsAsync(["L2"], false, default);
        var rows = db.AiVerdicts.ToList();
        rows.Single(r => r.KeyId == "L1").SubmittedAtUtc = DateTimeOffset.UtcNow.AddHours(-2);   // inside the late window
        rows.Single(r => r.KeyId == "L2").SubmittedAtUtc = DateTimeOffset.UtcNow.AddHours(-30);  // outside it
        db.SaveChanges();

        await service.PollAsync(default); // gives up on both
        Assert.All(db.AiVerdicts, r => Assert.StartsWith("TIMEOUT:", r.Error));

        master.Answer = i => new MasterVerifyItemResult(i.ExternalItemId, "Done", "OK", 0.1, null, "[]", "{}", "Production", null);
        await service.PollAsync(default);

        var l1 = db.AiVerdicts.Single(r => r.KeyId == "L1");
        var l2 = db.AiVerdicts.Single(r => r.KeyId == "L2");
        Assert.Equal(AiVerdictStatus.Done, l1.Status);
        Assert.Equal("OK", l1.Verdict);
        Assert.Equal(AiVerdictStatus.Unavailable, l2.Status);
        Assert.DoesNotContain("TIMEOUT:", l2.Error);
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
        master.SubmitError = new HttpRequestException("400 INVALID_STATION_OR_PRODUCT", null, System.Net.HttpStatusCode.BadRequest);

        var submit = await service.SubmitProductsAsync(["K4"], false, default);

        Assert.Equal(0, submit.Submitted);
        Assert.False(submit.Transient);
        Assert.Contains("INVALID_STATION_OR_PRODUCT", submit.Message);
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
