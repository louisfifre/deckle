using Deckle.Catalog;

namespace Deckle.Input.PrecisionScroll;

public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "PrecisionScrollPage_Group",
            Keywords = ["mouse wheel", "touchpad", "smooth", "inertia", "speed", "sensitivity"],
        },
    ];
}
