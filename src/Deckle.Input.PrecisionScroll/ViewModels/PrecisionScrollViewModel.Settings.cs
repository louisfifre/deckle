using Deckle.Catalog;

namespace Deckle.Input.PrecisionScroll;

public partial class PrecisionScrollViewModel
{
    public IReadOnlyList<SettingDescriptor> SettingsManifest =>
    [
        Setting.Group(
            "PrecisionScrollPage_Group",
            () => Enabled,
            value => Enabled = value,
            children:
            [
                Setting.Magnitude(
                    "PrecisionScrollPage_SpeedCard",
                    () => Sensitivity,
                    value => Sensitivity = value,
                    new MagnitudeArgs(0.5, 2.0, Unit: "×"),
                    glyph: Glyphs.Tuning,
                    defaultValue: () => new PrecisionScrollSettings().Sensitivity),
            ],
            glyph: Glyphs.Mouse),
    ];
}
