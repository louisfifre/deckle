using System.Collections.Generic;
using Deckle.Audio;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// Contract tests for the live progress signal. They feed the same 50 ms frames
// the capture loop emits and keep the emitted utterances as the cut oracle.
[Trait("Category", "unit")]
public sealed class UtteranceProgressTests
{
    private const int FrameSamples = 800;

    private static CaptureFrame Frame(bool voiced)
        => new(new float[FrameSamples], voiced ? 0.1f : 0f);

    private static EnergySegmenterSettings Settings(
        int hangoverMs = 400,
        int minUtteranceMs = 250)
        => new()
        {
            HangoverMaxMs       = hangoverMs,
            HangoverMinMs       = hangoverMs,
            HangoverRampStartMs = 1_000_000,
            HangoverRampEndMs   = 1_000_000,
            MinUtteranceMs      = minUtteranceMs,
        };

    private static void PushN(EnergySegmenter segmenter, bool voiced, int count)
    {
        for (int i = 0; i < count; i++)
            segmenter.Push(Frame(voiced));
    }

    [Fact]
    public void ProgressFractionFollowsConsecutiveSilenceUntilCut()
    {
        var emitted = new List<Utterance>();
        var segmenter = new EnergySegmenter(Settings(), emitted.Add);

        PushN(segmenter, voiced: true, count: 6);
        segmenter.Push(Frame(voiced: false));

        Assert.Empty(emitted);
        Assert.True(segmenter.Progress.IsPausing);
        Assert.Equal(50, segmenter.Progress.SilenceElapsedMs);
        Assert.Equal(400, segmenter.Progress.SilenceRequiredMs);
        Assert.Equal(0.125f, segmenter.Progress.Fraction, 3);

        PushN(segmenter, voiced: false, count: 6);
        Assert.Empty(emitted);
        Assert.Equal(0.875f, segmenter.Progress.Fraction, 3);
        segmenter.Push(Frame(voiced: false));

        Assert.Single(emitted);
        Assert.Equal(1, segmenter.Progress.CompletedUtterances);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0f, segmenter.Progress.Fraction);
    }

    [Fact]
    public void ResumedSpeechClearsPauseProgressWithoutEmitting()
    {
        var emitted = new List<Utterance>();
        var segmenter = new EnergySegmenter(Settings(), emitted.Add);

        PushN(segmenter, voiced: true, count: 6);
        PushN(segmenter, voiced: false, count: 2);
        segmenter.Push(Frame(voiced: true));

        Assert.Empty(emitted);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0, segmenter.Progress.SilenceElapsedMs);
        Assert.Equal(0, segmenter.Progress.CompletedUtterances);
    }

    [Fact]
    public void CompletedCountSurvivesSpeechResumingOnNextFrame()
    {
        var emitted = new List<Utterance>();
        var segmenter = new EnergySegmenter(Settings(hangoverMs: 100), emitted.Add);

        PushN(segmenter, voiced: true, count: 6);
        PushN(segmenter, voiced: false, count: 2);
        segmenter.Push(Frame(voiced: true));

        Assert.Single(emitted);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(1, segmenter.Progress.CompletedUtterances);
    }

    [Fact]
    public void LongUtteranceUsesReducedHangoverAndThenEmits()
    {
        var emitted = new List<Utterance>();
        var settings = new EnergySegmenterSettings
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 100,
            HangoverRampStartMs = 500,
            HangoverRampEndMs   = 1_000,
            MinUtteranceMs      = 50,
        };
        var segmenter = new EnergySegmenter(settings, emitted.Add);

        PushN(segmenter, voiced: true, count: 25);
        segmenter.Push(Frame(voiced: false));

        Assert.Empty(emitted);
        Assert.True(segmenter.Progress.IsPausing);
        Assert.Equal(50, segmenter.Progress.SilenceElapsedMs);
        Assert.Equal(100, segmenter.Progress.SilenceRequiredMs);

        segmenter.Push(Frame(voiced: false));

        Assert.Single(emitted);
        Assert.Equal(1, segmenter.Progress.CompletedUtterances);
    }

    [Fact]
    public void VoicedNoiseBlipDoesNotIncrementCompletedCount()
    {
        var emitted = new List<Utterance>();
        var segmenter = new EnergySegmenter(Settings(hangoverMs: 100), emitted.Add);

        PushN(segmenter, voiced: true, count: 3); // 150 ms < 250 ms minimum
        PushN(segmenter, voiced: false, count: 2);

        Assert.Empty(emitted);
        Assert.Equal(0, segmenter.Progress.CompletedUtterances);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0f, segmenter.Progress.Fraction);
    }
}
