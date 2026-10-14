using static Deckle.Composition.HudComposition;

namespace Deckle.Playground;

// Mutable shadow of HudComposition.ConicArcStrokeConfig. The shipping config
// is a readonly struct with init-only properties so a stroke's baked geometry
// cannot change after creation; the playground mutates this class from its
// sliders and projects it into a fresh config on every rebuild.
//
// Field defaults are read from a default-constructed shipping struct, never
// restated here, so a fresh model holds exactly the compiled defaults; the
// page then lays the stored HUD settings over it with Load. A value that
// proves right in the playground is saved from the page's bar into those
// settings, or promoted to the struct's default; it never becomes a
// playground-only default in this file.
internal sealed class TuningModel
{
    private static readonly ConicArcStrokeConfig Defaults = new();

    // ── HUD geometry (shared by the stroke and the countdown previews) ──
    public float StrokeThickness = Defaults.StrokeThickness;
    public float InsetDip        = Defaults.InsetDip;

    public void LoadGeometry(ConicArcStrokeConfig source)
    {
        StrokeThickness = source.StrokeThickness;
        InsetDip = source.InsetDip;
    }

    // Every field, mirror of ToConfig; TuningModelTests catches a field left
    // out of either.
    public void Load(ConicArcStrokeConfig source)
    {
        StrokeThickness = source.StrokeThickness;
        InsetDip        = source.InsetDip;
        OklchLightness  = source.OklchLightness;
        OklchChroma     = source.OklchChroma;
        HueStart        = source.HueStart;
        HueRange        = source.HueRange;
        WedgeCount      = source.WedgeCount;

        HuePeriodSeconds    = source.HuePeriodSeconds;
        HueDirection        = source.HueDirection;
        HuePhaseTurns       = source.HuePhaseTurns;
        HueEaseP1X          = source.HueEaseP1X;
        HueEaseP1Y          = source.HueEaseP1Y;
        HueEaseP2X          = source.HueEaseP2X;
        HueEaseP2Y          = source.HueEaseP2Y;
        HueMinSpeedFraction = source.HueMinSpeedFraction;

        ConicSpanTurns     = source.ConicSpanTurns;
        ConicLeadFadeTurns = source.ConicLeadFadeTurns;
        ConicTailFadeTurns = source.ConicTailFadeTurns;
        ConicFadeCurve     = source.ConicFadeCurve;
        ArcMirror          = source.ArcMirror;

        ArcPeriodSeconds    = source.ArcPeriodSeconds;
        ArcDirection        = source.ArcDirection;
        ArcPhaseTurns       = source.ArcPhaseTurns;
        ArcEaseP1X          = source.ArcEaseP1X;
        ArcEaseP1Y          = source.ArcEaseP1Y;
        ArcEaseP2X          = source.ArcEaseP2X;
        ArcEaseP2Y          = source.ArcEaseP2Y;
        ArcMinSpeedFraction = source.ArcMinSpeedFraction;

        CloneCentreXFraction = source.CloneCentreXFraction;
        CloneCentreYFraction = source.CloneCentreYFraction;

        CloneOklchLightness = source.CloneOklchLightness;
        CloneOklchChroma    = source.CloneOklchChroma;

        CloneHuePeriodSeconds = source.CloneHuePeriodSeconds;
        CloneHueDirection     = source.CloneHueDirection;
        CloneArcPeriodSeconds = source.CloneArcPeriodSeconds;
        CloneArcDirection     = source.CloneArcDirection;

        RewritingSaturation    = source.RewritingSaturation;
        RewritingHueShiftTurns = source.RewritingHueShiftTurns;
        RewritingExposure      = source.RewritingExposure;
        RewritingOpacity       = source.RewritingOpacity;
        RewritingBlendSeconds  = source.RewritingBlendSeconds;

        TranscribingSaturationDark  = source.TranscribingSaturationDark;
        TranscribingSaturationLight = source.TranscribingSaturationLight;
        TranscribingHueShiftTurns   = source.TranscribingHueShiftTurns;
        TranscribingExposureDark    = source.TranscribingExposureDark;
        TranscribingExposureLight   = source.TranscribingExposureLight;
        TranscribingOpacity         = source.TranscribingOpacity;
        TranscribingBlendSeconds    = source.TranscribingBlendSeconds;

        RecordingConicSpanTurns     = source.RecordingConicSpanTurns;
        RecordingConicLeadFadeTurns = source.RecordingConicLeadFadeTurns;
        RecordingConicTailFadeTurns = source.RecordingConicTailFadeTurns;
        RecordingConicFadeCurve     = source.RecordingConicFadeCurve;
        RecordingArcMirror          = source.RecordingArcMirror;
        RecordingArcPhaseTurns      = source.RecordingArcPhaseTurns;

        RecordingSaturationDark   = source.RecordingSaturationDark;
        RecordingSaturationLight  = source.RecordingSaturationLight;
        RecordingHueShiftTurns    = source.RecordingHueShiftTurns;
        RecordingExposureDark     = source.RecordingExposureDark;
        RecordingExposureLight    = source.RecordingExposureLight;
        RecordingBlendSeconds     = source.RecordingBlendSeconds;
        RecordingHuePeriodSeconds = source.RecordingHuePeriodSeconds;
    }

    // ── Colour palette (paint-time, OKLCh) ──────────────────────────────
    public float OklchLightness = Defaults.OklchLightness;
    public float OklchChroma    = Defaults.OklchChroma;
    public float HueStart       = Defaults.HueStart;
    public float HueRange       = Defaults.HueRange;
    public int   WedgeCount     = Defaults.WedgeCount;

    // ── Hue rotation ────────────────────────────────────────────────────
    public double HuePeriodSeconds = Defaults.HuePeriodSeconds;
    public float  HueDirection     = Defaults.HueDirection;
    public float  HuePhaseTurns    = Defaults.HuePhaseTurns;
    public float  HueEaseP1X       = Defaults.HueEaseP1X;
    public float  HueEaseP1Y       = Defaults.HueEaseP1Y;
    public float  HueEaseP2X       = Defaults.HueEaseP2X;
    public float  HueEaseP2Y       = Defaults.HueEaseP2Y;
    public float  HueMinSpeedFraction = Defaults.HueMinSpeedFraction;

    // ── Arc mask shape ──────────────────────────────────────────────────
    public float ConicSpanTurns     = Defaults.ConicSpanTurns;
    public float ConicLeadFadeTurns = Defaults.ConicLeadFadeTurns;
    public float ConicTailFadeTurns = Defaults.ConicTailFadeTurns;
    public float ConicFadeCurve     = Defaults.ConicFadeCurve;
    public bool  ArcMirror          = Defaults.ArcMirror;

    // ── Arc rotation ────────────────────────────────────────────────────
    public double ArcPeriodSeconds = Defaults.ArcPeriodSeconds;
    public float  ArcDirection     = Defaults.ArcDirection;
    public float  ArcPhaseTurns    = Defaults.ArcPhaseTurns;
    public float  ArcEaseP1X       = Defaults.ArcEaseP1X;
    public float  ArcEaseP1Y       = Defaults.ArcEaseP1Y;
    public float  ArcEaseP2X       = Defaults.ArcEaseP2X;
    public float  ArcEaseP2Y       = Defaults.ArcEaseP2Y;
    public float  ArcMinSpeedFraction = Defaults.ArcMinSpeedFraction;

    // ── Clone-cone placement (digit reveal + ConicClone preview) ─────────
    // Fraction of the host frame; (0.5, 0.5) = centred.
    public float CloneCentreXFraction = Defaults.CloneCentreXFraction;
    public float CloneCentreYFraction = Defaults.CloneCentreYFraction;

    // ── Clone-cone palette ──────────────────────────────────────────────
    public float CloneOklchLightness = Defaults.CloneOklchLightness;
    public float CloneOklchChroma    = Defaults.CloneOklchChroma;

    // ── Clone-cone rotation ─────────────────────────────────────────────
    public double CloneHuePeriodSeconds = Defaults.CloneHuePeriodSeconds;
    public float  CloneHueDirection     = Defaults.CloneHueDirection;
    public double CloneArcPeriodSeconds = Defaults.CloneArcPeriodSeconds;
    public float  CloneArcDirection     = Defaults.CloneArcDirection;

    // ── Rewriting variant ───────────────────────────────────────────────
    public float  RewritingSaturation    = Defaults.RewritingSaturation;
    public float  RewritingHueShiftTurns = Defaults.RewritingHueShiftTurns;
    public float  RewritingExposure      = Defaults.RewritingExposure;
    public float  RewritingOpacity       = Defaults.RewritingOpacity;
    public double RewritingBlendSeconds  = Defaults.RewritingBlendSeconds;

    // ── Transcribing variant ────────────────────────────────────────────
    public float  TranscribingSaturationDark  = Defaults.TranscribingSaturationDark;
    public float  TranscribingSaturationLight = Defaults.TranscribingSaturationLight;
    public float  TranscribingHueShiftTurns   = Defaults.TranscribingHueShiftTurns;
    public float  TranscribingExposureDark    = Defaults.TranscribingExposureDark;
    public float  TranscribingExposureLight   = Defaults.TranscribingExposureLight;
    public float  TranscribingOpacity         = Defaults.TranscribingOpacity;
    public double TranscribingBlendSeconds    = Defaults.TranscribingBlendSeconds;

    // ── Recording variant — paint-time ──────────────────────────────────
    public float RecordingConicSpanTurns     = Defaults.RecordingConicSpanTurns;
    public float RecordingConicLeadFadeTurns = Defaults.RecordingConicLeadFadeTurns;
    public float RecordingConicTailFadeTurns = Defaults.RecordingConicTailFadeTurns;
    public float RecordingConicFadeCurve     = Defaults.RecordingConicFadeCurve;
    public bool  RecordingArcMirror          = Defaults.RecordingArcMirror;
    public float RecordingArcPhaseTurns      = Defaults.RecordingArcPhaseTurns;

    // ── Recording variant — runtime ─────────────────────────────────────
    public float  RecordingSaturationDark  = Defaults.RecordingSaturationDark;
    public float  RecordingSaturationLight = Defaults.RecordingSaturationLight;
    public float  RecordingHueShiftTurns   = Defaults.RecordingHueShiftTurns;
    public float  RecordingExposureDark    = Defaults.RecordingExposureDark;
    public float  RecordingExposureLight   = Defaults.RecordingExposureLight;
    public double RecordingBlendSeconds    = Defaults.RecordingBlendSeconds;
    public double RecordingHuePeriodSeconds = Defaults.RecordingHuePeriodSeconds;

    // Explicit field-by-field, no reflection: a renamed or removed struct
    // property breaks the build, and TuningModelTests catches a field left
    // out of the projection.
    public ConicArcStrokeConfig ToConfig() => new()
    {
        StrokeThickness = StrokeThickness,
        InsetDip = InsetDip,
        OklchLightness    = OklchLightness,
        OklchChroma       = OklchChroma,
        HueStart          = HueStart,
        HueRange          = HueRange,
        WedgeCount        = WedgeCount,

        HuePeriodSeconds  = HuePeriodSeconds,
        HueDirection      = HueDirection,
        HuePhaseTurns     = HuePhaseTurns,
        HueEaseP1X        = HueEaseP1X,
        HueEaseP1Y        = HueEaseP1Y,
        HueEaseP2X        = HueEaseP2X,
        HueEaseP2Y        = HueEaseP2Y,
        HueMinSpeedFraction = HueMinSpeedFraction,

        ConicSpanTurns     = ConicSpanTurns,
        ConicLeadFadeTurns = ConicLeadFadeTurns,
        ConicTailFadeTurns = ConicTailFadeTurns,
        ConicFadeCurve     = ConicFadeCurve,
        ArcMirror          = ArcMirror,

        ArcPeriodSeconds  = ArcPeriodSeconds,
        ArcDirection      = ArcDirection,
        ArcPhaseTurns     = ArcPhaseTurns,
        ArcEaseP1X        = ArcEaseP1X,
        ArcEaseP1Y        = ArcEaseP1Y,
        ArcEaseP2X        = ArcEaseP2X,
        ArcEaseP2Y        = ArcEaseP2Y,
        ArcMinSpeedFraction = ArcMinSpeedFraction,

        CloneCentreXFraction = CloneCentreXFraction,
        CloneCentreYFraction = CloneCentreYFraction,

        CloneOklchLightness = CloneOklchLightness,
        CloneOklchChroma    = CloneOklchChroma,

        CloneHuePeriodSeconds = CloneHuePeriodSeconds,
        CloneHueDirection     = CloneHueDirection,
        CloneArcPeriodSeconds = CloneArcPeriodSeconds,
        CloneArcDirection     = CloneArcDirection,

        RewritingSaturation    = RewritingSaturation,
        RewritingHueShiftTurns = RewritingHueShiftTurns,
        RewritingExposure      = RewritingExposure,
        RewritingOpacity       = RewritingOpacity,
        RewritingBlendSeconds  = RewritingBlendSeconds,

        TranscribingSaturationDark  = TranscribingSaturationDark,
        TranscribingSaturationLight = TranscribingSaturationLight,
        TranscribingHueShiftTurns   = TranscribingHueShiftTurns,
        TranscribingExposureDark    = TranscribingExposureDark,
        TranscribingExposureLight   = TranscribingExposureLight,
        TranscribingOpacity         = TranscribingOpacity,
        TranscribingBlendSeconds    = TranscribingBlendSeconds,

        RecordingConicSpanTurns     = RecordingConicSpanTurns,
        RecordingConicLeadFadeTurns = RecordingConicLeadFadeTurns,
        RecordingConicTailFadeTurns = RecordingConicTailFadeTurns,
        RecordingConicFadeCurve     = RecordingConicFadeCurve,
        RecordingArcMirror          = RecordingArcMirror,
        RecordingArcPhaseTurns      = RecordingArcPhaseTurns,

        RecordingSaturationDark  = RecordingSaturationDark,
        RecordingSaturationLight = RecordingSaturationLight,
        RecordingHueShiftTurns   = RecordingHueShiftTurns,
        RecordingExposureDark    = RecordingExposureDark,
        RecordingExposureLight   = RecordingExposureLight,
        RecordingBlendSeconds    = RecordingBlendSeconds,
        RecordingHuePeriodSeconds = RecordingHuePeriodSeconds,
    };
}
