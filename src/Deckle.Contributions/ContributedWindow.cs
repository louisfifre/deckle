using Microsoft.UI.Xaml;

namespace Deckle.Contributions;

// One window a contribution opens, described so the host can create it on
// first use and handle it like its own windows without naming its type.
public sealed record ContributedWindow
{
    // Short logical name: the window's capture name in the interface capture,
    // its key in the saved placements and its name in the log. Lowercase
    // ASCII, unique across the whole composition.
    public required string Name { get; init; }

    // Builds the window. The host calls it at most once at a time: a window
    // closed is built again on the next open.
    public required Func<Window> Create { get; init; }

    // Shows the window and brings it forward (the window's own
    // ShowAndActivate): the host never shows a window any other way.
    public required Action<Window> Show { get; init; }

    // Resource key of the tray menu entry that opens the window; null when
    // the window has no entry.
    public string? TrayLabelKey { get; init; }

    // A window that opens, and whose tray entry shows, only while Developer
    // mode is on: a preview for the maintainer.
    public bool DeveloperOnly { get; init; }

    // Shows the recording state in the window's icons; null for a window that
    // does not. Called at creation with the last state, then at every change.
    public Action<Window, bool>? SetRecordingState { get; init; }

    // The window has closed and the host dropped it, before the next open
    // builds a fresh one (a work that outlives the window says so here).
    public Action<Window>? OnClosed { get; init; }
}
