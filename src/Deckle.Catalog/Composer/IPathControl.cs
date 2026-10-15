using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── IPathControl ──────────────────────────────────────────────────────────────
//
// The contract between the composer and the control that renders the Location
// line of a Path setting: a SettingsCard headed "Location", the folder's path
// as its description (or a field in its place, in the Editable mode) and the
// actions at its trailing edge, "Choose a folder" then "Open" as words. The
// concrete control (FolderLocationLine) lives in Deckle.Settings: it anchors the
// OS folder picker to the Settings window and logs through that module's ETW
// source, neither of which belongs in Catalog. So the composer builds it
// through SettingsComposer.PathControlFactory and drives it through this
// surface.
//
// Line is the card itself, so a fold can host it in Items (which accepts
// SettingsCard instances only). Actions is the row at its trailing edge, the
// line's content at rest: the composer takes it out of that slot to put its
// reset wheel ahead of it, [reset | actions] like every other card.
public interface IPathControl
{
    // The folder the setting holds; empty means the default folder. The control
    // raises PathChanged on the person's gesture only (a pick, a field commit),
    // never on a value the composer pushes.
    string Path { get; set; }
    event EventHandler? PathChanged;

    SettingsCard Line { get; }
    FrameworkElement Actions { get; }
}
