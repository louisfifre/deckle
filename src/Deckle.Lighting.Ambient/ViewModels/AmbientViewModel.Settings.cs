using Deckle.Catalog;

namespace Deckle.Lighting.Ambient;

// ── Settings manifests ───────────────────────────────────────────────────────
//
// What the Settings AmbientPage composes, one manifest per page region (the page
// hands each to a SettingsComposer over its own host panel, between the bespoke
// cards the composer cannot express). Every leaf reads its default from a fresh
// AmbientSettings, the single source the per-card and per-fold resets write back.
// Headers, descriptions and option labels resolve from this module's .resw as
// "<LabelKey>.Header" / ".Description" and "<OptionKey>.Content". No glyph on
// any card: the rail carries the module's icon (2026-10-14).
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
            defaultValue: () => new AmbientSettings().Enabled),
    ];

    // Behaviour: per-zone colours with their band shape. The two band sliders
    // share one place and show only the one the chosen band mode reads. The
    // measure is a dropdown, not radio buttons: two options the eye need not
    // compare at a glance, and the card's description says what each does
    // (Louis, 2026-10-15).
    public IReadOnlyList<SettingDescriptor> BehaviourSettings =>
    [
        Setting.Group(
            "AmbientZonesGroup",
            () => UseMultiLight,
            value => UseMultiLight = value,
            [
                Setting.Choice<BorderThicknessMode>(
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
            defaultValue: () => new AmbientSettings().UseMultiLight),
    ];

    // Fine tuning: flat cards, the brightness curve first at full width (the
    // value the section is about, so it opens it: Louis, 2026-10-14, reversing
    // the "instrument last" order for this page), then the
    // brightness floor (its switch and its level, drawn on the curve as a guide
    // while it is on), reaction speed, exposure, saturation. The preset fold
    // that held them (Game / Vivid / Movie / Ambient / Custom in its header) is
    // set aside with every preset: the family still runs underneath (Preset,
    // AmbientSettingsService.Presets), so a hand edit still records Custom, but
    // nothing on the page selects a preset any more. The change threshold, an
    // engine optimisation nobody tunes, keeps its AmbientSettings default and is
    // no longer exposed (triage of 2026-10-14).
    public IReadOnlyList<SettingDescriptor> TuningSettings =>
    [
        Setting.Curve(
            "AmbientBrightnessCurveCard",
            () => new CurvePoints(BrightnessCurveX1, BrightnessCurveY1, BrightnessCurveX2, BrightnessCurveY2),
            p =>
            {
                BrightnessCurveX1 = p.X1; BrightnessCurveY1 = p.Y1;
                BrightnessCurveX2 = p.X2; BrightnessCurveY2 = p.Y2;
            },
            // The same wide form as the dictation's cut curve, with axes: the
            // screen's brightness along X, the light's along Y, both in percent,
            // and the floor drawn in those units while it is on. The square
            // unit box drew the same curve in a different component next to
            // the Transcription one, which Louis refused (2026-10-14).
            new CurveArgs(
                Frame: () => new CurveFrame(0, 0, 100, 100, 100, 100, Axes: true, XUnit: "%", YUnit: "%"),
                Floor: () => MinBrightnessEnabled ? MinBrightness / 254.0 * 100.0 : null),
            defaultValue: () =>
            {
                var d = new AmbientSettings();
                return new CurvePoints(d.BrightnessCurveX1, d.BrightnessCurveY1, d.BrightnessCurveX2, d.BrightnessCurveY2);
            }),
        Setting.Toggle(
            "AmbientMinBrightnessGroup",
            () => MinBrightnessEnabled,
            value => MinBrightnessEnabled = value,
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
            defaultValue: () => new AmbientSettings().SmoothingAlpha),
        Setting.Magnitude(
            "AmbientExposureCard",
            () => ExposureEv,
            value => ExposureEv = value,
            new MagnitudeArgs(-2, 2, Unit: "EV"),
            defaultValue: () => new AmbientSettings().ExposureEv),
        Setting.Magnitude(
            "AmbientSaturationCard",
            () => SaturationBoost,
            value => SaturationBoost = value,
            new MagnitudeArgs(0, 2, Unit: "×"),
            defaultValue: () => new AmbientSettings().SaturationBoost),
    ];

    // Configuration: the capture rate (60 / 30 / 15 fps), after the bespoke
    // display picker. Dropped by the triage of 2026-10-14 and put back the
    // same evening: Louis looked for the 60 fps choice and found nothing
    // ("j'ai plus d'options"). A dropdown, not radio buttons: three figures
    // nobody compares at a glance, and the flat group read loud for what it
    // decides (Louis, 2026-10-15). The preview block still reads the rate the
    // capture really holds, which may sit under the one chosen here.
    public IReadOnlyList<SettingDescriptor> CaptureSettings =>
    [
        Setting.Choice<int>(
            "AmbientCaptureRateCard",
            () => CaptureFrameRateHz,
            value => CaptureFrameRateHz = value,
            [
                (60, "AmbientCaptureRate60"),
                (30, "AmbientCaptureRate30"),
                (15, "AmbientCaptureRate15"),
            ],
            defaultValue: () => new AmbientSettings().CaptureFrameRateHz),
    ];
}
