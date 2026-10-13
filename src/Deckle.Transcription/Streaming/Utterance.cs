namespace Deckle.Transcription;

// One unit emitted by the EnergySegmenter: a speech span bounded by detected
// silence or by the end of capture, handed to the ASR backend as ONE
// transcription call. Distinct from Whisper's output "segment"
// (TranscriptionSegment).
//
//   Samples  — mono 16 kHz float[-1, 1], the concatenation of the kept 50 ms
//              frames (voiced span + the trailing margin), trailing silence
//              beyond the margin already dropped.
//   Index    — 0-based emission order within one recording.
//   StartSec — onset of the utterance relative to the start of capture.
//   EndSec   — end of the kept span (StartSec + kept duration).
//   EndedOnSilence — true when the segmenter cut on a detected silence (a real
//              pause the speaker made), false when the span was closed by the
//              end-of-capture Flush. A silence cut marks a paragraph boundary
//              in the assembled text; a Flush cut does not.
public sealed record Utterance(
    float[] Samples,
    int Index,
    double StartSec,
    double EndSec,
    bool EndedOnSilence);
