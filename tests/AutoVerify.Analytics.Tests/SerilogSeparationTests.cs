using AutoVerify.Api.Logging;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class SerilogSeparationTests
{
    private static readonly MessageTemplateParser Parser = new();

    private static LogEvent CreateLogEvent(
        LogEventLevel level,
        string messageTemplate,
        IEnumerable<LogEventProperty>? properties = null)
    {
        var template = Parser.Parse(messageTemplate);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            null,
            template,
            properties ?? Enumerable.Empty<LogEventProperty>());
    }

    [Fact]
    public void IsSpecialLog_WithExplicitLogTypeSpecial_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "Special event occurred",
            new[]
            {
                new LogEventProperty(LoggingConstants.LogTypeProperty, new ScalarValue(LoggingConstants.LogTypes.Special))
            });

        Assert.True(SerilogConfigurationExtensions.IsSpecialLog(evt));
    }

    [Fact]
    public void IsSpecialLog_WithErrorOrFatal_ReturnsTrue()
    {
        var errorEvt = CreateLogEvent(LogEventLevel.Error, "Database connection dropped");
        var fatalEvt = CreateLogEvent(LogEventLevel.Fatal, "Out of memory crash");
        var infoEvt = CreateLogEvent(LogEventLevel.Information, "Normal operational heartbeat");

        Assert.True(SerilogConfigurationExtensions.IsSpecialLog(errorEvt));
        Assert.True(SerilogConfigurationExtensions.IsSpecialLog(fatalEvt));
        Assert.False(SerilogConfigurationExtensions.IsSpecialLog(infoEvt));
    }

    [Fact]
    public void IsSpecialLog_WithAuditActionProperty_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "User permissions modified",
            new[]
            {
                new LogEventProperty(LoggingConstants.AuditActionProperty, new ScalarValue("User.UpdatePermissions"))
            });

        Assert.True(SerilogConfigurationExtensions.IsSpecialLog(evt));
    }

    [Fact]
    public void IsSpecialLog_WithDatabaseSeederContext_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "Database seeding complete",
            new[]
            {
                new LogEventProperty("SourceContext", new ScalarValue("AutoVerify.Api.Persistence.DatabaseSeeder"))
            });

        Assert.True(SerilogConfigurationExtensions.IsSpecialLog(evt));
    }

    [Fact]
    public void IsUserEventLog_WithExplicitLogTypeUserEvent_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "User updated profile",
            new[]
            {
                new LogEventProperty(LoggingConstants.LogTypeProperty, new ScalarValue(LoggingConstants.LogTypes.UserEvent))
            });

        Assert.True(SerilogConfigurationExtensions.IsUserEventLog(evt));
    }

    [Fact]
    public void IsUserEventLog_WithHttpRequestProperties_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "HTTP GET /api/v1/defects responded 200",
            new[]
            {
                new LogEventProperty("RequestPath", new ScalarValue("/api/v1/defects")),
                new LogEventProperty("RequestMethod", new ScalarValue("GET"))
            });

        Assert.True(SerilogConfigurationExtensions.IsUserEventLog(evt));
    }

    [Fact]
    public void IsUserEventLog_WithControllerSourceContext_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "Executing action",
            new[]
            {
                new LogEventProperty("SourceContext", new ScalarValue("AutoVerify.Api.Controllers.VerificationController"))
            });

        Assert.True(SerilogConfigurationExtensions.IsUserEventLog(evt));
    }

    [Fact]
    public void IsAutoLog_WithExplicitLogTypeAuto_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "Automated background cycle executed",
            new[]
            {
                new LogEventProperty(LoggingConstants.LogTypeProperty, new ScalarValue(LoggingConstants.LogTypes.Auto))
            });

        Assert.True(SerilogConfigurationExtensions.IsAutoLog(evt));
    }

    [Fact]
    public void IsAutoLog_WithJobNameProperty_ReturnsTrue()
    {
        var evt = CreateLogEvent(
            LogEventLevel.Information,
            "Starting scheduled run",
            new[]
            {
                new LogEventProperty(LoggingConstants.JobNameProperty, new ScalarValue("OverkillQueryScheduler"))
            });

        Assert.True(SerilogConfigurationExtensions.IsAutoLog(evt));
    }

    [Fact]
    public void IsAutoLog_WithHostedServiceContext_ReturnsTrue()
    {
        var schedulerEvt = CreateLogEvent(
            LogEventLevel.Information,
            "Scheduler tick",
            new[]
            {
                new LogEventProperty("SourceContext", new ScalarValue("AutoVerify.Api.Services.OverkillQuerySchedulerHostedService"))
            });

        var cleanupEvt = CreateLogEvent(
            LogEventLevel.Information,
            "Cleanup tick",
            new[]
            {
                new LogEventProperty("SourceContext", new ScalarValue("AutoVerify.Api.Services.DefectPreviewCleanupHostedService"))
            });

        Assert.True(SerilogConfigurationExtensions.IsAutoLog(schedulerEvt));
        Assert.True(SerilogConfigurationExtensions.IsAutoLog(cleanupEvt));
    }

    [Fact]
    public void EndToEnd_FileSeparation_DispatchesLogsToCorrectDestinations()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AutoVerify_SerilogTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var specialDir = Path.Combine(tempDir, "special");
            var userDir = Path.Combine(tempDir, "user-events");
            var autoDir = Path.Combine(tempDir, "auto");
            var appDir = Path.Combine(tempDir, "app");

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(SerilogConfigurationExtensions.IsSpecialLog)
                    .WriteTo.File(Path.Combine(specialDir, "special-.log"), rollingInterval: RollingInterval.Day))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(SerilogConfigurationExtensions.IsUserEventLog)
                    .WriteTo.File(Path.Combine(userDir, "user-events-.log"), rollingInterval: RollingInterval.Day))
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(SerilogConfigurationExtensions.IsAutoLog)
                    .WriteTo.File(Path.Combine(autoDir, "auto-.log"), rollingInterval: RollingInterval.Day))
                .WriteTo.File(Path.Combine(appDir, "app-.log"), rollingInterval: RollingInterval.Day);

            using (var serilogLogger = loggerConfig.CreateLogger())
            {
                using var factory = LoggerFactory.Create(builder =>
                {
                    builder.AddSerilog(serilogLogger, dispose: false);
                });

                var appLogger = factory.CreateLogger("Test");

                // 1. Emit Special log
                appLogger.LogSpecial(LogLevel.Warning, "Test Special Security Incident");

                // 2. Emit UserEvent log
                appLogger.LogUserEvent("john_doe", "Action.Click", "User clicked save button");

                // 3. Emit Auto log
                appLogger.LogAuto("NightlySyncJob", "Automated sync started");

                // 4. Emit General App log
                appLogger.LogInformation("General system informative message");
            }

            // Verify files exist and contain expected messages
            var specialFiles = Directory.GetFiles(specialDir, "special-*.log");
            var userFiles = Directory.GetFiles(userDir, "user-events-*.log");
            var autoFiles = Directory.GetFiles(autoDir, "auto-*.log");
            var appFiles = Directory.GetFiles(appDir, "app-*.log");

            Assert.Single(specialFiles);
            Assert.Single(userFiles);
            Assert.Single(autoFiles);
            Assert.Single(appFiles);

            var specialContent = File.ReadAllText(specialFiles[0]);
            var userContent = File.ReadAllText(userFiles[0]);
            var autoContent = File.ReadAllText(autoFiles[0]);
            var appContent = File.ReadAllText(appFiles[0]);

            Assert.Contains("Test Special Security Incident", specialContent);
            Assert.DoesNotContain("NightlySyncJob", specialContent);

            Assert.Contains("User clicked save button", userContent);
            Assert.DoesNotContain("NightlySyncJob", userContent);

            Assert.Contains("Automated sync started", autoContent);
            Assert.DoesNotContain("User clicked save button", autoContent);

            Assert.Contains("General system informative message", appContent);
            Assert.Contains("Test Special Security Incident", appContent);
            Assert.Contains("User clicked save button", appContent);
            Assert.Contains("Automated sync started", appContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void AddCustomSerilog_OnWebApplicationBuilder_ConfiguresLoggerSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AutoVerify_BuilderTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
            builder.Configuration["Serilog:LogDirectory"] = tempDir;
            builder.AddCustomSerilog();

            var app = builder.Build();
            var logger = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<ILogger<SerilogSeparationTests>>(app.Services);

            logger.LogSpecial(LogLevel.Warning, "Integration Test Special Incident");
            logger.LogUserEvent("admin_tester", "Test.Action", "Integration Test User Action");
            logger.LogAuto("TestWorkerJob", "Integration Test Auto Worker Action");

            Log.CloseAndFlush();

            var specialFiles = Directory.GetFiles(Path.Combine(tempDir, "special"), "special-*.log");
            var userFiles = Directory.GetFiles(Path.Combine(tempDir, "user-events"), "user-events-*.log");
            var autoFiles = Directory.GetFiles(Path.Combine(tempDir, "auto"), "auto-*.log");

            Assert.Single(specialFiles);
            Assert.Single(userFiles);
            Assert.Single(autoFiles);

            Assert.Contains("Integration Test Special Incident", File.ReadAllText(specialFiles[0]));
            Assert.Contains("Integration Test User Action", File.ReadAllText(userFiles[0]));
            Assert.Contains("Integration Test Auto Worker Action", File.ReadAllText(autoFiles[0]));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
