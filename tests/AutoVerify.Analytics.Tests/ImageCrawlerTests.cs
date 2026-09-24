using System.Net;
using System.Text;
using System.Text.Json;
using AutoVerify.Api;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class ImageCrawlerTests
{
    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }

    private static readonly byte[] FakeJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    [Fact]
    public async Task CrawlImageAsync_BinaryImageResponse_ReturnsSuccessAndBytes()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("path=D%3A%5CAOIScan%5Ctest.jpg", req.RequestUri!.Query);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeJpegBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var result = await crawler.CrawlImageAsync(@"D:\AOIScan\test.jpg", "http://192.168.1.50:5000/api/image");

        Assert.True(result.Success);
        Assert.NotNull(result.ImageBytes);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(FakeJpegBytes, result.ImageBytes);
    }

    [Fact]
    public async Task CrawlImageAsync_JsonBase64Response_ExtractsAndDecodesImageBytes()
    {
        var base64 = Convert.ToBase64String(FakeJpegBytes);
        var jsonResponse = JsonSerializer.Serialize(new
        {
            success = true,
            data = base64,
            contentType = "image/jpeg"
        });

        var handler = new MockHttpMessageHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var result = await crawler.CrawlImageAsync(@"C:\Data\img.jpg", "http://10.20.1.100:8080/image");

        Assert.True(result.Success);
        Assert.NotNull(result.ImageBytes);
        Assert.Equal(FakeJpegBytes, result.ImageBytes);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task CrawlImageAsync_UrlTemplateWithPlaceholders_SubstitutesCorrectly()
    {
        string? requestedUrl = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeJpegBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var result = await crawler.CrawlImageAsync(@"D:\AOI\board.png", "http://192.168.1.15:8080/crawl?imagePath={path}&format=raw");

        Assert.True(result.Success);
        Assert.NotNull(requestedUrl);
        Assert.Contains("imagePath=D%3A%5CAOI%5Cboard.png", requestedUrl);
        Assert.Contains("format=raw", requestedUrl);
    }

    [Fact]
    public async Task ResolveApiUrlForDefectAsync_PipelineConfigured_ResolvesPipelineUrl()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var conn = new DatabaseConnectionEntity
        {
            Id = Guid.NewGuid(),
            LineCode = "LINE-01",
            MachineCode = "AOI-01",
            Name = "L1-AOI",
            Host = "10.0.0.51",
            Port = 3306,
            ImageCrawlerApiUrl = "http://10.0.0.51:5000/api/image"
        };
        db.DatabaseConnections.Add(conn);

        var pipeline = new OverkillPipelineConfigEntity
        {
            Id = Guid.NewGuid(),
            Name = "Pipeline_LINE-01_AOI-01",
            DatabaseConnectionId = conn.Id,
            LineCode = "LINE-01",
            MachineCode = "AOI-01",
            ImageCrawlerApiUrl = "http://{host}:8080/api/crawl"
        };
        db.OverkillPipelineConfigs.Add(pipeline);

        var prod = new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "PROD_12345",
            LineCode = "LINE-01",
            MachineCode = "AOI-01"
        };
        db.ProductRecords.Add(prod);

        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "PROD_12345",
            DefectName = "SolderBridge",
            ImageUri = @"D:\Images\def_01.jpg"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();

        var crawler = new ImageCrawlerService(new HttpClient(), db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var resolvedUrl = await crawler.ResolveApiUrlForDefectAsync(defect);

        Assert.NotNull(resolvedUrl);
        // {host} should be substituted by conn.Host
        Assert.Equal("http://10.0.0.51:8080/api/crawl", resolvedUrl);
    }

    [Fact]
    public async Task CrawlAndCacheDefectImageAsync_SuccessfulCrawl_UpdatesDefectRecord()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeJpegBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var conn = new DatabaseConnectionEntity
        {
            Id = Guid.NewGuid(),
            LineCode = "LINE-02",
            MachineCode = "AOI-02",
            Name = "L2-AOI",
            Host = "10.0.0.52",
            Port = 3306,
            ImageCrawlerApiUrl = "http://10.0.0.52:5000/api/image"
        };
        db.DatabaseConnections.Add(conn);

        var prod = new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "PROD_9999",
            LineCode = "LINE-02",
            MachineCode = "AOI-02"
        };
        db.ProductRecords.Add(prod);

        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "PROD_9999",
            DefectName = "MissingComponent",
            ImageUri = @"D:\AOI_Data\Images\9999_def1.jpg"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var result = await crawler.CrawlAndCacheDefectImageAsync(defect.Id);

        Assert.True(result.Success);
        Assert.NotNull(result.ImageBytes);

        var updatedDefect = await db.DefectRecords.FindAsync(defect.Id);
        Assert.NotNull(updatedDefect);
        Assert.NotNull(updatedDefect.ImageUrl);
    }

    [Fact]
    public async Task TestApiAsync_ValidInputs_ReturnsProperMetrics()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeJpegBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var testReq = new TestImageCrawlerApiRequest("http://192.168.1.200:5000/api/image", @"D:\sample.jpg");
        var resp = await crawler.TestApiAsync(testReq);

        Assert.True(resp.Success);
        Assert.Equal(200, resp.StatusCode);
        Assert.Equal("image/jpeg", resp.ContentType);
        Assert.Equal(FakeJpegBytes.Length, resp.SizeBytes);
        Assert.NotNull(resp.Base64Preview);
        Assert.StartsWith("data:image/jpeg;base64,", resp.Base64Preview);
    }

    [Fact]
    public async Task CrawlAndCacheDefectImageAsync_AnywherePixPath_PostsExpectedZipPayload()
    {
        string? requestJson = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("http://10.121.45.43:336/api/image/get", req.RequestUri!.ToString());
            requestJson = await req.Content!.ReadAsStringAsync();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeJpegBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return response;
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        db.ProductRecords.Add(new ProductRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "P1EF650120BA27311SDA",
            LineCode = "301",
            MachineCode = "CP-AOI"
        });

        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "P1EF650120BA27311SDA",
            DefectName = "AOI",
            ImageUri = "2026/07/25/CP-AOI #301-A/P1EF650120BA27311SDA/VP02/P1EF650120BA27311SDA_164426_A_2/VP02_P1EF650120BA27311SDA_164426_A_2_2183_.jpg",
            FeatureJson = "{\"Header\":{\"G010_VpID\":2},\"Value\":{\"G010_VpID\":2}}"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var result = await crawler.CrawlAndCacheDefectImageAsync(
            defect.Id,
            forceRefresh: true,
            customApiUrl: "http://10.121.45.43:336/api/image/get",
            customVp: "zip");

        Assert.True(result.Success);
        Assert.NotNull(requestJson);

        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;
        Assert.Equal("301", root.GetProperty("AgentName").GetString());
        Assert.Equal("301", root.GetProperty("Line").GetString());
        Assert.Equal("A", root.GetProperty("Zone").GetString());
        Assert.Equal("2", root.GetProperty("VP").GetString());
        Assert.Contains(@"\192.168.200.121\", root.GetProperty("FilePath").GetString());
        Assert.Contains(@"P1EF650120BA27311SDA_164426_A_2.zip", root.GetProperty("FilePath").GetString());
        Assert.Equal(
            "VP02_P1EF650120BA27311SDA_164426_A_2_2183_.jpg",
            root.GetProperty("ZipEntry").GetString());
    }

    [Fact]
    public void BuildAnywherePixRequest_Vp0_UsesRelativeZipPathAndJpgEntry()
    {
        var rawPath = "2026/09/21/CP-AOI #404-B/E1D6660D06BE32416LQV/VP01/E1D6660D06BE32416LQV_023319_B_1/VP01_source.jpg";

        var payload = ImageCrawlerService.BuildAnywherePixRequest(rawPath, null, "0");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = document.RootElement;

        Assert.Equal("0", root.GetProperty("VP").GetString());
        Assert.Equal(
            @"2026\09\21\CP-AOI #404-B\E1D6660D06BE32416LQV\VP01\E1D6660D06BE32416LQV_023319_B_1\E1D6660D06BE32416LQV_023319_B_1.zip",
            root.GetProperty("FilePath").GetString());
        Assert.Equal("VP01_source.jpg", root.GetProperty("ZipEntry").GetString());
    }

    [Fact]
    public void CombineImagePath_JoinsFolderAndFileNameLikeDesktopClient()
    {
        var folder = @"2026\09\21\CP-AOI #305-B\A1C468CP10B137321127\VP01\A1C468CP10B137321127_044005_B_2";
        var combined = ImageCrawlerService.CombineImagePath(folder, "VP01_A1C468CP10B137321127_044005_B_2_1_.jpg");

        Assert.Equal(
            @"2026\09\21\CP-AOI #305-B\A1C468CP10B137321127\VP01\A1C468CP10B137321127_044005_B_2\VP01_A1C468CP10B137321127_044005_B_2_1_.jpg",
            combined);
    }

    [Fact]
    public void BuildAnywherePixRequest_FolderOnlyPath_KeepsFolderZipAndEmptyZipEntry()
    {
        var folder = @"2026\09\21\CP-AOI #305-B\A1C468CP10B137321127\VP01\A1C468CP10B137321127_044005_B_2";
        var payload = ImageCrawlerService.BuildAnywherePixRequest(folder, null, "0");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = document.RootElement;

        Assert.Equal("0", root.GetProperty("VP").GetString());
        Assert.Equal(
            @"2026\09\21\CP-AOI #305-B\A1C468CP10B137321127\VP01\A1C468CP10B137321127_044005_B_2\A1C468CP10B137321127_044005_B_2.zip",
            root.GetProperty("FilePath").GetString());
        Assert.Equal(string.Empty, root.GetProperty("ZipEntry").GetString());
    }

    [Fact]
    public void TryBuildContext_ValidRawPath_ParsesAllFieldsCorrectly()
    {
        var rawPath = "2026/07/25/CP-AOI #301-A/P1EF650120BA27311SDA/VP02/P1EF650120BA27311SDA_164426_A_2/VP02_P1EF650120BA27311SDA_164426_A_2_2183_.jpg";
        var success = ImageCrawlerService.TryBuildContext(rawPath, null, null, out var context, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(context);
        Assert.Equal("301", context.Line);
        Assert.Equal("A", context.Zone);
        Assert.Equal("2", context.Vp);
        Assert.Equal("P1EF650120BA27311SDA", context.PanelId);
        Assert.Equal(new DateTime(2026, 7, 25), context.Date);
        Assert.Equal("VP02_P1EF650120BA27311SDA_164426_A_2_2183_.jpg", context.TargetFileName);
    }

    [Fact]
    public void CpAoiImageService_TryParseCpAoiZipPath_ParsesAccurately()
    {
        var rawPath = "2026/08/05/CP-AOI #301-B/P1E1650119BA27311SD2/VP01/P1E1650119BA27311SD2_094722_B_1/VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg";
        var isCp = ImageCrawlerService.IsCpAoiImagePath(rawPath);
        var parsed = ImageCrawlerService.TryParseCpAoiZipPath(rawPath, out var zipPath, out var zipEntry, out var lineName);

        Assert.True(isCp);
        Assert.True(parsed);
        Assert.Equal("301_B", lineName);
        Assert.Equal("VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg", zipEntry);
        Assert.NotNull(zipPath);
        Assert.EndsWith(@"P1E1650119BA27311SD2_094722_B_1\P1E1650119BA27311SD2_094722_B_1.zip", zipPath);

        var candidates = ImageCrawlerService.BuildCandidateZipPaths(zipPath, rawPath, lineName);
        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, p => p.Contains(@"\d\defectimage\"));
        Assert.Contains(candidates, p => p.Contains(@"\defectimage\"));
    }

    [Fact]
    public async Task CrawlImageAsync_CpAoiZipWorkflow_DirectlyCallsImageGetWithZipEntry()
    {
        string? requestJson = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/image/get"))
            {
                requestJson = await req.Content!.ReadAsStringAsync();
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(FakeJpegBytes)
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var rawPath = "2026/08/05/CP-AOI #301-B/P1E1650119BA27311SD2/VP01/P1E1650119BA27311SD2_094722_B_1/VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg";
        var result = await crawler.CrawlImageAsync(rawPath, "http://10.121.45.43:336/api/image/get", customVp: "zip");

        Assert.True(result.Success);
        Assert.NotNull(requestJson);
        using var doc = JsonDocument.Parse(requestJson);
        var root = doc.RootElement;
        Assert.Equal("301", root.GetProperty("Line").GetString());
        Assert.Equal("1", root.GetProperty("VP").GetString());
        Assert.Equal("VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg", root.GetProperty("ZipEntry").GetString());
        Assert.Contains(".zip", root.GetProperty("FilePath").GetString());
    }

    [Fact]
    public async Task CrawlImageAsync_ProxySearchPanelsWorkflow_ReturnsDownloadedImage()
    {
        var searchPanelsCalled = false;
        var panelImagesCalled = false;
        var imageGetCalled = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/image/search-panels"))
            {
                searchPanelsCalled = true;
                var json = JsonSerializer.Serialize(new
                {
                    success = true,
                    folders = new[]
                    {
                        new { folderName = "test_folder", folderPath = @"\\192.168.200.132\d\image\test", imageCount = 1 }
                    }
                });
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            }

            if (url.Contains("/api/image/panel-images"))
            {
                panelImagesCalled = true;
                var json = JsonSerializer.Serialize(new
                {
                    Success = true,
                    Data = new
                    {
                        Images = new[]
                        {
                            new { FileName = "VP02_test.jpg", FilePath = @"\\192.168.200.132\d\image\test\VP02_test.jpg" }
                        }
                    }
                });
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(resp);
            }

            if (url.Contains("/api/image/get"))
            {
                imageGetCalled = true;
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(FakeJpegBytes)
                };
                resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(resp);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);

        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var rawPath = "2026/07/25/SPI_STATION #301-A/P1EF650120BA27311SDA/VP02/P1EF650120BA27311SDA_164426_A_2/VP02_test.jpg";
        var result = await crawler.CrawlImageAsync(rawPath, "http://10.121.45.43:336/api/image/get", customVp: "2");

        Assert.True(result.Success);
        Assert.True(searchPanelsCalled);
        Assert.True(panelImagesCalled);
        Assert.True(imageGetCalled);
        Assert.Equal(FakeJpegBytes, result.ImageBytes);
    }

    [Fact]
    public async Task CrawlImageAsync_OriginalMode_UsesFolderBrowseAndPathVp_NotZip()
    {
        string? searchJson = null;
        string? getJson = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/image/search-panels"))
            {
                searchJson = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        folders = new[] { new { folderPath = @"\\server\image\panel-folder" } }
                    }), Encoding.UTF8, "application/json")
                };
            }

            if (url.Contains("/api/image/panel-images"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        Data = new
                        {
                            Images = new[]
                            {
                                new
                                {
                                    FileName = "VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg",
                                    FilePath = @"\\server\image\panel-folder\VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg"
                                }
                            }
                        }
                    }), Encoding.UTF8, "application/json")
                };
            }

            if (url.Contains("/api/image/get"))
            {
                getJson = await req.Content!.ReadAsStringAsync();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(FakeJpegBytes)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);
        var crawler = new ImageCrawlerService(httpClient, db, new NullStorageService(), NullLogger<ImageCrawlerService>.Instance);
        var rawPath = "2026/08/05/CP-AOI #301-B/P1E1650119BA27311SD2/VP01/P1E1650119BA27311SD2_094722_B_1/VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg";

        var result = await crawler.CrawlImageAsync(rawPath, "http://10.121.45.43:336/api/image/get", customVp: "original");

        Assert.True(result.Success);
        Assert.NotNull(searchJson);
        Assert.NotNull(getJson);
        using var searchDoc = JsonDocument.Parse(searchJson);
        Assert.Equal("1", searchDoc.RootElement.GetProperty("VP").GetString());
        Assert.Equal(rawPath, searchDoc.RootElement.GetProperty("ImagePath").GetString());
        Assert.Equal(rawPath, searchDoc.RootElement.GetProperty("image_path").GetString());
        using var getDoc = JsonDocument.Parse(getJson);
        Assert.Equal(@"\\server\image\panel-folder\VP01_P1E1650119BA27311SD2_094722_B_1_1_.jpg", getDoc.RootElement.GetProperty("FilePath").GetString());
        Assert.False(getDoc.RootElement.TryGetProperty("ZipEntry", out _));
    }

    [Fact]
    public void ResolveRealDefectVp_ExtractsCorrectly()
    {
        // 1. From feature JSON G010_VpID
        var vp1 = ImageCrawlerService.ResolveRealDefectVp("path/no_vp/file.jpg", "{\"G010_VpID\": 2}");
        Assert.Equal("2", vp1);

        // 2. From folder path /VP03/
        var vp2 = ImageCrawlerService.ResolveRealDefectVp("2026/07/25/LINE-A/PANEL/VP03/sub/file.jpg", null);
        Assert.Equal("3", vp2);

        // 3. From filename prefix VP04_
        var vp3 = ImageCrawlerService.ResolveRealDefectVp("2026/07/25/LINE-A/PANEL/folder/VP04_123.jpg", null);
        Assert.Equal("4", vp3);

        // 4. Default fallback when neither has specific VP > 0
        var vpFallback = ImageCrawlerService.ResolveRealDefectVp(null, null);
        Assert.Equal("1", vpFallback);
    }

    [Fact]
    public void ResolveOriginalDefectVp_UsesOnlyImagePath()
    {
        Assert.Equal("3", ImageCrawlerService.ResolveOriginalDefectVp(@"D:\AOI\PANEL\VP03\image.jpg"));
        Assert.Equal("4", ImageCrawlerService.ResolveOriginalDefectVp("2026/07/25/PANEL/VP04_123.jpg"));
        Assert.Equal("0", ImageCrawlerService.ResolveOriginalDefectVp("2026/07/25/PANEL/VP00/image.jpg"));
        Assert.Null(ImageCrawlerService.ResolveOriginalDefectVp("2026/07/25/PANEL/image.jpg"));
        Assert.Null(ImageCrawlerService.ResolveOriginalDefectVp(null));
    }

    [Fact]
    public async Task CrawlAndCacheDefectImageAsync_Vp0_PersistsToStorageAndUpdatesImageUrl()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(FakeJpegBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);
        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "VP0-PRODUCT",
            DefectName = "AOI",
            ImageUri = @"D:\AOI\source-vp0.jpg"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();
        var storage = new TrackingStorageService();
        var crawler = new ImageCrawlerService(httpClient, db, storage, NullLogger<ImageCrawlerService>.Instance);

        var result = await crawler.CrawlAndCacheDefectImageAsync(
            defect.Id,
            forceRefresh: true,
            customApiUrl: "http://127.0.0.1:5000/image?path={path}",
            customVp: "0");

        Assert.True(result.Success);
        Assert.Equal(1, storage.ImageUploadCount);
        Assert.NotNull(result.StoredUrl);
        Assert.NotNull((await db.DefectRecords.FindAsync(defect.Id))!.ImageUrl);
    }

    [Fact]
    public async Task CrawlAndCacheDefectImageAsync_NonVp0_ReturnsDirectWithoutStorageOrImageUrlUpdate()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/image/search-panels"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        folders = new[] { new { folderPath = @"\\server\images\panel" } }
                    }), Encoding.UTF8, "application/json")
                });
            }
            if (url.Contains("/api/image/panel-images"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        Data = new { Images = new[] { new { FileName = "VP02_full.jpg", FilePath = @"\\server\images\panel\VP02_full.jpg" } } }
                    }), Encoding.UTF8, "application/json")
                });
            }
            if (url.Contains("/api/image/get"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(FakeJpegBytes)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);
        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "VP2-PRODUCT",
            DefectName = "AOI",
            ImageUri = "2026/08/05/CP-AOI #301-B/VP2-PRODUCT/VP01/FOLDER/VP01_source.jpg",
            ImageUrl = "seaweed://existing-vp0"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();
        var storage = new TrackingStorageService();
        var crawler = new ImageCrawlerService(httpClient, db, storage, NullLogger<ImageCrawlerService>.Instance);

        var result = await crawler.CrawlAndCacheDefectImageAsync(
            defect.Id,
            forceRefresh: true,
            customApiUrl: "http://10.121.45.43:336/api/image/get",
            customVp: "2");

        Assert.True(result.Success);
        Assert.Equal(FakeJpegBytes, result.ImageBytes);
        Assert.Null(result.StoredUrl);
        Assert.Equal(0, storage.ImageUploadCount);
        Assert.Equal("seaweed://existing-vp0", (await db.DefectRecords.FindAsync(defect.Id))!.ImageUrl);
    }

    [Fact]
    public async Task CrawlAndCacheDefectImageAsync_Vp0StorageCacheHit_DoesNotCallSourceApi()
    {
        var sourceApiCalls = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            sourceApiCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);
        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "VP0-CACHED",
            DefectName = "AOI",
            ImageUri = "2026/09/21/CP-AOI #303-A/VP0-CACHED/VP01/FOLDER/source.jpg",
            ImageUrl = "seaweed://cached-vp0.jpg"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();
        var storage = new TrackingStorageService { CachedImageBytes = FakeJpegBytes };
        var crawler = new ImageCrawlerService(httpClient, db, storage, NullLogger<ImageCrawlerService>.Instance);

        var result = await crawler.CrawlAndCacheDefectImageAsync(
            defect.Id,
            forceRefresh: false,
            customApiUrl: "http://10.121.45.43:336/api/image/get",
            customVp: "0");

        Assert.True(result.Success);
        Assert.Equal(FakeJpegBytes, result.ImageBytes);
        Assert.Equal("seaweed://cached-vp0.jpg", result.StoredUrl);
        Assert.Equal(0, sourceApiCalls);
        Assert.Equal(0, storage.ImageUploadCount);
    }

    [Fact]
    public async Task CrawlAndCacheDefectImageAsync_Vp0Primary404_BackfillsViaBrowseAndPersists()
    {
        var primaryCalls = 0;
        var searchCalls = 0;
        var panelCalls = 0;
        var downloadCalls = 0;
        string? primaryPayload = null;
        string? searchPayload = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/image/search-panels"))
            {
                searchCalls++;
                searchPayload = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        folders = new[] { new { folderPath = @"\\server\vp0\panel-folder" } }
                    }), Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("/api/image/panel-images"))
            {
                panelCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        Data = new { Images = new[] { new { FileName = "VP00_backfill.jpg", FilePath = @"\\server\vp0\panel-folder\VP00_backfill.jpg" } } }
                    }), Encoding.UTF8, "application/json")
                };
            }
            if (url.Contains("/api/image/get"))
            {
                var payload = await req.Content!.ReadAsStringAsync();
                using var payloadDocument = JsonDocument.Parse(payload);
                var requestedFilePath = payloadDocument.RootElement.GetProperty("FilePath").GetString() ?? string.Empty;
                if (requestedFilePath.Contains("panel-folder", StringComparison.OrdinalIgnoreCase))
                {
                    downloadCalls++;
                    var ok = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(FakeJpegBytes)
                    };
                    ok.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                    return ok;
                }

                primaryCalls++;
                primaryPayload = payload;
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"Success\":false,\"Message\":\"File not found\"}", Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var httpClient = new HttpClient(handler);
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var db = new AutoVerifyDbContext(options);
        var defect = new DefectRecordEntity
        {
            Id = Guid.NewGuid(),
            KeyId = "VP0-BACKFILL",
            DefectName = "AOI",
            ImageUri = "2026/09/21/SPI_STATION #303-A/VP0-BACKFILL/VP01/FOLDER/VP01_source.jpg"
        };
        db.DefectRecords.Add(defect);
        await db.SaveChangesAsync();
        var storage = new TrackingStorageService();
        var crawler = new ImageCrawlerService(httpClient, db, storage, NullLogger<ImageCrawlerService>.Instance);

        var result = await crawler.CrawlAndCacheDefectImageAsync(
            defect.Id,
            forceRefresh: true,
            customApiUrl: "http://10.121.45.43:336/api/image/get",
            customVp: "0");

        Assert.True(result.Success);
        Assert.Equal(1, primaryCalls);
        Assert.NotNull(primaryPayload);
        Assert.Contains("FOLDER.zip", primaryPayload);
        Assert.Contains("VP01_source.jpg", primaryPayload);
        Assert.Contains("ZipEntry", primaryPayload);
        Assert.True(searchCalls > 0);
        Assert.True(panelCalls > 0);
        Assert.Equal(1, downloadCalls);
        Assert.Equal(1, storage.ImageUploadCount);
        Assert.NotNull((await db.DefectRecords.FindAsync(defect.Id))!.ImageUrl);
    }

    private sealed class TrackingStorageService : IStorageService
    {
        public int ImageUploadCount { get; private set; }
        public byte[]? CachedImageBytes { get; init; }
        public string Mode => "SeaweedFS";
        public Task<StoredObjectResponse> UploadImageAsync(string? bucket, string objectName, Stream imageStream, string? contentType, CancellationToken cancellationToken = default)
        {
            ImageUploadCount++;
            return Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), "seaweedfs", bucket ?? "defect-images", objectName, contentType ?? "image/jpeg", imageStream.Length, $"seaweed://{objectName}", DateTimeOffset.UtcNow));
        }
        public Task<StoredObjectResponse> UploadJsonAsync(string? bucket, string? objectName, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredObjectResponse> UploadJsonStringAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredObjectResponse> UploadJsonGzipAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductBundleUploadResult> UploadProductDefectBundleAsync(string keyId, IEnumerable<DefectRecordEntity> defects, IDictionary<string, byte[]>? imageFiles = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> UploadProductDefectPackAsync(string keyId, IEnumerable<DefectRecordEntity> defects, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> ReadDefectPackJsonAsync(string packLocationOrUrl, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<(byte[] Data, string ContentType)?> ExtractZipEntryAsync(string zipLocationOrUrl, string entryNameOrPrefix, CancellationToken cancellationToken = default) => Task.FromResult<(byte[] Data, string ContentType)?>(null);
        public Task<string?> ReadTextAsync(string locationOrUrl, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<StorageStreamResult?> OpenReadStreamAsync(string locationOrUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(CachedImageBytes is null
                ? null
                : new StorageStreamResult
                {
                    Stream = new MemoryStream(CachedImageBytes, writable: false),
                    ContentType = "image/jpeg",
                    ContentLength = CachedImageBytes.Length
                });
    }

    private sealed class NullStorageService : IStorageService
    {
        public string Mode => "Offline";

        public Task<StoredObjectResponse> UploadJsonAsync(string? bucket, string? objectName, object payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), "local", bucket ?? "default", objectName ?? "obj", "application/json", 0, $"local://{objectName}", DateTimeOffset.UtcNow));

        public Task<StoredObjectResponse> UploadJsonStringAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default) =>
            UploadJsonAsync(bucket, objectName, jsonContent, cancellationToken);

        public Task<StoredObjectResponse> UploadJsonGzipAsync(string? bucket, string? objectName, string jsonContent, CancellationToken cancellationToken = default) =>
            UploadJsonAsync(bucket, objectName, jsonContent, cancellationToken);

        public Task<StoredObjectResponse> UploadImageAsync(string? bucket, string objectName, Stream imageStream, string? contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredObjectResponse(Guid.NewGuid(), "local", bucket ?? "default", objectName, contentType ?? "image/jpeg", imageStream.Length, $"local://{objectName}", DateTimeOffset.UtcNow));

        public Task<ProductBundleUploadResult> UploadProductDefectBundleAsync(string keyId, IEnumerable<DefectRecordEntity> defects, IDictionary<string, byte[]>? imageFiles = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductBundleUploadResult($"local://{keyId}", keyId, 0, 0, 1.0, 0, new Dictionary<Guid, DefectBundleEntryInfo>()));

        public Task<string> UploadProductDefectPackAsync(string keyId, IEnumerable<DefectRecordEntity> defects, CancellationToken cancellationToken = default) =>
            Task.FromResult($"local://pack_{keyId}.json.gz");

        public Task<string?> ReadDefectPackJsonAsync(string packLocationOrUrl, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<(byte[] Data, string ContentType)?> ExtractZipEntryAsync(string zipLocationOrUrl, string entryNameOrPrefix, CancellationToken cancellationToken = default) => Task.FromResult<(byte[] Data, string ContentType)?>(null);
        public Task<string?> ReadTextAsync(string locationOrUrl, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<StorageStreamResult?> OpenReadStreamAsync(string locationOrUrl, CancellationToken cancellationToken = default) => Task.FromResult<StorageStreamResult?>(null);
    }
}
