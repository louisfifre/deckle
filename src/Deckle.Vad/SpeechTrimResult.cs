namespace Deckle.Vad;

// SpeechSegments is an observability signal: it says how fragmented the chunk
// was (many spans = many internal pauses were cut out). Samples is empty (and
// SpeechSegments is 0) when no speech was found, the caller's drop signal.
//
// FirstSpeechSample and LastSpeechSample bound the detected speech inside the
// input buffer (padding included): everything before the first and after the
// last was cut, whatever sat between the spans. Both are 0 when no speech was
// found. They exist so the log can tell a tail the trim removed from a tail
// the segmenter never kept.
public readonly record struct SpeechTrimResult(
    float[] Samples,
    int SpeechSegments,
    int FirstSpeechSample = 0,
    int LastSpeechSample = 0);
