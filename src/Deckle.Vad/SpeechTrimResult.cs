namespace Deckle.Vad;

// Result of SileroVad.Trim: the speech-only audio, plus how many distinct speech
// spans Silero found in the input. The count is an observability signal — it says
// how fragmented the chunk was (many spans = many internal pauses were cut out),
// which is what makes the trim's effect legible in the logs. Samples is empty (and
// SpeechSegments is 0) when no speech was found, the caller's drop signal.
public readonly record struct SpeechTrimResult(float[] Samples, int SpeechSegments);
