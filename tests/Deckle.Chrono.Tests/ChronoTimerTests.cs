using System;
using System.Threading;
using Deckle.Chrono;
using Xunit;

namespace Deckle.Chrono.Tests;

// The HUD clock relies on Start being Restart: a new session never starts from
// the previous one's time.
//
// Time-dependent cases sleep briefly and assert relative to an earlier reading,
// not against an absolute threshold, so scheduler jitter cannot fail them.
[Collection("chrono provider singleton")]
[Trait("Category", "unit")]
public class ChronoTimerTests
{
    [Fact]
    public void FreshTimerIsNotRunningAndReadsZero()
    {
        var t = new ChronoTimer();

        Assert.False(t.IsRunning);
        Assert.Equal(TimeSpan.Zero, t.Elapsed);
    }

    [Fact]
    public void StartBeginsRunning()
    {
        var t = new ChronoTimer();

        t.Start();

        Assert.True(t.IsRunning);
    }

    [Fact]
    public void StopFreezesTheElapsedValue()
    {
        var t = new ChronoTimer();
        t.Start();
        t.Stop();

        Assert.False(t.IsRunning);
        var first = t.Elapsed;
        var second = t.Elapsed;
        Assert.Equal(first, second);
    }

    [Fact]
    public void ResetZeroesAndStops()
    {
        var t = new ChronoTimer();
        t.Start();

        t.Reset();

        Assert.False(t.IsRunning);
        Assert.Equal(TimeSpan.Zero, t.Elapsed);
    }

    [Fact]
    public void StartZeroesAnyPriorAccumulation()
    {
        var t = new ChronoTimer();

        t.Start();
        Thread.Sleep(20);
        t.Stop();
        var afterAccumulation = t.Elapsed;
        Assert.True(afterAccumulation > TimeSpan.Zero);

        t.Start();   // Restart: resets, then runs again
        t.Stop();
        var afterRestart = t.Elapsed;

        Assert.True(
            afterRestart < afterAccumulation,
            $"Start aurait dû repartir de zéro : {afterRestart} >= {afterAccumulation}");
    }

    [Fact]
    public void ResumeContinuesFromTheCurrentValueWithoutZeroing()
    {
        var t = new ChronoTimer();

        t.Start();
        Thread.Sleep(20);
        t.Stop();
        var afterStop = t.Elapsed;

        t.Resume();
        t.Stop();
        var afterResume = t.Elapsed;

        Assert.True(
            afterResume >= afterStop,
            $"Resume aurait dû conserver l'accumulé : {afterResume} < {afterStop}");
    }
}
