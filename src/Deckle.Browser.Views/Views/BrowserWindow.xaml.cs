using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using WinRT.Interop;

namespace Deckle.Browser.Views;

// ─── Browser window ──────────────────────────────────────────────────────────
//
// Zen's layout: a panel on the left (the button row, the address field, and at
// its foot the settings), the web page as a card with rounded corners on the
// right, all on Mica. There is no title bar of its own: the button row stands
// in for it and the caption buttons sit at the top right, above the page.
//
// The panel is a NavigationView whose pane slots hold our own controls, given
// the common pane behaviour (NavigationPane: open, rail, narrow). The page is
// one WebView2, on a user data folder of the module's own, created once.
//
// Nothing a person visits is logged: no address, no title, no domain.
public sealed partial class BrowserWindow : Window
{
    private const string WindowKey = "browser";

    private readonly IntPtr _hwnd;
    private readonly NavigationPane _pane;
    private readonly TaskCompletionSource _webViewReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CoreWebView2Environment? _environment;
    private Uri? _current;
    private TaskCompletionSource<bool>? _navigation;

    // One environment for the process, created on first use over the module's
    // own user data folder.
    private static Task<CoreWebView2Environment>? _environmentTask;

    public BrowserWindow()
    {
        InitializeComponent();
        RoundedClip.Attach(PageCard, Web);
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

        _pane = NavigationPane.Attach(Nav, WindowKey);
        _pane.Changed += ApplyPaneState;
        ApplyPaneState();

        _ = StartWebViewAsync();
    }

    // ── Pane ─────────────────────────────────────────────────────────────────

    private void OnPaneToggleClick(object sender, RoutedEventArgs e) => _pane.Toggle();

    // The rail shows the button row alone, in a column; the address field
    // needs the open pane. A narrow window hides the panel, and a button over
    // the page opens it.
    private void ApplyPaneState()
    {
        bool open = _pane.IsOpen;
        AddressBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ButtonStack.Orientation = open ? Orientation.Horizontal : Orientation.Vertical;
        NarrowToggleButton.Visibility = _pane.IsOverPage && !open ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Web view ─────────────────────────────────────────────────────────────

    private async Task StartWebViewAsync()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            _environment = await EnvironmentAsync();
            await Web.EnsureCoreWebView2Async(_environment);

            CoreWebView2 core = Web.CoreWebView2;
            core.SourceChanged += (_, _) => OnSourceChanged();
            core.HistoryChanged += (_, _) => UpdateHistoryButtons();
            core.NavigationCompleted += OnNavigationCompleted;
            core.Navigate("about:blank");

            DeckleBrowserViewsSource.Log.WebViewReady(clock.ElapsedMilliseconds);
            _webViewReady.TrySetResult();
        }
        catch (Exception ex)
        {
            DeckleBrowserViewsSource.Log.WebViewFailed(ex.GetType().Name, PathRedaction.Redact(ex.Message));
            _webViewReady.TrySetException(ex);
        }
    }

    private static Task<CoreWebView2Environment> EnvironmentAsync() => _environmentTask ??= CreateEnvironmentAsync();

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        string userData = Path.Combine(AppPaths.GetModuleDirectory("browser"), "WebView2");
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, userData, new CoreWebView2EnvironmentOptions());
    }

    private void OnSourceChanged()
    {
        _current = Uri.TryCreate(Web.CoreWebView2.Source, UriKind.Absolute, out Uri? uri) ? uri : null;
        if (AddressBox.FocusState == FocusState.Unfocused)
            AddressBox.Text = SiteName.Display(_current);
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        if (Web.CoreWebView2 is not { } core) return;
        BackButton.IsEnabled = core.CanGoBack;
        ForwardButton.IsEnabled = core.CanGoForward;
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        UpdateHistoryButtons();
        _navigation?.TrySetResult(args.IsSuccess);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack();
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward();
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();

    // ── Address field ────────────────────────────────────────────────────────

    private void OnAddressGotFocus(object sender, RoutedEventArgs e)
    {
        AddressBox.Text = _current is { } uri && uri.AbsoluteUri != "about:blank" ? uri.AbsoluteUri : "";
        AddressBox.SelectAll();
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs e) => AddressBox.Text = SiteName.Display(_current);

    private void OnAddressKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (AddressInput.Resolve(AddressBox.Text) is { } target) Web.CoreWebView2?.Navigate(target.AbsoluteUri);
                Web.Focus(FocusState.Programmatic);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                Web.Focus(FocusState.Programmatic);
                break;
        }
    }

    // ── What the self-check uses ─────────────────────────────────────────────

    /// <summary>The window's content, the root the XAML capture takes.</summary>
    public FrameworkElement ContentRoot => (FrameworkElement)Content;

    /// <summary>The panel, open or in the rail, as a person would choose it.</summary>
    public void SetPaneOpen(bool open) => _pane.SetOpen(open);

    /// <summary>Waits for the web view to be ready (its environment created and
    /// its first page opened), then navigates and waits for the navigation to
    /// complete. False when the page failed to load.</summary>
    public async Task<bool> NavigateAsync(Uri uri)
    {
        await _webViewReady.Task;
        _navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Web.CoreWebView2.Navigate(uri.AbsoluteUri);
        return await _navigation.Task;
    }

    /// <summary>The page as WebView2 draws it, as a PNG.</summary>
    public async Task CapturePagePreviewAsync(Stream png)
    {
        await _webViewReady.Task;
        await Web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png.AsRandomAccessStream());
    }

    /// <summary>How many processes the web view's environment runs and the
    /// private memory they hold, together. Numbers only: no address.</summary>
    public async Task<(int Processes, long PrivateBytes)> MeasureWebViewMemoryAsync()
    {
        await _webViewReady.Task;
        var infos = _environment!.GetProcessInfos();
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
