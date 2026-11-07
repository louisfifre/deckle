namespace Deckle.Shell.Taskbar;

// Mutate, then call TaskbarSettingsService.Save().
//
// The one value the module keeps. The Start menu's Recommended section has no
// setting here: Windows holds the policy, and the page reads it from there.
public sealed class TaskbarSettings
{
    /// <summary>
    /// Keep every tray icon on the taskbar instead of the overflow flyout.
    /// Written by the Settings page; the App relays a change to the tray
    /// promoter.
    /// </summary>
    public bool ShowAllTrayIcons { get; set; } = false;
}
