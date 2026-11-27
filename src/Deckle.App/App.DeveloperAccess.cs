using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Shell;
using Deckle.Shell.TrayMenu;
using Deckle.Localization;

namespace Deckle.App;

// Developer mode (Settings > General > Developer, off by default) is the one
// door to what an ordinary user has no use for: the surfaces still in alpha
// and the developer tools. Its entry point is the right-click tray menu,
// driven from here.
public partial class App
{
    private static bool DeveloperModeEnabled
        => SettingsService.Instance.Current.Developer.DeveloperMode;

    // Composed once: the mode decides whether the menu opens, not what it
    // holds, and module presence does not change during a run.
    private IReadOnlyList<TrayMenuEntry> ComposeDeveloperTray()
        => TrayDeveloperMenu.Compose(
            key => Loc.Get(key),
            () => CaptureInterfaceFromTray(),
            () => ShowLogWindowLazy(),
            () => RestartAppFromTray());

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
}
