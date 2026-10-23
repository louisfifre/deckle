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

// _entries holds every entry whatever the filter; _visible is the displayed
// subset. Copy and Save resolve All, Filtered or Selection from the full buffer:
// the viewport is never a transfer boundary.

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private readonly Queue<LogEntry> _entries = new();
    private readonly RangeObservableCollection<LogEntry> _visible = new();
    private readonly IntPtr _hwnd;
    private bool _isVisible;

    // The App owns the other windows, so it hands these in, as it does for
    // SettingsWindow.OnShowLogsRequested.
    public Action? OnShowSettingsRequested { get; set; }
    public Action? OnShowPlaygroundRequested { get; set; }

    private BitmapImage? _iconIdle;
    private BitmapImage? _iconRecording;
    private string? _iconIdlePath;
    private string? _iconRecordingPath;

    private ScrollViewer? _listScrollViewer;
    private bool _autoScrollPending;

    private readonly LogFilterSelection _filterSelection = LogWindowFilterSession.Selection;
    private string _currentSearch = "";
    private bool _isRecording;

    // Each filter pass walks the full buffer on the UI thread, enough to stall
    // the HUD animation under fast typing; the debounce waits for a pause.
    private DispatcherTimer? _searchDebounce;

    // Window width in DIPs below which the SearchBox collapses into an icon button.
    private const double SearchCollapseThreshold = 520.0;
    private bool _isSearchNarrow;

    public LogWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        LogItems.ItemsSource = _visible;
        FilterBar.Selection = _filterSelection;
        UpdateFiltersToggleLabel();

        LoadAppIcons();
        AppTitleBarIcon.ImageSource = _iconIdle;
        if (_iconIdlePath is not null) AppWindow.SetIcon(_iconIdlePath);

        // The TitleBar control still needs both calls to replace the system title bar.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // The caption buttons follow PreferredHeightOption, not the TitleBar
        // control: Tall keeps them aligned with the SearchBox.
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;

        // The control stamps its caption padding in raw physical pixels — an
        // upstream px/DIP bug that inflates the reserve at >100 % scale.
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);

        SystemBackdrop = new MicaBackdrop();

        Title = Loc.Get("LogWindow_WindowTitle");
        // AppWindow sizes are physical pixels: scale the intended DIPs, clamped to
        // the work area so the scaled height never overflows the screen.
        double dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(960 * dpiScale), workArea.Width),
            Math.Min((int)(1440 * dpiScale), workArea.Height)));

        // The minimum keeps the command bar above its tightest layout. Presenter
        // minimums are physical pixels too.
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

        SizeChanged += OnWindowSizeChanged;

        // Observed on the root so the theme trace sees both the App theme
        // broadcast and a system switch while the window is open.
        if (Content is FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }
    }

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

        // Activate() does not always bring the window to front from a tray
        // callback. SetForegroundWindow is allowed: the tray host is same-process.
        NativeMethods.SetForegroundWindow(_hwnd);

        // After Show, so the rect is the one DWM placed; on every open, since a
        // drag between two opens moves it.
        WindowingProbe.EmitWindowPositioned(_hwnd, "log", "Center");
    }

    private ScrollViewer? GetListViewScrollViewer()
    {
        // The internal ScrollViewer exists only after the first layout pass.
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

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
        => OnShowSettingsRequested?.Invoke();

    private void OnPlaygroundButtonClick(object sender, RoutedEventArgs e)
        => OnShowPlaygroundRequested?.Invoke();

    private void OnWrapToggleClick(object sender, RoutedEventArgs e)
    {
        bool wrap = WrapToggle.IsChecked == true;
        string key = wrap ? "WrapSelector" : "NoWrapSelector";
        LogItems.ItemTemplateSelector = (DataTemplateSelector)RootGrid.Resources[key];

        // Wrapping needs a finite width, which a horizontally scrolling
        // ScrollViewer never gives its content.
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
