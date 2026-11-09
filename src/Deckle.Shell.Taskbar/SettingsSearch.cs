using Deckle.Catalog;

namespace Deckle.Shell.Taskbar;

public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_ShowAllTrayIconsCard",
            Keywords = ["taskbar", "tray", "notification area", "overflow", "icons", "show all"],
        },
    ];
}
