using Deckle.Catalog;

namespace Deckle.Shell.Taskbar;

// The page's cards. Headers and descriptions resolve from this module's .resw as
// "<LabelKey>.Header" / ".Description". Every default reads from a fresh
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