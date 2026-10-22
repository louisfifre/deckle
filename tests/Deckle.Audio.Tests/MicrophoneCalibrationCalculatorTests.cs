using Xunit;

namespace Deckle.Audio.Tests;

// The HUD level window derived from recent microphone sessions: median p25 and
// p90 with a 5 dB margin, a -75 dBFS floor, slider-domain clamps, and the reasons
// a window is not worth applying.
[Trait("Category", "unit")]
public sealed class MicrophoneCalibrationCalculatorTests
{
    // Far from any computed window, so "changed" never depends on the current values.
    private const float AnyCurrentMin = -40f;
    private const float AnyCurrentMax = -10f;

    [Fact]
    public void AnEmptyBufferIsRejected()
    {
        var result = Compute([]);

        Assert.False(result.ShouldUpdate);
        Assert.Equal("empty buffer", result.RejectReason);
    }

    [Fact]
    public void TheWindowIsTheMedianP25AndP90WithAFiveDecibelMargin()
    {
        var result = Compute([Session(p25: -60, p90: -20)]);

        Assert.True(result.ShouldUpdate);
        Assert.Null(result.RejectReason);
        Assert.Equal(-65f, result.NewMinDbfs);
        Assert.Equal(-15f, result.NewMaxDbfs);
    }

    [Fact]
    public void OneRogueSessionDoesNotMoveTheMedian()
    {
        var steady = Session(p25: -60, p90: -20);
        var rogue = Session(p25: -90, p90: -5);

        var result = Compute([steady, steady, rogue, steady, steady]);

        Assert.Equal(-65f, result.NewMinDbfs);
        Assert.Equal(-15f, result.NewMaxDbfs);
    }

    [Fact]
    public void TheFloorNeverGoesBelowMinusSeventyFiveDbfs()
    {
        // A gate-dominated floor (p25 at -90) would put the window at -95.
        var result = Compute([Session(p25: -90, p90: -30)]);

        Assert.True(result.ShouldUpdate);
        Assert.Equal(-75f, result.NewMinDbfs);
        Assert.Equal(-25f, result.NewMaxDbfs);
    }

    [Fact]
    public void AWindowNarrowerThanTenDecibelsIsRejected()
    {
        var result = Compute([Session(p25: -40, p90: -46)]);

        Assert.False(result.ShouldUpdate);
        Assert.Equal("window spread < 10 dB", result.RejectReason);
    }

    [Fact]
    public void AWindowOfExactlyTenDecibelsIsAccepted()
    {
        // p90 == p25 leaves only the two 5 dB margins: -45 .. -35.
        var result = Compute([Session(p25: -40, p90: -40)]);

        Assert.True(result.ShouldUpdate);
        Assert.Equal(-45f, result.NewMinDbfs);
        Assert.Equal(-35f, result.NewMaxDbfs);
    }

    [Fact]
    public void TheCeilingStaysInsideTheSliderDomain()
    {
        var result = Compute([Session(p25: -50, p90: -3)]);

        Assert.True(result.ShouldUpdate);
        Assert.Equal(-55f, result.NewMinDbfs);
        Assert.Equal(-10f, result.NewMaxDbfs);
    }

    [Fact]
    public void AWindowThatCollapsesWhenClampedIsRejected()
    {
        // 0 dBFS everywhere: -5 .. 5 passes the spread check, then both ends clamp to -10.
        var result = Compute([Session(p25: 0, p90: 0)]);

        Assert.False(result.ShouldUpdate);
        Assert.Equal("clamped window collapsed", result.RejectReason);
    }

    [Fact]
    public void AStableMicrophoneReportsNoChangeButStillCarriesTheWindow()
    {
        var result = Compute([Session(p25: -60, p90: -20)], currentMin: -65f, currentMax: -15f);

        Assert.False(result.ShouldUpdate);
        Assert.Equal("no change", result.RejectReason);
        Assert.Equal(-65f, result.NewMinDbfs);
        Assert.Equal(-15f, result.NewMaxDbfs);
    }

    [Theory]
    [InlineData(-64.7f, -15f, false)]   // 0.3 dB off on the floor: noise
    [InlineData(-64.5f, -15f, true)]    // exactly 0.5 dB: worth a save
    [InlineData(-65f, -15.4f, false)]
    [InlineData(-65f, -14.5f, true)]
    public void OnlyAHalfDecibelShiftOnEitherEndCountsAsAChange(float currentMin, float currentMax, bool expected)
    {
        var result = Compute([Session(p25: -60, p90: -20)], currentMin, currentMax);

        Assert.Equal(expected, result.ShouldUpdate);
    }

    private static MicrophoneCalibrationCalculator.CalibrationResult Compute(
        MicrophoneTelemetryPayload[] samples,
        float currentMin = AnyCurrentMin,
        float currentMax = AnyCurrentMax)
        => MicrophoneCalibrationCalculator.Compute(samples, currentMin, currentMax);

    // Only the two percentiles the calculator reads carry a value.
    private static MicrophoneTelemetryPayload Session(double p25, double p90)
        => new(
            DurationSeconds: 5, Samples: 100, MinDbfs: 0, P10Dbfs: 0, P25Dbfs: p25, P50Dbfs: 0,
            P75Dbfs: 0, P90Dbfs: p90, MaxDbfs: 0, MeanRms: 0, MeanDbfs: 0, TailRms: 0, TailDbfs: 0,
            TailState: "");
}