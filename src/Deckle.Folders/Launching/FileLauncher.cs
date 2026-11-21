namespace Deckle.Folders;

public enum LaunchStatus
{
    // Windows took the item: its application, or File Explorer, is opening.
    Launched,
    // The item was gone when it was asked for.
    Missing,
    // The shell refused or failed; ErrorCode says why.
    Failed,
}

public readonly record struct LaunchOutcome(LaunchStatus Status, int ErrorCode = 0);

// Hands an item of a folder to Windows, the two ways the file tool does: open
// a file in its default application, as a double-click in File Explorer does,
// and show an item selected in its folder in File Explorer, the row action
// "Show in File Explorer". Both leave the app, so the self-check never
// calls them and the tests stop at what can be checked without a window
// opening; ShellFileLauncher is the real one.
public interface IFileLauncher
{
    LaunchOutcome Open(string path);

    LaunchOutcome ShowInFolder(string path);
}
