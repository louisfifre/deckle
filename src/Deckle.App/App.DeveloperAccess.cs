using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Shell;
using Deckle.Shell.TrayMenu;
using Microsoft.UI.Dispatching;
using Deckle.Localization;

namespace Deckle.App;

// The Playground is a developer surface: every way into it follows the
// developer setting (Settings > General > Developer, off by default). Three
// entry points exist: the right-click tray menu, and the Playground buttons in
// the Settings and Logs title bars. All three are driven from here, and
// ShowPlaygroundLazy refuses when the setting is off, so no path can bypass it.
// The Deckle Mail window preview follows the same gate: its one entry, in the
// right-click tray menu, shows only while the setting is on, ShowMailLazy
// refuses otherwise, and an open Mail window closes when the setting goes off.
public partial class App
{
    // What the right-click menu was last composed with, to recompose only on a
    // change and never swap items under a menu that is open.
    private bool _trayPlaygroundShown;

    private static bool DeveloperModeEnabled
        => SettingsService.Instance.Current.Developer.DeveloperMode;

    private IReadOnlyList<TrayMenuEntry> ComposeRightClickTray()
    {
        _trayPlaygroundShown = DeveloperModeEnabled;
        return TrayRightClickMenu.Compose(
            AtelierPresent,
            _trayPlaygroundShown,
            key => Loc.Get(key),
            () => ShowLogWindowLazy(),
            () => ShowSettingsWindowLazy(),
            () => ShowAtelierWindowLazy(),
            () => ShowPlaygroundLazy(),
            () => ShowMailLazy(),
            () => RestartAppFromTray(),
            () => QuitApp());
    }

    // Right click: the setting is read at each open, so the entry follows it
    // without a restart.
    private void ShowRightClickTray()
    {
        if (_trayMenu is null) return;
        if (_trayPlaygroundShown != DeveloperModeEnabled)
            _trayMenu.Entries = ComposeRightClickTray();
        _trayMenu.Show();
    }

    // SettingsService.Changed fires after the disk write, on any thread. The
    // windows' buttons are updated on the UI thread; a Playground left open
    // when the setting goes off is closed, since its entry points are gone.
    private void WirePlaygroundAccess(DispatcherQueue ui)
    {
        SettingsService.Instance.Changed += () =>
            ui.TryEnqueueOrLog(ApplyPlaygroundAccess, "SETTINGS", "playground access");
    }

    private void ApplyPlaygroundAccess()
    {
        bool enabled = DeveloperModeEnabled;
        _settingsWindow?.SetPlaygroundAvailable(enabled);
        _logWindow?.SetPlaygroundAvailable(enabled);
        if (enabled) return;
        _playgroundWindow?.Close();
        _mailWindow?.Close();
    }
}
