using Xunit;

namespace Deckle.Playground.Tests;

// The Recording preview's gauge is only as faithful as the samples the
// simulated cycle sends. These pin the engine's shape: no progress while
// speech runs, a fraction that rises through the pause without reaching 1,
// and a cut that reports one more utterance with the fraction back at 0.
[Trait("Category", "unit")]
public sealed class UtteranceCycleTests
{
    private const double Speech = 4;
    private const double Pause = 5;

    [Fact]
    public void SpeechReportsNoProgress()
    {
        var cycle = new UtteranceCycle();
        cycle.Advance(3.9, Speech, Pause, speed: 1);

        Assert.False(cycle.IsPausing);
        Assert.Equal(0f, cycle.Fraction);
        Assert.Equal(0, cycle.CompletedUtterances);
    }

    [Fact]
    public void ThePauseFillsTheGaugeWithoutReachingFull()
    {
        var cycle = new UtteranceCycle();
        cycle.Advance(Speech + Pause / 2, Speech, Pause, speed: 1);
        Assert.True(cycle.IsPausing);
        Assert.Equal(0.5f, cycle.Fraction, 3);

        cycle.Advance(Pause / 2 - 0.05, Speech, Pause, speed: 1);
        Assert.True(cycle.Fraction < 1f);
        Assert.Equal(0, cycle.CompletedUtterances);
    }

    [Fact]
    public void TheCutCountsOneUtteranceAndSpeechResumes()
    {
        var cycle = new UtteranceCycle();
        cycle.Advance(Speech + Pause, Speech, Pause, speed: 1);

        Assert.Equal(1, cycle.CompletedUtterances);
        Assert.False(cycle.IsPausing);
        Assert.Equal(0f, cycle.Fraction);
    }

    [Fact]
    public void SpeedScalesTheWholeCycle()
    {
        var cycle = new UtteranceCycle();
        cycle.Advance((Speech + Pause) / 2, Speech, Pause, speed: 2);

        Assert.Equal(1, cycle.CompletedUtterances);
    }

    // A duration or Speed change mid-cycle carries on from where the cycle
    // stands: the elapsed time is integrated, never recomputed from the clock.
    [Fact]
    public void ASpeedChangeKeepsThePhase()
    {
        var cycle = new UtteranceCycle();
        cycle.Advance(2, Speech, Pause, speed: 1);
        cycle.Advance(1, Speech, Pause, speed: 4);

        // 2 s + 4 s of cycle time: 2 s into the pause.
        Assert.True(cycle.IsPausing);
        Assert.Equal(2 / Pause, cycle.Fraction, 3);
        Assert.Equal(0, cycle.CompletedUtterances);
    }
}
