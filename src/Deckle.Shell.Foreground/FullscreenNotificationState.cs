namespace Deckle.Shell.Foreground;

// The shell half of the fullscreen test, kept free of Win32 so it can be
// tested: which SHQueryUserNotificationState answers say the foreground
// window is fullscreen.
//
// The answer describes the whole session, not a window ("the state of the
// computer for the current user"), and the watcher pins it on whatever
// window holds the foreground. Two answers survive that: an exclusive
// Direct3D app owns the display, so it is the foreground while it runs, and
// presentation mode is the person's own request to be left alone.
//
// QUNS_BUSY is left out. Microsoft Learn defines it as "a full-screen
// application is running", which a window that is not the foreground can
// make true, and the geometry stage already catches the foreground window
// that is fullscreen in a window: kept, it could only stand the taskbar
// cover down for an ordinary window.
public static class FullscreenNotificationState
{
    public const int QUNS_BUSY                    = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE       = 4;

    public static bool ReadsAsFullscreen(int quns) =>
        quns is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
}
