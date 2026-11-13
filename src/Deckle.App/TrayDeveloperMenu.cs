using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The right-click tray menu, offered only while Developer mode is on: the
// surfaces still in alpha (the Atelier while its module is present, the
// Deckle Mail preview), then the developer tools. Everything a person uses
// every day is in the left-click menu. Composed here, apart from the App, so
// the rules can be tested without a tray.
internal static class TrayDeveloperMenu
{
    internal const string AtelierId = "atelier";
    internal const string MailId = "mail";
    internal const string CaptureInterfaceId = "capture-interface";

    internal static IReadOnlyList<TrayMenuEntry> Compose(
        bool showAtelier,
        Func<string, string> label,
        Action openAtelier,
        Action openMail,
        Action captureInterface)
    {
        var entries = new List<TrayMenuEntry>();
        if (showAtelier)
            entries.Add(new TrayMenuCommand(AtelierId, () => label("TrayMenu_Atelier"), openAtelier));
        // Deckle Mail on its built-in sample, before its engine exists: the
        // maintainer's way to open the window in his own Deckle.
        entries.Add(new TrayMenuCommand(MailId, () => label("TrayMenu_Mail"), openMail));
        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuCommand(
            CaptureInterfaceId, () => label("TrayMenu_CaptureInterface"), captureInterface));
        return entries;
    }
}
