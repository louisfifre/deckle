namespace Deckle.Shell.Taskbar;

/// <summary>
/// How a pass of <see cref="TrayIconPromoter"/> ended. The page reads the last
/// one to say when the tray could not be kept complete.
/// </summary>
public enum TrayOutcome
{
    /// <summary>The icons are on the bar and a watcher keeps them there (Apply), or are given back (Restore).</summary>
    Applied,

    /// <summary>The notify icon settings key does not exist, so there is no icon to promote.</summary>
    KeyMissing,

    /// <summary>Windows refused the registry access.</summary>
    PermissionDenied,

    /// <summary>Anything else: a registry error, or no watcher left running.</summary>
    Failed,
}