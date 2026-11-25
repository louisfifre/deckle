using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The right-click tray menu, offered only while Developer mode is on: the
// developer tools, then Logs and Restart at the bottom, where a tray menu keeps the
// app's own commands. Everything a person uses every day is in the left-click
// menu, which ends on Settings and Quit: the logs stay one click away from the
// Settings footer, and a person who wants a fresh start quits and relaunches
// (Louis, 2026-11-14). Composed here, apart from the App, so the rules can be
// tested without a tray.
internal static class TrayDeveloperMenu
{
    internal const string CaptureInterfaceId = "capture-interface";
    internal const string LogsId = "logs";
    internal const string RestartId = "restart";

    internal static IReadOnlyList<TrayMenuEntry> Compose(
        Func<string, string> label,
        Action captureInterface,
        Action openLogs,
        Action restart)
    {
        var entries = new List<TrayMenuEntry>();
        entries.Add(new TrayMenuCommand(
            CaptureInterfaceId, () => label("TrayMenu_CaptureInterface"), captureInterface));
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand(LogsId, () => label("TrayMenu_Logs"), openLogs));
        entries.Add(new TrayMenuCommand(RestartId, () => label("TrayMenu_Restart"), restart));
        return entries;
    }
}
