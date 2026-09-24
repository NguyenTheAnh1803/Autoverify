using AutoVerify.Api.Controllers;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class OverkillPipelineModelMappingTests
{
    [Fact]
    public async Task CreateConfig_IgnoresRequestedModelAndUsesConnectionModel()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AutoVerifyDbContext(options);
        var connection = new DatabaseConnectionEntity
        {
            Id = Guid.NewGuid(), Name = "C1", Engine = "MariaDB", MachineCode = "M1", LineCode = "L1",
            ModelCode = "D992", Host = "localhost", Port = 3306, DatabaseName = "db", Username = "user",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.DatabaseConnections.Add(connection);
        await db.SaveChangesAsync();

        var controller = new OverkillPipelineController(db, null!, null!);
        var request = new CreateOverkillPipelineConfigRequest(
            Name: "P1",
            DatabaseConnectionId: connection.Id,
            ModelCode: "ILLEGAL_OVERRIDE");

        var action = await controller.CreateConfig(request, default);

        Assert.IsType<CreatedResult>(action);
        Assert.Equal("D992", (await db.OverkillPipelineConfigs.SingleAsync()).ModelCode);
    }
}
