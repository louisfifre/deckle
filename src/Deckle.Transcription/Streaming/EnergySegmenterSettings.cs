namespace Deckle.Transcription;

// Parameters of the energy segmenter. Auto-properties so the POCO round-trips
// cleanly through JsonSettingsStore. The segmenter works in 50 ms frames, so
// every duration below resolves to a whole frame count. The level that makes a
// frame voiced is not here: it is the Audio module's voice level
// (CaptureSettings.VoiceLevelDbfs), passed to the segmenter beside these.
//
//   HangoverMaxMs      — DECISION delay at the start of an utterance: how much
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
//   those two control points: pulling both handles into a corner gives a true
//   right-angle hug, the diagonal gives a straight decline.
//
//   HangoverCurveX1/Y1  — first control point (P1), each in [0, 1]. Its direction
//                         from (0,0) sets the curve's entry slope (Y1/X1).
//   HangoverCurveX2/Y2  — second control point (P2), each in [0, 1]. Its direction
//                         into (1,1) sets the exit slope ((1−Y2)/(1−X2)).
//
//   The default curve holds the delay near the max through most of the ramp,
//   then drops late.
//   MarginMs            — CUT POSITION: the kept span ends MarginMs after the
//                         last voiced frame. Distinct from hangover — the silence
//                         between the margin and the hangover expiry is dropped.
//
//   The floor under which an utterance's voiced extent is a blip is not here
//   either: it is StreamingSettings.MinSpeechMs, which Silero reads too.
public sealed class EnergySegmenterSettings
{
    public int    HangoverMaxMs       { get; set; } = 5_000;
    public int    HangoverMinMs       { get; set; } = 500;
    public int    HangoverRampStartMs { get; set; } = 15_000;
    public int    HangoverRampEndMs   { get; set; } = 120_000;
    public double HangoverCurveX1     { get; set; } = 0.85;
    public double HangoverCurveY1     { get; set; } = 0.10;
    public double HangoverCurveX2     { get; set; } = 0.90;
    public double HangoverCurveY2     { get; set; } = 0.25;
    public int    MarginMs            { get; set; } = 150;
}
