using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Shell;
using Deckle.Shell.TrayMenu;
using Microsoft.UI.Dispatching;
using Deckle.Localization;

namespace Deckle.App;

// Developer mode (Settings > General > Developer, off by default) is the one
// door to what an ordinary user has no use for: the surfaces still in alpha
// and the developer tools. Its entry points are the right-click tray menu and
// the Playground buttons in the Settings and Logs title bars. All are driven
// from here, and ShowPlaygroundLazy and ShowMailLazy refuse while the mode is
// off, so no path can bypass it. A Playground or Mail window left open when
// the mode goes off is closed, since its entry points are gone.
public partial class App
{
    private static bool DeveloperModeEnabled
        => SettingsService.Instance.Current.Developer.DeveloperMode;

    // The developer capture under Settings > Diagnostics > Detailed logs, the
    // microphone level, is a dataset, not log lines a person shares. It
    // records only while both switches are on: an ordinary user who turns
    // Detailed logs on gets the list shown to them, nothing hidden under it.
    private static bool DeveloperCapturesAdmitted
        => DeveloperModeEnabled
        && Diagnostics.Logging.LoggingSettingsService.Instance.Current.DetailedLogs;

    // Composed once: the mode decides whether the menu opens, not what it
    // holds, and module presence does not change during a run.
    private IReadOnlyList<TrayMenuEntry> ComposeDeveloperTray()
        => TrayDeveloperMenu.Compose(
            AtelierPresent,
            key => Loc.Get(key),
            () => ShowAtelierWindowLazy(),
            () => ShowPlaygroundLazy(),
            () => ShowMailLazy(),
            () => CaptureInterfaceFromTray());

    // Right click: the developer menu while the mode is on, read at each
    // click so it follows the setting without a restart. With the mode off it
    // opens the left-click menu, since a tray icon whose right click does
    // nothing reads as broken.
    private void ShowRightClickTray()
    {
        if (DeveloperModeEnabled)
            _trayDeveloperMenu?.Show();
        else
            ShowTrayActions();
    }

    // SettingsService.Changed fires after the disk write, on any thread. The
    // windows' buttons are updated on the UI thread.
    private void WireDeveloperAccess(DispatcherQueue ui)
    {
        SettingsService.Instance.Changed += () =>
            ui.TryEnqueueOrLog(ApplyDeveloperAccess, "SETTINGS", "developer access");
    }

    private void ApplyDeveloperAccess()
    {
        bool enabled = DeveloperModeEnabled;
        _settingsWindow?.SetPlaygroundAvailable(enabled);
        _logWindow?.SetPlaygroundAvailable(enabled);
        if (enabled) return;
        _playgroundWindow?.Close();
        _mailWindow?.Close();
    }
}
