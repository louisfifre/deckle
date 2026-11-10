using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The right-click tray menu: the window-opening commands, then the lifecycle
// commands. The Atelier's entry exists only while its module is present; the
// Playground is a developer surface, so its entry exists only while the
// developer setting is on. Composed here, apart from the App, so the rules
// can be tested without a tray.
internal static class TrayRightClickMenu
{
    internal const string AtelierId = "atelier";
    internal const string PlaygroundId = "playground";

    internal static IReadOnlyList<TrayMenuEntry> Compose(
        bool showAtelier,
        bool showPlayground,
        Func<string, string> label,
        Action showLogs,
        Action showSettings,
        Action openAtelier,
        Action openPlayground,
        Action restart,
        Action quit)
    {
        var entries = new List<TrayMenuEntry>
        {
            new TrayMenuCommand("logs", () => label("TrayMenu_Logs"), showLogs),
            new TrayMenuCommand("settings", () => label("TrayMenu_Settings"), showSettings),
        };
        if (showAtelier)
            entries.Add(new TrayMenuCommand(AtelierId, () => label("TrayMenu_Atelier"), openAtelier));
        if (showPlayground)
            entries.Add(new TrayMenuCommand(PlaygroundId, () => label("TrayMenu_Playground"), openPlayground));
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand("restart", () => label("TrayMenu_Restart"), restart));
        entries.Add(new TrayMenuCommand("quit", () => label("TrayMenu_Quit"), quit));
        return entries;
    }
}
