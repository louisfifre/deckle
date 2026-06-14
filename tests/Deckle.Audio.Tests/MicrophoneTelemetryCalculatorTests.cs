using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// RmsSeries re-derives the 50 ms-window RMS series the post-DSP distribution is
// built from. These pin the windowing (whole windows only, trailing remainder
// dropped) and the RMS value itself — the whole post-DSP telemetry rests on it.
[Trait("Category", "unit")]
public class MicrophoneTelemetryCalculatorTests
{
    [Fact]
    public void RmsSeriesYieldsOneRmsPerWholeWindow()
    {
        // Two 800-sample windows: first at amplitude 0.5, second at 1.0. A constant
        // amplitude has RMS equal to that amplitude, so the series is exact.
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
        // 1000 samples, window 800 → one whole window; the 200-sample remainder is
        // dropped, mirroring the live path which only emits whole sub-windows.
        var series = MicrophoneTelemetryCalculator.RmsSeries(new float[1000], 800);
        Assert.Single(series);
    }

    [Fact]
    public void RmsSeriesShorterThanOneWindowIsEmpty()
    {
        var series = MicrophoneTelemetryCalculator.RmsSeries(new float[799], 800);
        Assert.Empty(series);
    }
}
