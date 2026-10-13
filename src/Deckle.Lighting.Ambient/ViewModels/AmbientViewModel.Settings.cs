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

    // Fine tuning: the preset fold (settings design system, decision 12). The
    // preset selector sits in the fold's header, the values it governs are its
    // children — the brightness floor (its switch and its level, flat because
    // folds never nest), smoothing, change threshold, exposure, saturation. Every
    // child shows in every mode: a preset fills the values, and any hand edit
    // turns the selection to Custom (AmbientViewModel reports it to the family).
    // The preset options come from the family (each preset in declared order,
    // then Custom); the fold's reset writes the children's defaults, then returns
    // the selector to the default preset. The bespoke brightness curve stays
    // beside the fold until it becomes a composer instrument.
    public IReadOnlyList<SettingDescriptor> TuningSettings =>
    [
        Setting.Mode<string>(
            "AmbientPresetCard",
            () => Preset,
            value => Preset = value,
            AmbientSettingsService.Instance.Presets.SelectorOptions("AmbientPresetCustom"),
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
                    "AmbientChangeThresholdCard",
                    () => ChangeThreshold,
                    value => ChangeThreshold = (int)Math.Round(value),
                    new MagnitudeArgs(0, 30),
                    glyph: Glyphs.Filter,
                    defaultValue: () => new AmbientSettings().ChangeThreshold),
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
                // The instrument last and full width (decision 12): the shared
                // curve block (decision 21) over the brightness response, the unit
                // square alone, with the brightness floor drawn as a guide while it
                // is on.
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
            ],
            glyph: Glyphs.Theme,
            defaultValue: () => AmbientPresets.Default),
    ];

    // Configuration: the capture rate, after the bespoke display picker.
    public IReadOnlyList<SettingDescriptor> CaptureSettings =>
    [
        Setting.Radio<int>(
            "AmbientCaptureRateCard",
            () => CaptureFrameRateHz,
            value => CaptureFrameRateHz = value,
            [
                (60, "AmbientCaptureRate60"),
                (30, "AmbientCaptureRate30"),
                (15, "AmbientCaptureRate15"),
            ],
            glyph: Glyphs.Gpu,
            defaultValue: () => new AmbientSettings().CaptureFrameRateHz),
    ];
}
