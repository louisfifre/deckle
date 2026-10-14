using Deckle.Catalog;

namespace Deckle.Input.PrecisionScroll;

// The page's manifest: the module switch, then the one everyday preference,
// the scroll distance, flat and hidden while the module is off. The two
// calibration constants (first-step duration, release timing) are no longer
// exposed (triage of 2026-10-14): they belong to the model, not to the user,
// and keep their PrecisionScrollTuning defaults. The view model keeps their
// properties.
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

        Setting.Magnitude(
            "PrecisionScrollPage_DistancePerDetentCard",
            () => DistancePerDetentMm,
            value => DistancePerDetentMm = value,
            new MagnitudeArgs(
                PrecisionScrollTuning.DistancePerDetentMinimum,
                PrecisionScrollTuning.DistancePerDetentMaximum,
                Unit: "mm/step"),
            visibleWhen: () => Enabled,
            defaultValue: () => new PrecisionScrollTuning().DistancePerDetentMm),
    ];
}
