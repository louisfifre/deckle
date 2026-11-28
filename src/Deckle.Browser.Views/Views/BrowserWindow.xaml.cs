using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

namespace Deckle.Browser.Views;

// ─── Browser window ──────────────────────────────────────────────────────────
//
// Zen's layout: a panel on the left (the button row, the address field, the
// pinned grid, the tabs, and at its foot the settings), the web page as a card
// with rounded corners on the right, all on Mica. There is no title bar of its
// own: the button row stands in for it and the caption buttons sit at the top
// right, above the page.
//
// The panel is a NavigationView whose pane slots hold our own controls, given
// the common pane behaviour (NavigationPane: open, rail, narrow).
//
// What the panel lists is a BrowserSession (the domain: pinned sites, tabs,
// the selection); each of its items is a PanelItem, which also keeps the item's
// WebView2. All the web views are children of one grid inside the page card,
// share ONE environment, and only the selected one is visible. An item gets
// its web view when it is first selected, never ahead: at startup every item
// is an entry with its cached icon and title. A hidden page is suspended
// unless it plays sound.
//
// Nothing a person visits is logged: no address, no title, no domain.
public sealed partial class BrowserWindow : Window
{
    private const string WindowKey = "browser";
    private const string ModuleKey = "browser";

    private readonly bool _isPrivate;
    private readonly IntPtr _hwnd;
    private readonly NavigationPane _pane;
    private readonly BrowserSession _session;
    private readonly string _sessionPath = Path.Combine(AppPaths.GetModuleDirectory(ModuleKey), "session.json");
    private readonly ObservableCollection<PanelItem> _pinnedItems = [];
    private readonly ObservableCollection<PanelItem> _tabItems = [];
    private readonly Dictionary<Guid, PanelItem> _byId = [];
    private readonly Dictionary<Guid, Action> _detachClip = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _saveTimer;
    private readonly string _historyPath = Path.Combine(AppPaths.GetModuleDirectory(ModuleKey), "history.json");
    private readonly History _history;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _historyTimer;
    private readonly ClosedTabs _closedTabs = new();
    private CoreWebView2Environment? _environment;
    private PanelItem? _shown;
    private Uri? _current;
    private int _syncing;
    private int _completedNavigations;

    // One environment for the process, created on first use over the module's
    // own user data folder.
    private static Task<CoreWebView2Environment>? _environmentTask;

    // Bumped each time the environment is made again after the browser process
    // failed: a web view of an older generation is dead.
    private static int _environmentGeneration = 1;

    public BrowserWindow() : this(false)
    {
    }

    /// <summary>A private window (Ctrl+Shift+N): its web views run in the
    /// engine's in-private mode, it has no pinned sites, and it loads and saves
    /// no session.</summary>
    public BrowserWindow(bool isPrivate)
    {
        _isPrivate = isPrivate;
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        // The button row stands in for the title bar; the caption buttons stay
        // at the top right, over the page area.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ButtonRow);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        Title = BrowserText.Get("Window_Title");

        // Window sizes are PHYSICAL pixels: scale the intended DIPs, clamped to
        // the work area.
        double dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(1280 * dpiScale), workArea.Width),
            Math.Min((int)(860 * dpiScale), workArea.Height)));

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable = true;
        presenter.PreferredMinimumWidth = (int)(400 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(400 * dpiScale);
        AppWindow.SetPresenter(presenter);

        string? icoPath = IconAssets.ResolvePath(false);
        if (icoPath is not null) AppWindow.SetIcon(icoPath);

        // The ad blocker's engine is built on a background thread, once.
        BrowserAdblock.Converter = ConvertUboResourcesAsync;
        BrowserAdblock.Start();
        BrowserActivity.Start(DispatcherQueue);

        _pane = NavigationPane.Attach(Nav, WindowKey);
        _pane.Changed += ApplyPaneState;
        Nav.SizeChanged += (_, _) => FitPanelHeight();
        PanelRoot.Loaded += (_, _) => FitPanelHeight();
        ApplyPaneState();

        // The session: what was kept, or one empty tab.
        _session = _isPrivate ? new BrowserSession() : SessionStore.Load(_sessionPath);
        if (_isPrivate)
        {
            PrivateLabel.Visibility = Visibility.Visible;
            PinnedGrid.Visibility = Visibility.Collapsed;
        }
        _permissions = _isPrivate ? new SitePermissions() : SitePermissions.Load(PermissionsPath);
        if (_session.Pinned.Count == 0 && _session.Tabs.Count == 0) _session.OpenTab("about:blank");
        foreach (BrowserItem item in _session.Pinned) _pinnedItems.Add(Register(item));
        foreach (BrowserItem item in _session.Tabs) _tabItems.Add(Register(item));
        PinnedGrid.ItemsSource = _pinnedItems;
        TabList.ItemsSource = _tabItems;

        // Saved 500 ms after the last change, and once more at the close.
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveSession();
        _session.Changed += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };

        // The pages visited, for the address field's suggestions: kept apart
        // from the session, saved 2 s after the last change; a private window
        // keeps none.
        _history = _isPrivate ? new History() : HistoryStore.Load(_historyPath);
        _historyTimer = DispatcherQueue.CreateTimer();
        _historyTimer.Interval = TimeSpan.FromSeconds(2);
        _historyTimer.IsRepeating = false;
        _historyTimer.Tick += (_, _) => SaveHistory();
        _history.Changed += (_, _) =>
        {
            _historyTimer.Stop();
            _historyTimer.Start();
        };
        WireKeys();
        WireMedia();

        Closed += (_, _) =>
        {
            _historyTimer.Stop();
            SaveHistory();
            StopMediaTimer();
            if (BrowserAdblock.Converter is { } mine && ReferenceEquals(mine.Target, this)) BrowserAdblock.Converter = null;
            _saveTimer.Stop();
            SaveSession();
            DetachEnvironment();
            BrowserActivity.Flush();
        };

        ApplySelection();
    }

    // ── Pane ─────────────────────────────────────────────────────────────────

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.PaneToggle);
        _pane.Toggle();
    }

    // The rail shows the icons: the button row in a column, the tiles and the
    // lines as far as they fit; the address field needs the open pane. A
    // narrow window hides the panel, and a button over the page opens it.
    private void ApplyPaneState()
    {
        bool open = _pane.IsOpen;
        AddressRow.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PrivateLabel.Visibility = _isPrivate && open ? Visibility.Visible : Visibility.Collapsed;
        // The foot: downloads on the left, settings on the right; in the rail, one above the other.
        Grid.SetColumn(SettingsButton, open ? 2 : 0);
        Grid.SetRow(SettingsButton, open ? 0 : 1);
        ButtonStack.Orientation = open ? Orientation.Horizontal : Orientation.Vertical;
        NarrowToggleButton.Visibility = _pane.IsOverPage && !open ? Visibility.Visible : Visibility.Collapsed;
        PinnedLayout.MinItemWidth = open ? 64 : 36;
        if (_isPrivate) PinnedGrid.Visibility = Visibility.Collapsed;
        PinnedGrid.Margin = open ? new Thickness(8, 0, 8, 8) : new Thickness(4, 0, 4, 8);
        NewTabButton.Margin = open ? new Thickness(8, 0, 8, 4) : new Thickness(4, 0, 4, 4);
        UpdateMediaCard();
    }

    // The pane's custom slot does not bound what it holds, so the panel takes
    // the height from its own top to the pane's bottom: the foot is the
    // panel's last row and the tabs' list, the row of height *, scrolls.
    private void FitPanelHeight()
    {
        if (Nav.ActualHeight <= 0) return;
        double top = PanelRoot.TransformToVisual(Nav).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        PanelRoot.Height = Math.Max(0, Nav.ActualHeight - top);
    }

    // ── Items ────────────────────────────────────────────────────────────────

    private PanelItem Register(BrowserItem item)
    {
        var panel = new PanelItem(item) { Title = item.Title };
        _byId[item.Id] = panel;
        LoadCachedIcon(panel);
        return panel;
    }

    private IEnumerable<PanelItem> AllItems => _byId.Values;

    private int IndexOfTab(BrowserItem item)
    {
        for (int i = 0; i < _session.Tabs.Count; i++)
            if (_session.Tabs[i].Id == item.Id) return i;
        return -1;
    }

    // Opens a tab in the session and in the panel, and selects it (the session
    // does). With adopt the page that asked for the window will hand over its
    // own web view, so this one is not started here.
    private PanelItem AddTab(string url, int? at = null, bool adopt = false)
    {
        BrowserItem item = _session.OpenTab(url, at);
        PanelItem panel = Register(item);
        panel.Started = adopt;
        _syncing++;
        _tabItems.Insert(IndexOfTab(item), panel);
        _syncing--;
        ApplySelection();
        return panel;
    }

    private void SelectItem(PanelItem panel)
    {
        _session.Select(panel.Item.Id);
        ApplySelection();
    }

    private void PinItem(PanelItem panel)
    {
        if (panel.Item.IsPinned) return;
        _session.Pin(panel.Item.Id);
        _syncing++;
        _tabItems.Remove(panel);
        _pinnedItems.Add(panel);
        _syncing--;
        SyncSelectionUi();
    }

    private void UnpinItem(PanelItem panel)
    {
        if (!panel.Item.IsPinned) return;
        _session.Unpin(panel.Item.Id);
        _syncing++;
        _pinnedItems.Remove(panel);
        _tabItems.Insert(0, panel);
        _syncing--;
        SyncSelectionUi();
    }

    private void CloseItem(PanelItem panel)
    {
        if (panel.Item.IsPinned) return;
        _closedTabs.Push(panel.Item.Url, panel.Item.Title);
        _session.Close(panel.Item.Id);
        ForgetMedia(panel);
        _syncing++;
        _tabItems.Remove(panel);
        _syncing--;
        _byId.Remove(panel.Item.Id);
        if (_shown == panel) _shown = null;
        DisposeView(panel);

        // A browser never shows nothing: closing the last page opens a blank one.
        if (_session.Pinned.Count == 0 && _session.Tabs.Count == 0) AddTab("about:blank");
        else ApplySelection();
    }

    private void DisposeView(PanelItem panel)
    {
        if (panel.View is not { } view) return;
        panel.View = null;
        if (_detachClip.Remove(panel.Item.Id, out Action? detach)) detach();
        PageHost.Children.Remove(view);
        try { view.Close(); }
        catch (Exception) { /* already gone with its process */ }
    }

    // Makes the session's selection what the window shows: the item's web view
    // made if it has none yet, visible and awake, the one before it hidden and,
    // unless it plays sound, suspended.
    private void ApplySelection()
    {
        PanelItem? next = _session.SelectedId is { } id && _byId.TryGetValue(id, out PanelItem? found) ? found : null;
        PanelItem? previous = _shown;
        _shown = next;
        if (previous != next) CloseFind();
        SyncSelectionUi();

        if (previous is not null && previous != next && previous.View is { } hidden)
        {
            hidden.Visibility = Visibility.Collapsed;
            _ = SuspendIfIdleAsync(previous);
        }

        if (next is null)
        {
            UpdateChrome();
            return;
        }

        WebView2 view = EnsureView(next);
        view.Visibility = Visibility.Visible;
        if (!next.Started)
        {
            next.Started = true;
            _ = StartViewAsync(next);
        }
        else if (view.CoreWebView2 is { IsSuspended: true } core)
        {
            core.Resume();
        }
        UpdateChrome();
    }

    private void SyncSelectionUi()
    {
        foreach (PanelItem panel in AllItems) panel.IsSelected = panel == _shown;
        _syncing++;
        TabList.SelectedItem = _shown is { Item.IsPinned: false } ? _shown : null;
        _syncing--;
    }

    private WebView2 EnsureView(PanelItem panel)
    {
        if (panel.View is { } existing) return existing;
        var view = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
        };
        PageHost.Children.Add(view);
        _detachClip[panel.Item.Id] = RoundedClip.Attach(PageCard, view);
        panel.View = view;
        return view;
    }

    // ── Web views ────────────────────────────────────────────────────────────

    private static Task<CoreWebView2Environment> EnvironmentAsync() => _environmentTask ??= CreateEnvironmentAsync();

    // The core of a view, in the window's mode: a private window's views are
    // made with the engine's in-private option.
    private async Task EnsureCoreAsync(WebView2 view)
    {
        _environment = await EnvironmentAsync();
        OnEnvironmentReady(_environment);
        if (_isPrivate)
        {
            CoreWebView2ControllerOptions options = _environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await view.EnsureCoreWebView2Async(_environment, options);
        }
        else
        {
            await view.EnsureCoreWebView2Async(_environment);
        }
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        string userData = Path.Combine(AppPaths.GetModuleDirectory(ModuleKey), "WebView2");
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, userData, new CoreWebView2EnvironmentOptions());
    }

    private async Task StartViewAsync(PanelItem panel)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            WebView2 view = panel.View!;
            await EnsureCoreAsync(view);
            panel.Generation = _environmentGeneration;
            Wire(panel, view.CoreWebView2);
            view.CoreWebView2.Navigate(panel.Item.Url.Length > 0 ? panel.Item.Url : "about:blank");
            DeckleBrowserViewsSource.Log.WebViewReady(clock.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            DeckleBrowserViewsSource.Log.WebViewFailed(ex.GetType().Name, PathRedaction.Redact(ex.Message));
            panel.Loaded.TrySetException(ex);
        }
    }

    private void Wire(PanelItem panel, CoreWebView2 core)
    {
        SecurityWire(panel, core);
        AdblockWire(panel, core);
        core.DocumentTitleChanged += (_, _) =>
        {
            BrowserActivity.LabelChanged();
            panel.Item.Title = core.DocumentTitle;
            panel.Title = core.DocumentTitle;
            if (!_isPrivate) _history.Retitle(core.Source, core.DocumentTitle);
            _session.Touch();
        };
        core.SourceChanged += (_, _) =>
        {
            BrowserActivity.SourceChanged();
            panel.Item.Url = core.Source;
            LoadCachedIcon(panel);
            if (_shown == panel) UpdateChrome();
            _session.Touch();
        };
        core.HistoryChanged += (_, _) =>
        {
            if (_shown == panel) UpdateHistoryButtons();
        };
        core.NavigationCompleted += (_, args) =>
        {
            OnNavigationCompleted(panel, args);
            if (args.IsSuccess && !_isPrivate) _history.Record(core.Source, core.DocumentTitle, DateTimeOffset.UtcNow);
            if (_shown == panel) UpdateHistoryButtons();
            panel.Loaded.TrySetResult();
            panel.Navigation?.TrySetResult(args.IsSuccess);
        };
        core.FaviconChanged += (_, _) =>
        {
            BrowserActivity.IconChanged();
            _ = ReadFaviconAsync(panel, core);
        };
        core.IsDocumentPlayingAudioChanged += (_, _) =>
        {
            panel.IsPlaying = core.IsDocumentPlayingAudio;
            OnPlayingChanged(panel);
            if (!panel.IsPlaying && _shown != panel) _ = SuspendIfIdleAsync(panel);
        };
        core.NewWindowRequested += (_, args) => OnNewWindowRequested(panel, args);
    }

    // A hidden page is suspended unless it plays sound. A page shown again
    // meanwhile is woken here: a suspend that lands after the page was shown
    // would leave it frozen in front.
    private async Task SuspendIfIdleAsync(PanelItem panel)
    {
        if (!SuspendsHidden) return;
        if (panel.View?.CoreWebView2 is not { } core || core.IsDocumentPlayingAudio || _shown == panel) return;
        bool suspended = await core.TrySuspendAsync();
        DeckleBrowserViewsSource.Log.PageSuspended(suspended);
        if (_shown == panel && core.IsSuspended) core.Resume();
    }

    // A page asks for a window. One a person asked for (a click) opens as a
    // tab right below the current one and hands the page its web view, which
    // keeps window.opener for sign-in pop-ups; any other is refused.
    private void OnNewWindowRequested(PanelItem opener, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (!args.IsUserInitiated)
        {
            args.Handled = true;
            return;
        }
        Windows.Foundation.Deferral deferral = args.GetDeferral();
        _ = HandOverWindowAsync(opener, args, deferral);
    }

    private async Task HandOverWindowAsync(PanelItem opener, CoreWebView2NewWindowRequestedEventArgs args, Windows.Foundation.Deferral deferral)
    {
        try
        {
            int at = opener.Item.IsPinned ? 0 : IndexOfTab(opener.Item) + 1;
            PanelItem tab = AddTab("about:blank", at, adopt: true);
            await EnsureCoreAsync(tab.View!);
            tab.Generation = _environmentGeneration;
            Wire(tab, tab.View.CoreWebView2);
            args.NewWindow = tab.View.CoreWebView2;
            args.Handled = true;
        }
        catch (Exception ex)
        {
            DeckleBrowserViewsSource.Log.WebViewFailed(ex.GetType().Name, PathRedaction.Redact(ex.Message));
            args.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ── Icons ────────────────────────────────────────────────────────────────

    private static string FaviconDirectory => Path.Combine(AppPaths.GetModuleDirectory(ModuleKey), "Favicons");

    private static string? FaviconPath(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? Path.Combine(FaviconDirectory, FaviconCache.FileName(uri.IdnHost))
            : null;

    // The icon a site left on an earlier visit, so a restored entry shows it
    // before its page is awake.
    private void LoadCachedIcon(PanelItem panel)
    {
        if (_isPrivate) return;
        if (FaviconPath(panel.Item.Url) is not { } path || !File.Exists(path)) return;
        try { _ = ShowIconAsync(panel, File.ReadAllBytes(path)); }
        catch (IOException) { /* the icon is only a convenience */ }
    }

    private async Task ReadFaviconAsync(PanelItem panel, CoreWebView2 core)
    {
        try
        {
            using IRandomAccessStream stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null || stream.Size == 0) return;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            await ShowIconAsync(panel, bytes);
            if (!_isPrivate && FaviconPath(core.Source) is { } path)
            {
                Directory.CreateDirectory(FaviconDirectory);
                await File.WriteAllBytesAsync(path, bytes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            // The icon is only a convenience; the page may be gone.
        }
    }

    private static async Task ShowIconAsync(PanelItem panel, byte[] png)
    {
        using var memory = new InMemoryRandomAccessStream();
        await memory.WriteAsync(png.AsBuffer());
        memory.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(memory);
        panel.Icon = bitmap;
    }

    // ── Panel gestures ───────────────────────────────────────────────────────

    private void OnPinnedInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        using var gesture = new GestureScope(BrowserGesture.SelectPinned);
        if (args.InvokedItem is PanelItem panel) SelectItem(panel);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing > 0) return;
        using var gesture = new GestureScope(BrowserGesture.SelectTab);
        if (TabList.SelectedItem is PanelItem panel) SelectItem(panel);
    }

    // The list reordered its lines; the session follows.
    private void OnTabDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        FollowTabOrder(args.Items.OfType<PanelItem>());

    private void FollowTabOrder(IEnumerable<PanelItem> moved)
    {
        using var gesture = new GestureScope(BrowserGesture.ReorderTab);
        foreach (PanelItem panel in moved) _session.Move(panel.Item.Id, _tabItems.IndexOf(panel));
        SyncSelectionUi();
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) => NewTab();

    private void CloseShownTab()
    {
        using var gesture = new GestureScope(BrowserGesture.CloseTab);
        if (_shown is { Item.IsPinned: false } tab) CloseItem(tab);
    }

    private void NewTab()
    {
        using var gesture = new GestureScope(BrowserGesture.NewTab);
        AddTab("about:blank");
        if (AddressRow.Visibility == Visibility.Visible) AddressBox.Focus(FocusState.Programmatic);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.PinTab);
        if (((FrameworkElement)sender).Tag is PanelItem panel) PinItem(panel);
    }

    private void OnUnpinClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.UnpinTab);
        if (((FrameworkElement)sender).Tag is PanelItem panel) UnpinItem(panel);
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        using var gesture = new GestureScope(BrowserGesture.CloseTab);
        if (((FrameworkElement)sender).Tag is PanelItem panel) CloseItem(panel);
    }

    private void OnTabPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PanelItem panel) panel.IsHovered = true;
    }

    private void OnTabPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PanelItem panel) panel.IsHovered = false;
    }

    // A middle click on a line closes the tab.
    private void OnTabPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.DataContext is PanelItem panel
            && e.GetCurrentPoint(element).Properties.IsMiddleButtonPressed)
        {
            e.Handled = true;
            CloseByMiddleClick(panel);
        }
    }

    private void CloseByMiddleClick(PanelItem panel)
    {
        using var gesture = new GestureScope(BrowserGesture.CloseTabMiddleClick);
        CloseItem(panel);
    }

    // ── Chrome that follows the shown page ───────────────────────────────────

    private CoreWebView2? ShownCore => _shown?.View?.CoreWebView2;

    private void UpdateChrome()
    {
        _current = _shown is not null && Uri.TryCreate(_shown.Item.Url, UriKind.Absolute, out Uri? uri) ? uri : null;
        if (!AddressFocused) AddressBox.Text = AddressText();
        UpdateShield();
        SiteSettingsButton.IsEnabled = SiteName.Display(_current).Length > 0;
        UpdateNotice();
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = ShownCore?.CanGoBack == true;
        ForwardButton.IsEnabled = ShownCore?.CanGoForward == true;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        using var gesture = new GestureScope(BrowserGesture.Back);
        if (ShownCore?.CanGoBack == true) ShownCore.GoBack();
    }

    private void OnForwardClick(object sender, RoutedEventArgs e) => GoForward();

    private void GoForward()
    {
        using var gesture = new GestureScope(BrowserGesture.Forward);
        if (ShownCore?.CanGoForward == true) ShownCore.GoForward();
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => ReloadShown();

    private void ReloadShown()
    {
        using var gesture = new GestureScope(BrowserGesture.Reload);
        if (_shown is { Notice: PageNotice.Crashed } crashed) ReloadAfterCrash(crashed);
        else ShownCore?.Reload();
    }

    // ── Address field ────────────────────────────────────────────────────────

    // The field is an AutoSuggestBox: the focus rests on the text box in its
    // template, which is found once the template is applied.
    private TextBox? AddressInner => FindAll<TextBox>(AddressBox).FirstOrDefault();

    private bool AddressFocused => AddressInner is { FocusState: not FocusState.Unfocused };

    private void OnAddressGotFocus(object sender, RoutedEventArgs e)
    {
        AddressBox.Text = _current is { } uri && uri.AbsoluteUri != "about:blank" ? uri.AbsoluteUri : "";
        AddressInner?.SelectAll();
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs e)
    {
        AddressBox.Text = AddressText();
        AddressBox.ItemsSource = null;
    }

    // The registrable domain alone; a page over plain http says so in words.
    private string AddressText()
    {
        string domain = SiteName.Display(_current);
        return domain.Length > 0 && SiteName.IsNotSecure(_current)
            ? $"{BrowserText.Get("Not_Secure")} \u00b7 {domain}"
            : domain;
    }

    // Enter is the box's own QuerySubmitted; Escape is ours.
    private void OnAddressKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelAddress();
        }
    }

    // Typing makes the list of suggestions from the pages visited: local only,
    // nothing is sent anywhere. A change the window made itself is not typing.
    private void OnAddressTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) UpdateSuggestions(sender.Text);
    }

    private void UpdateSuggestions(string typed)
    {
        BrowserActivity.SuggestionsMade();
        AddressBox.ItemsSource = _history.Suggest(typed).Select(s => new SuggestionLine(s)).ToList();
    }

    // Enter in the field, or a suggestion chosen: the page goes there.
    private void OnAddressQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        CommitAddress(args.ChosenSuggestion as SuggestionLine);

    // The typed text is resolved (or the chosen suggestion's address taken)
    // and the page goes there.
    private void CommitAddress(SuggestionLine? chosen = null)
    {
        using var gesture = new GestureScope(chosen is null ? BrowserGesture.AddressCommit : BrowserGesture.AddressSuggestionChosen);
        Uri? target = chosen is not null ? new Uri(chosen.Url) : AddressInput.Resolve(AddressBox.Text);
        if (target is not null && _shown is not null)
        {
            if (ShownCore is { } core) core.Navigate(target.AbsoluteUri);
            else _shown.Item.Url = target.AbsoluteUri;
        }
        else
        {
            gesture.Outcome = "refused";
        }
        _shown?.View?.Focus(FocusState.Programmatic);
    }

    // Escape in the field: the focus goes to the page, and the field, having
    // lost it, shows the page's address again (OnAddressLostFocus).
    private void CancelAddress()
    {
        using var gesture = new GestureScope(BrowserGesture.AddressCancel);
        _shown?.View?.Focus(FocusState.Programmatic);
    }

    // ── History ──────────────────────────────────────────────────────────────

    private void SaveHistory()
    {
        if (_isPrivate) return;
        try { HistoryStore.Save(_history, _historyPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleBrowserViewsSource.Log.SessionSaveFailed(ex.GetType().Name);
        }
    }

    // ── Session ──────────────────────────────────────────────────────────────

    /// <summary>Writes the session now, as the debounce and the close do.</summary>
    public void SaveSession()
    {
        if (_isPrivate) return;
        try { SessionStore.Save(_session, _sessionPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleBrowserViewsSource.Log.SessionSaveFailed(ex.GetType().Name);
        }
    }

    // ── What the self-check uses ─────────────────────────────────────────────

    /// <summary>The window's content, the root the XAML capture takes.</summary>
    public FrameworkElement ContentRoot => (FrameworkElement)Content;

    /// <summary>The panel, open or in the rail, as a person would choose it.</summary>
    public void SetPaneOpen(bool open) => _pane.SetOpen(open);

    /// <summary>The window's session: the pinned sites, the tabs, the selection.</summary>
    public BrowserSession Session => _session;

    /// <summary>The file the session is kept in.</summary>
    public string SessionPath => _sessionPath;

    /// <summary>Whether a hidden page is suspended (it is, as a person's
    /// window does). The self-check turns it off to measure what suspending
    /// saves.</summary>
    public bool SuspendsHidden { get; set; } = true;

    /// <summary>How many web views are suspended right now.</summary>
    public int SuspendedCount => AllItems.Count(p => p.View?.CoreWebView2 is { IsSuspended: true });

    /// <summary>Suspends every hidden page that plays no sound, now, and waits.</summary>
    public async Task SuspendHiddenAsync()
    {
        foreach (PanelItem panel in AllItems.Where(p => p != _shown).ToList())
        {
            if (panel.View?.CoreWebView2 is { IsDocumentPlayingAudio: false } core) await core.TrySuspendAsync();
        }
    }

    /// <summary>Opens a tab on the address, as a person would, and waits for
    /// its first page to load. Returns the new item's id.</summary>
    public async Task<Guid> OpenTabAsync(Uri uri)
    {
        PanelItem panel = AddTab(uri.AbsoluteUri);
        await panel.Loaded.Task;
        return panel.Item.Id;
    }

    /// <summary>Closes a tab.</summary>
    public void CloseTab(Guid id)
    {
        if (_byId.TryGetValue(id, out PanelItem? panel)) CloseItem(panel);
    }

    /// <summary>Pins a tab.</summary>
    public void PinTab(Guid id)
    {
        if (_byId.TryGetValue(id, out PanelItem? panel)) PinItem(panel);
    }

    /// <summary>Selects an item, as a click on it would, and waits for its page.</summary>
    public async Task SelectAsync(Guid id)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return;
        SelectItem(panel);
        await panel.Loaded.Task;
    }

    /// <summary>Selects an item and measures the time until its page answers a
    /// script: what waking a suspended pinned site costs.</summary>
    public async Task<double> WakeAsync(Guid id)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return -1;
        var clock = Stopwatch.StartNew();
        SelectItem(panel);
        await panel.Loaded.Task;
        await panel.View!.CoreWebView2.ExecuteScriptAsync("1");
        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>Runs a script in the selected page and returns its JSON result.</summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        PanelItem panel = _shown ?? throw new InvalidOperationException("No page is shown.");
        await panel.Loaded.Task;
        return await panel.View!.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>Waits for the shown page to be ready (its environment created
    /// and its first page loaded), then navigates it and waits for the
    /// navigation to complete. False when the page failed to load.</summary>
    public async Task<bool> NavigateAsync(Uri uri)
    {
        PanelItem panel = _shown ?? throw new InvalidOperationException("No page is shown.");
        await panel.Loaded.Task;
        panel.Navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.View!.CoreWebView2.Navigate(uri.AbsoluteUri);
        return await panel.Navigation.Task;
    }

    /// <summary>The shown page as WebView2 draws it, as a PNG.</summary>
    public async Task CapturePagePreviewAsync(Stream png)
    {
        PanelItem panel = _shown ?? throw new InvalidOperationException("No page is shown.");
        await panel.Loaded.Task;
        await panel.View!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png.AsRandomAccessStream());
    }

    /// <summary>How many processes the web view's environment runs and the
    /// private memory they hold, together. Numbers only: no address.</summary>
    public async Task<(int Processes, long PrivateBytes)> MeasureWebViewMemoryAsync()
    {
        CoreWebView2Environment environment = await EnvironmentAsync();
        var infos = environment.GetProcessInfos();
        long bytes = 0;
        int count = 0;
        foreach (CoreWebView2ProcessInfo info in infos)
        {
            try
            {
                using Process process = Process.GetProcessById(info.ProcessId);
                bytes += process.PrivateMemorySize64;
                count++;
            }
            catch (ArgumentException)
            {
                // The process ended between the list and the read.
            }
        }
        return (count, bytes);
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Minimized)
            op.Restore();

        AppWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        WindowingProbe.EmitWindowPositioned(_hwnd, "browser", "Center");
    }
}
