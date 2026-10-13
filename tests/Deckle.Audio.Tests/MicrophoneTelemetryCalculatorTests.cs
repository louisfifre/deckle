using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// RmsSeries rebuilds the series the post-DSP distribution is computed from, so
// its windowing and RMS values are pinned here.
[Trait("Category", "unit")]
public class MicrophoneTelemetryCalculatorTests
{
    [Fact]
    public void RmsSeriesYieldsOneRmsPerWholeWindow()
    {
        // A constant amplitude has RMS equal to that amplitude, so the series is exact.
        var pcm = new float[1600];
        for (int i = 0; i < 800; i++) pcm[i] = 0.5f;
        for (int i = 800; i < 1600; i++) pcm[i] = 1.0f;

        var series = MicrophoneTelemetryCalculator.RmsSeries(pcm, 800);

        Assert.Equal(2, series.Count);
        Assert.True(System.Math.Abs(series[0] - 0.5f) < 1e-5);
        Assert.True(System.Math.Abs(series[1] - 1.0f) < 1e-5);
    }

    [Fact]
    public void RmsSeriesDropsTrailingPartialWindow()
    {
        var series = MicrophoneTelemetryCalculator.RmsSeries(new float[1000], 800);
        Assert.Single(series);
    }

    [Fact]
    public void RmsSeriesShorterThanOneWindowIsEmpty()
    {
        var series = MicrophoneTelemetryCalculator.RmsSeries(new float[799], 800);
        Assert.Empty(series);
    }

    [Fact]
    public void TailHeadlineIsBuiltSeparatelyFromTheDiagnostic()
    {
        var active = new MicrophoneTelemetryCalculator.TailDiagnostic(
            0.1, -20, 600, true, "active");
        var silent = new MicrophoneTelemetryCalculator.TailDiagnostic(
            0.001, -60, 600, false, "silent");

        Assert.Contains("still speaking", MicrophoneTelemetryCalculator.DescribeTail(active));
        Assert.Contains("after a silence", MicrophoneTelemetryCalculator.DescribeTail(silent));
    }
}
