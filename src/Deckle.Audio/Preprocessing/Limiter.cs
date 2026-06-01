using System;

namespace Deckle.Audio.Preprocessing;

// ── Limiter ────────────────────────────────────────────────────────────────
//
// Soft peak limiter, look-ahead-free, one instance fresh per take. Sits
// last in the chain — the safety guard after makeup gain, so no sample
// reaches the digital ceiling and clips on the way to Whisper.
//
// Design choice that matters for the unit test contract: the attack is
// INSTANTANEOUS (the gain can drop to the exact value needed on the very
// sample that would overshoot), the release is smoothed. Consequence —
// the output magnitude NEVER exceeds the ceiling: when |x|·g would exceed
// it, g is set to ceiling/|x| for that sample, so |out| == ceiling. The
// release only ever raises the gain back toward unity while the signal
// is below the ceiling, which cannot cause an overshoot. This is what the
// "limiter never crosses the ceiling" assertion relies on.
//
// No look-ahead means a hard transient is caught on its own sample rather
// than anticipated — slightly less transparent than a mastering limiter,
// but for speech intelligibility that is irrelevant and the guarantee is
// what we want.
internal sealed class Limiter
{
    private readonly float _ceiling;       // linear, > 0
    private readonly double _releaseCoef;
    private float _gain = 1f;

    public Limiter(double ceilingDbfs, double releaseMs, double sampleRate)
    {
        _ceiling     = (float)Math.Pow(10.0, ceilingDbfs / 20.0);
        _releaseCoef = releaseMs <= 0.0 ? 0.0 : Math.Exp(-1.0 / (releaseMs * 0.001 * sampleRate));
    }

    public void ProcessInPlace(float[] x)
    {
        float ceiling = _ceiling;
        double releaseCoef = _releaseCoef;
        float gain = _gain;

        for (int i = 0; i < x.Length; i++)
        {
            float a = Math.Abs(x[i]);

            // Largest gain that keeps this sample at or below the ceiling,
            // capped at unity (the limiter never boosts). When the signal
            // is quiet, target = 1; when |x| would clip, target = ceiling/|x|.
            float target = a > 1e-12f ? Math.Min(1f, ceiling / a) : 1f;

            if (target < gain)
            {
                gain = target;                 // attack: instantaneous, no overshoot
            }
            else
            {
                // release: ease the gain back up toward target. Because the
                // pre-release gain ≤ target, the smoothed gain stays ≤ target,
                // so |x|·gain ≤ ceiling holds on this sample too.
                gain = (float)(releaseCoef * gain + (1.0 - releaseCoef) * target);
            }

            x[i] *= gain;
        }

        _gain = gain;
    }
}
