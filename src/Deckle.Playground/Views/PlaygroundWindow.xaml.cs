using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Playground;

// ─── Playground window shell ─────────────────────────────────────────────────
//
// Native TitleBar + Mica backdrop + compact NavigationView + Frame. Hosts
// four pages : HomePage, HudPage, AmbientPage, SegmentationPage — each owning
// its tuning surface, ViewModel, and runtime resources. The window itself only
// routes navigation and forwards the lifecycle calls the App makes
// (SetRecordingState, ShowAndActivate, Closed→DisposeResources).
//
// Pattern : same as SettingsWindow. NavigationViewItem.Tag carries the
// fully-qualified Page type name, resolved via Type.GetType in
// OnNavSelectionChanged. Pages declare NavigationCacheMode.Required so
// their tuning state and runtime resources survive nav switches ;
// PlaygroundWindow.Closed disposes the resources owned by each page
// via the DisposeResources() entry points.
//
// Real-destruction lifetime : Close tears the window down, disposes
// HudPage/AmbientPage resources, and App clears its lazy reference so
// the next open constructs a fresh Playground instance.
public sealed partial class PlaygroundWindow : Window
{
    private readonly IntPtr _hwnd;

    // Icons shared with tray / LogWindow / SettingsWindow via IconAssets.
    // Swapping the .ico on disk propagates everywhere.
    private BitmapImage? _iconIdle;
    private BitmapImage? _iconRecording;
    private string? _iconIdlePath;
    private string? _iconRecordingPath;

    // Page references resolved on first navigate so the shell can call
    // ForcePause / DisposeResources without walking the Frame's content
    // tree on every interaction.
    private HudPage? _hudPage;
    private AmbientPage? _ambientPage;
    private SegmentationPage? _segmentationPage;

    // Collapses the per-frame Win2D recompute the Segmentation curve pays during an
    // interactive edge drag into a single crisp repaint on settle. See ResizeCoalescer.
    private ResizeCoalescer? _resizeCoalescer;

    public PlaygroundWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        LoadAppIcons();
        AppTitleBarIcon.ImageSource = _iconIdle;
        if (_iconIdlePath is not null) AppWindow.SetIcon(_iconIdlePath);

        // Native title bar. Standard height (not Tall) — no SearchBox in
        // this window so caption buttons don't need the extended chrome.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        SystemBackdrop = new MicaBackdrop();

        // Wire the Pages → shell navigation callback. HomePage's
        // routing cards invoke PlaygroundShell.NavigateTo("hud" /
        // "ambient") to bring the matching NavigationView item into
        // selected state without holding a back-reference to this
        // Window.
        PlaygroundShell.NavigateTo = NavigateTo;

        Title = "Deckle Playground";
        // Default 1800×1440 — comfortable two-column footprint (preview
        // + tuning expanders) on a typical 1440p display. Min 1280×600
        // keeps everything reachable below that.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1800, 1440));

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable   = true;
        presenter.PreferredMinimumWidth  = 1280;
        presenter.PreferredMinimumHeight = 600;
        AppWindow.SetPresenter(presenter);

        // Close → real destruction. The Playground holds heavy runtime
        // resources (Win2D composition, screen capture, frame sampler,
        // Hue REST client, preview timers) — when the user dismisses
        // the window they expect the costs to go with it. App.xaml.cs
        // nullifies its reference on Closed so the next ShowPlaygroundLazy
        // call builds a fresh instance, starting from the persisted
        // AmbientSettings without any in-memory carry-over from the
        // previous session.

        // NavigationView pane toggle tooltip override : default OS locale
        // string ("Ouvrir navigation" on FR) clashes with the rest of the
        // UI which is locked in English. Tooltip applied after the
        // template generator has materialised the button (Loaded → Low
        // priority dispatch), re-applied on PaneOpened / PaneClosed.
        Nav.Loaded += (_, _) =>
        {
            DispatcherQueue.TryEnqueueObserved(
                operation: "ui-update", caller: "playground-window-nav",
                callback: () =>
                {
                    SyncNavigationPane(Nav);
                    OverrideNavPaneToggleTooltip(Nav, "Open navigation");
                },
                rejectSource: "PLAYGROUND", rejectWhat: "nav tooltip override",
                priority: Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);
        };
        Nav.DisplayModeChanged += OnNavDisplayModeChanged;
        Nav.PaneOpened += (_, _) =>
            OverrideNavPaneToggleTooltip(Nav, "Open navigation");
        Nav.PaneClosed += (_, _) =>
            OverrideNavPaneToggleTooltip(Nav, "Open navigation");

        // Tap on empty background → move focus to RootGrid, dismisses
        // the caret from a NumberBox the user just edited. Filter on
        // OriginalSource so clicks landing on buttons / dropdowns /
        // ComboBox flyouts don't steal focus mid-action.
        RootGrid.Tapped += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, RootGrid))
                RootGrid.Focus(FocusState.Pointer);
        };

        // Initial selection : Home. Setting SelectedItem fires
        // OnNavSelectionChanged → PageFrame.Navigate(HomePage).
        Nav.SelectedItem = Nav.MenuItems[0];

        this.Closed += OnWindowClosed;

        // Resize coalescing. While the user drags a sizing border the Segmentation
        // curve's Win2D surface would re-layout and reissue its label text every
        // WM_SIZE — the visible lag. The coalescer flips the canvas's suspend flag
        // on the gesture's rising edge and clears it, with a crisp final repaint,
        // once the size settles. Pages without a Win2D surface see no-ops: the
        // cached page reference stays null until that page is visited. HWND is
        // already valid (captured above), so we register immediately.
        _resizeCoalescer = new ResizeCoalescer(
            _hwnd, "playground",
            onResizeSettled: () => _segmentationPage?.SetCurveResizeSuspended(false),
            onResizeStarted: () => _segmentationPage?.SetCurveResizeSuspended(true));
        _resizeCoalescer.Register();

        // Theme: wires ActualThemeChanged on the XAML root. Playground is
        // singleton-hidden (lives for the whole app session once opened), so it
        // sees all theme switches through ThemeRequestSourceProbe ("settings"
        // from user ThemeCombo, "system" from colorPrevalence) or direct ones
        // (the root follows the system when no RequestedTheme is set). Useful
        // trace to correlate a sliders / live preview glitch with a theme
        // switch while the window was active.
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
            "playground", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    // ── Lifecycle surface (called by App) ───────────────────────────────────

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            op.Restore();
        }

        // Reset to Pause systematically on each show — known, predictable
        // state on every reopen, independent of what the user left when
        // they last closed. The HudPage handles the actual ApplyTarget
        // through its VM observer.
        _hudPage?.ForcePause();

        AppWindow.Show();
        this.Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        // Windowing: emitted post-Show to capture the effective rect after DWM
        // has positioned the window. Anchor "Center" because the ctor only does
        // an AppWindow.Resize (1800×1440) without explicit Move; initial
        // centering is left to Windows. Emitted on each ShowAndActivate because
        // a user drag between two openings changes the rect: the last trace
        // remains the current truth.
        WindowingProbe.EmitWindowPositioned(_hwnd, "playground", "Center");
    }

    public void SetRecordingState(bool isRecording)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyRecordingState(isRecording);
        else DispatcherQueue.TryEnqueueObserved(
            operation: "engine-state-sync", caller: "playground-window",
            callback: () => ApplyRecordingState(isRecording),
            rejectSource: "PLAYGROUND", rejectWhat: "recording state sync");
    }

    private void ApplyRecordingState(bool isRecording)
    {
        // Rebuild the ImageIconSource wholesale — in-place ImageSource
        // mutation doesn't propagate to the TitleBar visual.
        AppTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = isRecording ? _iconRecording : _iconIdle,
        };
        var path = isRecording ? _iconRecordingPath : _iconIdlePath;
        if (path is not null) AppWindow.SetIcon(path);
    }

    private void LoadAppIcons()
    {
        _iconIdlePath      = IconAssets.ResolvePath(recording: false);
        _iconRecordingPath = IconAssets.ResolvePath(recording: true);

        if (_iconIdlePath is not null)
            _iconIdle = new BitmapImage(new Uri(_iconIdlePath));
        if (_iconRecordingPath is not null)
            _iconRecording = new BitmapImage(new Uri(_iconRecordingPath));
    }

    // ── Window close → page resource disposal ───────────────────────────────

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // Tear down resources owned by the pages — composition preview,
        // capture service, frame sampler, light output, observers. The
        // shell deliberately drives this from Closed (terminal) so close
        // releases the resources instead of leaving an idle visual tree
        // alive behind the tray.
        try { _hudPage?.DisposeResources(); } catch { /* best effort */ }
        try { _ambientPage?.DisposeResources(); } catch { /* best effort */ }

        // Remove the HWND subclass before the window is gone.
        _resizeCoalescer?.Dispose();

        // Drop the shell's nav callback so a stale page reference can't
        // route into a destroyed window. ReferenceEquals (not ==) because
        // Window inherits the default object equality and the compiler
        // can't prove no derived operator== was added — explicit identity
        // check sidesteps the CS0252 warning.
        if (PlaygroundShell.NavigateTo is not null
            && ReferenceEquals(PlaygroundShell.NavigateTo.Target, this))
        {
            PlaygroundShell.NavigateTo = null;
        }
    }

    // ── Navigation routing ──────────────────────────────────────────────────
    //
    // Same Tag → Type.GetType pattern as SettingsWindow. The CurrentSourcePageType
    // guard avoids redundant re-Navigate on the initial seed.

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag)
        {
            DecklePlaygroundSource.Log.NavigationRejected();
            DecklePlaygroundSource.Log.NavigationRejectedDetail("no_tag", item.Content?.ToString() ?? "");
            return;
        }

        var pageType = Type.GetType(tag);
        if (pageType is null)
        {
            DecklePlaygroundSource.Log.NavigationFailed();
            DecklePlaygroundSource.Log.NavigationFailedDetail(tag, "type_not_found", "");
            return;
        }

        if (PageFrame.CurrentSourcePageType == pageType) return;

        try
        {
            bool ok = PageFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
            if (!ok)
            {
                DecklePlaygroundSource.Log.NavigationFailed();
                DecklePlaygroundSource.Log.NavigationFailedDetail(pageType.Name, "frame_returned_false", "");
                return;
            }

            // Cache the resolved page instance so ShowAndActivate /
            // DisposeResources don't have to walk the Frame's content
            // tree. NavigationCacheMode.Required on each Page means the
            // first nav builds it ; subsequent navs reuse the same
            // instance, so the reference captured here stays valid for
            // the lifetime of the shell.
            switch (PageFrame.Content)
            {
                case HudPage hud:
                    _hudPage = hud;
                    break;
                case AmbientPage amb:
                    _ambientPage = amb;
                    break;
                case SegmentationPage seg:
                    _segmentationPage = seg;
                    break;
            }
        }
        catch (Exception ex)
        {
            DecklePlaygroundSource.Log.NavigationFailed();
            DecklePlaygroundSource.Log.NavigationFailedDetail(pageType.Name, "exception", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnNavDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        SyncNavigationPane(sender);
    }

    // PlaygroundShell.NavigateTo callback target. Pages invoke this with
    // a short tag ("home" / "hud" / "ambient") and the shell maps it to
    // the matching NavigationViewItem.Tag prefix.
    private void NavigateTo(string shortTag)
    {
        string fullTag = shortTag switch
        {
            "home"    => "Deckle.Playground.HomePage",
            "hud"     => "Deckle.Playground.HudPage",
            "ambient"      => "Deckle.Playground.AmbientPage",
            "segmentation" => "Deckle.Playground.SegmentationPage",
            _              => "",
        };
        if (string.IsNullOrEmpty(fullTag)) return;

        foreach (var menuItem in Nav.MenuItems)
        {
            if (menuItem is NavigationViewItem nvi
                && nvi.Tag is string tag
                && tag == fullTag)
            {
                Nav.SelectedItem = nvi;
                return;
            }
        }
    }

    // ── NavigationView tooltip i18n override ────────────────────────────────
    //
    // Same helper as in SettingsWindow ; kept duplicated by design — two
    // callsites isn't enough to justify a shared assembly, and pulling it
    // into Deckle.Catalog would force that pure resw-facing module
    // to take a WinUI 3 control dependency it doesn't otherwise need.
    private static void OverrideNavPaneToggleTooltip(NavigationView nav, string tooltip)
    {
        var toggle = FindVisualDescendantByName<Button>(nav, "TogglePaneButton");
        if (toggle is null) return;
        ToolTipService.SetToolTip(toggle, tooltip);
        AutomationProperties.SetName(toggle, tooltip);
    }

    private static void SyncNavigationPane(NavigationView nav)
    {
        nav.IsPaneOpen = nav.DisplayMode == NavigationViewDisplayMode.Expanded;
    }

    private static T? FindVisualDescendantByName<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && t.Name == name) return t;
            var found = FindVisualDescendantByName<T>(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}
