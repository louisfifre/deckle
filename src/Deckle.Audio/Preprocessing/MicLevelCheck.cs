using System;

namespace Deckle.Audio.Preprocessing;

// ── MicLevelCheck ──────────────────────────────────────────────────────────
//
// The indicator behind the Recording-page mic check. It answers, in plain
// terms, the only question that matters for the black-box toggle: is your
// microphone quiet enough that the pre-processing would help, or is it already
// at a good level? No transcription, no WER — just the signal, which speaks for
// itself.
//
// It reuses the shipped DSP rather than estimating: Process runs the real chain
// on the captured sample, so the "after" level is exactly what transcription
// would receive. The advice is read from the deficit (how far below target the
// raw level sits), with the same threshold the deferred-activation calculator
// uses — one source of truth for "this mic is meaningfully quiet".
//
// The decision is never taken for the user: the check proposes, the toggle is
// the user's call. The thresholds are provisional engineer's guesses, marked.
public static class MicLevelCheck
{
    // Deficit (target − measured level) at or above which the lift is clearly
    // worth recommending. ~6 dB ≈ "noticeably quiet, not just a touch under".
    // Provisional — an engineer's guess, to be grounded by measurement.
    public const double RecommendDeltaDb = 6.0;

    // Below RecommendDeltaDb but above this, the lift is marginal — the user is
    // told it would help "a little". At or below it, the mic is already fine.
    public const double MarginalDeltaDb = 2.0;

    public static MicLevelAssessment Assess(float[] pcm, PreprocessingSettings settings)
    {
        if (pcm is null || pcm.Length == 0)
        {
            return new MicLevelAssessment(false, -120.0, -120.0, settings.TargetRmsDbfs, 0.0, PreprocessingAdvice.NotNeeded);
        }

        // Run the actual DSP: InputRmsDbfs is the raw level, OutputRmsDbfs is
        // what the backend would get, MakeupGainDb the lift that was applied.
        PreprocessingResult r = TranscriptionPreprocessor.Process(pcm, settings);

        double deficit = settings.TargetRmsDbfs - r.InputRmsDbfs;
        PreprocessingAdvice advice =
            deficit >= RecommendDeltaDb ? PreprocessingAdvice.Recommended
          : deficit >= MarginalDeltaDb ? PreprocessingAdvice.Marginal
          :                              PreprocessingAdvice.NotNeeded;

        return new MicLevelAssessment(
            HasSignal:        true,
            RawRmsDbfs:       r.InputRmsDbfs,
            ProcessedRmsDbfs: r.OutputRmsDbfs,
            TargetRmsDbfs:    settings.TargetRmsDbfs,
            MakeupGainDb:     r.MakeupGainDb,
            Advice:           advice);
    }
}

// What the check tells the user. NotNeeded ranks first so it is the natural
// default for the no-signal / already-good cases.
public enum PreprocessingAdvice
{
    NotNeeded,
    Marginal,
    Recommended,
}

// The mic-check result. RawRmsDbfs is the captured level; ProcessedRmsDbfs is
// where the DSP would land it (≈ target when a lift is applied); MakeupGainDb is
// that lift. HasSignal is false when nothing usable was captured (mic error or
// silence) — the UI then shows a "couldn't read the mic" state, not a verdict.
public readonly record struct MicLevelAssessment(
    bool HasSignal,
    double RawRmsDbfs,
    double ProcessedRmsDbfs,
    double TargetRmsDbfs,
    double MakeupGainDb,
    PreprocessingAdvice Advice);
