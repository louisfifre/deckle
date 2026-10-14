using System;

namespace Deckle.Shell.TrayMenu;

/// <summary>
/// One on/off entry of the tray quick-actions menu: a switchable module the
/// App exposes, rendered as a menu item with an on/off pill
/// (<see cref="TraySwitchMenuItem"/>). The menu reads
/// <see cref="IsOn"/> at each open and calls <see cref="Toggle"/> when the user
/// clicks the item; the module's own settings observer does the actual start
/// or stop. The same record names the toggle's hotkey action.
/// </summary>
/// <param name="Id">Stable identifier, used in logs (for example "ambient",
/// "taskbar-cover").</param>
/// <param name="Label">Localized item label.</param>
/// <param name="IsOn">Reads the module's persisted enabled state.</param>
/// <param name="Toggle">Flips the module's persisted enabled state.</param>
public sealed record TrayToggle(string Id, string Label, Func<bool> IsOn, Action Toggle) : TrayMenuEntry;
