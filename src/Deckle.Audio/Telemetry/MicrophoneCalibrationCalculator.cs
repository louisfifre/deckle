namespace Deckle.Audio;

// Pure compute: derives a fresh (MinDbfs, MaxDbfs) HUD level window from recent
// MicrophoneTelemetryPayload samples. The caller (TranscriptionEngine) owns the
// save, the live apply and the log, and skips them when ShouldUpdate is false.
//
// The floor reads p25, not p10: a noise gate that cuts to digital silence would
// drag a lower percentile into the gated floor.
public static class MicrophoneCalibrationCalculator
{
    // NewMinDbfs / NewMaxDbfs are meaningful only when ShouldUpdate is true.
    public readonly record struct CalibrationResult(
        bool    ShouldUpdate,
        float   NewMinDbfs,
        float   NewMaxDbfs,
        string? RejectReason);

    // The caller owns the ring buffer and calls only once it holds
    // LevelWindow.AutoCalibrationSamples entries; no count check happens here.
    public static CalibrationResult Compute(
        System.Collections.Generic.IReadOnlyCollection<MicrophoneTelemetryPayload> samples,
        float currentMinDbfs,
        float currentMaxDbfs)
    {
        if (samples.Count == 0)
            return new CalibrationResult(false, 0, 0, "empty buffer");

        // Median across the buffer — avoids one rogue session pulling the
        // window in either direction.
        var p25s = System.Linq.Enumerable.OrderBy(
            System.Linq.Enumerable.Select(samples, p => p.P25Dbfs), v => v).ToArray();
        var p90s = System.Linq.Enumerable.OrderBy(
            System.Linq.Enumerable.Select(samples, p => p.P90Dbfs), v => v).ToArray();
        double medianP25 = p25s[p25s.Length / 2];
        double medianP90 = p90s[p90s.Length / 2];

        // -5 dB / +5 dB margins keep the HUD from sitting flush against
        // the user's measured percentiles — peaks above the median p90
        // still saturate cleanly, and the floor doesn't trigger on the
        // very edge of the silence band.
        double newMin = System.Math.Round(medianP25 - 5.0);
        double newMax = System.Math.Round(medianP90 + 5.0);

        // Floor guard — even with p25, a session dominated by gate-induced
        // silence can drag the median into the digital floor zone. Clamp
        // at -75 dBFS so we never calibrate the HUD to react to gated
        // silence. The user can still go lower manually via the slider
        // if they want to capture a quieter mic.
        if (newMin < -75) newMin = -75;

        // Sanity: dBFS window must span at least 10 dB to give the HUD a
        // visible dynamic range. A pathological all-silence buffer would
        // produce a near-flat window — skip and wait for richer sessions.
        if (newMax - newMin < 10)
            return new CalibrationResult(false, 0, 0, "window spread < 10 dB");

        // Clamp to the slider domains so the persisted values stay editable.
        newMin = System.Math.Clamp(newMin, -90, -10);
        newMax = System.Math.Clamp(newMax, -60, -10);
        if (newMax <= newMin)
            return new CalibrationResult(false, 0, 0, "clamped window collapsed");

        // Check whether anything changed — avoid log spam on stable mics.
        bool changed = System.Math.Abs(currentMinDbfs - newMin) >= 0.5f
                    || System.Math.Abs(currentMaxDbfs - newMax) >= 0.5f;
        if (!changed)
            return new CalibrationResult(false, (float)newMin, (float)newMax, "no change");

        return new CalibrationResult(true, (float)newMin, (float)newMax, null);
    }
}
