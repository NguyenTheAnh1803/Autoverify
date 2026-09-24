using AutoVerify.Api.Analytics.Contracts;
using AutoVerify.Api.Controllers;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class ClassifierRuleResolutionTests
{
    private static AutoVerifyDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AutoVerifyDbContext(options);
    }

    [Fact]
    public async Task ResolveRule_SelectsMostSpecificRule()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;

        // Rule 1: Product-level generic rule
        db.MachineClassifierRules.Add(new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "GENERIC-PROD",
            RuleName = "Product Generic Rule",
            Version = "v1.0",
            ProductKey = "PROD-A",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        // Rule 2: Line-level rule
        db.MachineClassifierRules.Add(new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "LINE-SPECIFIC",
            RuleName = "Line 1 Rule",
            Version = "v1.0",
            ProductKey = "PROD-A",
            LineCode = "LINE-01",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        // Rule 3: Station-level highly specific rule
        db.MachineClassifierRules.Add(new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "STATION-SPECIFIC",
            RuleName = "Station S01 Rule",
            Version = "v2.0",
            ProductKey = "PROD-A",
            LineCode = "LINE-01",
            MachineCode = "MCH-01",
            StationCode = "STN-01",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var controller = new ClassifierRulesController(db, NullLogger<ClassifierRulesController>.Instance);
        var response = await controller.ResolveRule(new ResolveClassifierRuleRequest
        {
            ProductKey = "PROD-A",
            LineCode = "LINE-01",
            MachineCode = "MCH-01",
            StationCode = "STN-01"
        });

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var resolved = Assert.IsType<ResolveClassifierRuleResponse>(okResult.Value);
        Assert.NotNull(resolved.Rule);
        Assert.Equal("STATION-SPECIFIC", resolved.Rule.RuleCode);
        Assert.Equal("v2.0", resolved.Rule.Version);
        Assert.True(resolved.SpecificityScore >= 28); // Station(16) + Machine(8) + Line(4) + Product(2)
    }

    [Fact]
    public async Task ResolveRule_IgnoresInactiveOrExpiredRules()
    {
        using var db = CreateInMemoryDbContext();
        var now = DateTimeOffset.UtcNow;

        // Inactive rule
        db.MachineClassifierRules.Add(new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "INACTIVE-RULE",
            RuleName = "Inactive Station Rule",
            Version = "v1.0",
            StationCode = "STN-01",
            IsActive = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        // Expired rule
        db.MachineClassifierRules.Add(new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "EXPIRED-RULE",
            RuleName = "Expired Station Rule",
            Version = "v1.0",
            StationCode = "STN-01",
            IsActive = true,
            EffectiveToUtc = now.AddDays(-1),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        // Valid fallback rule
        db.MachineClassifierRules.Add(new MachineClassifierRuleEntity
        {
            Id = Guid.NewGuid(),
            RuleCode = "FALLBACK-RULE",
            RuleName = "Active Global Rule",
            Version = "v1.0",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var controller = new ClassifierRulesController(db, NullLogger<ClassifierRulesController>.Instance);
        var response = await controller.ResolveRule(new ResolveClassifierRuleRequest
        {
            StationCode = "STN-01"
        });

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var resolved = Assert.IsType<ResolveClassifierRuleResponse>(okResult.Value);
        Assert.NotNull(resolved.Rule);
        Assert.Equal("FALLBACK-RULE", resolved.Rule.RuleCode);
    }
}
