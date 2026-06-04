using System;
using Deckle.Audio.Preprocessing;
using Xunit;

namespace Deckle.Audio.Tests;

// L'indicateur de niveau micro : on parle quelques secondes, il dit si le DSP
// vaut le coup. Ces tests épinglent le verdict (recommandé / limite / pas utile)
// selon l'écart au niveau cible, et le fait que le « après » atterrit bien sur
// la cible — la mesure qui « parle d'elle-même », sans transcription ni WER.
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

    private static PreprocessingSettings AtTarget(double targetDbfs = -20.0) => new()
    {
        Enabled = true,
        TargetRmsDbfs = (float)targetDbfs,
        MaxMakeupGainDb = 24f,
    };

    [Fact]
    public void QuietMicIsRecommended()
    {
        // -32 dBFS vs target -20 → 12 dB deficit, well over the recommend bar.
        var a = MicLevelCheck.Assess(SineAtDbfs(-32.0), AtTarget());

        Assert.True(a.HasSignal);
        Assert.Equal(PreprocessingAdvice.Recommended, a.Advice);
        Assert.Equal(-32.0, a.RawRmsDbfs, 1.0);
    }

    [Fact]
    public void MicAlreadyAtTargetIsNotNeeded()
    {
        // No deficit → nothing to gain.
        var a = MicLevelCheck.Assess(SineAtDbfs(-20.0), AtTarget());

        Assert.Equal(PreprocessingAdvice.NotNeeded, a.Advice);
    }

    [Fact]
    public void SlightlyQuietMicIsMarginal()
    {
        // -23.5 dBFS vs -20 → 3.5 dB deficit, between the marginal and recommend bars.
        var a = MicLevelCheck.Assess(SineAtDbfs(-23.5), AtTarget());

        Assert.Equal(PreprocessingAdvice.Marginal, a.Advice);
    }

    [Fact]
    public void ProcessedLevelLandsNearTargetForAQuietMic()
    {
        // The "after" the user sees is the real DSP output — it should reach
        // the target, that is the whole point of showing it.
        var a = MicLevelCheck.Assess(SineAtDbfs(-32.0), AtTarget());

        Assert.Equal(-20.0, a.ProcessedRmsDbfs, 1.5);
    }

    [Fact]
    public void EmptyCaptureHasNoSignal()
    {
        // Mic error / silence → no verdict, the UI shows a "couldn't read" state.
        var a = MicLevelCheck.Assess(Array.Empty<float>(), AtTarget());

        Assert.False(a.HasSignal);
    }
}
