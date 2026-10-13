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
using Deckle.Shell.WindowChrome;

namespace Deckle.Playground;

// ─── Playground window shell ─────────────────────────────────────────────────
//
// The window only routes navigation and forwards the App's lifecycle calls.
// Pages are cached (NavigationCacheMode.Required), so their state and
// runtime resources survive navigation until Closed disposes them. Close is
// real destruction: the App drops its reference and the next open builds a
// fresh window.
public sealed partial class PlaygroundWindow : Window
{
    private readonly IntPtr _hwnd;

    private BitmapImage? _iconIdle;
    private BitmapImage? _iconRecording;
    private string? _iconIdlePath;
    private string? _iconRecordingPath;

    private HudPage? _hudPage;
    private AmbientPage? _ambientPage;
    private CorrectionApplicationPage? _correctionApplicationPage;
    private PlaygroundWindowActivation _windowActivation;

<<<<<<< HEAD
    private ResizeCoalescer? _resizeCoalescer;

=======
>>>>>>> main
    public PlaygroundWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        LoadAppIcons();
        AppTitleBarIcon.ImageSource = _iconIdle;
        if (_iconIdlePath is not null) AppWindow.SetIcon(_iconIdlePath);

        // Standard height, not Tall: no SearchBox in this title bar.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        // The control stamps its caption padding in raw physical pixels — an
        // upstream px/DIP bug that inflates the reserve at >100 % scale.
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);

        SystemBackdrop = new MicaBackdrop();

        PlaygroundShell.NavigateTo = NavigateTo;
        PlaygroundShell.ReadWindowActivation = () => _windowActivation;
        Activated += OnWindowActivated;

        Title = "Deckle Playground";
        // AppWindow sizes are physical pixels: scale the intended DIPs (or a
        // 200 % display opens a half-size window), clamped to the work area.
        double dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(1800 * dpiScale), workArea.Width),
            Math.Min((int)(1440 * dpiScale), workArea.Height)));

        // Presenter minimums are physical pixels too.
        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable   = true;
        presenter.PreferredMinimumWidth  = (int)(1280 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(600 * dpiScale);
        AppWindow.SetPresenter(presenter);

        // The pane toggle's tooltip follows the OS locale while the UI is
        // English. The template materialises the button only after Loaded,
        // hence the Low-priority dispatch; the pane events re-apply it.
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

        // Only a tap on RootGrid itself takes focus, so clicks on buttons,
        // dropdowns or flyouts keep theirs.
        RootGrid.Tapped += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, RootGrid))
                RootGrid.Focus(FocusState.Pointer);
        };

        Nav.SelectedItem = Nav.MenuItems[0];

        this.Closed += OnWindowClosed;

<<<<<<< HEAD
        // While a sizing border is dragged, the Segmentation curve would redo
        // its label layout on every WM_SIZE; the coalescer suspends that and
        // repaints once the size settles. Until that page is visited the calls
        // are no-ops.
        _resizeCoalescer = new ResizeCoalescer(
            _hwnd, "playground",
            onResizeSettled: () => _segmentationPage?.SetCurveResizeSuspended(false),
            onResizeStarted: () => _segmentationPage?.SetCurveResizeSuspended(true));
        _resizeCoalescer.Register();

        // Traces theme switches, so a preview glitch can be matched to one.
=======
        // Theme: wires ActualThemeChanged on the XAML root. Playground is
        // singleton-hidden (lives for the whole app session once opened), so it
        // sees all theme switches through ThemeRequestSourceProbe ("settings"
        // from user ThemeCombo, "system" from colorPrevalence) or direct ones
        // (the root follows the system when no RequestedTheme is set). Useful
        // trace to correlate a sliders / live preview glitch with a theme
        // switch while the window was active.
>>>>>>> main
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

        _hudPage?.ForcePause();

        AppWindow.Show();
        this.Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        // After Show, so the trace carries the rect DWM settled on. "Center":
        // the ctor only resizes and leaves the placement to Windows. Emitted on
        // every show, since a drag between two shows moves the window.
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
        try { _hudPage?.DisposeResources(); } catch { /* best effort */ }
        try { _ambientPage?.DisposeResources(); } catch { /* best effort */ }
        try { _correctionApplicationPage?.DisposeResources(); } catch { /* best effort */ }

<<<<<<< HEAD
        // Remove the HWND subclass before the window is gone.
        _resizeCoalescer?.Dispose();

        // So a stale page cannot route into a destroyed window. ReferenceEquals,
        // not ==, avoids the CS0252 warning on an object-to-Window comparison.
=======
        // Drop the shell's nav callback so a stale page reference can't
        // route into a destroyed window. ReferenceEquals (not ==) because
        // Window inherits the default object equality and the compiler
        // can't prove no derived operator== was added — explicit identity
        // check sidesteps the CS0252 warning.
>>>>>>> main
        if (PlaygroundShell.NavigateTo is not null
            && ReferenceEquals(PlaygroundShell.NavigateTo.Target, this))
        {
            PlaygroundShell.NavigateTo = null;
        }
        PlaygroundShell.ReadWindowActivation = null;
        Activated -= OnWindowActivated;
    }

    // ── Navigation routing ──────────────────────────────────────────────────

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

        DecklePlaygroundSource.NavClock.Restart();
        try
        {
            bool ok = PageFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
            if (!ok)
            {
                DecklePlaygroundSource.Log.NavigationFailed();
                DecklePlaygroundSource.Log.NavigationFailedDetail(pageType.Name, "frame_returned_false", "");
                return;
            }

            DecklePlaygroundSource.Log.NavTiming(pageType.Name, DecklePlaygroundSource.NavClock.ElapsedMilliseconds);

            // Each page is cached, so the instance captured here stays valid
            // for the window's lifetime.
            switch (PageFrame.Content)
            {
                case HudPage hud:
                    _hudPage = hud;
                    break;
                case AmbientPage amb:
                    _ambientPage = amb;
                    break;
                case CorrectionApplicationPage correction:
                    _correctionApplicationPage = correction;
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

    private void NavigateTo(string shortTag)
    {
        string fullTag = shortTag switch
        {
            "home"         => "Deckle.Playground.HomePage",
            "hud"          => "Deckle.Playground.HudPage",
            "ambient"      => "Deckle.Playground.AmbientPage",
            "segmentation" => "Deckle.Playground.SegmentationPage",
            "correction"   => "Deckle.Playground.CorrectionApplicationPage",
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

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _windowActivation = new PlaygroundWindowActivation(
            IsKnown: true,
            IsActive: args.WindowActivationState != WindowActivationState.Deactivated,
            Generation: checked(_windowActivation.Generation + 1));
        PlaygroundShell.PublishWindowActivation(_windowActivation);
    }

    // ── NavigationView tooltip i18n override ────────────────────────────────

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
