using System;
using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// MicLevelCheck judges whether the DSP is worth it from the level deficit to the
// target alone, with no transcription or WER.
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
        var a = MicLevelCheck.Assess(MicBelowTarget(deficit), AtTarget());

        Assert.True(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.Recommended, a.Advice);
        Assert.Equal(Target - deficit, a.RawRmsDbfs, 1.0);
    }

    [Fact]
    public void MicAlreadyAtTargetIsNotNeeded()
    {
        var a = MicLevelCheck.Assess(SineAtDbfs(Target), AtTarget());

        Assert.Equal(PreprocessingAdvice.NotNeeded, a.Advice);
    }

    [Fact]
    public void SlightlyQuietMicIsMarginal()
    {
        // Midway between the marginal and recommend bars.
        double deficit = (MicLevelCheck.MarginalDeltaDb + MicLevelCheck.RecommendDeltaDb) / 2;
        var a = MicLevelCheck.Assess(MicBelowTarget(deficit), AtTarget());

        Assert.Equal(PreprocessingAdvice.Marginal, a.Advice);
    }

    [Theory]
    [InlineData(-0.5)]   // just under the marginal bar
    [InlineData(0.5)]    // just over it
    public void TheMarginalBarSeparatesNotNeededFromMarginal(double offsetDb)
    {
        var a = MicLevelCheck.Assess(
            MicBelowTarget(MicLevelCheck.MarginalDeltaDb + offsetDb), AtTarget());

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
            MicBelowTarget(MicLevelCheck.RecommendDeltaDb + offsetDb), AtTarget());

        Assert.Equal(
            offsetDb < 0 ? PreprocessingAdvice.Marginal : PreprocessingAdvice.Recommended,
            a.Advice);
    }

    [Fact]
    public void ProcessedLevelLandsNearTargetForAQuietMic()
    {
        // The "after" the user sees is the real DSP output — it should reach
        // the target, that is the whole point of showing it.
        var a = MicLevelCheck.Assess(MicBelowTarget(12.0), AtTarget());

        Assert.Equal(Target, a.ProcessedRmsDbfs, 1.5);
    }

    [Fact]
    public void EmptyCaptureHasNoSignal()
    {
        var a = MicLevelCheck.Assess(Array.Empty<float>(), AtTarget());

        Assert.False(a.HasSignal);
    }

    // A muted or dead microphone delivers a buffer of exact zeros: nothing was
    // heard, so the check gives no verdict instead of recommending a lift that
    // would only amplify nothing.
    [Fact]
    [Trait("Category", "regression")]
    public void ADigitallySilentCaptureHasNoSignalAndNoRecommendation()
    {
        var a = MicLevelCheck.Assess(new float[16_000], AtTarget());

        Assert.False(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.NotNeeded, a.Advice);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AVeryQuietButNonZeroCaptureStillHasASignal()
    {
        var a = MicLevelCheck.Assess(SineAtDbfs(-80.0), AtTarget());

        Assert.True(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.Recommended, a.Advice);
    }
}
