using System;

namespace Deckle.Audio;

// ── TranscriptionPreprocessor ──────────────────────────────────────────────
//
// The transcription pre-processing DSP stage (CONTEXT.md). A pure, terminal
// float[] → float[] transform sitting between microphone capture and the ASR
// backend, with the sole purpose of maximising machine intelligibility — not
// listening quality. It does NOT touch how the buffer is windowed for the
// backend (whisper.cpp keeps its 30 s + dynamic-seek windowing); when the
// home-grown windowing workstream lands, this stage simply runs per window
// instead of once, unchanged.
//
// Two-pass by design — and this is what the hotkey-driven dictation buys us.
// All audio is available at Stop, so we don't need a real-time adaptive AGC
// (which pumps, has transients, and is hard to test). Instead: pass 1 runs
// the dynamics chain and measures the resulting level; pass 2 applies the
// exact makeup gain to hit an absolute RMS target. Self-normalising per take,
// O(n), ~tens of ms over 20 min — invisible next to inference.
//
// Canonical voice order, each stage individually bypassable so the Playground
// can A/B a single stage's contribution:
//   1. high-pass   — rumble / plosives / DC offset
//   2. noise gate  — soft, OFF by default (silence is the VAD's job)
//   3. compressor  — gentle, tames intra-take dynamics
//   4. makeup gain — to an absolute RMS target (the two-pass step)
//   5. limiter     — soft peak guard, anti-clipping
//
// Central guardrail (kept here in prose because it governs every default):
// compressing hard + boosting hard lifts the inter-word noise floor, which
// is documented fuel for Whisper's silence hallucinations. Stay gentle; the
// clean silence fix is the upstream VAD, not this stage.
//
// Pure: Process clones its input and never mutates the caller's buffer — the
// raw capture must stay intact for the corpus (ADR-0006), which stores the
// unprocessed signal so a processed variant can always be re-derived.
public static class TranscriptionPreprocessor
{
    // The module captures at a single fixed format (16 kHz mono PCM16,
    // see Deckle.Audio/CLAUDE.md). The DSP time constants depend on it.
    public const double SampleRate = 16000.0;

    // Process a take. Returns the processed buffer plus the metrics worth
    // observing (input/output level, the makeup gain that was applied).
    // An empty input is returned as-is with neutral metrics.
    public static PreprocessingResult Process(float[] pcm, PreprocessingSettings s)
    {
        if (pcm.Length == 0)
        {
            return new PreprocessingResult(pcm, -120.0, -120.0, 0.0, 0f);
        }

        double inputRmsDbfs = RmsDbfs(pcm);

        // Work on a copy — the caller's raw buffer feeds the corpus untouched.
        float[] buf = (float[])pcm.Clone();

        if (s.HighPassEnabled)
        {
            new HighPassFilter(s.HighPassHz, SampleRate).ProcessInPlace(buf);
        }

        if (s.GateEnabled)
        {
            new NoiseGate(s.GateThresholdDbfs, s.GateRatio, s.GateAttackMs, s.GateReleaseMs, SampleRate)
                .ProcessInPlace(buf);
        }

        if (s.CompressorEnabled)
        {
            new Compressor(s.CompThresholdDbfs, s.CompRatio, s.CompKneeDb, s.CompAttackMs, s.CompReleaseMs, SampleRate)
                .ProcessInPlace(buf);
        }

        // ── Makeup gain (the two-pass step) ───────────────────────────────
        // Measure the level AFTER the dynamics chain, then apply the exact
        // gain to land on the target RMS. Capped both ways: never boost a
        // near-silent take into its noise floor, never over-attenuate.
        double makeupDb = 0.0;
        double measuredDb = RmsDbfs(buf);
        if (measuredDb > -120.0)
        {
            makeupDb = Math.Clamp(s.TargetRmsDbfs - measuredDb, -s.MaxMakeupGainDb, s.MaxMakeupGainDb);
            if (makeupDb != 0.0)
            {
                float g = (float)Math.Pow(10.0, makeupDb / 20.0);
                for (int i = 0; i < buf.Length; i++) buf[i] *= g;
            }
        }

        if (s.LimiterEnabled)
        {
            new Limiter(s.LimiterCeilingDbfs, s.LimiterReleaseMs, SampleRate).ProcessInPlace(buf);
        }

        double outputRmsDbfs = RmsDbfs(buf);
        float outputPeak = Peak(buf);

        return new PreprocessingResult(buf, inputRmsDbfs, outputRmsDbfs, makeupDb, outputPeak);
    }

    // Full-buffer RMS in dBFS, floored at -120 for a pure-zero buffer so the
    // log domain never sees 0 (mirrors PcmConversion.ToDbfs's floor).
    internal static double RmsDbfs(ReadOnlySpan<float> x)
    {
        if (x.Length == 0) return -120.0;
        double sumSq = 0.0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = x[i];
            sumSq += v * v;
        }
        double rms = Math.Sqrt(sumSq / x.Length);
        return rms > 0.0 ? 20.0 * Math.Log10(rms) : -120.0;
    }

    private static float Peak(ReadOnlySpan<float> x)
    {
        float peak = 0f;
        for (int i = 0; i < x.Length; i++)
        {
            float a = Math.Abs(x[i]);
            if (a > peak) peak = a;
        }
        return peak;
    }
}

// Outcome of a Process call: the processed buffer plus the metrics the
// orchestrator emits as observability (before/after level, applied makeup).
// MakeupGainDb is the gain actually applied (after clamping), in dB.
public readonly record struct PreprocessingResult(
    float[] Pcm,
    double InputRmsDbfs,
    double OutputRmsDbfs,
    double MakeupGainDb,
    float OutputPeak);
