namespace Deckle.Vad;

// SpeechSegments is an observability signal: it says how fragmented the chunk
// was (many spans = many internal pauses were cut out). Samples is empty (and
// SpeechSegments is 0) when no speech was found, the caller's drop signal.
public readonly record struct SpeechTrimResult(float[] Samples, int SpeechSegments);
