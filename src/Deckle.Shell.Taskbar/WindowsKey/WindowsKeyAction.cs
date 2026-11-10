namespace Deckle.Shell.Taskbar;

// What the Windows key tracker asks the caller to do after one key event.
public enum WindowsKeyAction
{
    /// <summary>Nothing: the event is not the tracker's business.</summary>
    None,

    /// <summary>
    /// A Windows key went down on its own. Inject a throwaway key press so
    /// Windows reads the press as part of a shortcut and leaves Start closed
    /// when the key comes up.
    /// </summary>
    SuppressStart,

    /// <summary>
    /// The Windows key came up with nothing pressed in between. Bring the
    /// taskbar forward, as Windows + T does.
    /// </summary>
    FocusTaskbar,
}
