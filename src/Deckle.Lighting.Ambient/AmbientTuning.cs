namespace Deckle.Lighting.Ambient;

// The eight values an Ambient preset fixes: how the sampled colour is graded
// (exposure, saturation, the Bézier brightness response) and how it moves
// (reaction speed, change threshold). This is the group Deckle.Presets stores
// as the Custom values and compares when it derives a selection.
//
// Left out on purpose: the brightness floor (a room choice: whether dark
// scenes may go dark, decided once per setup, never by a preset), the zone
// layout and band shape (the lamps' geometry), the capture rate and the
// display (machine and setup). None of them switches the selection to Custom.
public sealed record AmbientTuning(
    double ExposureEv,
    double SaturationBoost,
    double BrightnessCurveX1,
    double BrightnessCurveY1,
    double BrightnessCurveX2,
    double BrightnessCurveY2,
    double SmoothingAlpha,
    int ChangeThreshold)
{
    public static AmbientTuning From(AmbientSettings s) => new(
        s.ExposureEv,
        s.SaturationBoost,
        s.BrightnessCurveX1,
        s.BrightnessCurveY1,
        s.BrightnessCurveX2,
        s.BrightnessCurveY2,
        s.SmoothingAlpha,
        s.ChangeThreshold);

    public void ApplyTo(AmbientSettings s)
    {
        s.ExposureEv = ExposureEv;
        s.SaturationBoost = SaturationBoost;
        s.BrightnessCurveX1 = BrightnessCurveX1;
        s.BrightnessCurveY1 = BrightnessCurveY1;
        s.BrightnessCurveX2 = BrightnessCurveX2;
        s.BrightnessCurveY2 = BrightnessCurveY2;
        s.SmoothingAlpha = SmoothingAlpha;
        s.ChangeThreshold = ChangeThreshold;
    }
}
