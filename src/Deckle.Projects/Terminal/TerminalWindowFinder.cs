using System.Runtime.InteropServices;
using System.Text;

namespace Deckle.Projects;

public sealed record TopWindow(nint Handle, string Title);

// The window system as far as "Show terminal" needs it, so a test gives it a
// list of windows and sees what the finder does with them.
public interface IWindowSystem
{
    // The visible top-level windows with their titles.
    IReadOnlyList<TopWindow> TopWindows();

    bool IsMinimized(nint handle);

    void Restore(nint handle);

    // True when the window came to the front. Windows may refuse a process that
    // is not the one the person is using; Deckle asks from a click.
    bool BringToFront(nint handle);
}

// Brings an action's terminal window to the front. The window is found by its
// title: the launch gave it one (`--title` with `--suppressApplicationTitle`,
// or the wrapper's own WindowTitle for the console fallback), made of the
// action's label, the project's name and four hex characters that tell it from
// the windows of earlier Deckle sessions.
//
// Measured: the window's class is CASCADIA_HOSTING_WINDOW_CLASS and its owner
// process is WindowsTerminal.exe, the shell's parent, a second way to find it
// that needs no title. The title is kept because it also finds a console
// window and needs nothing but a window list.
//
// A minimised window is restored first, then given the foreground (measured:
// it came to the front).
public sealed class TerminalWindowFinder(IWindowSystem windows)
{
    public TerminalWindowFinder() : this(new User32WindowSystem()) { }

    // False when no visible window carries the title: the caller says nothing,
    // the run's own state line stays.
    public bool Show(string title)
    {
        string wanted = WindowsTerminalLauncher.TitleText(title);
        TopWindow? match = windows.TopWindows().FirstOrDefault(w => string.Equals(w.Title, wanted, StringComparison.Ordinal));
        if (match is null) return false;

        if (windows.IsMinimized(match.Handle)) windows.Restore(match.Handle);
        return windows.BringToFront(match.Handle);
    }
}

internal sealed class User32WindowSystem : IWindowSystem
{
    private const int SwRestore = 9;

    public IReadOnlyList<TopWindow> TopWindows()
    {
        var found = new List<TopWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            int length = GetWindowTextLengthW(handle);
            if (length <= 0) return true;

            var text = new StringBuilder(length + 1);
            if (GetWindowTextW(handle, text, text.Capacity) > 0) found.Add(new TopWindow(handle, text.ToString()));
            return true;
        }, 0);
        return found;
    }

    public bool IsMinimized(nint handle) => IsIconic(handle);

    public void Restore(nint handle) => ShowWindow(handle, SwRestore);

    public bool BringToFront(nint handle) => SetForegroundWindow(handle);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);
}
