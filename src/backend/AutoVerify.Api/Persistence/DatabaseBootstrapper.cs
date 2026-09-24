using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace AutoVerify.Api.Persistence;

public sealed class DatabaseBootstrapper(
    IWebHostEnvironment environment,
    IOptions<DatabaseBootstrapOptions> options,
    ILogger<DatabaseBootstrapper> logger)
{
    private readonly DatabaseBootstrapOptions _options = options.Value;

    public async Task<bool> ExecuteIfRequiredAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Database SQL bootstrap is disabled.");
            return false;
        }

        var scriptPath = ResolveScriptPath(_options.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            var message = $"Database bootstrap SQL was not found at '{scriptPath}'.";
            if (_options.FailOnError) throw new FileNotFoundException(message, scriptPath);
            logger.LogWarning("{Message}", message);
            return false;
        }

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (_options.RunOnlyWhenDatabaseIsEmpty && !await IsDatabaseEmptyAsync(connection, cancellationToken))
        {
            logger.LogInformation("Database already contains tables; SQL bootstrap skipped.");
            return false;
        }

        var script = await File.ReadAllTextAsync(scriptPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new InvalidOperationException($"Database bootstrap SQL '{scriptPath}' is empty.");
        }

        logger.LogInformation("Executing database bootstrap SQL from {ScriptPath}...", scriptPath);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = script;
            command.CommandTimeout = Math.Max(30, _options.CommandTimeoutSeconds);
            await command.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Database bootstrap SQL completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database bootstrap SQL failed at {ScriptPath}.", scriptPath);
            if (_options.FailOnError) throw;
            return false;
        }
    }

    public async Task<string> ExportCurrentModelAsync(
        AutoVerifyDbContext db,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveScriptPath(string.IsNullOrWhiteSpace(outputPath) ? _options.ScriptPath : outputPath);
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var script = db.Database.GenerateCreateScript();
        var header = $"-- AutoVerify MariaDB bootstrap schema\n-- Generated from EF Core model at {DateTimeOffset.UtcNow:O}\n-- Execute only against a new/empty database.\n\n";
        await File.WriteAllTextAsync(targetPath, header + script, cancellationToken);
        logger.LogInformation("Exported EF Core database bootstrap SQL to {ScriptPath}.", targetPath);
        return targetPath;
    }

    private string ResolveScriptPath(string? configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? "Database/bootstrap.sql" : configuredPath.Trim();
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        var contentRootPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
        if (File.Exists(contentRootPath) || Directory.Exists(Path.GetDirectoryName(contentRootPath))) return contentRootPath;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static async Task<bool> IsDatabaseEmptyAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE';";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count == 0;
    }
}
