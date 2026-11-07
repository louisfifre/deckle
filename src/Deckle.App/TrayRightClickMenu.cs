using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The right-click tray menu: the window-opening commands, then the lifecycle
// commands. The Playground is a developer surface, so its entry exists only
// while the developer setting is on; composed here, apart from the App, so the
// rule can be tested without a tray.
internal static class TrayRightClickMenu
{
    internal const string PlaygroundId = "playground";

    internal static IReadOnlyList<TrayMenuEntry> Compose(
        bool showPlayground,
        Func<string, string> label,
        Action showLogs,
        Action showSettings,
        Action openPlayground,
        Action restart,
        Action quit)
    {
        var entries = new List<TrayMenuEntry>
        {
            new TrayMenuCommand("logs", () => label("TrayMenu_Logs"), showLogs),
            new TrayMenuCommand("settings", () => label("TrayMenu_Settings"), showSettings),
        };
        if (showPlayground)
            entries.Add(new TrayMenuCommand(PlaygroundId, () => label("TrayMenu_Playground"), openPlayground));
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand("restart", () => label("TrayMenu_Restart"), restart));
        entries.Add(new TrayMenuCommand("quit", () => label("TrayMenu_Quit"), quit));
        return entries;
    }
}
