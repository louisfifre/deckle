namespace Deckle.Shell.Taskbar;

/// <summary>
/// One thing the person can turn on that the native engine does for them: the
/// unit the page speaks in when it says something is not in effect.
/// </summary>
public enum TaskbarPart
{
    Background,
    BarThickness,
    CompactTray,
    CompactTaskButtons,
    CompactClock,
    Thumbnails,
    StartMenuHeight,
    StartMenuWidth,
    StartMenuRecommended,
}

internal static class TaskbarPartProcess
{
    /// <summary>Whether the part lives in the Start menu's process rather than in explorer.</summary>
    public static bool IsStartMenu(this TaskbarPart part) =>
        part is TaskbarPart.StartMenuHeight or TaskbarPart.StartMenuWidth or TaskbarPart.StartMenuRecommended;
}
