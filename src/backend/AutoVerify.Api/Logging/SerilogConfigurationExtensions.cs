using AutoVerify.Api.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace AutoVerify.Api.Logging;

public static class SerilogConfigurationExtensions
{
    private const string DefaultOutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    private const string DetailedOutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    public static WebApplicationBuilder AddCustomSerilog(this WebApplicationBuilder builder)
    {
        var baseDir = builder.Environment.ContentRootPath;
        var configuredLogDir = builder.Configuration["Serilog:LogDirectory"] ?? "logs";
        var logDirectory = Path.IsPathRooted(configuredLogDir)
            ? configuredLogDir
            : Path.Combine(baseDir, configuredLogDir);

        Directory.CreateDirectory(Path.Combine(logDirectory, "special"));
        Directory.CreateDirectory(Path.Combine(logDirectory, "user-events"));
        Directory.CreateDirectory(Path.Combine(logDirectory, "auto"));
        Directory.CreateDirectory(Path.Combine(logDirectory, "app"));

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Information)
                .MinimumLevel.Override("Pomelo.EntityFrameworkCore.MySql", LogEventLevel.Warning)
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "AutoVerify")
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                
                // 1. Special Logs: Audit, Security, Critical Exceptions, DB migrations
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(IsSpecialLog)
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, "special", "special-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 90,
                        fileSizeLimitBytes: 100 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: true,
                        outputTemplate: DetailedOutputTemplate))

                // 2. User Event Logs: HTTP Requests, Controllers, User Actions
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(IsUserEventLog)
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, "user-events", "user-events-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 60,
                        fileSizeLimitBytes: 100 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: true,
                        outputTemplate: DetailedOutputTemplate))

                // 3. Automated System Logs: BackgroundServices, Schedulers, Crawlers, Automated Jobs
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(IsAutoLog)
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, "auto", "auto-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 60,
                        fileSizeLimitBytes: 100 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: true,
                        outputTemplate: DetailedOutputTemplate))

                // 4. General Application Log
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "app", "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: 100 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: DefaultOutputTemplate);
        });

        return builder;
    }

    public static WebApplication UseCustomSerilogRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set(LoggingConstants.LogTypeProperty, LoggingConstants.LogTypes.UserEvent);
                diagnosticContext.Set(LoggingConstants.ClientIpProperty, httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());

                var user = httpContext.User?.Identity?.Name;
                if (!string.IsNullOrWhiteSpace(user))
                {
                    diagnosticContext.Set(LoggingConstants.UserIdProperty, user);
                }
            };
        });

        return app;
    }

    public static bool IsSpecialLog(LogEvent evt)
    {
        // 1. Explicitly tagged as Special
        if (evt.Properties.TryGetValue(LoggingConstants.LogTypeProperty, out var typeVal))
        {
            var typeStr = typeVal.ToString().Trim('"');
            if (string.Equals(typeStr, LoggingConstants.LogTypes.Special, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Contains audit attributes
        if (evt.Properties.ContainsKey(LoggingConstants.AuditActionProperty) ||
            evt.Properties.ContainsKey("AuditEvent"))
        {
            return true;
        }

        // 3. High severity errors (Error, Fatal)
        if (evt.Level >= LogEventLevel.Error)
        {
            return true;
        }

        // 4. Critical infrastructure contexts
        if (evt.Properties.TryGetValue("SourceContext", out var sourceVal))
        {
            var source = sourceVal.ToString();
            if (source.Contains("DatabaseSeeder") || source.Contains("DataProtection"))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUserEventLog(LogEvent evt)
    {
        // 1. Explicitly tagged as UserEvent
        if (evt.Properties.TryGetValue(LoggingConstants.LogTypeProperty, out var typeVal))
        {
            var typeStr = typeVal.ToString().Trim('"');
            if (string.Equals(typeStr, LoggingConstants.LogTypes.UserEvent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. HTTP Request logs generated by middleware
        if (evt.Properties.ContainsKey("RequestPath") || evt.Properties.ContainsKey("RequestMethod"))
        {
            return true;
        }

        // 3. User identity or user action properties present
        if (evt.Properties.ContainsKey(LoggingConstants.UserIdProperty) ||
            evt.Properties.ContainsKey("UserAction"))
        {
            return true;
        }

        // 4. API Controller logs (unless explicitly marked as Auto)
        if (evt.Properties.TryGetValue("SourceContext", out var sourceVal))
        {
            var source = sourceVal.ToString();
            if (source.Contains("Controllers") && !IsExplicitAuto(evt))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAutoLog(LogEvent evt)
    {
        // 1. Explicitly tagged as Auto
        if (evt.Properties.TryGetValue(LoggingConstants.LogTypeProperty, out var typeVal))
        {
            var typeStr = typeVal.ToString().Trim('"');
            if (string.Equals(typeStr, LoggingConstants.LogTypes.Auto, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Contains automated job properties
        if (evt.Properties.ContainsKey(LoggingConstants.JobNameProperty) ||
            evt.Properties.ContainsKey("ScheduledJob"))
        {
            return true;
        }

        // 3. Emitted from BackgroundServices, Schedulers, Crawlers, Cleanup Services
        if (evt.Properties.TryGetValue("SourceContext", out var sourceVal))
        {
            var source = sourceVal.ToString();
            if (source.Contains("HostedService") ||
                source.Contains("Scheduler") ||
                source.Contains("Cleanup") ||
                source.Contains("Crawler") ||
                source.Contains("IngestionService"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExplicitAuto(LogEvent evt)
    {
        if (evt.Properties.TryGetValue(LoggingConstants.LogTypeProperty, out var typeVal))
        {
            return string.Equals(typeVal.ToString().Trim('"'), LoggingConstants.LogTypes.Auto, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

