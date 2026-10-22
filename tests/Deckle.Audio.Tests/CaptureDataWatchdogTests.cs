using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// The watchdog closes a blind spot: a microphone that opens and starts but
// never hands back audio, with every waveIn call reporting success. Times are
// the capture clock in milliseconds, driven by hand; every bound is expressed
// from ThresholdMs so the tests follow a tuned threshold.
[Trait("Category", "unit")]
public sealed class CaptureDataWatchdogTests
{
    private const int T = CaptureDataWatchdog.ThresholdMs;

    [Fact]
    public void SilenceShorterThanTheThresholdOpensNothing()
    {
        CaptureDataWatchdog watchdog = default;

        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveIdle(0));
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveIdle(T - 1));
        Assert.Equal(0, watchdog.Stalls);
    }

    [Fact]
    public void ADeviceThatNeverDeliversOpensTheIncidentAtTheThreshold()
    {
        CaptureDataWatchdog watchdog = default;

        Assert.Equal(CaptureAnomalyTransition.Opened, watchdog.ObserveIdle(T));
        Assert.Equal(1, watchdog.Stalls);
        Assert.Equal(T, watchdog.MsSinceData(T));
    }

    [Fact]
    public void ALongSilenceOpensOnlyOneIncident()
    {
        CaptureDataWatchdog watchdog = default;

        Assert.Equal(CaptureAnomalyTransition.Opened, watchdog.ObserveIdle(T));
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveIdle(2 * T));
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveIdle(10 * T));
        Assert.Equal(1, watchdog.Stalls);
    }

    [Fact]
    public void FirstAudioAfterAStallClosesTheIncident()
    {
        CaptureDataWatchdog watchdog = default;

        watchdog.ObserveIdle(T);
        Assert.Equal(CaptureAnomalyTransition.Recovered, watchdog.ObserveData(T + 50));
        Assert.True(watchdog.Recovered);
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveData(T + 100));
    }

    [Fact]
    public void DataWithoutAStallNeverReportsARecovery()
    {
        CaptureDataWatchdog watchdog = default;

        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveData(50));
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveIdle(100));
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveData(150));
        Assert.False(watchdog.Recovered);
        Assert.Equal(0, watchdog.Stalls);
    }

    [Fact]
    public void ASecondStallCountsWithoutReopeningTheIncident()
    {
        CaptureDataWatchdog watchdog = default;

        watchdog.ObserveIdle(T);
        watchdog.ObserveData(T + 50);

        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveIdle(2 * T + 50));
        Assert.Equal(2, watchdog.Stalls);
        Assert.Equal(CaptureAnomalyTransition.None, watchdog.ObserveData(2 * T + 100));
    }

    [Fact]
    public void LongestGapIncludesAStallStillRunningAtStop()
    {
        CaptureDataWatchdog watchdog = default;

        watchdog.ObserveData(0);
        watchdog.ObserveIdle(T);
        watchdog.ObserveData(T + 200);           // gap T + 200
        watchdog.ObserveIdle(3 * T);
        watchdog.Close(4 * T);                   // gap 4T - (T + 200)

        Assert.Equal(3 * T - 200, watchdog.LongestGapMs);
    }
}
