using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Composition;

namespace Deckle.Playground;

// ─── HudPage — tuning expanders ─────────────────────────────────────────────
//
// One Add*Expander per knob group, each with a Reset* back to the compiled
// defaults.
//
// Step-frequency convention:
//   fractional [0..1, ease Y, saturation/opacity/hue]   → 0.05
//   period seconds [0..60, 0.5..30]                     → 0.5
//   short period (fade curve)                           → 0.1
//   exposure [-2..2]                                    → 0.1
//   blend seconds [0..5]                                → 0.1
//   sim RMS values [0..0.3]                             → 0.001
//   WedgeCount (int)                                    → 1

public sealed partial class HudPage
{
    private void AddHudGeometryExpander()
    {
        var stack = NewTuningSection("HUD geometry", ResetHudGeometry);
        stack.Children.Add(TuningHelp("Shared by Recording, Transcribing and Rewriting. Negative inset places part of the stroke outside the HUD."));
        AddFloatRow(stack, "Stroke thickness (dip)", 0.5, 12, 0.5, _tuning.StrokeThickness,
            v => _tuning.StrokeThickness = (float)v, rebuild: true);
        AddFloatRow(stack, "InsetDip", -16, 4, 0.5, _tuning.InsetDip,
            v => _tuning.InsetDip = (float)v, rebuild: true);
    }

    private void ResetHudGeometry()
    {
        _tuning.LoadGeometry(new HudComposition.ConicArcStrokeConfig());
        RebuildTuningPanel();
        RequestRebuild();
    }

    private void AddPaletteExpander()
    {
        var stack = NewTuningSection("Palette (OKLCh)", ResetPalette);
        AddFloatRow(stack, "OklchLightness", 0, 1, 0.05, _tuning.OklchLightness,
            v => _tuning.OklchLightness = (float)v, rebuild: true);
        AddFloatRow(stack, "OklchChroma", 0, 0.4, 0.05, _tuning.OklchChroma,
            v => _tuning.OklchChroma = (float)v, rebuild: true);
        AddFloatRow(stack, "HueStart", 0, 1, 0.05, _tuning.HueStart,
            v => _tuning.HueStart = (float)v, rebuild: true);
        AddFloatRow(stack, "HueRange", 0, 1, 0.05, _tuning.HueRange,
            v => _tuning.HueRange = (float)v, rebuild: true);
        AddIntRow(stack, "WedgeCount", 16, 720, _tuning.WedgeCount,
            v => _tuning.WedgeCount = v, rebuild: true);
    }

    private void ResetPalette()
    {
        var d = new TuningModel();
        _tuning.OklchLightness = d.OklchLightness;
        _tuning.OklchChroma    = d.OklchChroma;
        _tuning.HueStart       = d.HueStart;
        _tuning.HueRange       = d.HueRange;
        _tuning.WedgeCount     = d.WedgeCount;
        RebuildTuningPanel();
    }

    private void AddConicFadeExpander()
    {
        var stack = NewTuningSection("Conic fade & span", ResetConicFade);
        AddFloatRow(stack, "ConicSpanTurns", 0.05, 1.0, 0.05, _tuning.ConicSpanTurns,
            v => _tuning.ConicSpanTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "ConicLeadFadeTurns", 0, 1, 0.05, _tuning.ConicLeadFadeTurns,
            v => _tuning.ConicLeadFadeTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "ConicTailFadeTurns", 0, 1, 0.05, _tuning.ConicTailFadeTurns,
            v => _tuning.ConicTailFadeTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "ConicFadeCurve", 0.5, 10, 0.1, _tuning.ConicFadeCurve,
            v => _tuning.ConicFadeCurve = (float)v, rebuild: true);
        AddToggleRow(stack, "ArcMirror", _tuning.ArcMirror,
            v => _tuning.ArcMirror = v, rebuild: true);
    }

    private void ResetConicFade()
    {
        var d = new TuningModel();
        _tuning.ConicSpanTurns     = d.ConicSpanTurns;
        _tuning.ConicLeadFadeTurns = d.ConicLeadFadeTurns;
        _tuning.ConicTailFadeTurns = d.ConicTailFadeTurns;
        _tuning.ConicFadeCurve     = d.ConicFadeCurve;
        _tuning.ArcMirror          = d.ArcMirror;
        RebuildTuningPanel();
    }

    private void AddHueRotationExpander()
    {
        var stack = NewTuningSection("Hue rotation", ResetHueRotation);
        AddFloatRow(stack, "HuePeriodSeconds", 0, 60, 0.5, _tuning.HuePeriodSeconds,
            v => _tuning.HuePeriodSeconds = v, rebuild: true);
        AddDirectionRow(stack, "HueDirection", _tuning.HueDirection,
            v => _tuning.HueDirection = v);
        AddFloatRow(stack, "HueEaseP1.X", 0, 1, 0.05, _tuning.HueEaseP1X,
            v => _tuning.HueEaseP1X = (float)v, rebuild: true);
        AddFloatRow(stack, "HueEaseP1.Y", -0.5, 1.5, 0.05, _tuning.HueEaseP1Y,
            v => _tuning.HueEaseP1Y = (float)v, rebuild: true);
        AddFloatRow(stack, "HueEaseP2.X", 0, 1, 0.05, _tuning.HueEaseP2X,
            v => _tuning.HueEaseP2X = (float)v, rebuild: true);
        AddFloatRow(stack, "HueEaseP2.Y", -0.5, 1.5, 0.05, _tuning.HueEaseP2Y,
            v => _tuning.HueEaseP2Y = (float)v, rebuild: true);
        AddFloatRow(stack, "HueMinSpeedFraction", 0, 1, 0.05, _tuning.HueMinSpeedFraction,
            v => _tuning.HueMinSpeedFraction = (float)v, rebuild: true);
    }

    private void ResetHueRotation()
    {
        var d = new TuningModel();
        _tuning.HuePeriodSeconds = d.HuePeriodSeconds;
        _tuning.HueDirection     = d.HueDirection;
        _tuning.HueEaseP1X       = d.HueEaseP1X;
        _tuning.HueEaseP1Y       = d.HueEaseP1Y;
        _tuning.HueEaseP2X       = d.HueEaseP2X;
        _tuning.HueEaseP2Y       = d.HueEaseP2Y;
        _tuning.HueMinSpeedFraction = d.HueMinSpeedFraction;
        RebuildTuningPanel();
    }

    private void AddArcRotationExpander()
    {
        var stack = NewTuningSection("Arc rotation", ResetArcRotation);
        AddFloatRow(stack, "ArcPeriodSeconds", 0.5, 30, 0.5, _tuning.ArcPeriodSeconds,
            v => _tuning.ArcPeriodSeconds = v, rebuild: true);
        AddDirectionRow(stack, "ArcDirection", _tuning.ArcDirection,
            v => _tuning.ArcDirection = v);
        AddFloatRow(stack, "ArcEaseP1.X", 0, 1, 0.05, _tuning.ArcEaseP1X,
            v => _tuning.ArcEaseP1X = (float)v, rebuild: true);
        AddFloatRow(stack, "ArcEaseP1.Y", -0.5, 1.5, 0.05, _tuning.ArcEaseP1Y,
            v => _tuning.ArcEaseP1Y = (float)v, rebuild: true);
        AddFloatRow(stack, "ArcEaseP2.X", 0, 1, 0.05, _tuning.ArcEaseP2X,
            v => _tuning.ArcEaseP2X = (float)v, rebuild: true);
        AddFloatRow(stack, "ArcEaseP2.Y", -0.5, 1.5, 0.05, _tuning.ArcEaseP2Y,
            v => _tuning.ArcEaseP2Y = (float)v, rebuild: true);
        AddFloatRow(stack, "ArcMinSpeedFraction", 0, 1, 0.05, _tuning.ArcMinSpeedFraction,
            v => _tuning.ArcMinSpeedFraction = (float)v, rebuild: true);
    }

    private void ResetArcRotation()
    {
        var d = new TuningModel();
        _tuning.ArcPeriodSeconds = d.ArcPeriodSeconds;
        _tuning.ArcDirection     = d.ArcDirection;
        _tuning.ArcEaseP1X       = d.ArcEaseP1X;
        _tuning.ArcEaseP1Y       = d.ArcEaseP1Y;
        _tuning.ArcEaseP2X       = d.ArcEaseP2X;
        _tuning.ArcEaseP2Y       = d.ArcEaseP2Y;
        _tuning.ArcMinSpeedFraction = d.ArcMinSpeedFraction;
        RebuildTuningPanel();
    }

    private void AddRecordingExpander()
    {
        var stack = NewTuningSection("Recording", ResetRecording);
        if (ViewModel.IsCountdownTarget)
            stack.Children.Add(TuningHelp(
                "The countdown reads the fades, the hue period and the grading. " +
                "Span, mirror and arc phase change nothing there: the two wedges follow the pause."));
        AddFloatRow(stack, "RecordingConicSpanTurns", 0.05, 1, 0.05, _tuning.RecordingConicSpanTurns,
            v => _tuning.RecordingConicSpanTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingConicLeadFadeTurns", 0, 1, 0.05, _tuning.RecordingConicLeadFadeTurns,
            v => _tuning.RecordingConicLeadFadeTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingConicTailFadeTurns", 0, 1, 0.05, _tuning.RecordingConicTailFadeTurns,
            v => _tuning.RecordingConicTailFadeTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingConicFadeCurve", 0.5, 10, 0.1, _tuning.RecordingConicFadeCurve,
            v => _tuning.RecordingConicFadeCurve = (float)v, rebuild: true);
        AddToggleRow(stack, "RecordingArcMirror", _tuning.RecordingArcMirror,
            v => _tuning.RecordingArcMirror = v, rebuild: true);
        AddFloatRow(stack, "RecordingArcPhaseTurns", 0, 1, 0.05, _tuning.RecordingArcPhaseTurns,
            v => _tuning.RecordingArcPhaseTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingHuePeriodSeconds", 0, 60, 0.5, _tuning.RecordingHuePeriodSeconds,
            v => _tuning.RecordingHuePeriodSeconds = v, rebuild: true);
        AddFloatRow(stack, "RecordingSaturationDark", 0, 1, 0.05, _tuning.RecordingSaturationDark,
            v => _tuning.RecordingSaturationDark = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingSaturationLight", 0, 1, 0.05, _tuning.RecordingSaturationLight,
            v => _tuning.RecordingSaturationLight = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingHueShiftTurns", 0, 1, 0.05, _tuning.RecordingHueShiftTurns,
            v => _tuning.RecordingHueShiftTurns = (float)v, rebuild: true);
        // Exposure clamped to D2D1_EXPOSURE spec [-2, +2].
        AddFloatRow(stack, "RecordingExposureDark", -2, 2, 0.1, _tuning.RecordingExposureDark,
            v => _tuning.RecordingExposureDark = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingExposureLight", -2, 2, 0.1, _tuning.RecordingExposureLight,
            v => _tuning.RecordingExposureLight = (float)v, rebuild: true);
    }

    private void ResetRecording()
    {
        var d = new TuningModel();
        _tuning.RecordingConicSpanTurns     = d.RecordingConicSpanTurns;
        _tuning.RecordingConicLeadFadeTurns = d.RecordingConicLeadFadeTurns;
        _tuning.RecordingConicTailFadeTurns = d.RecordingConicTailFadeTurns;
        _tuning.RecordingConicFadeCurve     = d.RecordingConicFadeCurve;
        _tuning.RecordingArcMirror          = d.RecordingArcMirror;
        _tuning.RecordingArcPhaseTurns      = d.RecordingArcPhaseTurns;
        _tuning.RecordingHuePeriodSeconds   = d.RecordingHuePeriodSeconds;
        _tuning.RecordingSaturationDark     = d.RecordingSaturationDark;
        _tuning.RecordingSaturationLight    = d.RecordingSaturationLight;
        _tuning.RecordingHueShiftTurns      = d.RecordingHueShiftTurns;
        _tuning.RecordingExposureDark       = d.RecordingExposureDark;
        _tuning.RecordingExposureLight      = d.RecordingExposureLight;
        RebuildTuningPanel();
    }

    private void AddTranscribingExpander()
    {
        var stack = NewTuningSection("Transcribing", ResetTranscribing);
        AddFloatRow(stack, "TranscribingSaturationDark", 0, 1, 0.05, _tuning.TranscribingSaturationDark,
            v => _tuning.TranscribingSaturationDark = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingSaturationLight", 0, 1, 0.05, _tuning.TranscribingSaturationLight,
            v => _tuning.TranscribingSaturationLight = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingHueShiftTurns", 0, 1, 0.05, _tuning.TranscribingHueShiftTurns,
            v => _tuning.TranscribingHueShiftTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingExposureDark", -2, 2, 0.1, _tuning.TranscribingExposureDark,
            v => _tuning.TranscribingExposureDark = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingExposureLight", -2, 2, 0.1, _tuning.TranscribingExposureLight,
            v => _tuning.TranscribingExposureLight = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingOpacity", 0, 1, 0.05, _tuning.TranscribingOpacity,
            v => _tuning.TranscribingOpacity = (float)v, rebuild: true);
    }

    private void ResetTranscribing()
    {
        var d = new TuningModel();
        _tuning.TranscribingSaturationDark  = d.TranscribingSaturationDark;
        _tuning.TranscribingSaturationLight = d.TranscribingSaturationLight;
        _tuning.TranscribingHueShiftTurns   = d.TranscribingHueShiftTurns;
        _tuning.TranscribingExposureDark    = d.TranscribingExposureDark;
        _tuning.TranscribingExposureLight   = d.TranscribingExposureLight;
        _tuning.TranscribingOpacity         = d.TranscribingOpacity;
        RebuildTuningPanel();
    }

    private void AddRewritingExpander()
    {
        var stack = NewTuningSection("Rewriting", ResetRewriting);
        AddFloatRow(stack, "RewritingSaturation", 0, 1, 0.05, _tuning.RewritingSaturation,
            v => _tuning.RewritingSaturation = (float)v, rebuild: true);
        AddFloatRow(stack, "RewritingHueShiftTurns", 0, 1, 0.05, _tuning.RewritingHueShiftTurns,
            v => _tuning.RewritingHueShiftTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RewritingExposure", -2, 2, 0.1, _tuning.RewritingExposure,
            v => _tuning.RewritingExposure = (float)v, rebuild: true);
        AddFloatRow(stack, "RewritingOpacity", 0, 1, 0.05, _tuning.RewritingOpacity,
            v => _tuning.RewritingOpacity = (float)v, rebuild: true);
    }

    private void ResetRewriting()
    {
        var d = new TuningModel();
        _tuning.RewritingSaturation    = d.RewritingSaturation;
        _tuning.RewritingHueShiftTurns = d.RewritingHueShiftTurns;
        _tuning.RewritingExposure      = d.RewritingExposure;
        _tuning.RewritingOpacity       = d.RewritingOpacity;
        RebuildTuningPanel();
    }

    // The preview maps the simulated RMS through the live AudioLevelMapper
    // calibration, which Settings > Recording and auto-calibration own. The
    // Playground reads it and never writes it: the HUD tab changes previews
    // only.
    private void AddSimulatedRmsExpander()
    {
        var stack = NewTuningSection("Simulated RMS (Recording only)", ResetSimulatedRms);
        // Step 0.001: the defaults (0.013, 0.100) need three decimals.
        AddFloatRow(stack, "SimRmsMin", 0, 0.3, 0.001, _simRmsMin,
            v => _simRmsMin = (float)v);
        AddFloatRow(stack, "SimRmsMax", 0, 0.3, 0.001, _simRmsMax,
            v => _simRmsMax = (float)v);
        AddFloatRow(stack, "SimRmsPeriodSeconds", 0.2, 10, 0.1, _simRmsPeriodSeconds,
            v => _simRmsPeriodSeconds = (float)v);
        AddToggleRow(stack, "Manual override", _simManualOverride,
            v => _simManualOverride = v);
        AddFloatRow(stack, "SimRmsManualValue", 0, 0.3, 0.001, _simManualValue,
            v => _simManualValue = (float)v);
    }

    private void ResetSimulatedRms()
    {
        _simRmsMin           = 0.013f;
        _simRmsMax           = 0.100f;
        _simRmsPeriodSeconds = 2.0f;
        _simManualOverride   = false;
        _simManualValue      = 0.012f;
        RebuildTuningPanel();
    }

    // The clone is the material the pinned reveal shows behind the digits; these
    // knobs drive the ConicClone preview and the digit reveal of the
    // Transcribing and Rewriting previews. The centre sliders read in px
    // against the 272×78 frame but store a fraction (CloneCentre*Fraction).
    private void AddClonePlacementExpander()
    {
        var stack = NewTuningSection("Conic clone", ResetClonePlacement);
        AddFloatRow(stack, "Cone centre X", 0, NakedHudSize.X, 2,
            _tuning.CloneCentreXFraction * NakedHudSize.X,
            v => _tuning.CloneCentreXFraction = (float)v / NakedHudSize.X, rebuild: true);
        AddFloatRow(stack, "Cone centre Y", 0, NakedHudSize.Y, 2,
            _tuning.CloneCentreYFraction * NakedHudSize.Y,
            v => _tuning.CloneCentreYFraction = (float)v / NakedHudSize.Y, rebuild: true);
        // The clone's own OKLCh lightness/chroma, decoupled from the contour's
        // palette because exposure caps at +2 EV. Lightness brightens the
        // Transcribing greyscale sweep; chroma feeds the Rewriting colour.
        AddFloatRow(stack, "Clone OKLCh lightness", 0, 1, 0.05, _tuning.CloneOklchLightness,
            v => _tuning.CloneOklchLightness = (float)v, rebuild: true);
        AddFloatRow(stack, "Clone OKLCh chroma", 0, 0.4, 0.05, _tuning.CloneOklchChroma,
            v => _tuning.CloneOklchChroma = (float)v, rebuild: true);
        AddFloatRow(stack, "Clone hue period (s)", 0, 60, 0.5, _tuning.CloneHuePeriodSeconds,
            v => _tuning.CloneHuePeriodSeconds = v, rebuild: true);
        AddDirectionRow(stack, "Clone hue direction", _tuning.CloneHueDirection,
            v => _tuning.CloneHueDirection = v);
        AddFloatRow(stack, "Clone arc period (s)", 0.5, 30, 0.5, _tuning.CloneArcPeriodSeconds,
            v => _tuning.CloneArcPeriodSeconds = v, rebuild: true);
        AddDirectionRow(stack, "Clone arc direction", _tuning.CloneArcDirection,
            v => _tuning.CloneArcDirection = v);
    }

    private void ResetClonePlacement()
    {
        var d = new TuningModel();
        _tuning.CloneCentreXFraction  = d.CloneCentreXFraction;
        _tuning.CloneCentreYFraction  = d.CloneCentreYFraction;
        _tuning.CloneOklchLightness   = d.CloneOklchLightness;
        _tuning.CloneOklchChroma      = d.CloneOklchChroma;
        _tuning.CloneHuePeriodSeconds = d.CloneHuePeriodSeconds;
        _tuning.CloneHueDirection     = d.CloneHueDirection;
        _tuning.CloneArcPeriodSeconds = d.CloneArcPeriodSeconds;
        _tuning.CloneArcDirection     = d.CloneArcDirection;
        RebuildTuningPanel();
        ApplyTarget();
    }

    // Phases are only observable at stroke creation, the blends only during a
    // variant transition (in the preview, on Rewriting at each Start and on a
    // live theme switch), so these rows stay parked on every target that has a
    // stroke.
    private void AddParkedExpander()
    {
        var stack = NewTuningSection("Transition and rotation defaults", ResetParked);
        stack.Children.Add(new TextBlock
        {
            Text = "Initial orientations and blends between HUD states. These do not control utterance timing.",
            Style = (Style)Resources["HudTuningHelpTextStyle"],
        });
        AddFloatRow(stack, "HuePhaseTurns", 0, 1, 0.05, _tuning.HuePhaseTurns,
            v => _tuning.HuePhaseTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "ArcPhaseTurns", 0, 1, 0.05, _tuning.ArcPhaseTurns,
            v => _tuning.ArcPhaseTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingBlendSeconds", 0, 5, 0.1, _tuning.RecordingBlendSeconds,
            v => _tuning.RecordingBlendSeconds = v, rebuild: true);
        AddFloatRow(stack, "TranscribingBlendSeconds", 0, 5, 0.1, _tuning.TranscribingBlendSeconds,
            v => _tuning.TranscribingBlendSeconds = v, rebuild: true);
        AddFloatRow(stack, "RewritingBlendSeconds", 0, 5, 0.1, _tuning.RewritingBlendSeconds,
            v => _tuning.RewritingBlendSeconds = v, rebuild: true);
    }

    private void ResetParked()
    {
        var d = new TuningModel();
        _tuning.HuePhaseTurns             = d.HuePhaseTurns;
        _tuning.ArcPhaseTurns             = d.ArcPhaseTurns;
        _tuning.RecordingBlendSeconds     = d.RecordingBlendSeconds;
        _tuning.TranscribingBlendSeconds  = d.TranscribingBlendSeconds;
        _tuning.RewritingBlendSeconds     = d.RewritingBlendSeconds;
        RebuildTuningPanel();
    }
}
