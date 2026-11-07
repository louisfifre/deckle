using Deckle.Catalog;

namespace Deckle.Shell.Taskbar;

// The page's two cards, each its own switch with no module switch above them:
// the tray card (a setting) and the Recommended card (Windows' state, see
// TaskbarViewModel.Recommended.cs). Headers and descriptions resolve from this
// module's .resw as "<LabelKey>.Header" / ".Description"; the wording is
// provisional until the taskbar copy pass. Every default reads from a fresh
// TaskbarSettings, the single source the reset writes back.
public partial class TaskbarViewModel
{
    public IReadOnlyList<SettingDescriptor> TrayManifest =>
    [
        Setting.Toggle(
            "TaskbarPage_ShowAllTrayIconsCard",
            () => ShowAllTrayIcons,
            value => ShowAllTrayIcons = value,
            defaultValue: () => new TaskbarSettings().ShowAllTrayIcons,
            advisory: TrayAdvisory),
    ];

    // No default: the card edits Windows' state, not a setting, so it has no
    // reset and the page's Reset all leaves it alone.
    public IReadOnlyList<SettingDescriptor> StartManifest =>
    [
        Setting.Toggle(
            "TaskbarPage_HideRecommendedCard",
            () => HideRecommended,
            RecommendedSwitched,
            confirmOnEnable: _ => ConfirmHideRecommendedAsync(),
            advisory: RecommendedAdvisory),
    ];

    // The promoter's last pass, said only when it did not go through. While
    // the switch is on the pass is the one that keeps the icons on the bar;
    // once it is off, the one that gives them back.
    private string? TrayAdvisory() =>
        TrayMessageKey is { } key ? Loc.GetFromOptional(TaskbarSettingsModule.ResourceLibrary, key) : null;

    // The .resw key of that line, or null when there is nothing to say.
    internal string? TrayMessageKey
    {
        get
        {
            if (_tray.LastOutcome is null or TrayOutcome.Applied) return null;
            return ShowAllTrayIcons ? "TaskbarPage_TrayFailedOn" : "TaskbarPage_TrayFailedOff";
        }
    }
}