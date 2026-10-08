namespace Deckle.Transcription;

// The eight values that decide when a pause cuts an utterance: the decision-
// delay envelope (HangoverMax, HangoverMin, RampStart, RampEnd) and the Bezier
// shape of its decay (the two control points). This is the group a cut preset
// fixes. ThresholdDbfs, MarginMs and MinUtteranceMs are excluded on purpose:
// they are detection rules, and the threshold depends on the microphone.
//
// Value equality of the record struct is what preset matching relies on
// (PresetFamily compares a preset's values to the current ones).
public readonly record struct CutCurveValues(
    int HangoverMaxMs,
    int HangoverMinMs,
    int HangoverRampStartMs,
    int HangoverRampEndMs,
    double CurveX1,
    double CurveY1,
    double CurveX2,
    double CurveY2)
{
    public static CutCurveValues From(EnergySegmenterSettings s) => new(
        s.HangoverMaxMs,
        s.HangoverMinMs,
        s.HangoverRampStartMs,
        s.HangoverRampEndMs,
        s.HangoverCurveX1,
        s.HangoverCurveY1,
        s.HangoverCurveX2,
        s.HangoverCurveY2);

    public void ApplyTo(EnergySegmenterSettings s)
    {
        s.HangoverMaxMs = HangoverMaxMs;
        s.HangoverMinMs = HangoverMinMs;
        s.HangoverRampStartMs = HangoverRampStartMs;
        s.HangoverRampEndMs = HangoverRampEndMs;
        s.HangoverCurveX1 = CurveX1;
        s.HangoverCurveY1 = CurveY1;
        s.HangoverCurveX2 = CurveX2;
        s.HangoverCurveY2 = CurveY2;
    }
}
