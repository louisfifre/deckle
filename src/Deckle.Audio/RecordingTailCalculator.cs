using Deckle.Audio;

namespace Deckle.Audio;

// Pure compute over a recording's RMS series, one value per 50 ms sub-window:
// the tail diagnostic that the operational log carries at the end of a take.
public static class RecordingTailCalculator
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

        // Tail-600 ms diagnostic.
        //
        // Root-mean-square of the last 12 sub-windows: sums the squared
        // sub-window RMS values and re-roots, the correct way to combine RMS
        // samples (NOT a plain mean of RMS). TailState is the word the log
        // carries: active, still speaking at Stop (the last words may be
        // clipped), or silent (the capture ended cleanly).
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
}