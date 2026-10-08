namespace Deckle.Transcription;

// Live capture feedback, in audio time. Completion is cumulative so a consumer
// can never lose a cut when speech resumes before the next rendered frame.
public readonly record struct UtteranceProgress(
    bool IsActive,
    bool IsPausing,
    int SilenceElapsedMs,
    int SilenceRequiredMs,
    int CompletedUtterances)
{
    public float Fraction => IsActive && IsPausing && SilenceRequiredMs > 0
        ? Math.Clamp((float)SilenceElapsedMs / SilenceRequiredMs, 0f, 1f)
        : 0f;
}
