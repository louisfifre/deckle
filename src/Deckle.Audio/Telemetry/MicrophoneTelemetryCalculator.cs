using Deckle.Audio;

namespace Deckle.Audio;

// Pure compute over a recording's RMS series, one value per sub-window:
// the tail diagnostic and the distribution payload. Callers emit the events:
// MicrophoneCapture for the raw signal, TranscriptionEngine for the DSP output.
public static class MicrophoneTelemetryCalculator
{
    public readonly record struct TailDiagnostic(
        double TailRms,
        double TailDbfs,
        int    TailMs,
        bool   TailActive,
        string TailState);

    public static TailDiagnostic? ComputeTail(System.Collections.Generic.IReadOnlyList<float> rmsLog)
    {
        int n = rmsLog.Count;
        if (n == 0) return null;

        // ── Tail-600 ms diagnostic (always on) ─────────────────────────────
        //
        // Root-mean-square of the last 12 sub-windows: sums the squared
        // sub-window RMS values and re-roots, the correct way to combine RMS
        // samples (NOT a plain mean of RMS).
        //
        // The operational sentence (DescribeTail) is built separately so this
        // functional telemetry calculation never allocates it before admission.
        int tailCount = System.Math.Min(12, n);
        double tailSumSq = 0;
        for (int i = n - tailCount; i < n; i++)
        {
            double v = rmsLog[i];
            tailSumSq += v * v;
        }
        double tailRms = System.Math.Sqrt(tailSumSq / tailCount);
        double tailDbfs = PcmConversion.ToDbfs((float)tailRms);
        int tailMs = tailCount * 50;
        bool tailActive = tailDbfs > -50;
        string tailState = tailActive ? "active" : "silent";
        return new TailDiagnostic(tailRms, tailDbfs, tailMs, tailActive, tailState);
    }

    public static string DescribeTail(TailDiagnostic tail)
        => tail.TailActive
            ? "You were still speaking at Stop — the last words may be clipped."
            : "You stopped after a silence — capture ends cleanly.";

    public static MicrophoneTelemetryPayload? Compute(
        System.Collections.Generic.IReadOnlyList<float> rmsLog,
        TailDiagnostic                                  tail)
    {
        int n = rmsLog.Count;
        if (n == 0) return null;

        // ── Distribution payload (always computed) ─────────────────────────
        //
        // Computed regardless of the log toggles: auto-calibration consumes it
        // too, and sorting a few thousand floats is cheap.
        var sorted = new float[n];
        for (int i = 0; i < n; i++) sorted[i] = rmsLog[i];
        System.Array.Sort(sorted);

        // Nearest-rank is good enough for human-readable telemetry; no stats
        // engine reads these.
        float Pick(double frac) => sorted[System.Math.Clamp((int)(n * frac), 0, n - 1)];

        float min = sorted[0];
        float max = sorted[n - 1];
        float p10 = Pick(0.10), p25 = Pick(0.25), p50 = Pick(0.50);
        float p75 = Pick(0.75), p90 = Pick(0.90);

        // MeanDbfs is the log of the linear mean, NOT the mean of dBFS values,
        // which the silence floor drags too low.
        double meanLinear = 0;
        for (int i = 0; i < n; i++) meanLinear += sorted[i];
        meanLinear /= n;
        double meanDbfs = PcmConversion.ToDbfs((float)meanLinear);

        double durSec = n * 0.05; // 50 ms per sub-window

        return new MicrophoneTelemetryPayload(
            DurationSeconds: durSec,
            Samples:         n,
            MinDbfs:         PcmConversion.ToDbfs(min),
            P10Dbfs:         PcmConversion.ToDbfs(p10),
            P25Dbfs:         PcmConversion.ToDbfs(p25),
            P50Dbfs:         PcmConversion.ToDbfs(p50),
            P75Dbfs:         PcmConversion.ToDbfs(p75),
            P90Dbfs:         PcmConversion.ToDbfs(p90),
            MaxDbfs:         PcmConversion.ToDbfs(max),
            MeanRms:         meanLinear,
            MeanDbfs:        meanDbfs,
            TailRms:         tail.TailRms,
            TailDbfs:        tail.TailDbfs,
            TailState:       tail.TailState);
    }

    // The live capture emits one RMS value per sub-window of this size, the
    // cadence Compute assumes (durSec = n * 0.05). A series rebuilt from a
    // finished buffer (the DSP output) must use the same window so the two
    // payloads stay comparable.
    public const int SubWindowSamples = 800; // 50 ms @ 16 kHz

    // A trailing partial window is dropped, matching the live path, which only
    // emits whole sub-windows.
    public static System.Collections.Generic.IReadOnlyList<float> RmsSeries(
        System.ReadOnlySpan<float> pcm, int windowSamples)
    {
        if (windowSamples <= 0 || pcm.Length < windowSamples)
            return System.Array.Empty<float>();

        int nWindows = pcm.Length / windowSamples;
        var series = new float[nWindows];
        for (int w = 0; w < nWindows; w++)
        {
            double sumSq = 0;
            int start = w * windowSamples;
            for (int i = 0; i < windowSamples; i++)
            {
                double v = pcm[start + i];
                sumSq += v * v;
            }
            series[w] = (float)System.Math.Sqrt(sumSq / windowSamples);
        }
        return series;
    }
}
