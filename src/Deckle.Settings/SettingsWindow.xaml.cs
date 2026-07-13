using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Settings;

// ─── Settings Window ──────────────────────────────────────────────────────────
//
// Mica + system theme, native TitleBar (icon + title + hamburger + Logs).
// NavigationView in PaneDisplayMode=Auto manages the Left / LeftCompact /
// LeftMinimal switch; its own pane toggle is off — the TitleBar carries the
// hamburger and relays through PaneToggleRequested.
//
// Navigation: Tag on each item = full Page type name, resolved via
// Type.GetType in OnNavSelectionChanged (pattern from the official Microsoft
// Learn "Code example" sample).
//
// Auto-save everywhere, so no global Cancel/Save. Close destroys the window;
// App lazily recreates the instance on the next open.

public sealed partial class SettingsWindow : Window
{
    private readonly IntPtr _hwnd;

    private BitmapImage? _iconIdle;
    private string? _iconIdlePath;

    // Callback injected by App to open the shared LogWindow from the TitleBar
    // "Logs" command. Left null = the button has no effect.
    public Action? OnShowLogsRequested { get; set; }

    // The module nav items this window inserted from SettingsModuleRegistry,
    // tracked so a live registry change (a module installed / removed while the
    // window is open) can remove the old band before rebuilding it.
    private readonly List<NavigationViewItem> _moduleNavItems = new();

    public SettingsWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        // App icon, shared with tray / LogWindow.
        _iconIdlePath = IconAssets.ResolvePath(recording: false);
        if (_iconIdlePath is not null)
        {
            _iconIdle = new BitmapImage(new Uri(_iconIdlePath));
            AppTitleBarIcon.ImageSource = _iconIdle;
            AppWindow.SetIcon(_iconIdlePath);
        }

        // Native title bar: height/drag/caption are managed by the control.
        // Tall gives the bar room for the interactive content it now carries
        // (Logs command, and the search box added later).
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        SystemBackdrop = new MicaBackdrop();

        // Keep the pane's open state aligned with the display mode as the
        // window is resized across the Auto breakpoints.
        Nav.DisplayModeChanged += OnNavDisplayModeChanged;

        // Same sync once the template is up, for the initial mode.
        Nav.Loaded += (_, _) => SyncNavigationPane(Nav);

        // Materialise the module-owned pages from the registry into the nav
        // (between Recording and Diagnostics) before the initial selection, so
        // the first item is present and selectable. Rebuild on registry change so
        // an install / uninstall while the window is open reflects live.
        BuildModuleNavItems();
        SettingsModuleRegistry.Changed += OnModulesChanged;
        this.Closed += (_, _) => SettingsModuleRegistry.Changed -= OnModulesChanged;

        // Initial selection → triggers SelectionChanged → navigates to
        // GeneralPage. One navigation path, no double-navigation.
        Nav.SelectedItem = Nav.MenuItems[0];

        Title = Loc.Get("Settings_WindowTitle");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 1440));

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable   = true;
        // Minimum consistent with NavigationView Auto breakpoints (640/1008).
        // Go below 640 to expose the native LeftMinimal mode.
        presenter.PreferredMinimumWidth  = 320;
        presenter.PreferredMinimumHeight = 400;
        AppWindow.SetPresenter(presenter);

        // Theme: wire ActualThemeChanged on the XAML root to trace
        // light/dark/HC transitions. SettingsWindow is the UI-side trigger for
        // any "user" switch (Appearance combo in GeneralPage, pushed through
        // SettingsHost.ApplyTheme → App.ApplyTheme), so this event is
        // especially useful here to confirm that the switch was actually
        // received by the window that triggered it.
        if (Content is FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }
    }

    // ── Theme tracing ────────────────────────────────────────────────────────
    private ElementTheme _lastTheme;

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        var to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged(
            "settings", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    // ── Module nav band ──────────────────────────────────────────────────────
    //
    // Builds one NavigationViewItem per registered module and routes it into the
    // band its Tier names: Header at the top of the primary menu, Main appended after
    // the General anchor, Footer into the footer menu. Within a band, the registry
    // already hands them back in (Tier, Order) order. Idempotent: any items a previous
    // call inserted are removed from both menus first, so a live registry change
    // rebuilds the band rather than duplicating it. Labels resolve from the OWNING
    // module's PRI subtree (Loc.GetFrom) — the module ships its own nav wording — and
    // the glyph is a Glyphs.* character built straight into a FontIcon, the same
    // code-side path the composer uses.
    private void BuildModuleNavItems()
    {
        foreach (NavigationViewItem old in _moduleNavItems)
        {
            Nav.MenuItems.Remove(old);
            Nav.FooterMenuItems.Remove(old);
        }
        _moduleNavItems.Clear();

        // Main items append after General, the sole remaining static anchor (its
        // count is the current end of the primary menu). Header items go to the very
        // top and push the Main insertion point down as they are inserted.
        int mainInsertAt = Nav.MenuItems.Count;
        int headerInsertAt = 0;

        foreach (SettingsModuleDescriptor module in SettingsModuleRegistry.Modules)
        {
            var item = new NavigationViewItem
            {
                Content = Loc.GetFrom(module.OwningAssembly, module.LabelKey),
                Tag = module.PageTag,
                Icon = new FontIcon { Glyph = module.Glyph },
            };

            switch (module.Tier)
            {
                case SettingsNavTier.Header:
                    Nav.MenuItems.Insert(headerInsertAt, item);
                    headerInsertAt++;
                    mainInsertAt++;
                    break;
                case SettingsNavTier.Footer:
                    Nav.FooterMenuItems.Add(item);
                    break;
                default: // Main
                    Nav.MenuItems.Insert(mainInsertAt, item);
                    mainInsertAt++;
                    break;
            }
            _moduleNavItems.Add(item);
        }
    }

    // Registry changed (a module installed / removed) — rebuild the band. Register
    // / Unregister may be called from any thread, so marshal onto the UI thread
    // before touching the NavigationView.
    private void OnModulesChanged()
    {
        DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "settings-window-modules",
            callback: BuildModuleNavItems,
            rejectSource: "SETTINGS", rejectWhat: "module nav rebuild");
    }

    public void ShowAndActivate(string? pageTag = null)
    {
        if (AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            op.Restore();
        }

        // If a page tag is requested, select the corresponding nav item. The
        // selection triggers OnNavSelectionChanged → Frame navigation.
        if (pageTag is not null)
        {
            foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            {
                if (item.Tag as string == pageTag)
                {
                    Nav.SelectedItem = item;
                    break;
                }
            }
        }

        AppWindow.Show();
        this.Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        // Windowing: emitted post-Show to capture the effective rect after DWM
        // has positioned the window. The anchor is "Center": SettingsWindow
        // only does an AppWindow.Resize (960×1440) in the ctor; initial
        // centering is done by Windows. Emitted on every ShowAndActivate
        // because a user drag between two openings changes the rect; the last
        // trace remains the current truth.
        WindowingProbe.EmitWindowPositioned(_hwnd, "settings", "Center");
    }

    // ── TitleBar pane toggle ─────────────────────────────────────────────
    //
    // The hamburger lives on the TitleBar; NavigationView.IsPaneToggleButtonVisible
    // is False. Relay the request to the pane. SyncNavigationPane is deliberately
    // NOT called here — it forces IsPaneOpen from the display mode, which would
    // immediately re-close a pane the user just opened in a compact mode.

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
    {
        Nav.IsPaneOpen = !Nav.IsPaneOpen;
    }

    // Logs command in the TitleBar: delegate to App, which opens the shared
    // LogWindow. The OnShowLogsRequested field (wired by App) is unchanged.
    private void OnLogsButtonClick(object sender, RoutedEventArgs e)
    {
        DeckleSettingsSource.Log.OpenLogsFromFooter();
        OnShowLogsRequested?.Invoke();
    }

    // ── NavigationView: pane sync by DisplayMode ─────────────────────────
    //
    // The pane toggle no longer renders inside the content area (it lives on
    // the TitleBar), so no Frame margin compensation is needed in Minimal mode.

    private void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        SyncNavigationPane(sender);
    }

    // ── NavigationView: page swap ────────────────────────────────────────────
    //
    // Canonical Microsoft Learn pattern (sample §"Code example"): the item's Tag
    // carries the full Page type name, resolved by Type.GetType.
    // Keeps CurrentSourcePageType != pageType to avoid redundant re-Navigate
    // on initial setup.

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        DeckleSettingsSource.Log.NavSelectionChanged((args.SelectedItem as NavigationViewItem)?.Content?.ToString() ?? "");

        if (args.SelectedItem is not NavigationViewItem item)
        {
            DeckleSettingsSource.Log.NavSelectionIgnored("not-navview-item");
            return;
        }
        if (item.Tag is not string tag)
        {
            DeckleSettingsSource.Log.NavImpossibleNoTag();
            DeckleSettingsSource.Log.NavImpossibleNoTagDetail(item.Content?.ToString() ?? "");
            return;
        }

        var pageType = Type.GetType(tag);
        if (pageType is null)
        {
            DeckleSettingsSource.Log.NavFailedTypeNotFound();
            DeckleSettingsSource.Log.NavFailedTypeNotFoundDetail(tag);
            return;
        }

        if (PageFrame.CurrentSourcePageType == pageType)
        {
            DeckleSettingsSource.Log.NavSkippedAlreadyCurrent(pageType.Name);
            return;
        }

        DeckleSettingsSource.Log.NavStarted();
        DeckleSettingsSource.Log.NavStartedDetail(pageType.Name);
        // One nav-start timestamp for BOTH measures: this restart feeds the
        // Navigate-return NavTiming below and the page's first-Loaded PageReady.
        DeckleSettingsSource.NavClock.Restart();
        try
        {
            bool ok = PageFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
            if (!ok)
            {
                DeckleSettingsSource.Log.NavFailedFrameRejected();
                DeckleSettingsSource.Log.NavFailedFrameRejectedDetail(pageType.Name);
            }
            else
            {
                DeckleSettingsSource.Log.NavCompleted();
                DeckleSettingsSource.Log.NavCompletedDetail(pageType.Name);
                DeckleSettingsSource.Log.NavTiming(pageType.Name, DeckleSettingsSource.NavClock.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.NavFailedThrew();
            DeckleSettingsSource.Log.NavFailedThrewDetail(pageType.Name, ex.GetType().Name, ex.Message);
            DeckleSettingsSource.Log.NavStackTrace(ex.StackTrace ?? "(no stack)");
        }
    }

    private static void SyncNavigationPane(NavigationView nav)
    {
        nav.IsPaneOpen = nav.DisplayMode == NavigationViewDisplayMode.Expanded;
    }
}
