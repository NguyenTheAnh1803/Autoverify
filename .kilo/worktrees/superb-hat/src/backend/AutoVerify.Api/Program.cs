using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5173", "https://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<SeaweedFsStorageManager>();
builder.Services.Configure<SeaweedFsOptions>(builder.Configuration.GetSection("SeaweedFs"));

var connectionString = builder.Configuration.GetConnectionString("AutoVerify")
    ?? "Server=localhost;Port=3306;Database=autoverify;User=autoverify;Password=autoverify-pass;CharSet=utf8mb4;Allow User Variables=True;";
builder.Services.AddDbContext<AutoVerifyDbContext>(options => options.UseMySql(connectionString, new MariaDbServerVersion(new Version(10, 6, 0))));
builder.Services.AddScoped<AutoVerifyStore>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DatabaseSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

var api = app.MapGroup("/api/v1");

api.MapPost("/auth/login", async (LoginRequest request, AutoVerifyStore store) =>
{
    if (!await store.ValidateCredentialsAsync(request.Username, request.Password))
    {
        return Results.Unauthorized();
    }

    await store.RecordAuditAsync("admin", "Auth.Login", "User", "admin", "Temporary administrator login succeeded.");
    return Results.Ok(new LoginResponse(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), new UserProfile("admin", "Administrator", ["Administrator"])));
})
.WithName("Login")
.WithOpenApi();

api.MapGet("/auth/me", () => new UserProfile("admin", "Administrator", ["Administrator"]))
    .WithName("GetCurrentUser")
    .WithOpenApi();

api.MapPost("/auth/logout", async (AutoVerifyStore store) =>
{
    await store.RecordAuditAsync("admin", "Auth.Logout", "User", "admin", "User session ended.");
    return Results.NoContent();
})
.WithName("Logout")
.WithOpenApi();

api.MapGet("/dashboard/summary", async (AutoVerifyStore store) => await store.GetDashboardSummaryAsync())
    .WithName("GetDashboardSummary")
    .WithOpenApi();

api.MapGet("/dashboard/trends", async (AutoVerifyStore store) => await store.GetTrendBucketsAsync())
    .WithName("GetDashboardTrends")
    .WithOpenApi();

api.MapPost("/imports/preview", ([FromBody] ImportPreviewRequest request, AutoVerifyStore store) => store.PreviewImport(request))
    .WithName("PreviewImport")
    .WithOpenApi();

api.MapPost("/scan-lists", async ([FromBody] CreateScanListRequest request, AutoVerifyStore store, SeaweedFsStorageManager storage) =>
{
    var importPreview = store.PreviewImport(new ImportPreviewRequest(request.RawText, request.MappingName));
    var evidence = new
    {
        request.Name,
        request.MappingName,
        importPreview.AcceptedCount,
        importPreview.RejectedCount,
        importPreview.DuplicateCount,
        importPreview.Checksum,
        ImportedAtUtc = DateTimeOffset.UtcNow
    };
    var storedObject = await storage.UploadJsonAsync("scan-imports", $"{Guid.NewGuid():N}.json", evidence);
    await store.SaveStorageObjectAsync(storedObject, "ScanImportEvidence");
    var scanList = await store.CreateScanListAsync(request, storedObject);
    await store.RecordAuditAsync("admin", "ScanList.Create", "ScanList", scanList.Id.ToString(), $"Created scan list '{scanList.Name}'.");
    return Results.Created($"/api/v1/scan-lists/{scanList.Id}", scanList);
})
.WithName("CreateScanList")
.WithOpenApi();

api.MapGet("/scan-lists", async (AutoVerifyStore store) => await store.GetScanListsAsync())
    .WithName("GetScanLists")
    .WithOpenApi();

api.MapPost("/scan-executions", async ([FromBody] CreateScanExecutionRequest request, AutoVerifyStore store, SeaweedFsStorageManager storage) =>
{
    var execution = await store.CreateScanExecutionAsync(request, storage);
    await store.RecordAuditAsync("admin", "ScanExecution.Create", "ScanExecution", execution.Id.ToString(), $"Started scan '{execution.Name}'.");
    return Results.Created($"/api/v1/scan-executions/{execution.Id}", execution);
})
.WithName("CreateScanExecution")
.WithOpenApi();

api.MapGet("/scan-executions", async (AutoVerifyStore store) => await store.GetScanExecutionsAsync())
    .WithName("GetScanExecutions")
    .WithOpenApi();

api.MapGet("/scan-executions/{executionId:guid}/results", async (Guid executionId, AutoVerifyStore store) => await store.GetResultsAsync(executionId))
    .WithName("GetScanExecutionResults")
    .WithOpenApi();

api.MapGet("/data-sources", async (AutoVerifyStore store) => await store.GetDataSourcesAsync())
    .WithName("GetDataSources")
    .WithOpenApi();

api.MapPost("/data-sources/{sourceId:guid}/test", async (Guid sourceId, AutoVerifyStore store) =>
{
    var source = await store.GetDataSourceAsync(sourceId);
    return source is null
        ? Results.NotFound()
        : Results.Ok(new ConnectionTestResult(source.Id, source.Name, true, source.AverageLatencyMs, "Connection test simulated successfully."));
})
.WithName("TestDataSource")
.WithOpenApi();

api.MapPost("/storage/json", async ([FromBody] UploadJsonRequest request, SeaweedFsStorageManager storage, AutoVerifyStore store) =>
{
    var storedObject = await storage.UploadJsonAsync(request.Bucket, request.ObjectName, request.Payload);
    await store.SaveStorageObjectAsync(storedObject, "JsonPayload");
    return Results.Created($"/api/v1/storage/{storedObject.Id}", storedObject);
})
.WithName("UploadJsonToSeaweedFs")
.WithOpenApi();

api.MapPost("/storage/images", async (HttpRequest request, SeaweedFsStorageManager storage, AutoVerifyStore store) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected multipart/form-data with a file field.");
    }

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("Image file is required.");
    }

    await using var stream = file.OpenReadStream();
    var bucket = form["bucket"].FirstOrDefault() ?? "images";
    var storedObject = await storage.UploadImageAsync(bucket, file.FileName, stream, file.ContentType);
    await store.SaveStorageObjectAsync(storedObject, "Image");
    return Results.Created($"/api/v1/storage/{storedObject.Id}", storedObject);
})
.DisableAntiforgery()
.WithName("UploadImageToSeaweedFs")
.WithOpenApi();

api.MapGet("/storage", async (AutoVerifyStore store) => await store.GetStorageObjectsAsync())
    .WithName("GetStorageObjects")
    .WithOpenApi();

api.MapPost("/product-queries", async ([FromBody] CreateProductQueryRequest request, AutoVerifyStore store) =>
{
    var query = await store.CreateProductQueryAsync(request);
    await store.RecordAuditAsync("admin", "ProductQuery.Create", "ProductQuery", query.Id.ToString(), $"Created product query '{query.QueryKey}'.");
    return Results.Created($"/api/v1/product-queries/{query.Id}", query);
})
.WithName("CreateProductQuery")
.WithOpenApi();

api.MapGet("/product-queries", async ([FromQuery] string? status, AutoVerifyStore store) => await store.GetProductQueriesAsync(status))
    .WithName("GetProductQueries")
    .WithOpenApi();

api.MapPost("/product-queries/{queryId:guid}/status", async (Guid queryId, [FromBody] UpdateProductQueryStatusRequest request, AutoVerifyStore store) =>
{
    var query = await store.UpdateProductQueryStatusAsync(queryId, request.Status, request.JudgeResult, request.Message);
    return query is null ? Results.NotFound() : Results.Ok(query);
})
.WithName("UpdateProductQueryStatus")
.WithOpenApi();

api.MapGet("/audit-events", async (AutoVerifyStore store) => await store.GetAuditEventsAsync())
    .WithName("GetAuditEvents")
    .WithOpenApi();

api.MapGet("/system/health", async (AutoVerifyDbContext db, SeaweedFsStorageManager storage) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    var storageMode = storage.Mode;
    return new SystemHealthResponse(canConnect ? "Healthy" : "Degraded", DateTimeOffset.UtcNow, "AutoVerify.Api", "0.3.0-mariadb-storage", storageMode);
})
.WithName("GetSystemHealth")
.WithOpenApi();

await app.RunAsync();
