using Deckle.Composition;

namespace Deckle.Hud;

// Process-local source for the base stroke's own fields: sweep mask, arc
// rotation, the Recording and Rewriting variants and the digit-reveal
// clone. Together with HudMaterialTuning and HudGeometryTuning it covers
// every ConicArcStrokeConfig field. The countdown never reads it: its mask
// and contour stay independent.
public static class HudStrokeTuning
{
    private sealed record Snapshot(HudComposition.ConicArcStrokeConfig Config);
    private static Snapshot _current = new(new());
    public static HudComposition.ConicArcStrokeConfig Current => Volatile.Read(ref _current).Config;
    public static event Action? Changed;

    public static void Apply(HudComposition.ConicArcStrokeConfig config)
    {
        var stroke = CopyStroke(config, new());
        if (stroke.Equals(Current)) return;
        Volatile.Write(ref _current, new Snapshot(stroke));
        Changed?.Invoke();
    }

    public static HudComposition.ConicArcStrokeConfig ApplyTo(HudComposition.ConicArcStrokeConfig target)
        => CopyStroke(Current, target);

    // The live base stroke's config: the three sources layered on a target.
    public static HudComposition.ConicArcStrokeConfig ApplyAll(HudComposition.ConicArcStrokeConfig target)
        => HudGeometryTuning.ApplyTo(HudMaterialTuning.ApplyTo(ApplyTo(target)));

    private static HudComposition.ConicArcStrokeConfig CopyStroke(
        HudComposition.ConicArcStrokeConfig source, HudComposition.ConicArcStrokeConfig target) => target with
    {
        ConicSpanTurns = source.ConicSpanTurns,
        ConicLeadFadeTurns = source.ConicLeadFadeTurns,
        ConicTailFadeTurns = source.ConicTailFadeTurns,
        ConicFadeCurve = source.ConicFadeCurve,
        ArcMirror = source.ArcMirror,
        ArcPeriodSeconds = source.ArcPeriodSeconds,
        ArcDirection = source.ArcDirection,
        ArcPhaseTurns = source.ArcPhaseTurns,
        ArcEaseP1X = source.ArcEaseP1X,
        ArcEaseP1Y = source.ArcEaseP1Y,
        ArcEaseP2X = source.ArcEaseP2X,
        ArcEaseP2Y = source.ArcEaseP2Y,
        ArcMinSpeedFraction = source.ArcMinSpeedFraction,
        CloneCentreXFraction = source.CloneCentreXFraction,
        CloneCentreYFraction = source.CloneCentreYFraction,
        CloneOklchLightness = source.CloneOklchLightness,
        CloneOklchChroma = source.CloneOklchChroma,
        CloneHuePeriodSeconds = source.CloneHuePeriodSeconds,
        CloneHueDirection = source.CloneHueDirection,
        CloneArcPeriodSeconds = source.CloneArcPeriodSeconds,
        CloneArcDirection = source.CloneArcDirection,
        RewritingSaturation = source.RewritingSaturation,
        RewritingHueShiftTurns = source.RewritingHueShiftTurns,
        RewritingExposure = source.RewritingExposure,
        RewritingOpacity = source.RewritingOpacity,
        RewritingBlendSeconds = source.RewritingBlendSeconds,
        RecordingConicSpanTurns = source.RecordingConicSpanTurns,
        RecordingConicLeadFadeTurns = source.RecordingConicLeadFadeTurns,
        RecordingConicTailFadeTurns = source.RecordingConicTailFadeTurns,
        RecordingConicFadeCurve = source.RecordingConicFadeCurve,
        RecordingArcMirror = source.RecordingArcMirror,
        RecordingArcPhaseTurns = source.RecordingArcPhaseTurns,
        RecordingSaturationDark = source.RecordingSaturationDark,
        RecordingSaturationLight = source.RecordingSaturationLight,
        RecordingHueShiftTurns = source.RecordingHueShiftTurns,
        RecordingExposureDark = source.RecordingExposureDark,
        RecordingExposureLight = source.RecordingExposureLight,
        RecordingBlendSeconds = source.RecordingBlendSeconds,
        RecordingHuePeriodSeconds = source.RecordingHuePeriodSeconds,
    };
}
