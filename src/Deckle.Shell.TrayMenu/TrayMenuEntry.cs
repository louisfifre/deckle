// TrayMenuEntry — what a tray menu shows, composed by the App, rendered by TrayMenuHost.

using System;

namespace Deckle.Shell.TrayMenu;

// ─── Tray menu entries ────────────────────────────────────────────────────────
//
// The App composes a list of entries from module presence; TrayMenuHost renders
// them as native menu items. The module knows no label and no module: a
// command carries a label function, read at each open so "Record" can read
// "Stop recording" while a dictation runs, and an action; a toggle (TrayToggle)
// carries its on/off reader and flipper; a separator groups.

/// <summary>One entry of a tray menu, rendered by <see cref="TrayMenuHost"/>.</summary>
public abstract record TrayMenuEntry;

/// <summary>A command item: the menu closes, then <paramref name="Invoke"/> runs.</summary>
/// <param name="Id">Stable identifier, used in logs (for example "record", "quit").</param>
/// <param name="Label">Read at each open, so a label can follow a state.</param>
/// <param name="Invoke">The action, run after the menu has closed.</param>
public sealed record TrayMenuCommand(string Id, Func<string> Label, Action Invoke) : TrayMenuEntry;

/// <summary>A divider between two groups of entries.</summary>
public sealed record TrayMenuSeparator : TrayMenuEntry;
