using System;
using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// MicLevelCheck judges whether the DSP is worth it from the level deficit to the
// target alone, with no transcription or WER, and reports the margin of the
// measured level to the voice level.
[Trait("Category", "unit")]
public class MicLevelCheckTests
{
    private const double SampleRate = TranscriptionPreprocessor.SampleRate;

    // Sine whose RMS sits at a given dBFS (amp = √2 · 10^(dBFS/20)).
    private static float[] SineAtDbfs(double dbfs, double seconds = 1.0)
    {
        double amp = Math.Sqrt(2.0) * Math.Pow(10.0, dbfs / 20.0);
        int n = (int)(SampleRate * seconds);
        var x = new float[n];
        double w = 2.0 * Math.PI * 440.0 / SampleRate;
        for (int i = 0; i < n; i++) x[i] = (float)(amp * Math.Sin(w * i));
        return x;
    }

    private const double Target = -20.0;

    // The shipped voice level: the margin is read against it.
    private static readonly double VoiceLevel = new CaptureSettings().VoiceLevelDbfs;

    // A mic whose raw level sits `deficit` dB under the target, so the bars are
    // read from the production constants instead of copied as dB figures.
    private static float[] MicBelowTarget(double deficitDb) => SineAtDbfs(Target - deficitDb);

    private static PreprocessingSettings AtTarget(double targetDbfs = Target) => new()
    {
        Enabled = true,
        TargetRmsDbfs = (float)targetDbfs,
        MaxMakeupGainDb = 24f,
    };

    [Fact]
    public void QuietMicIsRecommended()
    {
        double deficit = MicLevelCheck.RecommendDeltaDb + 6.0; // well over the recommend bar
        var a = MicLevelCheck.Assess(MicBelowTarget(deficit), AtTarget(), VoiceLevel);

        Assert.True(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.Recommended, a.Advice);
        Assert.Equal(Target - deficit, a.RawRmsDbfs, 1.0);
    }

    [Fact]
    public void MicAlreadyAtTargetIsNotNeeded()
    {
        var a = MicLevelCheck.Assess(SineAtDbfs(Target), AtTarget(), VoiceLevel);

        Assert.Equal(PreprocessingAdvice.NotNeeded, a.Advice);
    }

    [Fact]
    public void SlightlyQuietMicIsMarginal()
    {
        // Midway between the marginal and recommend bars.
        double deficit = (MicLevelCheck.MarginalDeltaDb + MicLevelCheck.RecommendDeltaDb) / 2;
        var a = MicLevelCheck.Assess(MicBelowTarget(deficit), AtTarget(), VoiceLevel);

        Assert.Equal(PreprocessingAdvice.Marginal, a.Advice);
    }

    [Theory]
    [InlineData(-0.5)]   // just under the marginal bar
    [InlineData(0.5)]    // just over it
    public void TheMarginalBarSeparatesNotNeededFromMarginal(double offsetDb)
    {
        var a = MicLevelCheck.Assess(
            MicBelowTarget(MicLevelCheck.MarginalDeltaDb + offsetDb), AtTarget(), VoiceLevel);

        Assert.Equal(
            offsetDb < 0 ? PreprocessingAdvice.NotNeeded : PreprocessingAdvice.Marginal,
            a.Advice);
    }

    [Theory]
    [InlineData(-0.5)]   // just under the recommend bar
    [InlineData(0.5)]    // just over it
    public void TheRecommendBarSeparatesMarginalFromRecommended(double offsetDb)
    {
        var a = MicLevelCheck.Assess(
            MicBelowTarget(MicLevelCheck.RecommendDeltaDb + offsetDb), AtTarget(), VoiceLevel);

        Assert.Equal(
            offsetDb < 0 ? PreprocessingAdvice.Marginal : PreprocessingAdvice.Recommended,
            a.Advice);
    }

    [Fact]
    public void ProcessedLevelLandsNearTargetForAQuietMic()
    {
        // The "after" the user sees is the real DSP output — it should reach
        // the target, that is the whole point of showing it.
        var a = MicLevelCheck.Assess(MicBelowTarget(12.0), AtTarget(), VoiceLevel);

        Assert.Equal(Target, a.ProcessedRmsDbfs, 1.5);
    }

    // The check says where the voice sits against the voice level the
    // segmenter cuts on: a steady level, every frame alike, reads the same
    // whether averaged or read per frame, positive above and negative below.
    [Theory]
    [InlineData(-30.0, -45.0)]   // a voice 15 dB above the level
    [InlineData(-50.0, -40.0)]   // a voice 10 dB under it
    public void ASteadyVoiceReadsItsMarginToTheVoiceLevel(double voiceDbfs, double voiceLevelDbfs)
    {
        var a = MicLevelCheck.Assess(SineAtDbfs(voiceDbfs), AtTarget(), voiceLevelDbfs);

        Assert.Equal(voiceDbfs - voiceLevelDbfs, a.VoiceMarginDb, 1.0);
    }

    // The voice level applies to each 50 ms frame, so the margin is read from
    // the loudest tenth of the frames, not from the capture's average: words
    // separated by pauses clear a level their average sits well under.
    [Fact]
    [Trait("Category", "regression")]
    public void TheMarginFollowsTheLoudFramesNotTheAverage()
    {
        const double Level = -45.0;
        const double Words = -40.0;
        // 5 s: 12 % of the frames at -40 dBFS, the rest at -70 dBFS. The
        // average sits near -49 dBFS.
        float[] pcm = FramesAt(Words, loudFrames: 12, quietDbfs: -70.0, totalFrames: 100);

        var a = MicLevelCheck.Assess(pcm, AtTarget(), Level);

        // The average is under the level; the words are 5 dB above it.
        Assert.True(a.RawRmsDbfs < Level, $"average {a.RawRmsDbfs:F1} dBFS");
        Assert.Equal(Words - Level, a.VoiceMarginDb, 0.5);
    }

    [Fact]
    public void TheVoicedShareIsTheShareOfFramesAtOrAboveTheVoiceLevel()
    {
        float[] pcm = FramesAt(-35.0, loudFrames: 30, quietDbfs: -70.0, totalFrames: 100);

        var a = MicLevelCheck.Assess(pcm, AtTarget(), -45.0);

        Assert.Equal(0.30, a.VoicedShare, 3);
    }

    [Fact]
    public void ThePerFrameReadingTakesTheNinetiethPercentile()
    {
        // Ten frames at ten distinct levels, -80 to -35 dBFS by 5 dB: the
        // nearest-rank 90th percentile of ten is the ninth, -40.
        var frames = new List<float>();
        for (int i = 0; i < 10; i++)
            frames.AddRange(FrameAt(-80.0 + 5.0 * i));

        var reading = MicFrameLevels.Measure(frames.ToArray(), -45.0);

        Assert.Equal(10, reading.Frames);
        Assert.Equal(-40.0, reading.LoudestTenthDbfs, 0.1);
        // -45 and above: the frames at -45, -40 and -35.
        Assert.Equal(0.3, reading.VoicedShare, 3);
    }

    [Fact]
    public void NoSignalHasNoVoicedFrame()
    {
        var a = MicLevelCheck.Assess(new float[16_000], AtTarget(), VoiceLevel);

        Assert.Equal(0.0, a.VoicedShare);
    }

    // One 50 ms frame of a sine at the given RMS level.
    private static float[] FrameAt(double dbfs)
    {
        double amp = Math.Sqrt(2.0) * Math.Pow(10.0, dbfs / 20.0);
        var x = new float[MicFrameLevels.FrameSamples];
        double w = 2.0 * Math.PI * 440.0 / SampleRate;
        for (int i = 0; i < x.Length; i++) x[i] = (float)(amp * Math.Sin(w * i));
        return x;
    }

    // loudFrames frames at loudDbfs, spread one in every few, the rest at
    // quietDbfs, like words between pauses.
    private static float[] FramesAt(double loudDbfs, int loudFrames, double quietDbfs, int totalFrames)
    {
        var x = new List<float>(totalFrames * MicFrameLevels.FrameSamples);
        int step = totalFrames / loudFrames;
        int placed = 0;
        for (int f = 0; f < totalFrames; f++)
        {
            bool loud = placed < loudFrames && f % step == 0;
            if (loud) placed++;
            x.AddRange(FrameAt(loud ? loudDbfs : quietDbfs));
        }
        return x.ToArray();
    }

    [Fact]
    public void EmptyCaptureHasNoSignal()
    {
        var a = MicLevelCheck.Assess(Array.Empty<float>(), AtTarget(), VoiceLevel);

        Assert.False(a.HasSignal);
    }

    // A muted or dead microphone delivers a buffer of exact zeros: nothing was
    // heard, so the check gives no verdict instead of recommending a lift that
    // would only amplify nothing.
    [Fact]
    [Trait("Category", "regression")]
    public void ADigitallySilentCaptureHasNoSignalAndNoRecommendation()
    {
        var a = MicLevelCheck.Assess(new float[16_000], AtTarget(), VoiceLevel);

        Assert.False(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.NotNeeded, a.Advice);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AVeryQuietButNonZeroCaptureStillHasASignal()
    {
        var a = MicLevelCheck.Assess(SineAtDbfs(-80.0), AtTarget(), VoiceLevel);

        Assert.True(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.Recommended, a.Advice);
    }
}
