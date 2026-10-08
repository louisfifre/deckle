using System;

namespace Deckle.Shell.TrayMenu;

/// <summary>
/// One on/off row of the tray quick-actions panel: a switchable module the App
/// exposes. The panel reads <see cref="IsOn"/> at each open and calls
/// <see cref="Toggle"/> when the user flips the switch; the module's own
/// settings observer does the actual start or stop.
/// </summary>
/// <param name="Id">Stable identifier, used in logs (for example "ambient",
/// "taskbar-cover").</param>
/// <param name="Label">Localized row label.</param>
/// <param name="IsOn">Reads the module's persisted enabled state.</param>
/// <param name="Toggle">Flips the module's persisted enabled state.</param>
public sealed record TrayToggle(string Id, string Label, Func<bool> IsOn, Action Toggle);
