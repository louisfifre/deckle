using Microsoft.UI.Xaml;
using Deckle.Composition;

namespace Deckle.Playground;

// ─── HudPage — tuning expanders ─────────────────────────────────────────────
//
// One Add*Expander per HudTuningSection, each with a Reset* back to the
// compiled defaults that covers exactly the rows its section can show. The
// Gauge section lives in HudPage.Gauge.cs, the Simulation section in
// HudPage.Simulation.cs.
//
// Step-frequency convention:
//   fractional [0..1, ease Y, lightness/opacity/hue]    → 0.05
//   period seconds [0..60, 0.5..30]                     → 0.5
//   short period (fade curve)                           → 0.1
//   blend seconds [0..5]                                → 0.1
//   sim RMS values [0..0.3]                             → 0.001
//   WedgeCount (int)                                    → 1

public sealed partial class HudPage
{
    private void AddHudGeometryExpander()
    {
        var stack = NewTuningSection("HUD geometry", ResetHudGeometry);
        stack.Children.Add(TuningHelp("Shared by Recording, Transcribing, Rewriting and the gauge. Negative inset places part of the stroke outside the HUD."));
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

    // The digit-reveal clone paints from its own lightness and chroma
    // (HudComposition.CloneSurfaceConfig), so on that target only the wheel's
    // hue layout reaches the preview, and only those rows show.
    private bool ColourMaterialHueLayoutOnly => ViewModel.CurrentTarget == HudTarget.DigitReveal;

    private void AddColourMaterialExpander()
    {
        var stack = NewTuningSection("Colour material", ResetColourMaterial);
        if (ColourMaterialHueLayoutOnly)
        {
            stack.Children.Add(TuningHelp("On this target, lightness and chroma come from the Digit reveal section."));
        }
        else
        {
            stack.Children.Add(TuningHelp("Lightness, chroma and hue paint the colour wheel."));
            AddFloatRow(stack, "OklchLightness", 0, 1, 0.05, _tuning.OklchLightness,
                v => _tuning.OklchLightness = (float)v, rebuild: true);
            AddFloatRow(stack, "OklchChroma", 0, 0.4, 0.05, _tuning.OklchChroma,
                v => _tuning.OklchChroma = (float)v, rebuild: true);
        }
        AddFloatRow(stack, "HueStart", 0, 1, 0.05, _tuning.HueStart,
            v => _tuning.HueStart = (float)v, rebuild: true);
        AddFloatRow(stack, "HueRange", 0, 1, 0.05, _tuning.HueRange,
            v => _tuning.HueRange = (float)v, rebuild: true);
        AddIntRow(stack, "WedgeCount", 16, 720, _tuning.WedgeCount,
            v => _tuning.WedgeCount = v, rebuild: true);
    }

    private void ResetColourMaterial()
    {
        var d = new TuningModel();
        if (!ColourMaterialHueLayoutOnly)
        {
            _tuning.OklchLightness = d.OklchLightness;
            _tuning.OklchChroma    = d.OklchChroma;
        }
        _tuning.HueStart   = d.HueStart;
        _tuning.HueRange   = d.HueRange;
        _tuning.WedgeCount = d.WedgeCount;
        RebuildTuningPanel();
    }

    // One lightness per theme; the previews read the Playground's own theme,
    // so only one of the two rows shows on screen. Read at panel build: the
    // page has no theme-change handler, so after a live theme switch the line
    // stays stale until the next rebuild.
    private void AddGreyMaterialExpander()
    {
        var stack = NewTuningSection("Grey material", ResetGreyMaterial);
        stack.Children.Add(TuningHelp(ActualTheme == ElementTheme.Light
            ? "The Playground is in light theme, so the preview shows GreyLightnessLight."
            : "The Playground is in dark theme, so the preview shows GreyLightnessDark."));
        AddFloatRow(stack, "GreyLightnessDark", 0, 1, 0.05, _tuning.GreyLightnessDark,
            v => _tuning.GreyLightnessDark = (float)v, rebuild: true);
        AddFloatRow(stack, "GreyLightnessLight", 0, 1, 0.05, _tuning.GreyLightnessLight,
            v => _tuning.GreyLightnessLight = (float)v, rebuild: true);
    }

    private void ResetGreyMaterial()
    {
        var d = new TuningModel();
        _tuning.GreyLightnessDark  = d.GreyLightnessDark;
        _tuning.GreyLightnessLight = d.GreyLightnessLight;
        RebuildTuningPanel();
    }

    private void AddSweepMaskExpander()
    {
        var stack = NewTuningSection("Sweep mask", ResetSweepMask);
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

    private void ResetSweepMask()
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

    private void AddSweepRotationExpander()
    {
        var stack = NewTuningSection("Sweep rotation", ResetSweepRotation);
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

    private void ResetSweepRotation()
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

    // ── The three state sections: material, then the state's own rows ──────
    //
    // The hue shift turns the wheel, so it shows only on the Colour material;
    // its reset still covers it, as a row the section owns. The blend is the
    // time a transition into this state takes (ProcessingStroke.ApplyVariant
    // reads the entered state's BlendSeconds), so it sits with the state.

    private void AddRecordingExpander()
    {
        var stack = NewTuningSection("Recording", ResetRecording);
        AddMaterialRow(stack, _tuning.RecordingMaterial, m => _tuning.RecordingMaterial = m);
        if (_tuning.RecordingMaterial == HudMaterial.Colour)
            AddFloatRow(stack, "RecordingHueShiftTurns", 0, 1, 0.05, _tuning.RecordingHueShiftTurns,
                v => _tuning.RecordingHueShiftTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RecordingBlendSeconds", 0, 5, 0.1, _tuning.RecordingBlendSeconds,
            v => _tuning.RecordingBlendSeconds = v, rebuild: true);
        // GetUtteranceCountdownMaskConfig forces the mirror and poses the
        // wedges from the pause; the span only scales the fades, which shows
        // only once they are shorter than the span.
        if (ViewModel.CurrentTarget == HudTarget.Gauge)
            stack.Children.Add(TuningHelp(
                "The gauge reads the fades and the material. " +
                "Mirror and arc phase don't reach it, because the two wedges follow the pause. " +
                "Span only rescales the fades."));
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
    }

    private void ResetRecording()
    {
        var d = new TuningModel();
        _tuning.RecordingMaterial           = d.RecordingMaterial;
        _tuning.RecordingHueShiftTurns      = d.RecordingHueShiftTurns;
        _tuning.RecordingBlendSeconds       = d.RecordingBlendSeconds;
        _tuning.RecordingConicSpanTurns     = d.RecordingConicSpanTurns;
        _tuning.RecordingConicLeadFadeTurns = d.RecordingConicLeadFadeTurns;
        _tuning.RecordingConicTailFadeTurns = d.RecordingConicTailFadeTurns;
        _tuning.RecordingConicFadeCurve     = d.RecordingConicFadeCurve;
        _tuning.RecordingArcMirror          = d.RecordingArcMirror;
        _tuning.RecordingArcPhaseTurns      = d.RecordingArcPhaseTurns;
        RebuildTuningPanel();
    }

    private void AddTranscribingExpander()
    {
        var stack = NewTuningSection("Transcribing", ResetTranscribing);
        AddMaterialRow(stack, _tuning.TranscribingMaterial, m => _tuning.TranscribingMaterial = m);
        if (_tuning.TranscribingMaterial == HudMaterial.Colour)
            AddFloatRow(stack, "TranscribingHueShiftTurns", 0, 1, 0.05, _tuning.TranscribingHueShiftTurns,
                v => _tuning.TranscribingHueShiftTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingOpacity", 0, 1, 0.05, _tuning.TranscribingOpacity,
            v => _tuning.TranscribingOpacity = (float)v, rebuild: true);
        AddFloatRow(stack, "TranscribingBlendSeconds", 0, 5, 0.1, _tuning.TranscribingBlendSeconds,
            v => _tuning.TranscribingBlendSeconds = v, rebuild: true);
    }

    private void ResetTranscribing()
    {
        var d = new TuningModel();
        _tuning.TranscribingMaterial      = d.TranscribingMaterial;
        _tuning.TranscribingHueShiftTurns = d.TranscribingHueShiftTurns;
        _tuning.TranscribingOpacity       = d.TranscribingOpacity;
        _tuning.TranscribingBlendSeconds  = d.TranscribingBlendSeconds;
        RebuildTuningPanel();
    }

    private void AddRewritingExpander()
    {
        var stack = NewTuningSection("Rewriting", ResetRewriting);
        AddMaterialRow(stack, _tuning.RewritingMaterial, m => _tuning.RewritingMaterial = m);
        if (_tuning.RewritingMaterial == HudMaterial.Colour)
            AddFloatRow(stack, "RewritingHueShiftTurns", 0, 1, 0.05, _tuning.RewritingHueShiftTurns,
                v => _tuning.RewritingHueShiftTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "RewritingOpacity", 0, 1, 0.05, _tuning.RewritingOpacity,
            v => _tuning.RewritingOpacity = (float)v, rebuild: true);
        AddFloatRow(stack, "RewritingBlendSeconds", 0, 5, 0.1, _tuning.RewritingBlendSeconds,
            v => _tuning.RewritingBlendSeconds = v, rebuild: true);
    }

    private void ResetRewriting()
    {
        var d = new TuningModel();
        _tuning.RewritingMaterial      = d.RewritingMaterial;
        _tuning.RewritingHueShiftTurns = d.RewritingHueShiftTurns;
        _tuning.RewritingOpacity       = d.RewritingOpacity;
        _tuning.RewritingBlendSeconds  = d.RewritingBlendSeconds;
        RebuildTuningPanel();
    }

    // The clone is the material the pinned reveal shows behind the digits;
    // these rows drive the Digit reveal target and the digit reveal of the
    // Transcribing and Rewriting previews. The centre sliders read in px
    // against the 272×78 frame but store a fraction (CloneCentre*Fraction).
    // Lightness and chroma replace the wheel's own (CloneSurfaceConfig) and
    // only show on the Colour material: the Grey mix hides the wheel.
    private void AddDigitRevealExpander()
    {
        var stack = NewTuningSection("Digit reveal", ResetDigitReveal);
        AddFloatRow(stack, "Centre X", 0, NakedHudSize.X, 2,
            _tuning.CloneCentreXFraction * NakedHudSize.X,
            v => _tuning.CloneCentreXFraction = (float)v / NakedHudSize.X, rebuild: true);
        AddFloatRow(stack, "Centre Y", 0, NakedHudSize.Y, 2,
            _tuning.CloneCentreYFraction * NakedHudSize.Y,
            v => _tuning.CloneCentreYFraction = (float)v / NakedHudSize.Y, rebuild: true);
        stack.Children.Add(TuningHelp("Lightness and chroma show on the Colour material only."));
        AddFloatRow(stack, "Lightness", 0, 1, 0.05, _tuning.CloneOklchLightness,
            v => _tuning.CloneOklchLightness = (float)v, rebuild: true);
        AddFloatRow(stack, "Chroma", 0, 0.4, 0.05, _tuning.CloneOklchChroma,
            v => _tuning.CloneOklchChroma = (float)v, rebuild: true);
        AddFloatRow(stack, "Hue period (s)", 0, 60, 0.5, _tuning.CloneHuePeriodSeconds,
            v => _tuning.CloneHuePeriodSeconds = v, rebuild: true);
        AddDirectionRow(stack, "Hue direction", _tuning.CloneHueDirection,
            v => _tuning.CloneHueDirection = v);
        AddFloatRow(stack, "Sweep period (s)", 0.5, 30, 0.5, _tuning.CloneArcPeriodSeconds,
            v => _tuning.CloneArcPeriodSeconds = v, rebuild: true);
        AddDirectionRow(stack, "Sweep direction", _tuning.CloneArcDirection,
            v => _tuning.CloneArcDirection = v);
    }

    private void ResetDigitReveal()
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
    }

    // Phases are only observable at stroke creation, which every rebuild of
    // the preview performs.
    private void AddOrientationsExpander()
    {
        var stack = NewTuningSection("Initial orientations", ResetOrientations);
        stack.Children.Add(TuningHelp("Where the cone and the sweep start when the stroke is built."));
        AddFloatRow(stack, "HuePhaseTurns", 0, 1, 0.05, _tuning.HuePhaseTurns,
            v => _tuning.HuePhaseTurns = (float)v, rebuild: true);
        AddFloatRow(stack, "ArcPhaseTurns", 0, 1, 0.05, _tuning.ArcPhaseTurns,
            v => _tuning.ArcPhaseTurns = (float)v, rebuild: true);
    }

    private void ResetOrientations()
    {
        var d = new TuningModel();
        _tuning.HuePhaseTurns = d.HuePhaseTurns;
        _tuning.ArcPhaseTurns = d.ArcPhaseTurns;
        RebuildTuningPanel();
    }
}
