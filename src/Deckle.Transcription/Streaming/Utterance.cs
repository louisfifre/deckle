namespace Deckle.Transcription.Streaming;

// One unit emitted by the EnergySegmenter: a speech span bounded by detected
// silence (or by the safety ceiling), and the atomic audio span handed to the
// ASR backend for ONE transcription call. See CONTEXT.md § Speech segmentation
// for the term and its distinction from Whisper's output "segment".
//
//   Samples  — mono 16 kHz float[-1, 1], the concatenation of the kept 50 ms
//              frames (voiced span + the trailing margin), trailing silence
//              beyond the margin already dropped.
//   Index    — 0-based emission order within one recording, for ordering/logs.
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
