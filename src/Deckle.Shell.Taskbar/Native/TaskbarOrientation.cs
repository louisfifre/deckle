using static Deckle.Shell.Taskbar.TaskbarNativeMethods;

namespace Deckle.Shell.Taskbar;

// Which way the taskbar runs. The compact tray is written for a vertical bar
// (a column of icons), so the customizer asks before it sends those rules.
//
// Read from C# and not left to the engine's visual states: the answer is a
// plain window rectangle, it is the same on every Windows build, a test can
// stand in for it, and the engine stays a generic rule applier that knows
// nothing of Deckle's settings. The customizer asks again each time the engine
// connects, which covers an Explorer restart (the only way Windows 11 changes
// the taskbar's edge).
internal static class TaskbarOrientation
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";

    /// <summary>
    /// True when the primary taskbar is taller than it is wide, which is what a
    /// bar on the left or right edge is, auto-hidden or not. False when there is
    /// no taskbar window yet (Explorer is starting): the horizontal answer only
    /// holds the vertical-only rules back, and the next connection asks again.
    /// </summary>
    public static bool IsVertical()
    {
        nint taskbar = FindWindow(PrimaryTaskbarClass, null);
        if (taskbar == 0 || !GetWindowRect(taskbar, out WindowRect rect)) return false;
        return rect.right - rect.left < rect.bottom - rect.top;
    }
}
