namespace Deckle.Transcription;

// Parameters of the energy segmenter — the threshold-on-RMS state machine that
// places utterance boundaries on the live capture stream. Auto-properties so the
// POCO round-trips cleanly through JsonSettingsStore.
//
// The segmenter works in 50 ms frames, so every duration below resolves to a
// whole frame count.
//
//   ThresholdDbfs       — a frame counts as voiced when its RAW linear RMS is at
//                         or above this level. -45 dBFS leaves headroom for
//                         quieter mics while staying above the broken-mic floor.
//   HangoverMaxMs       — DECISION delay at the start of an utterance: how much
//                         trailing silence to wait, after the last voiced frame,
//                         before declaring the utterance ended. Long by default
//                         so only a true paragraph-break silence cuts.
//   HangoverMinMs       — floor the decision delay decays to as the utterance
//                         grows past HangoverRampEndMs. Never below this — a
//                         micro-pause shorter than this is treated as intra-word.
//   HangoverRampStartMs — utterance length above which the decision delay starts
//                         shrinking from HangoverMaxMs toward HangoverMinMs.
//                         Below this, the delay stays at HangoverMaxMs.
//   HangoverRampEndMs   — utterance length at and above which the delay equals
//                         HangoverMinMs. Between RampStart and RampEnd the delay
//                         eases from Max to Min along the shaped curve below.
//
//   The decay between RampStart and RampEnd is a slope-integral curve: it declines
//   from the start of the ramp — continuously, with no built-in plateau (whole-
//   frame rounding aside) — and its slope ramps from entry to exit under three
//   independent shape knobs.
//
//   HangoverContrast    — ratio of the exit slope to the entry slope. 1 is a
//                         straight line (constant decline). Above 1 the curve is
//                         gentle at entry then steepens toward a cliff at RampEnd;
//                         below 1 is the mirror (steep entry, gentle tail).
//   HangoverPosition    — where the knee sits along the ramp, in [0, 1]: 0 puts
//                         the slope change at the very start (ease-out feel), 1 at
//                         the very end (ease-in feel), ~0.8 keeps most of the ramp
//                         gentle then breaks late.
//   HangoverSharpness   — how abrupt the knee is. Low values blend the slope
//                         change over the whole ramp; high values make it a sharp
//                         corner at HangoverPosition.
//   MarginMs            — CUT POSITION: the kept span ends MarginMs after the
//                         last voiced frame. Distinct from hangover — the silence
//                         between the margin and the hangover expiry is dropped.
//   MinUtteranceMs      — utterances whose VOICED extent is shorter than this
//                         are dropped as noise blips rather than emitted.
public sealed class EnergySegmenterSettings
{
    public double ThresholdDbfs       { get; set; } = -45.0;
    public int    HangoverMaxMs       { get; set; } = 5_000;
    public int    HangoverMinMs       { get; set; } = 500;
    public int    HangoverRampStartMs { get; set; } = 60_000;
    public int    HangoverRampEndMs   { get; set; } = 180_000;
    public double HangoverContrast    { get; set; } = 3.0;
    public double HangoverPosition    { get; set; } = 0.8;
    public double HangoverSharpness   { get; set; } = 20.0;
    public int    MarginMs            { get; set; } = 150;
    public int    MinUtteranceMs      { get; set; } = 250;
}
