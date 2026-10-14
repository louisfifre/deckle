using Deckle.Catalog;

namespace Deckle.Lighting.Ambient;

// ── Settings manifests ───────────────────────────────────────────────────────
//
// What the Settings AmbientPage composes, one manifest per page region (the page
// hands each to a SettingsComposer over its own host panel, between the bespoke
// cards the composer cannot express). Every leaf reads its default from a fresh
// AmbientSettings, the single source the per-card and per-fold resets write back.
// Headers, descriptions and option labels resolve from this module's .resw as
// "<LabelKey>.Header" / ".Description" and "<OptionKey>.Content".
//
// The setters are the view model's own properties, so the composed cards keep the
// contracts of AmbientViewModel exactly: a tuning edit reports itself to the preset
// family and turns the selection to Custom, a preset pick goes through
// AmbientSettingsService.Presets.Select.
public partial class AmbientViewModel
{
    // State: the master switch.
    public IReadOnlyList<SettingDescriptor> StateSettings =>
    [
        Setting.Toggle(
            "AmbientEnabledCard",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Lightbulb,
            defaultValue: () => new AmbientSettings().Enabled),
    ];

    // Behaviour: per-zone colours with their band shape. The two band sliders
    // share one place and show only the one the chosen band mode reads.
    public IReadOnlyList<SettingDescriptor> BehaviourSettings =>
    [
        Setting.Group(
            "AmbientZonesGroup",
            () => UseMultiLight,
            value => UseMultiLight = value,
            [
                Setting.Radio<BorderThicknessMode>(
                    "AmbientBorderModeCard",
                    () => BorderMode,
                    value => BorderMode = value,
                    [
                        (BorderThicknessMode.Share, "AmbientBorderModeShare"),
                        (BorderThicknessMode.Cells, "AmbientBorderModeCells"),
                    ],
                    defaultValue: () => new AmbientSettings().BorderMode),
                Setting.Magnitude(
                    "AmbientBorderDepthCard",
                    () => BorderDepthPercent,
                    value => BorderDepthPercent = value,
                    new MagnitudeArgs(5, 50, Unit: "%"),
                    visibleWhen: () => BorderMode == BorderThicknessMode.Share,
                    defaultValue: () => new AmbientSettings().BorderDepth * 100.0),
                Setting.Magnitude(
                    "AmbientBorderCellsCard",
                    () => BorderCells,
                    value => BorderCells = (int)Math.Round(value),
                    new MagnitudeArgs(4, 24, Integer: true),
                    visibleWhen: () => BorderMode == BorderThicknessMode.Cells,
                    defaultValue: () => new AmbientSettings().BorderCells),
            ],
            glyph: Glyphs.Pattern,
            defaultValue: () => new AmbientSettings().UseMultiLight),
    ];

    // Fine tuning: flat cards, in order the brightness floor (its switch and its
    // level), reaction speed, exposure, saturation, then the brightness curve
    // last and full width (decision 12), with the floor drawn as a guide while
    // it is on. The preset fold that held them (Game / Vivid / Movie / Ambient /
    // Custom in its header) is set aside with every preset (settings design
    // system, decision 22): the family still runs underneath (Preset,
    // AmbientSettingsService.Presets), so a hand edit still records Custom, but
    // nothing on the page selects a preset any more. The change threshold, an
    // engine optimisation nobody tunes, keeps its AmbientSettings default and is
    // no longer exposed (triage of 2026-10-14).
    public IReadOnlyList<SettingDescriptor> TuningSettings =>
    [
        Setting.Toggle(
            "AmbientMinBrightnessGroup",
            () => MinBrightnessEnabled,
            value => MinBrightnessEnabled = value,
            glyph: Glyphs.LightbulbFilled,
            defaultValue: () => new AmbientSettings().MinBrightnessEnabled),
        Setting.Magnitude(
            "AmbientMinBrightnessCard",
            () => MinBrightness,
            value => MinBrightness = (int)Math.Round(value),
            new MagnitudeArgs(0, 254),
            visibleWhen: () => MinBrightnessEnabled,
            defaultValue: () => new AmbientSettings().MinBrightness),
        Setting.Magnitude(
            "AmbientSmoothingCard",
            () => SmoothingAlpha,
            value => SmoothingAlpha = value,
            new MagnitudeArgs(0.05, 1),
            glyph: Glyphs.Latency,
            defaultValue: () => new AmbientSettings().SmoothingAlpha),
        Setting.Magnitude(
            "AmbientExposureCard",
            () => ExposureEv,
            value => ExposureEv = value,
            new MagnitudeArgs(-2, 2, Unit: "EV"),
            glyph: Glyphs.Lightning,
            defaultValue: () => new AmbientSettings().ExposureEv),
        Setting.Magnitude(
            "AmbientSaturationCard",
            () => SaturationBoost,
            value => SaturationBoost = value,
            new MagnitudeArgs(0, 2, Unit: "×"),
            glyph: Glyphs.Sparkle,
            defaultValue: () => new AmbientSettings().SaturationBoost),
        Setting.Curve(
            "AmbientBrightnessCurveCard",
            () => new CurvePoints(BrightnessCurveX1, BrightnessCurveY1, BrightnessCurveX2, BrightnessCurveY2),
            p =>
            {
                BrightnessCurveX1 = p.X1; BrightnessCurveY1 = p.Y1;
                BrightnessCurveX2 = p.X2; BrightnessCurveY2 = p.Y2;
            },
            new CurveArgs(Floor: () => MinBrightnessEnabled ? MinBrightness / 254.0 : null),
            glyph: Glyphs.Tuning,
            defaultValue: () =>
            {
                var d = new AmbientSettings();
                return new CurvePoints(d.BrightnessCurveX1, d.BrightnessCurveY1, d.BrightnessCurveX2, d.BrightnessCurveY2);
            }),
    ];

    // The capture rate (60 / 30 / 15 Hz) is no longer exposed (triage of
    // 2026-10-14): the engine runs at AmbientCaptureRate.Default and the preview
    // block reads the measured rate. CaptureFrameRateHz stays in the view model
    // and the settings file for the engine.
}
