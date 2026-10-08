using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientPushGateTests
{
    private const int Threshold = 6;

    [Fact]
    public void PersistentOutputsStillDropChangesUnderTheThreshold()
    {
        var decision = AmbientPushGate.Decide(
            new LightColor(11, 10, 10),
            (10, 10, 10),
            Threshold,
            requiresContinuousColorUpdates: false);

        Assert.Equal(AmbientPushDecision.Drop, decision);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ContinuousOutputsHoldThePreviousColourUnderTheThreshold()
    {
        var previous = (R: 10, G: 10, B: 10);

        var decision = AmbientPushGate.Decide(
            new LightColor(12, 11, 10),
            previous,
            Threshold,
            requiresContinuousColorUpdates: true);

        Assert.Equal(AmbientPushDecision.HoldPrevious, decision);
        Assert.Equal(new LightColor(10, 10, 10), AmbientPushGate.PreviousColor(previous));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangesAtTheThresholdPushTheNewColour(bool continuous)
    {
        var decision = AmbientPushGate.Decide(
            new LightColor(10 + Threshold, 10, 10),
            (10, 10, 10),
            Threshold,
            continuous);

        Assert.Equal(AmbientPushDecision.PushNew, decision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExactBlackTargetAlwaysPasses(bool continuous)
    {
        var decision = AmbientPushGate.Decide(
            LightColor.Black,
            (1, 1, 0),
            threshold: 50,
            continuous);

        Assert.Equal(AmbientPushDecision.PushNew, decision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFirstPushAfterStartAlwaysPasses(bool continuous)
    {
        var decision = AmbientPushGate.Decide(
            new LightColor(10, 10, 10),
            (-1, -1, -1),
            Threshold,
            continuous);

        Assert.Equal(AmbientPushDecision.PushNew, decision);
    }
}
