using System;
using Deckle.Audio;
using Xunit;

namespace Deckle.Hud.Tests;

public sealed class UtteranceCountdownStateTests
{
    private const int Precision = 5;

    // The share of a target the gauge takes on the first sample of a pause:
    // one step of the Recording outline's own smoother.
    private static double FirstSample(float target) => target * (1f - AudioLevelMapper.EmaAlpha);

    [Fact]
    public void PauseRisesThroughTheOutlinesSmootherAndResumedSpeechClearsIt()
    {
        var state = new UtteranceCountdownState();
        state.Accept(0.4f, 0, TimeSpan.Zero);
        Assert.Equal(FirstSample(0.4f), state.ValueAt(TimeSpan.Zero), Precision);
        state.Accept(0f, 0, TimeSpan.FromMilliseconds(50));
        Assert.Equal(0f, state.ValueAt(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void AHeldPauseSettlesOnItsTargetWithoutOvershoot()
    {
        var state = new UtteranceCountdownState();
        float previous = 0f;
        for (int sample = 0; sample < 40; sample++)
        {
            var now = TimeSpan.FromMilliseconds(50 * sample);
            state.Accept(0.4f, 0, now);
            float value = state.ValueAt(now);
            Assert.True(value >= previous && value <= 0.4f, $"Sample {sample}: {value} after {previous}.");
            previous = value;
        }
        Assert.Equal(0.4, previous, 3);
    }

    [Fact]
    public void CoalescedCutRemainsVisibleWhenSpeechHasAlreadyResumed()
    {
        var state = new UtteranceCountdownState();
        var cut = TimeSpan.FromSeconds(1);
        // UI skipped intermediate capture frames; only the cumulative count
        // tells it that a cut happened before speech resumed.
        state.Accept(0f, 2, cut);
        Assert.Equal(1f, state.ValueAt(cut));
        state.Accept(0f, 2, cut + TimeSpan.FromMilliseconds(50));
        Assert.Equal(1f, state.ValueAt(cut + UtteranceCountdownState.CompletionDuration / 2));
        Assert.Equal(0f, state.ValueAt(cut + UtteranceCountdownState.CompletionDuration));
    }

    [Fact]
    public void CompletionShowsLatestPauseWhenItsHoldExpires()
    {
        var state = new UtteranceCountdownState();
        state.Accept(0f, 1, TimeSpan.Zero);
        state.Accept(0.2f, 1, TimeSpan.FromMilliseconds(100));
        Assert.Equal(1f, state.ValueAt(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(FirstSample(0.2f), state.ValueAt(UtteranceCountdownState.CompletionDuration), Precision);
    }

    [Fact]
    public void StoppingClearsCompletionBeforeTheNextRecording()
    {
        var state = new UtteranceCountdownState();
        state.Accept(0f, 3, TimeSpan.Zero);
        state.Reset();
        Assert.Equal(0f, state.ValueAt(TimeSpan.Zero));
        state.Accept(0.1f, 0, TimeSpan.Zero);
        Assert.Equal(FirstSample(0.1f), state.ValueAt(TimeSpan.Zero), Precision);
        state.Accept(0f, 1, TimeSpan.FromSeconds(1));
        Assert.Equal(1f, state.ValueAt(TimeSpan.FromSeconds(1)));
    }
}
