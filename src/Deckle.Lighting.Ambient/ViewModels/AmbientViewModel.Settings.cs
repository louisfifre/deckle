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

    // Behaviour: the preset, then per-zone colours with their band shape.
    // The two band sliders share one place and show only the one the chosen
    // band mode reads. The preset options come from the family (each preset
    // in declared order, then Custom); the selector's reset returns to the
    // default preset.
    public IReadOnlyList<SettingDescriptor> BehaviourSettings =>
    [
        Setting.Choice<string>(
            "AmbientPresetCard",
            () => Preset,
            value => Preset = value,
            AmbientSettingsService.Instance.Presets.SelectorOptions("AmbientPresetCustom"),
            glyph: Glyphs.Theme,
            defaultValue: () => AmbientPresets.Default),

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

    // Fine tuning, after the bespoke brightness-curve card: a flat run of cards,
    // not a fold — this is the part of the page people come to adjust.
    public IReadOnlyList<SettingDescriptor> TuningSettings =>
    [
        Setting.Group(
            "AmbientMinBrightnessGroup",
            () => MinBrightnessEnabled,
            value => MinBrightnessEnabled = value,
            [
                Setting.Magnitude(
                    "AmbientMinBrightnessCard",
                    () => MinBrightness,
                    value => MinBrightness = (int)Math.Round(value),
                    new MagnitudeArgs(0, 254),
                    defaultValue: () => new AmbientSettings().MinBrightness),
            ],
            glyph: Glyphs.LightbulbFilled,
            defaultValue: () => new AmbientSettings().MinBrightnessEnabled),

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
