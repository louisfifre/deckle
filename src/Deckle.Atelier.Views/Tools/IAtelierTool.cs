using Microsoft.UI.Xaml;

namespace Deckle.Atelier.Views;

// ── The tool seam ─────────────────────────────────────────────────────────────
//
// A tool is one way of looking at the folder the window stands in (spec,
// Window: "Several tools look at the same folder"). It is four things: a
// name, a glyph, a centre view, and what it puts in the details panel for a
// selection. The window owns everything else: the top row, the place, the
// history, the panel's frame and position ("A tool changes what the panels
// hold, not where they are").
//
// To add or fill a tool: work in its own folder under Tools/, implement
// IAtelierTool and IAtelierToolView there, and keep its one line in
// AtelierToolCatalog. Nothing else in the window changes.
public interface IAtelierTool
{
    // Stable, lowercase, ASCII: what the log and the self-check name it by.
    string Id { get; }

    // What the active tool button shows and what the others say on hover.
    string Name { get; }

    // A Segoe Fluent Icons code point.
    string Glyph { get; }

    // Called once per window, the first time the tool is shown. The view
    // lives as long as the window, so it may keep what it computed.
    IAtelierToolView CreateView(IAtelierToolHost host);
}

// The centre of the window while the tool is active.
public interface IAtelierToolView
{
    FrameworkElement Content { get; }

    // Shows the folder. The window calls it when the place changes, when the
    // person switches to this tool, and on Refresh (F5), which re-runs the
    // current tool on the current folder (spec, Open 17). The selection is
    // the one the window keeps across tools, or null.
    //
    // Runs on the UI thread; work that touches the disk goes off it. The
    // token is cancelled when a newer call supersedes this one. To change the
    // details panel, call IAtelierToolHost.Select.
    Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation);
}

// What the window offers a tool.
public interface IAtelierToolHost
{
    // Goes into a folder of the current added folder, as a double-click on a
    // folder does. Recorded in Back and Forward.
    void Open(FolderPlace place);

    // The selection changed. The path is kept when the person switches tool
    // (spec: the selection stays); details is what the panel holds for it,
    // and null closes the panel. A click selects and opens the panel; it never
    // opens the item (spec, Behaviours).
    void Select(string? path, UIElement? details);

    // Maps a path and a pixel size to the icon a list shows for it, or null
    // for the neutral glyph.
    IFileIconProvider Icons { get; }
}
