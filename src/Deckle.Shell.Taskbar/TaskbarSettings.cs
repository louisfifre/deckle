namespace Deckle.Shell.Taskbar;

// Mutate, then call TaskbarSettingsService.Save().
//
// Every value is off (or Explorer's own) by default: a fresh install changes
// nothing on the taskbar until the person asks. The App turns these values into
// a TaskbarCustomization and hands it to the native engine (TaskbarCustomizer).
public sealed class TaskbarSettings
{
    /// <summary>
    /// Keep every tray icon on the taskbar instead of the overflow flyout.
    /// Written by the Settings page; the App relays a change to the tray
    /// promoter.
    /// </summary>
    public bool ShowAllTrayIcons { get; set; } = false;

    /// <summary>The taskbar's background; Native leaves Explorer's own.</summary>
    public TaskbarBackground Background { get; set; } = TaskbarBackground.Native;

    /// <summary>
    /// Tray icons and the quick-settings group packed at a 24 px pitch. It is
    /// applied on a vertical taskbar only; on a horizontal one the value is
    /// kept and does nothing.
    /// </summary>
    public bool CompactTray { get; set; } = false;

    /// <summary>The clock as stacked lines (month, day, weekday, hours, minutes).</summary>
    public bool CompactClock { get; set; } = false;

    /// <summary>The Start menu as high as the screen it opens on.</summary>
    public bool StartMenuFullHeight { get; set; } = false;
}
