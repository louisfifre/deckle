using Deckle.Catalog;

namespace Deckle.Shell.TaskbarCover;

public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "TaskbarCoverPage_Group",
            Keywords = ["taskbar", "cover", "hide", "black band", "mouse"],
        },
        new SettingSearchEntry
        {
            LabelKey = "TaskbarCoverPage_ApproachCard",
            Keywords = ["approach", "distance", "reveal", "hover", "near", "far"],
        },
    ];
}
