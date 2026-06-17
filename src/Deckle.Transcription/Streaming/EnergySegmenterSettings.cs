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
//   The decay between RampStart and RampEnd follows a cubic Bézier easing — the
//   same function CSS cubic-bezier(x1,y1,x2,y2) defines — with fixed endpoints
//   (0,0) and (1,1) and two free control points. The four coordinates below ARE
//   those two control points. They span the whole space the old contrast / knee
//   knobs reached and more: pulling both handles into a corner gives a true
//   right-angle hug, the diagonal gives a straight decline.
//
//   HangoverCurveX1/Y1  — first control point (P1), each in [0, 1]. Its direction
//                         from (0,0) sets the curve's entry slope (Y1/X1).
//   HangoverCurveX2/Y2  — second control point (P2), each in [0, 1]. Its direction
//                         into (1,1) sets the exit slope ((1−Y2)/(1−X2)).
//
//   The default (0.85, 0.10, 0.90, 0.25) is the curve currently tuned in use:
//   the delay holds near the max through most of the ramp, then drops late.
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
    public int    HangoverRampStartMs { get; set; } = 15_000;
    public int    HangoverRampEndMs   { get; set; } = 120_000;
    public double HangoverCurveX1     { get; set; } = 0.85;
    public double HangoverCurveY1     { get; set; } = 0.10;
    public double HangoverCurveX2     { get; set; } = 0.90;
    public double HangoverCurveY2     { get; set; } = 0.25;
    public int    MarginMs            { get; set; } = 150;
    public int    MinUtteranceMs      { get; set; } = 250;

    // True while the hangover curve still equals the shipped default — i.e. the
    // user has not customized it. Compared against a fresh instance so the four
    // default control points live in exactly one place: the initializers above.
    public bool HasDefaultHangoverCurve()
    {
        var d = new EnergySegmenterSettings();
        return HangoverCurveX1 == d.HangoverCurveX1
            && HangoverCurveY1 == d.HangoverCurveY1
            && HangoverCurveX2 == d.HangoverCurveX2
            && HangoverCurveY2 == d.HangoverCurveY2;
    }
}
