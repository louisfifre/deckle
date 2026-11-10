namespace Deckle.Shell.Taskbar;

// The port from the Windows key tracker to the keyboard it presses for the
// person. WindowsKeyInjector is the production implementation; the interface
// lets the tracker's runtime be tested without sending real input.
public interface IWindowsKeyInjector
{
    /// <summary>
    /// Presses and releases a throwaway key, so a Windows key that is down is
    /// read as part of a shortcut. False when Windows refused the input.
    /// </summary>
    bool SuppressStart();

    /// <summary>
    /// Presses Windows + T and releases both, which brings the taskbar forward.
    /// False when Windows refused the input.
    /// </summary>
    bool FocusTaskbar();

    /// <summary>The Win32 error behind the last refusal, 0 when the last send went through.</summary>
    int LastWin32Error { get; }
}
