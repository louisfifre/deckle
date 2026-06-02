namespace Deckle.Transcription.Streaming;

// Parameters of the energy segmenter — the threshold-on-RMS state machine that
// places utterance boundaries on the live capture stream. Auto-properties so the
// POCO round-trips cleanly through JsonSettingsStore.
//
// Starting values are reference points to be refined by measurement, NOT asserted
// as optimal (drawn from VAD / endpointing literature, see
// docs/research/research--energy-segmenter-params--2026-06-02.md). The segmenter
// works in 50 ms frames, so every duration below resolves to a whole frame count.
//
//   ThresholdDbfs   — a frame counts as voiced when its RAW linear RMS is at or
//                     above this level. -45 dBFS leaves headroom for quieter mics
//                     while staying above the broken-mic / silence floor.
//   HangoverMs      — DECISION delay: how much trailing silence to wait, after the
//                     last voiced frame, before declaring the utterance ended.
//                     Guards against cutting on a brief intra-phrase pause.
//   MarginMs        — CUT POSITION: the kept span ends MarginMs after the last
//                     voiced frame. Distinct from hangover — the silence between
//                     the margin and the hangover expiry is dropped.
//   MinUtteranceMs  — utterances whose VOICED extent is shorter than this are
//                     dropped as noise blips rather than emitted.
//   MaxUtteranceMs  — safety ceiling: an utterance reaching this length is
//                     force-flushed (emitted, no margin trim) and a new one starts
//                     — capture is NOT stopped. Do not confuse with the host's
//                     MaxRecordingDurationSeconds, which stops capture.
//   DegressiveHangover — optional curve that shortens the hangover as an utterance
//                     grows. Hook reserved; OFF by default (no behaviour yet).
public sealed class EnergySegmenterSettings
{
    public double ThresholdDbfs { get; set; } = -45.0;
    public int    HangoverMs    { get; set; } = 400;
    public int    MarginMs      { get; set; } = 150;
    public int    MinUtteranceMs { get; set; } = 250;
    public int    MaxUtteranceMs { get; set; } = 25_000;
    public bool   DegressiveHangover { get; set; } = false;
}
