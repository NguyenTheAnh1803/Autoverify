using AutoVerify.Api.Verification;
using Xunit;

namespace AutoVerify.Analytics.Tests;

public class AiVerdictMatrixTests
{
    private static AiDefectVerdict Ok => new("Done", "OK");
    private static AiDefectVerdict Ng => new("Done", "NG");
    private static AiDefectVerdict Unavailable => new("Unavailable", null);
    private static AiDefectVerdict Rejected => new("Rejected", null);

    [Fact]
    public void NgProduct_AllDefectsOk_IsOverkillConfirmed() =>
        Assert.Equal(AiFinalStatus.OverkillConfirmed, AiVerdictMatrix.Decide(true, [Ok, Ok]));

    [Fact]
    public void NgProduct_AnyDefectNg_IsConsistentNg() =>
        Assert.Equal(AiFinalStatus.ConsistentNg, AiVerdictMatrix.Decide(true, [Ok, Ng]));

    [Fact]
    public void OkProduct_AnyDefectNg_IsLeakageConfirmed() =>
        Assert.Equal(AiFinalStatus.LeakageConfirmed, AiVerdictMatrix.Decide(false, [Ok, Ng]));

    [Fact]
    public void OkProduct_AllDefectsOk_IsConsistentOk() =>
        Assert.Equal(AiFinalStatus.ConsistentOk, AiVerdictMatrix.Decide(false, [Ok]));

    [Fact]
    public void UnansweredImageIsNeverCountedAsOk()
    {
        Assert.Equal(AiFinalStatus.AiUnavailable, AiVerdictMatrix.Decide(true, [Ok, Unavailable]));
        Assert.Equal(AiFinalStatus.AiUnavailable, AiVerdictMatrix.Decide(false, [Ok, Rejected]));
        Assert.Equal(AiFinalStatus.AiUnavailable, AiVerdictMatrix.Decide(true, [Unavailable]));
    }

    [Fact]
    public void OneNgIsConclusiveEvenIfOtherImagesAreUnanswered()
    {
        Assert.Equal(AiFinalStatus.ConsistentNg, AiVerdictMatrix.Decide(true, [Ng, Unavailable]));
        Assert.Equal(AiFinalStatus.LeakageConfirmed, AiVerdictMatrix.Decide(false, [Unavailable, Ng]));
    }

    [Fact]
    public void DoneWithoutAValidVerdictIsTreatedAsUnanswered() =>
        Assert.Equal(AiFinalStatus.AiUnavailable, AiVerdictMatrix.Decide(true, [new AiDefectVerdict("Done", null)]));

    [Fact]
    public void NoImages_LeavesRuleStatusInPlace() =>
        Assert.Equal(AiFinalStatus.NoImages, AiVerdictMatrix.Decide(true, []));
}
