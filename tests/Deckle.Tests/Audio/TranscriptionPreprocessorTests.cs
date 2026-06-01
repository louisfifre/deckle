using System;
using Deckle.Audio.Preprocessing;
using Xunit;

namespace Deckle.Tests.Audio;

// Strate 1 de la mesure — correction signal. Le DSP est pur et déterministe,
// donc on assert sur ce qu'il fait au signal lui-même (niveau atteint,
// atténuation du high-pass, plafond du limiteur, non-mutation, déterminisme).
// La strate 2 — gain WER réel — vit hors scope automatique (banc).
//
// Tous les étages sont exercés via l'API publique TranscriptionPreprocessor
// .Process : on isole un étage en désactivant les autres. Astuce : poser
// MaxMakeupGainDb = 0 borne le makeup à [0,0] dB, ce qui le neutralise et
// laisse mesurer un étage seul sans renormalisation parasite.
[Trait("Category", "unit")]
public class TranscriptionPreprocessorTests
{
    private const double SampleRate = TranscriptionPreprocessor.SampleRate;

    // ── Helpers ────────────────────────────────────────────────────────────

    // Sine of a given amplitude (peak) at a given frequency, `seconds` long.
    private static float[] Sine(double freqHz, double amplitude, double seconds = 1.0)
    {
        int n = (int)(SampleRate * seconds);
        var x = new float[n];
        double w = 2.0 * Math.PI * freqHz / SampleRate;
        for (int i = 0; i < n; i++) x[i] = (float)(amplitude * Math.Sin(w * i));
        return x;
    }

    // Settings with the whole chain bypassed — the neutral baseline a test
    // opts stages into. Makeup neutralised via MaxMakeupGainDb = 0.
    private static PreprocessingSettings Bypassed() => new()
    {
        Enabled           = true,
        HighPassEnabled   = false,
        GateEnabled       = false,
        CompressorEnabled = false,
        MaxMakeupGainDb   = 0f,
        LimiterEnabled    = false,
    };

    private static double Dbfs(double linearRms) => 20.0 * Math.Log10(linearRms);

    // ── Makeup gain ──────────────────────────────────────────────────────

    [Fact]
    public void MakeupGainLiftsQuietSignalToTarget()
    {
        // -35 dBFS RMS sine: RMS = amp/√2, so amp = √2 · 10^(-35/20).
        double amp = Math.Sqrt(2.0) * Math.Pow(10.0, -35.0 / 20.0);
        var input = Sine(440, amp);

        var s = Bypassed();
        s.MaxMakeupGainDb = 30f; // allow the full lift
        s.TargetRmsDbfs   = -20f;

        var r = TranscriptionPreprocessor.Process(input, s);

        Assert.Equal(-20.0, r.OutputRmsDbfs, 1.0); // within 1 dB of target
    }

    [Fact]
    public void MakeupGainIsClampedToMaxBoost()
    {
        // -60 dBFS sine, target -20 → 40 dB deficit, capped at 24 dB.
        double amp = Math.Sqrt(2.0) * Math.Pow(10.0, -60.0 / 20.0);
        var input = Sine(440, amp);

        var s = Bypassed();
        s.MaxMakeupGainDb = 24f;
        s.TargetRmsDbfs   = -20f;

        var r = TranscriptionPreprocessor.Process(input, s);

        // Lifted by the cap (24 dB), not all the way to target.
        Assert.Equal(-36.0, r.OutputRmsDbfs, 1.0);
        Assert.Equal(24.0, r.MakeupGainDb, 0.01);
    }

    // ── Limiter ──────────────────────────────────────────────────────────

    [Fact]
    public void LimiterKeepsEveryOutputSampleBelowCeiling()
    {
        // Quiet sine boosted hard toward 0 dBFS so the post-makeup signal
        // would clip; the limiter must clamp every sample to the ceiling.
        var input = Sine(440, 0.1);

        var s = Bypassed();
        s.MaxMakeupGainDb  = 60f;  // no clamp on the boost
        s.TargetRmsDbfs    = 0f;   // push to digital full-scale RMS → overshoot
        s.LimiterEnabled   = true;
        s.LimiterCeilingDbfs = -1f;

        var r = TranscriptionPreprocessor.Process(input, s);

        double ceiling = Math.Pow(10.0, -1.0 / 20.0); // ≈ 0.8913
        Assert.True(r.OutputPeak <= ceiling + 1e-4,
            $"peak {r.OutputPeak} exceeded ceiling {ceiling}");
    }

    // ── High-pass ──────────────────────────────────────────────────────────

    [Fact]
    public void HighPassAttenuatesSubBassFarMoreThanSpeechBand()
    {
        var s = Bypassed();
        s.HighPassEnabled = true;
        s.HighPassHz      = 90f;

        // Same amplitude, two frequencies: one below cutoff, one in band.
        var sub    = TranscriptionPreprocessor.Process(Sine(50, 0.5), s);
        var speech = TranscriptionPreprocessor.Process(Sine(1000, 0.5), s);

        // The 1 kHz tone sits in the passband (≈ unchanged); the 50 Hz tone
        // is well below the 90 Hz cutoff and is attenuated by several dB.
        Assert.True(speech.OutputRmsDbfs - sub.OutputRmsDbfs >= 6.0,
            $"speech {speech.OutputRmsDbfs:F1} dBFS vs sub {sub.OutputRmsDbfs:F1} dBFS");
    }

    // ── Whole-chain sanity ─────────────────────────────────────────────────

    [Fact]
    public void SignalAlreadyAtTargetPassesThroughApproximatelyUnchanged()
    {
        // -20 dBFS sine, makeup target -20 → makeup ≈ 0, peak below ceiling
        // → limiter idle. The chain must not wreck an already-good signal.
        double amp = Math.Sqrt(2.0) * Math.Pow(10.0, -20.0 / 20.0);
        var input = Sine(440, amp);

        var s = new PreprocessingSettings
        {
            Enabled           = true,
            HighPassEnabled   = false,
            GateEnabled       = false,
            CompressorEnabled = false,
            TargetRmsDbfs     = -20f,
            MaxMakeupGainDb   = 24f,
            LimiterEnabled    = true,
            LimiterCeilingDbfs = -1f,
        };

        var r = TranscriptionPreprocessor.Process(input, s);

        Assert.Equal(-20.0, r.OutputRmsDbfs, 0.5);
    }

    [Fact]
    public void EmptyBufferReturnsEmptyWithNeutralMetrics()
    {
        var r = TranscriptionPreprocessor.Process(Array.Empty<float>(), Bypassed());

        Assert.Empty(r.Pcm);
        Assert.Equal(0.0, r.MakeupGainDb, 0.01);
    }

    [Fact]
    public void ProcessDoesNotMutateInputBuffer()
    {
        // The raw capture must stay intact — it feeds the corpus (ADR-0011).
        var input = Sine(440, 0.3);
        var copy = (float[])input.Clone();

        var s = Bypassed();
        s.HighPassEnabled = true;
        s.CompressorEnabled = true;
        s.MaxMakeupGainDb = 24f;
        s.LimiterEnabled = true;
        TranscriptionPreprocessor.Process(input, s);

        Assert.Equal(copy, input);
    }

    [Fact]
    public void ProcessIsDeterministic()
    {
        var input = Sine(440, 0.2);

        var s = new PreprocessingSettings
        {
            Enabled = true, HighPassEnabled = true, CompressorEnabled = true,
            MaxMakeupGainDb = 24f, TargetRmsDbfs = -20f, LimiterEnabled = true,
        };

        var a = TranscriptionPreprocessor.Process(input, s);
        var b = TranscriptionPreprocessor.Process(input, s);

        Assert.Equal(a.Pcm, b.Pcm);
    }
}
