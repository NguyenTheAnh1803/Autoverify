using AutoVerify.Api.Analytics;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class ScriptConditionEngineTests
{
    private const string SampleAoiScript = """
        if( G010_VpID != 1):
        	return False

        if( G008_LightID != 1 ):
        	return False
        	
        if( G009_RoiID != 1 ):
        	return False
        	
        if(G131_Real_DicToLeft>400 and G132_Real_DicToRigt>400 and G133_Real_DicToTop>400 and G134_Real_DicToBot>400 and G063_OrgGrayMin> 120):
        	return False

        IP_Cond = V003_VP_IP == 200131 or V003_VP_IP == 200231
        if(V003_VP_IP == 1):
        	IP_Cond = 1

        VP_Info1 = (G008_LightID ==1) and (G009_RoiID == 1)

        Cond1 = ( G020_PxlArea > 90 and (G017_PxlWidth > 20 or G018_PxlHeight > 20))
        Cond2 = ( G038_PrcGrayMin <  45 )
        #Improve DIC CRACK line  240807
        AVG_Line = G018_PxlHeight /G017_PxlWidth
        if(G018_PxlHeight <  10):
        	Cond2 = (G038_PrcGrayMin < 220)
        if(Cond2 ==1 and AVG_Line < 0.8):
        	Cond1 = (G020_PxlArea > 20 )
        	
        if(G017_PxlWidth > 80):
        	Cond2 = (G038_PrcGrayMin <  150)

        DL_Cond = MLClass01 > 90

        return IP_Cond and VP_Info1 and Cond1 and Cond2 and DL_Cond
        """;

    [Fact]
    public void Parse_ExtractsAllGuardsAndConditions()
    {
        var parsed = ScriptConditionEngine.Parse(SampleAoiScript);

        Assert.NotNull(parsed);
        Assert.Equal(4, parsed.Guards.Count);
        Assert.True(parsed.Conditions.Count >= 5);
        Assert.NotNull(parsed.FinalReturn);
        Assert.Contains("IP_Cond", parsed.FinalReturn.ConditionNames);
        Assert.Contains("Cond1", parsed.FinalReturn.ConditionNames);
        Assert.Contains("Cond2", parsed.FinalReturn.ConditionNames);
        Assert.Contains("DL_Cond", parsed.FinalReturn.ConditionNames);
    }

    [Fact]
    public void EvaluateImperative_GuardFailsWhenVpIdNotOne()
    {
        var parsed = ScriptConditionEngine.Parse(SampleAoiScript);
        var observations = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
        {
            (1, new Dictionary<string, double>
            {
                ["G010_VpID"] = 2.0, // Fails guard (expected 1)
                ["G008_LightID"] = 1.0,
                ["G009_RoiID"] = 1.0,
                ["V003_VP_IP"] = 1.0,
                ["G020_PxlArea"] = 100.0,
                ["G017_PxlWidth"] = 50.0,
                ["G018_PxlHeight"] = 50.0,
                ["G038_PrcGrayMin"] = 30.0,
                ["MLClass01"] = 95.0
            }, null)
        };

        var result = ScriptConditionEngine.EvaluateImperative(SampleAoiScript, parsed, observations);

        Assert.Equal(1, result.TotalObservations);
        Assert.Equal(1, result.GuardFailedCount);
        Assert.Equal(0, result.FinalTrueCount);
        Assert.False(result.Evaluations[0].FinalResult);
        Assert.True(result.Evaluations[0].GuardFailed);
    }

    [Fact]
    public void EvaluateImperative_PassesWhenAllConditionsMet()
    {
        var parsed = ScriptConditionEngine.Parse(SampleAoiScript);
        var observations = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
        {
            (1, new Dictionary<string, double>
            {
                ["G010_VpID"] = 1.0,
                ["G008_LightID"] = 1.0,
                ["G009_RoiID"] = 1.0,
                ["G131_Real_DicToLeft"] = 100.0, // Guard passes (not all > 400)
                ["G132_Real_DicToRigt"] = 100.0,
                ["G133_Real_DicToTop"] = 100.0,
                ["G134_Real_DicToBot"] = 100.0,
                ["G063_OrgGrayMin"] = 100.0,
                ["V003_VP_IP"] = 1.0, // Sets IP_Cond = 1
                ["G020_PxlArea"] = 100.0, // > 90
                ["G017_PxlWidth"] = 50.0, // > 20
                ["G018_PxlHeight"] = 50.0, // > 20
                ["G038_PrcGrayMin"] = 30.0, // < 45
                ["MLClass01"] = 95.0 // > 90
            }, null)
        };

        var result = ScriptConditionEngine.EvaluateImperative(SampleAoiScript, parsed, observations);

        Assert.Equal(1, result.TotalObservations);
        Assert.Equal(0, result.GuardFailedCount);
        Assert.Equal(1, result.FinalTrueCount);
        Assert.True(result.Evaluations[0].FinalResult);
        Assert.True(result.Evaluations[0].ConditionResults["IP_Cond"]);
        Assert.True(result.Evaluations[0].ConditionResults["VP_Info1"]);
        Assert.True(result.Evaluations[0].ConditionResults["Cond1"]);
        Assert.True(result.Evaluations[0].ConditionResults["Cond2"]);
        Assert.True(result.Evaluations[0].ConditionResults["DL_Cond"]);
        Assert.True(result.Evaluations[0].FinalResult);
        Assert.True(result.Evaluations[0].ConditionResults["IP_Cond"]);
        Assert.True(result.Evaluations[0].ConditionResults["Cond1"]);
        Assert.True(result.Evaluations[0].ConditionResults["Cond2"]);
        Assert.True(result.Evaluations[0].ConditionResults["DL_Cond"]);
    }

    [Fact]
    public void EvaluateImperative_HandlesConditionalReassignmentCorrectly()
    {
        var parsed = ScriptConditionEngine.Parse(SampleAoiScript);

        // Case: G018_PxlHeight = 5 (< 10), so Cond2 should allow GrayMin < 220
        // GrayMin is 100 (which is > 45, but < 220, so only passes if conditional reassignment works!)
        var observations = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
        {
            (1, new Dictionary<string, double>
            {
                ["G010_VpID"] = 1.0,
                ["G008_LightID"] = 1.0,
                ["G009_RoiID"] = 1.0,
                ["G131_Real_DicToLeft"] = 100.0,
                ["G132_Real_DicToRigt"] = 100.0,
                ["G133_Real_DicToTop"] = 100.0,
                ["G134_Real_DicToBot"] = 100.0,
                ["G063_OrgGrayMin"] = 100.0,
                ["V003_VP_IP"] = 1.0,
                ["G020_PxlArea"] = 100.0,
                ["G017_PxlWidth"] = 50.0,
                ["G018_PxlHeight"] = 5.0, // < 10 -> triggers Cond2 = (GrayMin < 220)
                ["G038_PrcGrayMin"] = 100.0, // > 45 but < 220
                ["MLClass01"] = 95.0
            }, null)
        };

        var result = ScriptConditionEngine.EvaluateImperative(SampleAoiScript, parsed, observations);

        // Cond2 should be TRUE because G018_PxlHeight < 10 raised threshold to 220
        Assert.True(result.Evaluations[0].ConditionResults["Cond2"]);
        Assert.True(result.Evaluations[0].FinalResult);
    }

    [Fact]
    public void EvaluateImperative_LightFailScript_EvaluatesCorrectly()
    {
        var lightFailScript = """
            #MCL Block1
            MCL_Position1 = (G010_VpID==1) and (G008_LightID ==1) and (G009_RoiID == 24 or G009_RoiID == 25)
            MCL_Light1 = (G062_OrgGrayMax < 40 and G064_OrgGrayStdDev < 5)
            MCL_Block1 = MCL_Position1 and MCL_Light1

            #MCL Block2
            MCL_Position2 = (G010_VpID==1) and (G008_LightID ==2) and (G009_RoiID == 3 or G009_RoiID == 4)
            MCL_Light2 = (G062_OrgGrayMax < 30 and G064_OrgGrayStdDev < 5)
            MCL_Block2 = MCL_Position2 and MCL_Light2

            return MCL_Block1 or MCL_Block2
            """;

        var parsed = ScriptConditionEngine.Parse(lightFailScript);
        Assert.NotNull(parsed);

        // Case 1: Meets MCL_Block1 (RoiID = 24, GrayMax = 35 < 40, StdDev = 4 < 5)
        var obsTrue = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
        {
            (1, new Dictionary<string, double>
            {
                ["G010_VpID"] = 1.0,
                ["G008_LightID"] = 1.0,
                ["G009_RoiID"] = 24.0, // matches 24 or 25
                ["G062_OrgGrayMax"] = 35.0,
                ["G064_OrgGrayStdDev"] = 4.0
            }, null)
        };

        var resTrue = ScriptConditionEngine.EvaluateImperative(lightFailScript, parsed, obsTrue);
        Assert.True(resTrue.Evaluations[0].FinalResult);

        // Case 1b: RoiID = 25 (second branch of "or")
        var obsTrue2 = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
        {
            (1, new Dictionary<string, double>
            {
                ["G010_VpID"] = 1.0,
                ["G008_LightID"] = 1.0,
                ["G009_RoiID"] = 25.0, // matches 25
                ["G062_OrgGrayMax"] = 35.0,
                ["G064_OrgGrayStdDev"] = 4.0
            }, null)
        };

        var resTrue2 = ScriptConditionEngine.EvaluateImperative(lightFailScript, parsed, obsTrue2);
        Assert.True(resTrue2.Evaluations[0].FinalResult);

        // Case 2: GrayMax too high (GrayMax = 50 >= 40) -> should be False
        var obsFalse = new List<(int RowIndex, Dictionary<string, double> Features, ObservationReference? Observation)>
        {
            (1, new Dictionary<string, double>
            {
                ["G010_VpID"] = 1.0,
                ["G008_LightID"] = 1.0,
                ["G009_RoiID"] = 24.0,
                ["G062_OrgGrayMax"] = 50.0, // Fails < 40
                ["G064_OrgGrayStdDev"] = 4.0
            }, null)
        };

        var resFalse = ScriptConditionEngine.EvaluateImperative(lightFailScript, parsed, obsFalse);
        Assert.False(resFalse.Evaluations[0].FinalResult);
    }
}
