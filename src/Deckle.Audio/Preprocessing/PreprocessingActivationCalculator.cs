using System.Collections.Generic;
using System.Linq;
using Deckle.Audio.Telemetry;

namespace Deckle.Audio.Preprocessing;

// ── PreprocessingActivationCalculator ──────────────────────────────────────
//
// Pure compute behind the deferred-activation model — calqued on
// MicrophoneCalibrationCalculator (the HUD's dBFS-window auto-calibration):
// same ring-of-N-recordings shape, same "median across the buffer so one
// rogue take doesn't swing the decision" discipline.
//
// The question it answers: once the user has opted in, does this microphone
// actually benefit from the DSP, or is it already at a healthy level? The
// answer is read from the SIGNAL, not from a WER measurement — which is why
// it can ship now and stand on its own. The "delta" is literally the makeup
// gain this mic would receive: target RMS minus the mic's median speech
// level. A mic sitting clearly below target gets a real lift → Active. A mic
// already near target gets nothing useful → Dormant (the user is told their
// mic doesn't need it).
//
// The activation THRESHOLD (how many dB of deficit warrants turning on) is
// the one piece marked provisional: DefaultActivationDeltaDb is an engineer's
// guess, to be grounded by the WER bench and re-tuned. Everything else here
// is deterministic signal arithmetic.
public static class PreprocessingActivationCalculator
{
    // Provisional. A median speech level ≥ this many dB below the makeup
    // target means the DSP brings a meaningful lift. ~6 dB ≈ "noticeably
    // quiet, not just a touch under". To be refined once the bench gives a
    // WER-vs-input-level curve.
    public const double DefaultActivationDeltaDb = 6.0;

    public readonly record struct ActivationResult(
        bool Decided,
        PreprocessingActivation State,
        double MedianDeltaDb,
        string Reason);

    // Evaluate the ring. Returns Decided=false (stay Calibrating) until the
    // buffer holds `needed` samples; then Active or Dormant on the delta.
    //
    // The mic's speech level is taken from MeanDbfs (log of the mean linear
    // RMS — the per-recording field already computed for telemetry), median
    // across the ring. delta = target - median: positive means the mic sits
    // below target by that many dB.
    public static ActivationResult Evaluate(
        IReadOnlyCollection<MicrophoneTelemetryPayload> samples,
        int needed,
        double targetRmsDbfs,
        double activationDeltaDb)
    {
        if (samples.Count < needed)
        {
            return new ActivationResult(false, PreprocessingActivation.Calibrating, 0.0, "collecting");
        }

        var means = samples.Select(p => p.MeanDbfs).OrderBy(v => v).ToArray();
        double medianMean = means[means.Length / 2];
        double delta = targetRmsDbfs - medianMean;

        return delta >= activationDeltaDb
            ? new ActivationResult(true, PreprocessingActivation.Active,  delta, "mic below target")
            : new ActivationResult(true, PreprocessingActivation.Dormant, delta, "mic already adequate");
    }
}
