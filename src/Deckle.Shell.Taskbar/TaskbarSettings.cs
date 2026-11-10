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
    /// Tray icons and the quick-settings group packed at <see cref="TrayIconPitch"/>.
    /// It is applied on a vertical taskbar only; on a horizontal one the value is
    /// kept and does nothing.
    /// </summary>
    public bool CompactTray { get; set; } = false;

    /// <summary>
    /// The distance between two tray icons while <see cref="CompactTray"/> is on,
    /// in effective pixels. Explorer's own is 32. A settings file written before
    /// this value existed reads 24, the pitch the compact tray always had.
    /// </summary>
    public int TrayIconPitch { get; set; } = TaskbarCustomization.DefaultCompactPitch;

    /// <summary>
    /// App buttons, Start and Task view packed at <see cref="TaskButtonPitch"/>.
    /// Like the compact tray, it is applied on a vertical taskbar only.
    /// </summary>
    public bool CompactTaskButtons { get; set; } = false;

    /// <summary>
    /// The height of an app button while <see cref="CompactTaskButtons"/> is on,
    /// in effective pixels. Explorer's own is 32 in Small mode. Older files read
    /// 24, as for the tray.
    /// </summary>
    public int TaskButtonPitch { get; set; } = TaskbarCustomization.DefaultCompactPitch;

    /// <summary>The clock as stacked lines (month, day, weekday, hours, minutes).</summary>
    public bool CompactClock { get; set; } = false;

    /// <summary>The Start menu as high as the screen it opens on.</summary>
    public bool StartMenuFullHeight { get; set; } = false;

    /// <summary>How wide the Start menu is: Windows' own, the screen's, or <see cref="StartMenuCustomWidth"/>.</summary>
    public StartMenuWidth StartMenuWidth { get; set; } = StartMenuWidth.Native;

    /// <summary>
    /// The Start menu's width while <see cref="StartMenuWidth"/> is Custom, in
    /// effective pixels: the panel the person sees, its shadow left out. A value
    /// wider than the screen the menu opens on is held to that screen.
    /// </summary>
    public int StartMenuCustomWidth { get; set; } = TaskbarCustomization.DefaultStartMenuWidth;

    /// <summary>
    /// The Windows key pressed on its own brings the taskbar forward instead of
    /// opening Start. Every shortcut that uses the key keeps working.
    /// </summary>
    public bool WindowsKeyShowsTaskbar { get; set; } = false;
}
