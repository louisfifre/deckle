using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The right-click tray menu: the window-opening commands, then the lifecycle
// commands. The Playground and the Deckle Mail preview are developer surfaces,
// so their entries exist only while the developer setting is on; composed
// here, apart from the App, so the rule can be tested without a tray.
internal static class TrayRightClickMenu
{
    internal const string PlaygroundId = "playground";
    internal const string MailId = "mail";

    internal static IReadOnlyList<TrayMenuEntry> Compose(
        bool showDeveloperSurfaces,
        Func<string, string> label,
        Action showLogs,
        Action showSettings,
        Action openPlayground,
        Action openMail,
        Action restart,
        Action quit)
    {
        var entries = new List<TrayMenuEntry>
        {
            new TrayMenuCommand("logs", () => label("TrayMenu_Logs"), showLogs),
            new TrayMenuCommand("settings", () => label("TrayMenu_Settings"), showSettings),
        };
        if (showDeveloperSurfaces)
        {
            entries.Add(new TrayMenuCommand(PlaygroundId, () => label("TrayMenu_Playground"), openPlayground));
            // Deckle Mail on its built-in sample, before its engine exists: the
            // maintainer's way to open the window in his own Deckle.
            entries.Add(new TrayMenuCommand(MailId, () => label("TrayMenu_Mail"), openMail));
        }
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand("restart", () => label("TrayMenu_Restart"), restart));
        entries.Add(new TrayMenuCommand("quit", () => label("TrayMenu_Quit"), quit));
        return entries;
    }
}
