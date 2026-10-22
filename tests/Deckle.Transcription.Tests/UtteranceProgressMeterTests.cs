using System;
using System.Collections.Generic;
using Deckle.Audio;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// The meter exists so a preview shows the gauge a dictation would show. These
// tests hold it to the segmenter a dictation uses, step for step, rather than
// to numbers of their own, so a re-tuned segmenter moves both together.
[Trait("Category", "unit")]
public class UtteranceProgressMeterTests
{
    private const int FrameSamples = 800; // 50 ms @ 16 kHz
    private const float Voiced = 0.1f;    // well above the -45 dBFS threshold
    private const float Silent = 0f;

    private static EnergySegmenterSettings TestSettings()
        => new()
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 400,
            HangoverRampStartMs = 1_000_000,
            HangoverRampEndMs   = 1_000_000,
        };

    // Two utterances and a blip, with a pause that breaks off and one that
    // reaches the cut: every branch of the state machine the gauge shows.
    private static IEnumerable<float> Speech()
    {
        for (int i = 0; i < 10; i++) yield return Voiced;
        for (int i = 0; i < 3; i++) yield return Silent;   // a pause that resumes
        for (int i = 0; i < 4; i++) yield return Voiced;
        for (int i = 0; i < 10; i++) yield return Silent;  // the cut
        for (int i = 0; i < 3; i++) yield return Voiced;   // a blip
        for (int i = 0; i < 10; i++) yield return Silent;  // dropped
        for (int i = 0; i < 8; i++) yield return Voiced;
        for (int i = 0; i < 4; i++) yield return Silent;   // left mid-pause
    }

    [Fact]
    public void ItReportsTheProgressTheDictationSegmenterReportsAtEveryLevel()
    {
        var settings = TestSettings();
        var segmenter = new EnergySegmenter(settings, _ => { });
        var meter = new UtteranceProgressMeter(settings);

        Assert.Equal(segmenter.Progress, meter.Progress);

        UtteranceProgress last = default;
        float peak = 0f;
        foreach (float rms in Speech())
        {
            segmenter.Push(new CaptureFrame(new float[FrameSamples], rms));
            last = meter.Push(rms);

            Assert.Equal(segmenter.Progress, last);
            peak = Math.Max(peak, last.Fraction);
        }

        // The sequence is not vacuous: a pause was shown and a cut was counted,
        // and the blip was not.
        Assert.True(peak > 0f);
        Assert.Equal(1, last.CompletedUtterances);
        Assert.True(last.IsPausing);
    }

    [Fact]
    public void AMeterBeforeItsFirstLevelIsActiveWithNoPauseAndNothingCompleted()
    {
        var progress = new UtteranceProgressMeter(TestSettings()).Progress;

        Assert.True(progress.IsActive);
        Assert.False(progress.IsPausing);
        Assert.Equal(0f, progress.Fraction);
        Assert.Equal(0, progress.CompletedUtterances);
    }
}
