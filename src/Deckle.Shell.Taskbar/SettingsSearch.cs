using Deckle.Catalog;

namespace Deckle.Shell.Taskbar;

public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_BackgroundCard",
            Keywords = ["taskbar", "background", "transparent", "black", "translucent", "acrylic", "fill"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_CompactTaskButtonsCard",
            Keywords = ["taskbar", "buttons", "apps", "icons", "spacing", "compact", "vertical", "start", "task view", "button height", "size"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_CompactClockCard",
            Keywords = ["taskbar", "clock", "time", "date", "stacked", "lines", "compact"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_ShowAllTrayIconsCard",
            Keywords = ["taskbar", "tray", "notification area", "overflow", "icons", "show all"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_CompactTrayCard",
            Keywords = ["taskbar", "tray", "notification area", "icons", "spacing", "icon spacing", "compact", "vertical", "quick settings"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_StartMenuFullHeightCard",
            Keywords = ["start", "start menu", "height", "tall", "full height", "size"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarPage_WindowsKeyShowsTaskbarCard",
            Keywords = ["windows key", "win key", "start", "start menu", "taskbar", "focus", "game", "fullscreen", "shortcut"],
        },
    ];
}
