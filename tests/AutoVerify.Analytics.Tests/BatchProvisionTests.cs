using AutoVerify.Api;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using AutoVerify.Api.Services;
using AutoVerify.Api.Verification;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class BatchProvisionTests
{
    [Fact]
    public void ParseLineFromPath_CsvWithModel_ParsesModelAndShiftedConnectionFields()
    {
        var result = AutoVerifyStore.ParseLineFromPath(
            "LINE-405, CP_AOI, D967, 10.20.1.45, aoi_405, root, secret",
            "127.0.0.1",
            "ProductDB",
            "default-user",
            "default-pass");

        Assert.Equal("LINE-405", result.LineCode);
        Assert.Equal("CP_AOI", result.MachineCode);
        Assert.Equal("D967", result.ModelCode);
        Assert.Equal("10.20.1.45", result.Host);
        Assert.Equal("aoi_405", result.DatabaseName);
        Assert.Equal("root", result.Username);
        Assert.Equal("secret", result.Password);
    }

    [Fact]
    public async Task BatchProvisionAsync_RangeGenerator_CreatesEightyLinesAndPipelinesSimultaneously()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var req = new BatchProvisionRequest(
            Engine: "MariaDB",
            Host: "10.20.1.{n}",
            Port: 3306,
            DatabaseName: "aoi_line_{n}",
            Username: "root",
            Password: "password123",
            LinePrefix: "LINE-",
            LineStart: 1,
            LineEnd: 80,
            LinePad: 2,
            MachinePrefix: "AOI-",
            MachineStart: 1,
            MachineEnd: 80,
            MachinePad: 2,
            CreatePipelines: true,
            ScheduleIntervalMinutes: 60,
            LookbackHours: 6,
            ProductTableName: "Product",
            IncludeOkProducts: true
        );

        var result = await store.BatchProvisionAsync(req);

        Assert.True(result.Success);
        Assert.Equal(80, result.CreatedConnectionsCount);
        Assert.Equal(80, result.CreatedPipelinesCount);
        Assert.Equal(80, result.TotalItemsProcessed);

        // Verify that 80 connections and 80 pipelines actually exist in database
        var totalConns = await db.DatabaseConnections.CountAsync();
        var totalPipes = await db.OverkillPipelineConfigs.CountAsync();

        Assert.Equal(80, totalConns);
        Assert.Equal(80, totalPipes);

        // Verify line 1 and line 80
        var line1Conn = await db.DatabaseConnections.FirstOrDefaultAsync(c => c.LineCode == "LINE-01");
        Assert.NotNull(line1Conn);
        Assert.Equal("AOI-01", line1Conn.MachineCode);
        Assert.Equal("10.20.1.1", line1Conn.Host);
        Assert.Equal("aoi_line_1", line1Conn.DatabaseName);

        var line80Conn = await db.DatabaseConnections.FirstOrDefaultAsync(c => c.LineCode == "LINE-80");
        Assert.NotNull(line80Conn);
        Assert.Equal("AOI-80", line80Conn.MachineCode);
        Assert.Equal("10.20.1.80", line80Conn.Host);
        Assert.Equal("aoi_line_80", line80Conn.DatabaseName);

        var line80Pipe = await db.OverkillPipelineConfigs.FirstOrDefaultAsync(p => p.LineCode == "LINE-80");
        Assert.NotNull(line80Pipe);
        Assert.Equal(line80Conn.Id, line80Pipe.DatabaseConnectionId);
        Assert.True(line80Pipe.IncludeOkProducts);
    }

    [Fact]
    public async Task BatchProvisionAsync_CustomCsvLines_CreatesSpecifiedItems()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var customLines = new List<BatchProvisionLineItem>
        {
            new("LINE-A", "AOI-M1", "192.168.1.10", "db_line_a"),
            new("LINE-B", "AOI-M2", "192.168.1.11", "db_line_b"),
            new("LINE-C", "AOI-M3", "192.168.1.12", "db_line_c"),
        };

        var req = new BatchProvisionRequest(
            Host: "192.168.1.100",
            Lines: customLines,
            CreatePipelines: true
        );

        var result = await store.BatchProvisionAsync(req);

        Assert.True(result.Success);
        Assert.Equal(3, result.CreatedConnectionsCount);
        Assert.Equal(3, result.CreatedPipelinesCount);

        var connA = await db.DatabaseConnections.FirstOrDefaultAsync(c => c.LineCode == "LINE-A");
        Assert.NotNull(connA);
        Assert.Equal("AOI-M1", connA.MachineCode);
        Assert.Equal("192.168.1.10", connA.Host);
    }

    [Fact]
    public async Task BatchProvisionAsync_MultiPathInput_AutoExtractsLinesAndMachines()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        var paths = new List<string>
        {
            @"\\10.20.1.10\AOI_Export\LINE-01",
            @"\\10.20.1.20\AOI_Export\LINE-02",
            @"D:\AOI_Data\LINE-03\AOI-03",
            @"10.20.1.40/aoi_line_04",
            @"Line 5",
        };

        var req = new BatchProvisionRequest(
            Host: "127.0.0.1",
            DatabaseName: "ProductDB",
            Paths: paths,
            CreatePipelines: true
        );

        var result = await store.BatchProvisionAsync(req);

        Assert.True(result.Success);
        Assert.Equal(5, result.CreatedConnectionsCount);
        Assert.Equal(5, result.CreatedPipelinesCount);

        var c1 = await db.DatabaseConnections.FirstOrDefaultAsync(c => c.LineCode == "LINE-01");
        Assert.NotNull(c1);
        Assert.Equal("10.20.1.10", c1.Host);

        var c3 = await db.DatabaseConnections.FirstOrDefaultAsync(c => c.LineCode == "LINE-03");
        Assert.NotNull(c3);
        Assert.Equal("AOI-03", c3.MachineCode);

        var c5 = await db.DatabaseConnections.FirstOrDefaultAsync(c => c.LineCode == "LINE-05");
        Assert.NotNull(c5);
    }

    [Fact]
    public async Task BatchGeneratePipelinesForConnectionsAsync_CustomProductParameters_SetsAndUpdatesAllFields()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var conn1 = new DatabaseConnectionEntity
        {
            Id = Guid.NewGuid(),
            LineCode = "LINE-01",
            MachineCode = "AOI-01",
            Name = "LINE-01 (AOI-01)",
            Engine = "MariaDB",
            Host = "10.0.0.1",
            Port = 3306,
            DatabaseName = "aoi_db",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.DatabaseConnections.Add(conn1);
        await db.SaveChangesAsync();

        var store = new AutoVerifyStore(
            db,
            null!,
            new AnomalyVerificationService(),
            new QueryEngineService(),
            new ImportValidationService());

        // 1. Initial Generation with custom product parameters
        var req1 = new BatchGeneratePipelinesForConnectionsRequest(
            ConnectionIds: [conn1.Id],
            ScheduleIntervalMinutes: 45,
            LookbackHours: 8,
            BatchSize: 2500,
            ProductTableName: "tbl_product_master",
            IdColumnName: "serial_id",
            FkIdColumnName: "fk_serial_id",
            JudgeColumnName: "judge_result",
            JudgeFilterValues: ["NG", "REJECT", "FAIL"],
            DateTimeInspectionColumnName: "inspected_datetime",
            ZoneColumnName: "board_zone",
            DefectNameColumnName: "ng_type",
            IncludeOkProducts: false,
            DefectTables: [new DefectTableMapping("tbl_defects", "fk_serial_id")]
        );

        var res1 = await store.BatchGeneratePipelinesForConnectionsAsync(req1);
        Assert.True(res1.Success);
        Assert.Equal(1, res1.CreatedPipelinesCount);

        var pipe = await db.OverkillPipelineConfigs.FirstOrDefaultAsync(p => p.DatabaseConnectionId == conn1.Id);
        Assert.NotNull(pipe);
        Assert.Equal("tbl_product_master", pipe.ProductTableName);
        Assert.Equal("serial_id", pipe.IdColumnName);
        Assert.Equal("fk_serial_id", pipe.FkIdColumnName);
        Assert.Equal("judge_result", pipe.JudgeColumnName);
        Assert.Equal("inspected_datetime", pipe.DateTimeInspectionColumnName);
        Assert.Equal("board_zone", pipe.ZoneColumnName);
        Assert.Equal("ng_type", pipe.DefectNameColumnName);
        Assert.Equal(2500, pipe.BatchSize);
        Assert.Equal(8, pipe.LookbackHours);
        Assert.False(pipe.IncludeOkProducts);
        Assert.Contains("REJECT", pipe.JudgeFilterValuesJson);

        // 2. Update with modified product parameters
        var req2 = new BatchGeneratePipelinesForConnectionsRequest(
            ConnectionIds: [conn1.Id],
            ScheduleIntervalMinutes: 30,
            LookbackHours: 12,
            BatchSize: 5000,
            ProductTableName: "UpdatedProduct",
            IdColumnName: "ID",
            FkIdColumnName: "fk_prod_id",
            JudgeColumnName: "Judge",
            JudgeFilterValues: ["N", "NG"],
            DateTimeInspectionColumnName: "datetime_inspection",
            ZoneColumnName: "zone",
            DefectNameColumnName: "defect_name",
            IncludeOkProducts: true
        );

        var res2 = await store.BatchGeneratePipelinesForConnectionsAsync(req2);
        Assert.True(res2.Success);
        Assert.Equal(0, res2.CreatedPipelinesCount);
        Assert.Equal(1, res2.UpdatedPipelinesCount);

        var updatedPipe = await db.OverkillPipelineConfigs.FirstOrDefaultAsync(p => p.DatabaseConnectionId == conn1.Id);
        Assert.NotNull(updatedPipe);
        Assert.Equal("UpdatedProduct", updatedPipe.ProductTableName);
        Assert.Equal("ID", updatedPipe.IdColumnName);
        Assert.Equal("fk_prod_id", updatedPipe.FkIdColumnName);
        Assert.Equal("Judge", updatedPipe.JudgeColumnName);
        Assert.Equal("datetime_inspection", updatedPipe.DateTimeInspectionColumnName);
        Assert.Equal("zone", updatedPipe.ZoneColumnName);
        Assert.Equal("defect_name", updatedPipe.DefectNameColumnName);
        Assert.Equal(5000, updatedPipe.BatchSize);
        Assert.Equal(12, updatedPipe.LookbackHours);
        Assert.True(updatedPipe.IncludeOkProducts);
    }
}
