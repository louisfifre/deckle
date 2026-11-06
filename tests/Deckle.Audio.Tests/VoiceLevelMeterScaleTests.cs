using Xunit;

namespace Deckle.Audio.Tests;

// The voice meter's arithmetic: where a level falls on the track, the smoothing
// it shares with the HUD outline, and the peak tick's hold. Positions are read
// from the scale's own bounds, so a re-tune of the span does not break them.
[Trait("Category", "unit")]
public sealed class VoiceLevelMeterScaleTests
{
    private const double Min = VoiceLevelMeterScale.MinDbfs;
    private const double Max = VoiceLevelMeterScale.MaxDbfs;
    private const double Hold = VoiceLevelMeterScale.PeakHoldSeconds;

    [Fact]
    public void TheShippedVoiceLevelSitsInTheMiddleThirdOfTheTrack()
    {
        double f = VoiceLevelMeterScale.Fraction(new CaptureSettings().VoiceLevelDbfs);

        Assert.InRange(f, 1.0 / 3, 2.0 / 3);
    }

    [Fact]
    public void TheTrackRunsFromItsMinimumToItsMaximumLinearly()
    {
        Assert.Equal(0.0, VoiceLevelMeterScale.Fraction(Min));
        Assert.Equal(1.0, VoiceLevelMeterScale.Fraction(Max));
        Assert.Equal(0.5, VoiceLevelMeterScale.Fraction((Min + Max) / 2), 9);
    }

    [Theory]
    [InlineData(-200.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [InlineData(double.NaN, 0.0)]
    public void ALevelOffTheTrackPinsTheBarAtAnEnd(double dbfs, double expected)
        => Assert.Equal(expected, VoiceLevelMeterScale.Fraction(dbfs));

    [Fact]
    public void ZeroRmsReadsAsTheSilenceFloorUnderTheTrack()
    {
        Assert.Equal(VoiceLevelMeterScale.SilenceFloorDbfs, VoiceLevelMeterScale.RmsToDbfs(0f));
        Assert.True(VoiceLevelMeterScale.SilenceFloorDbfs < Min);
        Assert.Equal(-20.0, VoiceLevelMeterScale.RmsToDbfs(0.1f), 4);
    }

    [Fact]
    public void TheFirstFrameIsTakenAsIsAndTheNextAreSmoothedWithTheHudsFactor()
    {
        Assert.Equal(-50.0, VoiceLevelMeterScale.Smooth(null, -50.0));

        double alpha = AudioLevelMapper.EmaAlpha;
        Assert.Equal(-60.0 * alpha + -30.0 * (1 - alpha), VoiceLevelMeterScale.Smooth(-60.0, -30.0), 9);
    }

    [Fact]
    public void ALouderValueTakesThePeakAndRestartsTheHold()
    {
        var (peak, since) = VoiceLevelMeterScale.NextPeak(-40, 0, -30, 0.5);

        Assert.Equal(-30, peak);
        Assert.Equal(0.5, since);
    }

    [Fact]
    public void AQuieterValueLeavesThePeakWhileTheHoldLasts()
    {
        var (peak, since) = VoiceLevelMeterScale.NextPeak(-30, 1.0, -50, 1.0 + Hold * 0.9);

        Assert.Equal(-30, peak);
        Assert.Equal(1.0, since);
    }

    [Fact]
    public void OnceTheHoldRunsOutThePeakFollowsTheLevelDownWithoutHoldingAgain()
    {
        var (peak, since) = VoiceLevelMeterScale.NextPeak(-30, 1.0, -50, 1.0 + Hold);
        Assert.Equal(-50, peak);

        // The next, quieter frame is followed at once: the hold did not restart.
        (peak, _) = VoiceLevelMeterScale.NextPeak(peak, since, -55, 1.0 + Hold + 0.05);
        Assert.Equal(-55, peak);
    }
}
