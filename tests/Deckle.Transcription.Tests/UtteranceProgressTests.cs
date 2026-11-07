using System;
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

    // 0.1 RMS (−20 dBFS) is clearly above the entry level, 0 below the exit.
    private static readonly double VoiceLevel = new CaptureSettings().VoiceLevelDbfs;

    // The shipped minimum speech: 250 ms = 5 frames of voiced extent.
    private static readonly int MinSpeechMs = new StreamingSettings().MinSpeechMs;

    private static CaptureFrame Frame(bool voiced)
        => new(new float[FrameSamples], voiced ? 0.1f : 0f);

    private static CaptureFrame FrameAtDbfs(double dbfs)
        => new(new float[FrameSamples], (float)Math.Pow(10.0, dbfs / 20.0));

    private static EnergySegmenterSettings Settings(
        int hangoverMs = 400)
        => new()
        {
            HangoverMaxMs       = hangoverMs,
            HangoverMinMs       = hangoverMs,
            HangoverRampStartMs = 1_000_000,
            HangoverRampEndMs   = 1_000_000,
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
        var segmenter = new EnergySegmenter(Settings(), VoiceLevel, MinSpeechMs, emitted.Add);

        PushN(segmenter, voiced: true, count: 6);
        // The pause shows after 4 silent frames (200 ms), not before.
        PushN(segmenter, voiced: false, count: 4);

        Assert.Empty(emitted);
        Assert.True(segmenter.Progress.IsPausing);
        Assert.Equal(200, segmenter.Progress.SilenceElapsedMs);
        Assert.Equal(400, segmenter.Progress.SilenceRequiredMs);
        Assert.Equal(0.5f, segmenter.Progress.Fraction, 3);

        PushN(segmenter, voiced: false, count: 3);
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
        var segmenter = new EnergySegmenter(Settings(), VoiceLevel, MinSpeechMs, emitted.Add);

        PushN(segmenter, voiced: true, count: 6);
        PushN(segmenter, voiced: false, count: 2);
        segmenter.Push(Frame(voiced: true));

        Assert.Empty(emitted);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0, segmenter.Progress.SilenceElapsedMs);
        Assert.Equal(0, segmenter.Progress.CompletedUtterances);
    }

    // The entry margin gates only the opening of an utterance. Once a pause has
    // started, a frame between the exit and the entry levels ends it, as any
    // frame at or above the level did before the detector existed. The
    // segmenter is built with a positive margin so the band exists; the levels
    // come from a detector with the same margin, so the test follows a re-tune.
    [Fact]
    public void AFrameBetweenExitAndEntryEndsAPauseThatHasStarted()
    {
        const double margin = 3.0;
        var detector = new VoiceDetector(VoiceLevel, margin);
        double between = (detector.ExitDbfs + detector.EntryDbfs) / 2;
        var segmenter = new EnergySegmenter(
            Settings(), VoiceLevel, MinSpeechMs, _ => { }, entryMarginDb: margin);

        PushN(segmenter, voiced: true, count: 6);
        for (int i = 0; i < EnergySegmenter.PauseDisplayFrames; i++)
            segmenter.Push(FrameAtDbfs(detector.ExitDbfs - 0.5));

        Assert.True(segmenter.Progress.IsPausing);
        Assert.Equal(200, segmenter.Progress.SilenceElapsedMs);

        segmenter.Push(FrameAtDbfs(between));

        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0, segmenter.Progress.SilenceElapsedMs);
    }

    [Fact]
    public void CompletedCountSurvivesSpeechResumingOnNextFrame()
    {
        var emitted = new List<Utterance>();
        var segmenter = new EnergySegmenter(Settings(hangoverMs: 100), VoiceLevel, MinSpeechMs, emitted.Add);

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
        };
        var segmenter = new EnergySegmenter(settings, VoiceLevel, minSpeechMs: 50, emitted.Add);

        PushN(segmenter, voiced: true, count: 25);
        segmenter.Push(Frame(voiced: false));

        // Counting has started (the cut below proves it) but the display waits.
        Assert.Empty(emitted);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0, segmenter.Progress.SilenceElapsedMs);
        Assert.Equal(100, segmenter.Progress.SilenceRequiredMs);

        segmenter.Push(Frame(voiced: false));

        Assert.Single(emitted);
        Assert.Equal(1, segmenter.Progress.CompletedUtterances);
    }

    [Fact]
    public void VoicedNoiseBlipDoesNotIncrementCompletedCount()
    {
        var emitted = new List<Utterance>();
        var segmenter = new EnergySegmenter(Settings(hangoverMs: 100), VoiceLevel, MinSpeechMs, emitted.Add);

        PushN(segmenter, voiced: true, count: 3); // 150 ms < 250 ms minimum
        PushN(segmenter, voiced: false, count: 2);

        Assert.Empty(emitted);
        Assert.Equal(0, segmenter.Progress.CompletedUtterances);
        Assert.False(segmenter.Progress.IsPausing);
        Assert.Equal(0f, segmenter.Progress.Fraction);
    }
}
