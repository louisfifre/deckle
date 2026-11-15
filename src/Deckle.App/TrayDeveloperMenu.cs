using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The right-click tray menu, offered only while Developer mode is on: the
// surfaces still in alpha (the Atelier and Deckle Projects while their modules
// are present, the Deckle Mail preview), then the developer tools, then Logs
// and Restart at the bottom, where a tray menu keeps the app's own commands. Everything a person
// uses every day is in the left-click menu, which ends on Settings and Quit:
// the logs stay one click away from the Settings footer, and a person who
// wants a fresh start quits and relaunches (Louis, 2026-11-14). Composed here,
// apart from the App, so the rules can be tested without a tray.
internal static class TrayDeveloperMenu
{
    internal const string AtelierId = "atelier";
    internal const string ProjectsId = "projects";
    internal const string MailId = "mail";
    internal const string CaptureInterfaceId = "capture-interface";
    internal const string LogsId = "logs";
    internal const string RestartId = "restart";

    internal static IReadOnlyList<TrayMenuEntry> Compose(
        bool showAtelier,
        bool showProjects,
        Func<string, string> label,
        Action openAtelier,
        Action openProjects,
        Action openMail,
        Action captureInterface,
        Action openLogs,
        Action restart)
    {
        var entries = new List<TrayMenuEntry>();
        if (showAtelier)
            entries.Add(new TrayMenuCommand(AtelierId, () => label("TrayMenu_Atelier"), openAtelier));
        if (showProjects)
            entries.Add(new TrayMenuCommand(ProjectsId, () => label("TrayMenu_Projects"), openProjects));
        // Deckle Mail on its built-in sample, before its engine exists: the
        // maintainer's way to open the window in his own Deckle.
        entries.Add(new TrayMenuCommand(MailId, () => label("TrayMenu_Mail"), openMail));
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand(
            CaptureInterfaceId, () => label("TrayMenu_CaptureInterface"), captureInterface));
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand(LogsId, () => label("TrayMenu_Logs"), openLogs));
        entries.Add(new TrayMenuCommand(RestartId, () => label("TrayMenu_Restart"), restart));
        return entries;
    }
}
