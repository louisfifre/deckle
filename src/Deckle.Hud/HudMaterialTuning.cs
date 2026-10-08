using Deckle.Composition;

namespace Deckle.Hud;

// One in-process material source for the Playground, processing stroke and
// countdown. Mask geometry and replay commands never enter this source.
public static class HudMaterialTuning
{
    private sealed record Snapshot(HudComposition.ConicArcStrokeConfig Config);
    private static Snapshot _current = new(new());
    public static HudComposition.ConicArcStrokeConfig Current => Volatile.Read(ref _current).Config;
    public static event Action? Changed;

    public static void Apply(HudComposition.ConicArcStrokeConfig config)
    {
        var material = CopyMaterial(config, new());
        if (material.Equals(Current)) return;
        Volatile.Write(ref _current, new Snapshot(material));
        Changed?.Invoke();
    }

    public static HudComposition.ConicArcStrokeConfig ApplyTo(HudComposition.ConicArcStrokeConfig target)
        => CopyMaterial(Current, target);

    private static HudComposition.ConicArcStrokeConfig CopyMaterial(
        HudComposition.ConicArcStrokeConfig source, HudComposition.ConicArcStrokeConfig target) => target with
    {
        OklchLightness = source.OklchLightness,
        OklchChroma = source.OklchChroma,
        HueStart = source.HueStart,
        HueRange = source.HueRange,
        WedgeCount = source.WedgeCount,
        HuePeriodSeconds = source.HuePeriodSeconds,
        HueDirection = source.HueDirection,
        HuePhaseTurns = source.HuePhaseTurns,
        HueEaseP1X = source.HueEaseP1X,
        HueEaseP1Y = source.HueEaseP1Y,
        HueEaseP2X = source.HueEaseP2X,
        HueEaseP2Y = source.HueEaseP2Y,
        HueMinSpeedFraction = source.HueMinSpeedFraction,
        TranscribingSaturationDark = source.TranscribingSaturationDark,
        TranscribingSaturationLight = source.TranscribingSaturationLight,
        TranscribingHueShiftTurns = source.TranscribingHueShiftTurns,
        TranscribingExposureDark = source.TranscribingExposureDark,
        TranscribingExposureLight = source.TranscribingExposureLight,
        TranscribingOpacity = source.TranscribingOpacity,
        TranscribingBlendSeconds = source.TranscribingBlendSeconds,
    };
}
