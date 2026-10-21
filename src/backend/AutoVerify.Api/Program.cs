using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System.Net;
using AutoVerify.Api.Analytics;
using AutoVerify.Api.Logging;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Services;
using AutoVerify.Api.Storage;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

    builder.AddCustomSerilog();

    // API listener and Kestrel limits are controlled from appsettings.json.
    var apiHost = builder.Configuration["ApiServer:Host"]?.Trim() ?? "0.0.0.0";
    var apiPort = Math.Clamp(builder.Configuration.GetValue("ApiServer:Port", 5058), 1, 65535);
    var enableHttps = builder.Configuration.GetValue("ApiServer:EnableHttps", false);
    var httpsPort = Math.Clamp(builder.Configuration.GetValue("ApiServer:HttpsPort", 7217), 1, 65535);
    var certificatePath = builder.Configuration["ApiServer:CertificatePath"]?.Trim();
    var certificatePassword = builder.Configuration["ApiServer:CertificatePassword"];
    var maxRequestBodySizeMb = Math.Max(1, builder.Configuration.GetValue("ApiServer:MaxRequestBodySizeMb", 256));

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxRequestBodySizeMb * 1024L * 1024L;

        void ConfigureHttp(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listen) { }

        if (apiHost is "0.0.0.0" or "*" or "+")
        {
            options.ListenAnyIP(apiPort, ConfigureHttp);
        }
        else if (apiHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            options.ListenLocalhost(apiPort, ConfigureHttp);
        }
        else if (IPAddress.TryParse(apiHost, out var address))
        {
            options.Listen(address, apiPort, ConfigureHttp);
        }
        else
        {
            throw new InvalidOperationException($"ApiServer:Host '{apiHost}' is not a valid IP address, localhost, 0.0.0.0, *, or +.");
        }

        if (enableHttps)
        {
            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                throw new InvalidOperationException("ApiServer:CertificatePath is required when ApiServer:EnableHttps is true.");
            }

            options.ListenAnyIP(httpsPort, listen => listen.UseHttps(certificatePath, certificatePassword));
        }
    });

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173", "https://localhost:5173", "http://127.0.0.1:5173"];
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy => policy
            .WithOrigins(allowedOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod());
    });

    // Storage Layer (SeaweedFS / Local Fallback)
    var imageCrawlerTimeoutSeconds = Math.Max(1, builder.Configuration.GetValue("HttpClients:ImageCrawlerTimeoutSeconds", 30));
    var seaweedFsTimeoutSeconds = Math.Max(1, builder.Configuration.GetValue("HttpClients:SeaweedFsTimeoutSeconds", 60));
    builder.Services.AddHttpClient<SeaweedFsStorageManager>(client =>
        client.Timeout = TimeSpan.FromSeconds(seaweedFsTimeoutSeconds));
    builder.Services.AddHttpClient<IImageCrawlerService, ImageCrawlerService>(client =>
        client.Timeout = TimeSpan.FromSeconds(imageCrawlerTimeoutSeconds));
    builder.Services.Configure<SeaweedFsOptions>(builder.Configuration.GetSection("SeaweedFs"));
    builder.Services.Configure<AutoVerify.Api.Integration.MasterServerOptions>(builder.Configuration.GetSection(AutoVerify.Api.Integration.MasterServerOptions.SectionName));
    builder.Services.Configure<AutoVerify.Api.Integration.AiVerificationOptions>(builder.Configuration.GetSection(AutoVerify.Api.Integration.AiVerificationOptions.SectionName));
    builder.Services.AddScoped<AutoVerify.Api.Integration.IAiVerificationService, AutoVerify.Api.Integration.AiVerificationService>();
    builder.Services.AddSingleton<AutoVerify.Api.Integration.AiDispatchState>();
    builder.Services.AddHostedService<AutoVerify.Api.Integration.AiVerificationHostedService>();
    builder.Services.AddHttpClient<AutoVerify.Api.Integration.IMasterServerClient, AutoVerify.Api.Integration.MasterServerClient>(client =>
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, builder.Configuration.GetValue("MasterServer:TimeoutSeconds", 60))));
    builder.Services.Configure<DefectPreviewOptions>(builder.Configuration.GetSection("DefectPreview"));
    builder.Services.Configure<ImageCrawlerOptions>(builder.Configuration.GetSection(ImageCrawlerOptions.SectionName));
    builder.Services.Configure<BackgroundJobOptions>(builder.Configuration.GetSection(BackgroundJobOptions.SectionName));
    builder.Services.Configure<DatabaseBootstrapOptions>(builder.Configuration.GetSection(DatabaseBootstrapOptions.SectionName));
    builder.Services.AddScoped<IStorageService>(services => services.GetRequiredService<SeaweedFsStorageManager>());

    // Data Protection
    var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"];
    var defaultKeysRoot = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AutoVerify")
        : "/var/lib/autoverify";
    var dataProtectionKeysPath = Path.GetFullPath(
        string.IsNullOrWhiteSpace(configuredKeysPath)
            ? Path.Combine(defaultKeysRoot, "keys")
            : configuredKeysPath);
    Directory.CreateDirectory(dataProtectionKeysPath);

    var legacyKeysPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "data", "keys"));
    if (!string.Equals(legacyKeysPath, dataProtectionKeysPath, StringComparison.OrdinalIgnoreCase) &&
        Directory.Exists(legacyKeysPath))
    {
        foreach (var legacyKeyPath in Directory.EnumerateFiles(legacyKeysPath, "*.xml", SearchOption.TopDirectoryOnly))
        {
            var destinationPath = Path.Combine(dataProtectionKeysPath, Path.GetFileName(legacyKeyPath));
            if (!File.Exists(destinationPath))
            {
                File.Copy(legacyKeyPath, destinationPath);
            }
        }
    }

    var dataProtectionApplicationName = builder.Configuration["DataProtection:ApplicationName"]?.Trim();
    if (string.IsNullOrWhiteSpace(dataProtectionApplicationName)) dataProtectionApplicationName = "AutoVerify";
    var dataProtection = builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName(dataProtectionApplicationName);

    if (OperatingSystem.IsWindows() && builder.Configuration.GetValue("DataProtection:ProtectKeysWithDpapi", true))
    {
        dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }

    // Database Connection & EF Core Context
    var connectionString = builder.Configuration.GetConnectionString("AutoVerify")
        ?? "Server=localhost;Port=3306;Database=autoverify;User=root;Password=1111;CharSet=utf8mb4;Allow User Variables=True;";
    var databaseServerVersion = Version.TryParse(builder.Configuration["Database:ServerVersion"], out var configuredVersion)
        ? configuredVersion
        : new Version(10, 6, 0);
    var databaseCommandTimeoutSeconds = Math.Max(1, builder.Configuration.GetValue("Database:CommandTimeoutSeconds", 120));
    var databaseRetryEnabled = builder.Configuration.GetValue("Database:EnableRetryOnFailure", true);
    var databaseMaxRetryCount = Math.Max(0, builder.Configuration.GetValue("Database:MaxRetryCount", 5));
    var databaseMaxRetryDelaySeconds = Math.Max(1, builder.Configuration.GetValue("Database:MaxRetryDelaySeconds", 10));
    builder.Services.AddDbContext<AutoVerifyDbContext>(options =>
        options.UseMySql(connectionString, new MariaDbServerVersion(databaseServerVersion), mySqlOptions =>
        {
            mySqlOptions.EnableStringComparisonTranslations();
            mySqlOptions.CommandTimeout(databaseCommandTimeoutSeconds);
            if (databaseRetryEnabled)
            {
                mySqlOptions.EnableRetryOnFailure(databaseMaxRetryCount, TimeSpan.FromSeconds(databaseMaxRetryDelaySeconds), null);
            }
        }));

    // Algorithms & Business Engines
    builder.Services.AddScoped<IAnomalyVerificationService, AnomalyVerificationService>();
    builder.Services.AddScoped<IQueryEngineService, QueryEngineService>();
    builder.Services.AddScoped<IImportValidationService, ImportValidationService>();
    builder.Services.AddSingleton<IDatabaseCredentialService, DatabaseCredentialService>();
    builder.Services.AddScoped<IOverkillQueryEngineService, OverkillQueryEngineService>();
    builder.Services.AddSingleton<ILeakageProcessingService, LeakageProcessingService>();
    builder.Services.AddHostedService<OverkillQuerySchedulerHostedService>();

    // Defect Preview Ingestion & Query Services
    builder.Services.AddSingleton<IDefectPreviewIngestionService, DefectPreviewIngestionService>();
    builder.Services.AddSingleton<IDefectFeatureAnalyticsService, DefectFeatureAnalyticsService>();
    builder.Services.AddSingleton<ProductionFeatureAnalyticsService>();
    builder.Services.AddScoped<IDefectPreviewService, DefectPreviewService>();
    builder.Services.AddHostedService<DefectPreviewCleanupHostedService>();

    // Data Store / Repositories
    builder.Services.AddScoped<AutoVerifyStore>();
    builder.Services.AddSingleton<DatabaseBootstrapper>();

    var app = builder.Build();

    if (args.Contains("--export-bootstrap-sql", StringComparer.OrdinalIgnoreCase))
    {
        var argumentIndex = Array.FindIndex(args, arg => arg.Equals("--export-bootstrap-sql", StringComparison.OrdinalIgnoreCase));
        var outputPath = argumentIndex >= 0 && argumentIndex + 1 < args.Length ? args[argumentIndex + 1] : null;
        using var exportScope = app.Services.CreateScope();
        var exportDb = exportScope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
        var bootstrapper = exportScope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
        await bootstrapper.ExportCurrentModelAsync(exportDb, outputPath);
        return;
    }

    // Database Migration & Seed Initializer
    using (var scope = app.Services.CreateScope())
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSeeder));
        var db = scope.ServiceProvider.GetRequiredService<AutoVerifyDbContext>();
        logger.LogInformation("Starting database migration and schema verification...");
        logger.LogInformation("Data Protection key ring path: {KeysPath}", dataProtectionKeysPath);
        await DatabaseSeeder.EnsureDatabaseCreatedAsync(connectionString, logger);
        var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
        await bootstrapper.ExecuteIfRequiredAsync(connectionString);
        await db.Database.EnsureCreatedAsync();
        await DatabaseSeeder.EnsureOperationalTablesAsync(db, logger);
        await DatabaseSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IDatabaseCredentialService>());
        logger.LogInformation("Database migration and schema verification completed successfully.");
    }

    if (builder.Configuration.GetValue("ApiServer:EnableSwagger", app.Environment.IsDevelopment()))
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("Frontend");

    app.UseCustomSerilogRequestLogging();

    var embeddedWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    if (builder.Configuration.GetValue("ApiServer:EnableStaticFiles", true) && Directory.Exists(embeddedWebRoot))
    {
        var embeddedFileProvider = new PhysicalFileProvider(embeddedWebRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embeddedFileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedFileProvider });
    }

    app.MapControllers();

    app.MapFallback(async context =>
    {
        var indexPath = Path.Combine(embeddedWebRoot, "index.html");
        if (!File.Exists(indexPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(indexPath);
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AutoVerify.Api host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
