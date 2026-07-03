namespace Deckle.Lighting.Ambient;

// Per-mode preset values. Applied onto an AmbientSettings instance
// when the user picks a preset in the Settings page or the Playground
// dropdown. Custom is intentionally absent — picking Custom doesn't
// reset anything, it just labels the current values as user-owned so
// a later preset switch can detect "the user has a custom tuning I'm
// about to overwrite".
//
// The values below are starting points : Louis will calibrate them
// once the underlying knobs (smoothing, curves) feel right. Until
// then they're plausible defaults that differ enough between modes
// to be visibly distinct on real content.

internal static class AmbientModePresets
{
    public static void Apply(AmbientMode mode, AmbientSettings target)
    {
        switch (mode)
        {
            case AmbientMode.Game:
                // Vivid, quick — matches the original V0 feel.
                target.ExposureEv           = 0.5;
                target.SaturationBoost      = 1.3;
                target.MinBrightnessEnabled = true;
                target.MinBrightness        = 100;
                target.BrightnessCurveX1    = 0.33;
                target.BrightnessCurveY1    = 0.33;
                target.BrightnessCurveX2    = 0.67;
                target.BrightnessCurveY2    = 0.67;
                target.SmoothingAlpha       = 0.40;
                target.ChangeThreshold      = 6;
                break;

            case AmbientMode.Movie:
                // Softened, long damping — cinematic mood lighting.
                target.ExposureEv           = 0.0;
                target.SaturationBoost      = 0.9;
                target.MinBrightnessEnabled = true;
                target.MinBrightness        = 60;
                target.BrightnessCurveX1    = 0.42;
                target.BrightnessCurveY1    = 0.08;
                target.BrightnessCurveX2    = 0.58;
                target.BrightnessCurveY2    = 0.92;
                target.SmoothingAlpha       = 0.15;
                target.ChangeThreshold      = 8;
                break;

            case AmbientMode.Ambient:
                // Very smooth, low saturation — never feels like the
                // room competes with the screen.
                target.ExposureEv           = -0.5;
                target.SaturationBoost      = 0.7;
                target.MinBrightnessEnabled = true;
                target.MinBrightness        = 40;
                target.BrightnessCurveX1    = 0.18;
                target.BrightnessCurveY1    = 0.55;
                target.BrightnessCurveX2    = 0.40;
                target.BrightnessCurveY2    = 0.90;
                target.SmoothingAlpha       = 0.08;
                target.ChangeThreshold      = 10;
                break;

            case AmbientMode.Custom:
            default:
                // Custom carries no preset — current tunings stay put.
                return;
        }
    }
}
