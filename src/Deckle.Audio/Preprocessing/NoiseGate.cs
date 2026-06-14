using System;

namespace Deckle.Audio;

// ── NoiseGate ──────────────────────────────────────────────────────────────
//
// Soft downward expander below a threshold — one instance, fresh per take.
//
// OFF by default in PreprocessingSettings, and that is the whole point of
// the comment here: the gate is present so the Playground can experiment,
// not because the chain needs it. Silence handling proper belongs to the
// energy VAD upstream of the windowing workstream; a gate sitting in the
// signal path is blunt and an aggressive one eats the weak phonemes of a
// quiet voice — exactly the signal we are trying to rescue. Kept soft
// (ratio ~2:1) and shallow on purpose.
//
// Below the threshold the gain is reduced proportionally to how far under
// the signal sits, in the log domain, smoothed with attack/release. Above
// the threshold the gate is transparent (unity gain).
internal sealed class NoiseGate
{
    private readonly double _thresholdDb;
    private readonly double _ratio;
    private readonly double _attackCoef;
    private readonly double _releaseCoef;

    // Smoothed gain reduction in dB (≤ 0). Starts transparent.
    private double _gainReductionDb;

    public NoiseGate(
        double thresholdDbfs,
        double ratio,
        double attackMs,
        double releaseMs,
        double sampleRate)
    {
        _thresholdDb = thresholdDbfs;
        _ratio       = Math.Max(1.0, ratio);
        _attackCoef  = TimeConstant(attackMs, sampleRate);
        _releaseCoef = TimeConstant(releaseMs, sampleRate);
    }

    private static double TimeConstant(double ms, double sampleRate)
    {
        if (ms <= 0.0) return 0.0;
        return Math.Exp(-1.0 / (ms * 0.001 * sampleRate));
    }

    public void ProcessInPlace(float[] x)
    {
        for (int i = 0; i < x.Length; i++)
        {
            double a = Math.Abs(x[i]);
            double levelDb = a > 1e-9 ? 20.0 * Math.Log10(a) : -180.0;

            // Downward expander: below threshold, the deficit is amplified
            // by (ratio - 1) into a gain reduction. Above threshold → 0.
            double under = _thresholdDb - levelDb;          // > 0 below threshold
            double target = under > 0.0 ? -under * (_ratio - 1.0) : 0.0;

            // For a gate, "attack" = opening (less reduction, target rising)
            // and "release" = closing (more reduction). Smooth in dB.
            double coef = target < _gainReductionDb ? _releaseCoef : _attackCoef;
            _gainReductionDb = coef * _gainReductionDb + (1.0 - coef) * target;

            float gain = (float)Math.Pow(10.0, _gainReductionDb / 20.0);
            x[i] *= gain;
        }
    }
}
