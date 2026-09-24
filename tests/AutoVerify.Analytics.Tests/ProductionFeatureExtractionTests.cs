using AutoVerify.Api.Services;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public sealed class ProductionFeatureExtractionTests
{
    [Fact]
    public void ExtractNumericFeaturesFromJson_GroupsNamedValues()
    {
        const string json = """
        {
          "feature": [
            { "name": "Area", "value": 12.4 },
            { "name": "Contrast", "value": 0.88 }
          ],
          "customFeature": { "Area": 13.1, "Roughness": 2.5 }
        }
        """;

        var features = DefectStreamParser.ExtractNumericFeaturesFromJson(json);

        Assert.Contains(features, f => f.Name.Equals("Area", StringComparison.OrdinalIgnoreCase) && Math.Abs(f.Value - 12.4) < 0.0001);
        Assert.Contains(features, f => f.Name.Equals("Contrast", StringComparison.OrdinalIgnoreCase) && Math.Abs(f.Value - 0.88) < 0.0001);
        Assert.Contains(features, f => f.Name.Contains("Roughness", StringComparison.OrdinalIgnoreCase) && Math.Abs(f.Value - 2.5) < 0.0001);
    }

    [Fact]
    public void ExtractNumericFeaturesFromJson_ReturnsEmptyForInvalidJson()
    {
        Assert.Empty(DefectStreamParser.ExtractNumericFeaturesFromJson("{not-json"));
        Assert.Empty(DefectStreamParser.ExtractNumericFeaturesFromJson(null));
    }
}
