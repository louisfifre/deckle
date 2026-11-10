namespace Deckle.Shell.Taskbar;

// The names a part goes by in the log: snake_case, one spelling everywhere.
internal static class TaskbarPartNames
{
    public static string ForLog(this TaskbarPart part) => part switch
    {
        TaskbarPart.Background => "background",
        TaskbarPart.CompactTray => "compact_tray",
        TaskbarPart.CompactTaskButtons => "compact_task_buttons",
        TaskbarPart.CompactClock => "compact_clock",
        TaskbarPart.StartMenuHeight => "start_menu_height",
        TaskbarPart.StartMenuWidth => "start_menu_width",
        TaskbarPart.StartMenuRecommended => "start_menu_recommended",
        _ => "unknown",
    };
}
