using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
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
using Deckle.Diagnostics.Logging.Ui.Collections;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;

namespace Deckle.App;

// ─── Log window ──────────────────────────────────────────────────────────────
//
// Custom title bar (ExtendsContentIntoTitleBar) with centered search field.
// Mica + system theme (light/dark auto, no forced RequestedTheme).
// Three-dimensional filter editor: Severity / Module / Category.
// CommandBar: Filters/Copy/Save/Clear + Auto-scroll/Word wrap.
// Live search via AutoSuggestBox.
//
// Model:
//   _entries : full buffer (cap 5000) — every LogEntry, any event
//   _visible : displayed subset (structured filter + search)
// Copy/Save operate on _visible — the user copies what they see.

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private readonly Queue<LogEntry> _entries = new();
    private readonly RangeObservableCollection<LogEntry> _visible = new();
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
    private bool _autoScrollPending;

    private readonly LogFilterSelection _filterSelection = LogWindowFilterSession.Selection;
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
        FilterBar.Selection = _filterSelection;
        UpdateFiltersToggleLabel();

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

        Title = Loc.Get("LogWindow_WindowTitle");
        // ~1:2 aspect ratio (vertical) — two stacked squares. Window sizes are
        // PHYSICAL pixels — scale the intended DIPs (or a 200 % display opens a
        // half-size window), clamped to the display's work area so the scaled
        // height never overflows the screen.
        double dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(960 * dpiScale), workArea.Width),
            Math.Min((int)(1440 * dpiScale), workArea.Height)));

        // Standard window: min, max, resize.
        // Min size: prevents the responsive command bar from being crushed
        // below its tightest threshold (400 DIPs = everything in the More
        // flyout, search hidden). Presenter minimums are PHYSICAL pixels —
        // scale the intended DIPs, or a 200 % display halves the real floor.
        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable   = true;
        presenter.PreferredMinimumWidth  = (int)(400 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(300 * dpiScale);
        AppWindow.SetPresenter(presenter);

        Closed += (_, _) =>
        {
            _isVisible = false;
            FilterBar.Detach();
        };

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

    private void OnFiltersToggleClick(object sender, RoutedEventArgs e)
    {
        FilterBar.Visibility = FiltersToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        UpdateFiltersToggleLabel();
        ApplyFilter();
    }

    private void UpdateFiltersToggleLabel()
    {
        FiltersToggle.Label = _filterSelection.Count == 0
            ? Loc.Get("LogWindow_FiltersButton_Default")
            : Loc.Format("LogWindow_FiltersButton_Format", _filterSelection.Count);
        AutomationProperties.SetName(FiltersToggle, FiltersToggle.Label);
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
