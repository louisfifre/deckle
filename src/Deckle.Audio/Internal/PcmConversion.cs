namespace Deckle.Audio;

// PCM16 mono ↔ float helpers for this module's capture, render and telemetry.
// All functions are pure; they never touch waveIn state.
internal static class PcmConversion
{
    // PCM16 little-endian → float [-1, 1].
    public static float[] PcmToFloat(byte[] pcm)
    {
        int n = pcm.Length / 2;
        float[] result = new float[n];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            result[i] = s / 32768.0f;
        }
        return result;
    }

    // float [-1, 1] → PCM16 little-endian. Clamps out-of-range input so a
    // synthesis overshoot can't wrap around to the opposite sign. Scales by
    // 32767 (full-scale positive); PcmToFloat divides by 32768, so the
    // round-trip stays within one LSB.
    public static byte[] FloatToPcm16(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float v = samples[i];
            if (v > 1f) v = 1f;
            else if (v < -1f) v = -1f;
            short s = (short)(v * 32767f);
            pcm[i * 2]     = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return pcm;
    }

    // Linear RMS → dBFS. Floors at -120 dBFS for a pure-zero buffer so the
    // log domain never sees 0 (the conventional "digital silence" floor).
    public static double ToDbfs(float linear) =>
        linear > 0f ? 20.0 * System.Math.Log10(linear) : -120.0;

    // Full-buffer dBFS — single pass over a PCM16 mono buffer, returns the
    // 20*log10(rms) value floored at -120 dBFS for a pure-zero buffer (so the
    // log domain never sees 0).
    public static double ComputeBufferDbfs(byte[] pcm16)
    {
        int nSamples = pcm16.Length / 2;
        if (nSamples == 0) return -120.0;

        double sumSq = 0;
        for (int i = 0; i < nSamples; i++)
        {
            short s = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
            double v = s / 32768.0;
            sumSq += v * v;
        }
        double rms = System.Math.Sqrt(sumSq / nSamples);
        return rms > 0 ? 20.0 * System.Math.Log10(rms) : -120.0;
    }
}
