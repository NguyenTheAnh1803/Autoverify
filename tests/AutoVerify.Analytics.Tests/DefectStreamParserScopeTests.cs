using System.Text;
using System.Text.Json;
using AutoVerify.Api.Services;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class DefectStreamParserScopeTests
{
    [Fact]
    public void ConvertJsonElementToRecord_ParsesScopeDimensionsAndObservationId()
    {
        var obsId = Guid.NewGuid();
        var json = $$"""
        {
            "observationId": "{{obsId}}",
            "defectId": "D-101",
            "defectName": "Bridge",
            "lineCode": "LINE-01",
            "machineCode": "MCH-02",
            "stationCode": "STN-03",
            "classifierRuleCode": "RULE-A",
            "classifierRuleName": "Rule Alpha",
            "classifierRuleVersion": "v1.2",
            "defectFamily": "SolderBridge",
            "defectClass": "Critical",
            "features": {
                "Area": 12.5,
                "Width": 3.2
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var record = DefectStreamParser.ConvertJsonElementToRecord(doc.RootElement, "test://stream.json");

        Assert.Equal(obsId, record.ObservationId);
        Assert.Equal("LINE-01", record.LineCode);
        Assert.Equal("MCH-02", record.MachineCode);
        Assert.Equal("STN-03", record.StationCode);
        Assert.Equal("RULE-A", record.ClassifierRuleCode);
        Assert.Equal("Rule Alpha", record.ClassifierRuleName);
        Assert.Equal("v1.2", record.ClassifierRuleVersion);
        Assert.Equal("SolderBridge", record.DefectFamily);
        Assert.Equal("Critical", record.DefectClass);

        Assert.Equal(3, record.ExtractedFeatures.Count);
        Assert.Contains(record.ExtractedFeatures, f => f.FeatureName == "Area" && f.FeatureValue == 12.5);
        Assert.Contains(record.ExtractedFeatures, f => f.FeatureName == "Width" && f.FeatureValue == 3.2);
        Assert.Contains(record.ExtractedFeatures, f => f.FeatureName == "ConfidenceScore");
        foreach (var feat in record.ExtractedFeatures)
        {
            Assert.Equal(obsId, feat.ObservationId);
        }
    }

    [Fact]
    public void ConvertJsonElementToRecord_MissingScopeFields_PreservesNullWithoutFabrication()
    {
        var json = """
        {
            "defectId": "D-102",
            "defectName": "Scratch",
            "features": {
                "Length": 5.4
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var record = DefectStreamParser.ConvertJsonElementToRecord(doc.RootElement, "test://stream.json");

        Assert.NotEqual(Guid.Empty, record.ObservationId);
        Assert.Null(record.LineCode);
        Assert.Null(record.MachineCode);
        Assert.Null(record.StationCode);
        Assert.Null(record.ClassifierRuleCode);
        Assert.Null(record.ClassifierRuleName);
        Assert.Null(record.ClassifierRuleVersion);
        Assert.Null(record.DefectFamily);
        Assert.Null(record.DefectClass);
    }
}
