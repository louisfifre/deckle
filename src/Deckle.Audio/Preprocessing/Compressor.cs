using System;

namespace Deckle.Audio;

// ── Compressor ─────────────────────────────────────────────────────────────
//
// Feed-forward, soft-knee, log-domain compressor — the textbook design
// (Reiss & McPherson, "Audio Effects"). One instance, fresh per take.
//
// Role in the chain: shrink the intra-take dynamic range so a posed,
// quiet passage and a raised sentence land closer together — the part of
// the problem that absolute makeup gain alone can't fix. Deliberately
// gentle (2:1 soft knee, not a broadcast 4:1): compressing hard lifts the
// inter-word noise floor, and a raised floor is the documented fuel for
// Whisper's silence hallucinations ("Subtitles made by the Amara.org
// community", see research--whisper-dynamic-vad-distil-fr).
// The clean fix for silence is the upstream VAD, not aggressive
// compression here.
//
// Detector on the rectified sample, gain smoothing applied to the gain
// reduction in dB with separate attack/release coefficients (peak-style
// follower — adequate for speech and cheaper than an RMS detector).
internal sealed class Compressor
{
    private readonly double _thresholdDb;
    private readonly double _ratio;
    private readonly double _kneeDb;
    private readonly double _attackCoef;
    private readonly double _releaseCoef;

    // Running smoothed gain reduction in dB (≤ 0). Starts at 0 (no
    // reduction) so the first samples are not spuriously ducked.
    private double _gainReductionDb;

    public Compressor(
        double thresholdDbfs,
        double ratio,
        double kneeDb,
        double attackMs,
        double releaseMs,
        double sampleRate)
    {
        _thresholdDb = thresholdDbfs;
        _ratio       = Math.Max(1.0, ratio);
        _kneeDb      = Math.Max(0.0, kneeDb);
        _attackCoef  = TimeConstant(attackMs, sampleRate);
        _releaseCoef = TimeConstant(releaseMs, sampleRate);
    }

    // One-pole smoothing coefficient for a given time constant. exp(-1/(τ·Fs)).
    private static double TimeConstant(double ms, double sampleRate)
    {
        if (ms <= 0.0) return 0.0; // instantaneous
        return Math.Exp(-1.0 / (ms * 0.001 * sampleRate));
    }

    public void ProcessInPlace(float[] x)
    {
        for (int i = 0; i < x.Length; i++)
        {
            double a = Math.Abs(x[i]);
            double levelDb = a > 1e-9 ? 20.0 * Math.Log10(a) : -180.0;

            // Static gain-computer output (target gain reduction, ≤ 0).
            double target = ComputeGainReductionDb(levelDb);

            // Attack when we need MORE reduction (target below current),
            // release when we need less. Smoothing in the dB domain.
            double coef = target < _gainReductionDb ? _attackCoef : _releaseCoef;
            _gainReductionDb = coef * _gainReductionDb + (1.0 - coef) * target;

            float gain = (float)Math.Pow(10.0, _gainReductionDb / 20.0);
            x[i] *= gain;
        }
    }

    // Soft-knee static curve. Returns the gain reduction in dB (≤ 0) to
    // apply at this input level.
    private double ComputeGainReductionDb(double levelDb)
    {
        double over = levelDb - _thresholdDb;
        double outDb;

        if (2.0 * over <= -_kneeDb)
        {
            // Below the knee — no compression.
            outDb = levelDb;
        }
        else if (2.0 * Math.Abs(over) <= _kneeDb)
        {
            // Inside the knee — quadratic interpolation toward the ratio.
            double t = over + _kneeDb / 2.0;
            outDb = levelDb + (1.0 / _ratio - 1.0) * t * t / (2.0 * _kneeDb);
        }
        else
        {
            // Above the knee — full ratio.
            outDb = _thresholdDb + over / _ratio;
        }

        return outDb - levelDb;
    }
}
