using System;

namespace Deckle.Audio;

// ── HighPassFilter ─────────────────────────────────────────────────────────
//
// Second-order (12 dB/oct) Butterworth high-pass biquad, RBJ Audio-EQ
// Cookbook coefficients, transposed direct-form II. One instance carries
// its own two-sample state (z1, z2) — fresh per recording, since each take
// is processed independently (no cross-take carry).
//
// Role in the chain: strip everything below the speech band — mains hum
// (50/60 Hz), HVAC rumble, plosive thumps, table vibration — and the DC
// offset along the way (a high-pass at ~90 Hz removes the 0 Hz component
// for free). This both cleans the signal Whisper sees and stabilises the
// downstream RMS measurement (rumble inflates RMS without carrying speech).
//
// Q = 1/√2 (≈0.707) gives the maximally-flat Butterworth response — no
// resonant bump at the cutoff. Coefficients are normalised by a0 once at
// construction; the per-sample loop is three multiply-adds.
internal sealed class HighPassFilter
{
    private readonly float _b0, _b1, _b2, _a1, _a2;
    private float _z1, _z2;

    public HighPassFilter(double cutoffHz, double sampleRate, double q = 0.70710678118)
    {
        // RBJ high-pass. w0 is the normalised angular cutoff.
        double w0    = 2.0 * Math.PI * cutoffHz / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2.0 * q);

        double a0 = 1.0 + alpha;
        _b0 = (float)(((1.0 + cosW0) / 2.0) / a0);
        _b1 = (float)((-(1.0 + cosW0)) / a0);
        _b2 = (float)(((1.0 + cosW0) / 2.0) / a0);
        _a1 = (float)((-2.0 * cosW0) / a0);
        _a2 = (float)((1.0 - alpha) / a0);
    }

    // Process the buffer in place. Transposed direct-form II:
    //   y[n] = b0*x[n] + z1
    //   z1   = b1*x[n] - a1*y[n] + z2
    //   z2   = b2*x[n] - a2*y[n]
    public void ProcessInPlace(float[] x)
    {
        float b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
        float z1 = _z1, z2 = _z2;
        for (int i = 0; i < x.Length; i++)
        {
            float xn = x[i];
            float yn = b0 * xn + z1;
            z1 = b1 * xn - a1 * yn + z2;
            z2 = b2 * xn - a2 * yn;
            x[i] = yn;
        }
        _z1 = z1;
        _z2 = z2;
    }
}
