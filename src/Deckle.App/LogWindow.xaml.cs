using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Data;
using WinRT.Interop;
using Deckle.App;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;

namespace Deckle.App;

// ─── Log window ──────────────────────────────────────────────────────────────
//
// Custom title bar (ExtendsContentIntoTitleBar) with centered search field.
// Mica + system theme (light/dark auto, no forced RequestedTheme).
// SelectorBar All/Activity/Alerts (default = All).
// CommandBar: Copy/Save/Clear (buttons) + Auto-scroll/Word wrap (toggles).
// Live search via AutoSuggestBox.
//
// Model:
//   _entries : full buffer (cap 5000) — every LogEntry, any event
//   _visible : displayed subset (event/level filter + search)
// Copy/Save operate on _visible — the user copies what they see.

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private readonly List<LogEntry> _entries = new();
    private readonly ObservableCollection<LogEntry> _visible = new();
    private readonly IntPtr _hwnd;
    private bool _isVisible;

    // App icons — same assets as TrayIconManager via IconAssets.
    // Single source of truth: changing an .ico propagates to tray + beacon + window icon.
    private BitmapImage? _iconIdle;
    private BitmapImage? _iconRecording;
    private string? _iconIdlePath;
    private string? _iconRecordingPath;

    private ScrollViewer? _listScrollViewer;
    private ItemsStackPanel? _itemsPanel;

    private LogWindowVisibilityMode _filterMode = LogWindowVisibilityMode.All;
    private string _currentSearch = "";
    private bool _isRecording;

    // Typing in the SearchBox triggers a filter pass over the full buffer (up
    // to 5000 entries). On fast typists, that blocked the UI thread enough to
    // freeze the HUD animation. 200 ms debounce: long enough to avoid filtering
    // mid-word, short enough that the user doesn't perceive lag after they pause.
    private DispatcherTimer? _searchDebounce;

    // Below this window width (DIPs), the inline SearchBox collapses into an
    // icon-only button to keep the TitleBar readable. Pattern matches Windows
    // 11 Task Manager: icon in the TitleBar, click reveals the SearchBox,
    // focus leaving it restores the icon.
    private const double SearchCollapseThreshold = 520.0;
    private bool _isSearchNarrow;

    public LogWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        LogItems.ItemsSource = _visible;

        // Click-to-copy + drag-to-select: PointerPressed/Released are marked
        // handled by the ListView for its own selection management, so
        // AddHandler with handledEventsToo=true.
        LogItems.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnLogPointerPressed),
            handledEventsToo: true);
        LogItems.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnLogPointerReleased),
            handledEventsToo: true);

        // App icons: resolved once, shared with tray.
        LoadAppIcons();
        AppTitleBarIcon.ImageSource = _iconIdle;
        if (_iconIdlePath is not null) AppWindow.SetIcon(_iconIdlePath);

        // Native title bar: ExtendsContentIntoTitleBar + SetTitleBar is still
        // required for the TitleBar control to replace the system title bar.
        // Height, drag region, themed caption buttons are handled by the control.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Tall caption buttons to stay aligned with the interactive content
        // (SearchBox) in the TitleBar. The TitleBar control manages its own
        // chrome height, but system caption buttons are still driven by
        // AppWindow.TitleBar.PreferredHeightOption.
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;

        // The control stamps its caption padding in raw physical pixels — an
        // upstream px/DIP bug that inflates the reserve at >100 % scale.
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);

        // Mica: translucent backdrop that follows system theme colors.
        // Win11 required (OK here); falls back to transparent otherwise.
        SystemBackdrop = new MicaBackdrop();

        // Initial SelectorBar selection: All — the broadest view by default.
        // Activity / Alerts remain one click away when the user wants to narrow
        // down. Narrative entries (white, full-strength text) sit alongside the
        // other levels in Activity rather than in their own dedicated tab —
        // they read as natural milestones inside the broader stream instead
        // of feeling cut off from context.
        _filterMode = LoggingSettingsService.Instance.Current.LogWindowVisibilityMode;
        LevelSelector.SelectedItem = _filterMode switch
        {
            LogWindowVisibilityMode.Activity => LevelFiltered,
            LogWindowVisibilityMode.Alerts   => LevelCritical,
            _                                => LevelFull,
        };

        Title = Loc.Get("LogWindow_WindowTitle");
        // ~1:2 aspect ratio (vertical) — two stacked squares. Fits on a 4K display.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 1440));

        // Standard window: min, max, resize.
        // Min size: prevents the responsive command bar from being crushed
        // below its tightest threshold (400 DIPs = everything in the More
        // flyout, search hidden). Presenter minimums are PHYSICAL pixels —
        // scale the intended DIPs, or a 200 % display halves the real floor.
        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable   = true;
        double dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        presenter.PreferredMinimumWidth  = (int)(400 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(300 * dpiScale);
        AppWindow.SetPresenter(presenter);

        Closed += (_, _) => _isVisible = false;

        // Responsive TitleBar search (Task Manager pattern).
        SizeChanged += OnWindowSizeChanged;

        // Theme: wire ActualThemeChanged on the XAML root. LogWindow uses the
        // system theme by default (no forced RequestedTheme) but receives the
        // App.ApplyTheme broadcast through ApplyThemeToSingle when lazily
        // created, so we observe both boot "app-init" applications and live
        // system switches (the OS changing Personalization while the window is
        // open).
        if (Content is FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }
    }

    // Open from tray: restore if minimized, show, activate.
    public void ShowAndActivate()
    {
        _isVisible = true;

        if (AppWindow.Presenter is OverlappedPresenter op &&
            op.State == OverlappedPresenterState.Minimized)
        {
            op.Restore();
        }

        AppWindow.Show();
        this.Activate();

        // WinUI 3: Activate() doesn't always bring the window to front when
        // called from a tray callback. SetForegroundWindow from the same
        // process is allowed (the message-only tray host is same-process).
        NativeMethods.SetForegroundWindow(_hwnd);

        // Windowing: emitted post-Show to capture the effective rect after DWM
        // has positioned the window. The anchor is "Center": LogWindow does an
        // AppWindow.Resize (960×1440) in the ctor, Windows applies initial
        // centering. Emitted on every ShowAndActivate because a user drag
        // between two opens changes the rect.
        WindowingProbe.EmitWindowPositioned(_hwnd, "log", "Center");
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private ScrollViewer? GetListViewScrollViewer()
    {
        // The ListView's internal ScrollViewer is only available after the first
        // layout pass. We find it in the visual tree and cache it.
        if (_listScrollViewer is not null) return _listScrollViewer;
        _listScrollViewer = FindDescendant<ScrollViewer>(LogItems);
        return _listScrollViewer;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => ClearAll();

    private void OnWrapToggleClick(object sender, RoutedEventArgs e)
    {
        bool wrap = WrapToggle.IsChecked == true;
        string key = wrap ? "WrapSelector" : "NoWrapSelector";
        LogItems.ItemTemplateSelector = (DataTemplateSelector)RootGrid.Resources[key];

        // In wrap mode: disable horizontal scroll so the ScrollViewer gives
        // finite width to its content (otherwise TextWrapping doesn't know
        // where to break). Attached property on the ListView.
        ScrollViewer.SetHorizontalScrollBarVisibility(LogItems,
            wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    private void OnLevelSelectorChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var sel = sender.SelectedItem;
        _filterMode = sel == LevelFiltered ? LogWindowVisibilityMode.Activity
                    : sel == LevelCritical ? LogWindowVisibilityMode.Alerts
                    : LogWindowVisibilityMode.All;
        var settings = LoggingSettingsService.Instance.Current;
        settings.LogWindowVisibilityMode = _filterMode;
        LoggingSettingsService.Instance.Save();
        ApplyFilter();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _currentSearch = sender.Text ?? "";

        if (_searchDebounce is null)
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDebounce.Tick += (_, _) => { _searchDebounce!.Stop(); ApplyFilter(); };
        }
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }
}
