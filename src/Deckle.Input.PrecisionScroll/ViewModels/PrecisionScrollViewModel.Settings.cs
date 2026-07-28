using Deckle.Catalog;

namespace Deckle.Input.PrecisionScroll;

public partial class PrecisionScrollViewModel
{
    public IReadOnlyList<SettingDescriptor> SettingsManifest =>
    [
        Setting.Toggle(
            "PrecisionScrollPage_Group",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Mouse,
            defaultValue: () => new PrecisionScrollSettings().Enabled),

        Setting.Section(
            "PrecisionScrollPage_BehaviorSection",
            [
                Magnitude(
                    "PrecisionScrollPage_DistancePerDetentCard",
                    () => DistancePerDetentMm,
                    value => DistancePerDetentMm = value,
                    PrecisionScrollTuning.DistancePerDetentMinimum,
                    PrecisionScrollTuning.DistancePerDetentMaximum,
                    "mm/step",
                    () => new PrecisionScrollTuning().DistancePerDetentMm),
            ],
            glyph: Glyphs.Tuning,
            visibleWhen: () => Enabled),

        Setting.Section(
            "PrecisionScrollPage_CalibrationSection",
            [
                Magnitude(
                    "PrecisionScrollPage_InitialStepDurationCard",
                    () => InitialStepDurationMs,
                    value => InitialStepDurationMs = value,
                    PrecisionScrollTuning.InitialStepDurationMinimum,
                    PrecisionScrollTuning.InitialStepDurationMaximum,
                    "ms",
                    () => new PrecisionScrollTuning().InitialStepDurationMs),
                Magnitude(
                    "PrecisionScrollPage_QuietPeriodScaleCard",
                    () => QuietPeriodScale,
                    value => QuietPeriodScale = value,
                    PrecisionScrollTuning.QuietPeriodScaleMinimum,
                    PrecisionScrollTuning.QuietPeriodScaleMaximum,
                    "×",
                    () => new PrecisionScrollTuning().QuietPeriodScale),
            ],
            glyph: Glyphs.Trackpad,
            visibleWhen: () => Enabled),
    ];

    private static SettingDescriptor Magnitude(
        string labelKey,
        Func<double> get,
        Action<double> set,
        double minimum,
        double maximum,
        string unit,
        Func<double> defaultValue) =>
        Setting.Magnitude(
            labelKey,
            get,
            set,
            new MagnitudeArgs(minimum, maximum, Unit: unit),
            defaultValue: defaultValue);
}
