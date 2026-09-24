using System.Reflection;
using Microsoft.EntityFrameworkCore;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class DefectPreviewSessionSchemaUpgradeTests
{
    [Fact]
    public void DefectPreviewSessionsCreateTableSql_ContainsAllParserRecoveryColumns()
    {
        var createSql = DatabaseSeeder.DefectPreviewSessionsCreateTableSql;

        Assert.Contains("`RepairsCount` int NOT NULL DEFAULT 0", createSql);
        Assert.Contains("`WarningMessage` text NULL", createSql);
        Assert.Contains("`DetectedFormat` varchar(64) NULL", createSql);
        Assert.Contains("`ProcessingStage` varchar(64) NULL", createSql);
    }

    [Fact]
    public void DefectPreviewSessionsUpgradeSqls_ContainsIdempotentRepairsCountAndWarningMessage()
    {
        var upgradeSqls = DatabaseSeeder.DefectPreviewSessionsUpgradeSqls;

        Assert.Contains(
            upgradeSqls,
            sql => sql.Contains("`RepairsCount`", StringComparison.Ordinal) &&
                   sql.Contains("ADD COLUMN IF NOT EXISTS", StringComparison.Ordinal) &&
                   sql.Contains("int NOT NULL DEFAULT 0", StringComparison.Ordinal)
        );

        Assert.Contains(
            upgradeSqls,
            sql => sql.Contains("`WarningMessage`", StringComparison.Ordinal) &&
                   sql.Contains("ADD COLUMN IF NOT EXISTS", StringComparison.Ordinal) &&
                   sql.Contains("text NULL", StringComparison.Ordinal)
        );

        Assert.Contains(
            upgradeSqls,
            sql => sql.Contains("`DetectedFormat`", StringComparison.Ordinal) &&
                   sql.Contains("ADD COLUMN IF NOT EXISTS", StringComparison.Ordinal)
        );

        Assert.Contains(
            upgradeSqls,
            sql => sql.Contains("`ProcessingStage`", StringComparison.Ordinal) &&
                   sql.Contains("ADD COLUMN IF NOT EXISTS", StringComparison.Ordinal)
        );

        // Verify every statement is idempotent with IF NOT EXISTS
        Assert.All(upgradeSqls, sql =>
        {
            Assert.StartsWith("ALTER TABLE `defect_preview_sessions` ADD COLUMN IF NOT EXISTS", sql, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DefectPreviewSessions_AllMappedPropertiesCoveredInDdlAndUpgrades()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);
        var entityType = db.Model.FindEntityType(typeof(DefectPreviewSessionEntity));
        Assert.NotNull(entityType);

        var createSql = DatabaseSeeder.DefectPreviewSessionsCreateTableSql;
        var upgradeSqls = DatabaseSeeder.DefectPreviewSessionsUpgradeSqls;

        foreach (var property in entityType.GetProperties())
        {
            var columnName = property.Name;
            // Primary key is defined in CREATE TABLE; other mapped properties must be in CREATE and UPGRADE DDL
            if (columnName == nameof(DefectPreviewSessionEntity.Id))
            {
                Assert.Contains($"`{columnName}`", createSql);
                continue;
            }

            Assert.True(
                createSql.Contains($"`{columnName}`", StringComparison.Ordinal),
                $"CREATE TABLE DDL is missing mapped column: {columnName}"
            );

            Assert.True(
                upgradeSqls.Any(sql => sql.Contains($"`{columnName}`", StringComparison.Ordinal)),
                $"Upgrade SQL statements missing idempotent ALTER for mapped column: {columnName}"
            );
        }
    }

    [Fact]
    public async Task EnsureOperationalTablesAsync_InMemoryDatabase_CompletesWithoutRelationalException()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        // EnsureOperationalTablesAsync must detect non-relational database and exit cleanly without throwing
        var exception = await Record.ExceptionAsync(() => DatabaseSeeder.EnsureOperationalTablesAsync(db));
        Assert.Null(exception);
    }
}
