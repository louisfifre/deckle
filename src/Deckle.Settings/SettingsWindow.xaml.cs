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
using Deckle.Shell.WindowChrome;

namespace Deckle.Settings;

// ─── Settings Window ──────────────────────────────────────────────────────────
//
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

    public Action? OnShowLogsRequested { get; set; }

    private readonly List<NavigationViewItem> _moduleNavItems = new();

    public SettingsWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        _iconIdlePath = IconAssets.ResolvePath(recording: false);
        if (_iconIdlePath is not null)
        {
            _iconIdle = new BitmapImage(new Uri(_iconIdlePath));
            AppTitleBarIcon.ImageSource = _iconIdle;
            AppWindow.SetIcon(_iconIdlePath);
        }

        // Tall gives the bar room for its interactive content (search box, Logs).
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // The control stamps its caption padding in raw physical pixels — an
        // upstream px/DIP bug that inflates the reserve at >100 % scale.
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);

        SystemBackdrop = new MicaBackdrop();

        InitializeSearch();

        Nav.DisplayModeChanged += OnNavDisplayModeChanged;
        Nav.Loaded += (_, _) => SyncNavigationPane(Nav);

        // Before the initial selection, so the module items are present and
        // selectable.
        BuildModuleNavItems();
        SettingsModuleRegistry.Changed += OnModulesChanged;
        this.Closed += (_, _) => SettingsModuleRegistry.Changed -= OnModulesChanged;

        // Initial selection → triggers SelectionChanged → navigates to
        // GeneralPage. One navigation path, no double-navigation.
        Nav.SelectedItem = Nav.MenuItems[0];

        Title = Loc.Get("Settings_WindowTitle");
        // Window sizes are PHYSICAL pixels — scale the intended DIPs (or a
        // 200 % display opens a half-size window), clamped to the display's
        // work area so the scaled height never overflows the screen.
        double dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(960 * dpiScale), workArea.Width),
            Math.Min((int)(1440 * dpiScale), workArea.Height)));

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable   = true;
        // Presenter minimums are PHYSICAL pixels — scale the intended DIPs, or a
        // 200 % display halves the real floor. 400 DIPs wide is the narrowest
        // width at which the TitleBar still shows every command (pane toggle,
        // icon, search icon, Logs, caption group; the title yields first via the
        // control's Compact state), while staying below the NavigationView Auto
        // breakpoints (640/1008) so the native LeftMinimal mode stays reachable.
        presenter.PreferredMinimumWidth  = (int)(400 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(400 * dpiScale);
        AppWindow.SetPresenter(presenter);

        // Traces light/dark/HC transitions. A user theme switch starts in this
        // window, so the trace confirms it reached the window that triggered it.
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
    // Idempotent: the items a previous call inserted are removed first, so a live
    // registry change rebuilds the band rather than duplicating it. Labels resolve
    // from the OWNING module's PRI subtree (Loc.GetFrom): the module ships its own
    // nav wording.
    //
    // Two passes, because a child must find its parent already built. A child
    // renders icon-less under an expanded parent — the WinUI hierarchical-navigation
    // sample's shape, where indentation carries the relation. Only the top-level
    // items are tracked in _moduleNavItems: removing a parent takes its children
    // with it. A child whose parent is missing is a wiring gap, traced and skipped,
    // never a boot-stopper.
    private void BuildModuleNavItems()
    {
        foreach (NavigationViewItem old in _moduleNavItems)
        {
            Nav.MenuItems.Remove(old);
            Nav.FooterMenuItems.Remove(old);
        }
        _moduleNavItems.Clear();

        // Main items append after the static General anchor. Header items go to the
        // very top and push the Main insertion point down as they are inserted.
        int mainInsertAt = Nav.MenuItems.Count;
        int headerInsertAt = 0;
        var parents = new Dictionary<string, NavigationViewItem>();

        foreach (SettingsModuleDescriptor module in SettingsModuleRegistry.Modules)
        {
            if (module.ParentId is not null) continue;

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
            parents[module.Id] = item;
        }

        foreach (SettingsModuleDescriptor module in SettingsModuleRegistry.Modules)
        {
            if (module.ParentId is null) continue;
            if (!parents.TryGetValue(module.ParentId, out NavigationViewItem? parent))
            {
                DeckleSettingsSource.Log.SettingsModuleParentMissing(module.Id, module.ParentId);
                continue;
            }

            parent.MenuItems.Add(new NavigationViewItem
            {
                Content = Loc.GetFrom(module.OwningAssembly, module.LabelKey),
                Tag = module.PageTag,
            });
            // Expanded from the start: the children ARE the family's surfaces, and a
            // collapsed parent would hide destinations the user has no other route to.
            parent.IsExpanded = true;
        }
    }

    // Register / Unregister may be called from any thread, so marshal onto the UI
    // thread before touching the NavigationView.
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

        if (pageTag is not null)
            SelectPage(pageTag);

        AppWindow.Show();
        this.Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        // After Show, on every call: the trace carries the effective rect once
        // DWM has placed the window, and a user drag between openings moves it.
        WindowingProbe.EmitWindowPositioned(_hwnd, "settings", "Center");
    }

    // Selection is what navigates (it drives OnNavSelectionChanged →
    // Frame.Navigate), so every deep link goes through here. A tag with no rail
    // item is silently ignored: a module withdrawn while the window is open has
    // nowhere to go, and that is not an error worth a dialog.
    public void SelectPage(string pageTag)
    {
        NavigationViewItem? item = FindNavItem(pageTag);
        if (item is null) return;
        ExpandAncestorsOf(item);
        Nav.SelectedItem = item;
    }

    // A child is only selectable once its parent is expanded, and a deep link may
    // land on one before the user has ever opened the family. Parents are built
    // expanded, so this is the guard for a parent the user collapsed by hand.
    private void ExpandAncestorsOf(NavigationViewItem target)
    {
        foreach (var parent in Nav.MenuItems.OfType<NavigationViewItem>())
            if (parent.MenuItems.OfType<NavigationViewItem>().Any(child => ReferenceEquals(child, target)))
                parent.IsExpanded = true;
    }

    // ── TitleBar pane toggle ─────────────────────────────────────────────
    //
    // SyncNavigationPane is deliberately NOT called here — it forces IsPaneOpen
    // from the display mode, which would immediately re-close a pane the user
    // just opened in a compact mode.

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
    {
        Nav.IsPaneOpen = !Nav.IsPaneOpen;
    }

    private void OnLogsButtonClick(object sender, RoutedEventArgs e)
    {
        DeckleSettingsSource.Log.OpenLogsFromFooter();
        OnShowLogsRequested?.Invoke();
    }

    // ── NavigationView: pane sync by DisplayMode ─────────────────────────

    private void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        SyncNavigationPane(sender);
    }

    // ── NavigationView: page swap ────────────────────────────────────────────

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
