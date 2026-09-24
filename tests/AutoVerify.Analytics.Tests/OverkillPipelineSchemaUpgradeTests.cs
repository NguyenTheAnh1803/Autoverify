using Microsoft.EntityFrameworkCore;
using AutoVerify.Api.Persistence;
using AutoVerify.Api.Persistence.Entities;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class OverkillPipelineSchemaUpgradeTests
{
    [Fact]
    public void OverkillPipelineConfigs_And_Executions_MappedProperties_ExistInModel()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var configEntityType = db.Model.FindEntityType(typeof(OverkillPipelineConfigEntity));
        Assert.NotNull(configEntityType);
        Assert.NotNull(configEntityType.FindProperty(nameof(OverkillPipelineConfigEntity.DefectBypassRuleJson)));
        Assert.NotNull(configEntityType.FindProperty(nameof(OverkillPipelineConfigEntity.LastRunDefectsBypassedCount)));
        Assert.NotNull(configEntityType.FindProperty(nameof(OverkillPipelineConfigEntity.IncludeOkProducts)));

        var config = new OverkillPipelineConfigEntity();
        Assert.True(config.IncludeOkProducts);

        var execEntityType = db.Model.FindEntityType(typeof(OverkillPipelineExecutionEntity));
        Assert.NotNull(execEntityType);
        Assert.NotNull(execEntityType.FindProperty(nameof(OverkillPipelineExecutionEntity.DefectsScanned)));
        Assert.NotNull(execEntityType.FindProperty(nameof(OverkillPipelineExecutionEntity.DefectsBypassed)));
        Assert.NotNull(execEntityType.FindProperty(nameof(OverkillPipelineExecutionEntity.BypassRuleSummary)));
    }

    [Fact]
    public void OverkillPipelineExecution_Defaults_AreSafe()
    {
        var exec = new OverkillPipelineExecutionEntity();
        Assert.Equal(0, exec.DefectsScanned);
        Assert.Equal(0, exec.DefectsBypassed);
        Assert.Null(exec.BypassRuleSummary);
    }

    [Fact]
    public void DefectRecord_ModelCode_And_ConnectionId_MappedProperties_ExistInModel()
    {
        var options = new DbContextOptionsBuilder<AutoVerifyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AutoVerifyDbContext(options);

        var defectEntityType = db.Model.FindEntityType(typeof(DefectRecordEntity));
        Assert.NotNull(defectEntityType);
        Assert.NotNull(defectEntityType.FindProperty(nameof(DefectRecordEntity.ModelCode)));
        Assert.NotNull(defectEntityType.FindProperty(nameof(DefectRecordEntity.ConnectionId)));
        Assert.NotNull(defectEntityType.FindProperty(nameof(DefectRecordEntity.MachineCode)));
        Assert.NotNull(defectEntityType.FindProperty(nameof(DefectRecordEntity.LineCode)));

        var defect = new DefectRecordEntity();
        Assert.Equal("UNASSIGNED", defect.ModelCode);
        Assert.Null(defect.ConnectionId);
        Assert.Equal("MACHINE-A", defect.MachineCode);
        Assert.Equal("LINE-1", defect.LineCode);
    }
}
