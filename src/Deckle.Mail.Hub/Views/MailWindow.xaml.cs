using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;

namespace Deckle.Mail.Hub;

// ─── Mail window ─────────────────────────────────────────────────────────────
//
// The Deckle Mail hub: a TitleBar (back, pane toggle, search, problems), a
// NavigationView built from the session, a Frame of pages, on Mica. The shape
// of SettingsWindow, with what the spec asks differently: the back button is
// always there and disabled when there is nowhere to go back to, nothing in
// the title bar moves from page to page, the problems button shows only while
// something does not work, and nothing is reached by a right-click.
//
// It runs on the built-in sample (SampleMailHubSource) until the engine is
// wired: UseSource swaps what stands behind it. Close destroys the window;
// the App recreates it on the next open, and the treated rows go with it.
//
// Split by responsibility: .Rail builds the navigation, .Navigation moves
// between pages, .Search and .Problems hold the two commands of the title
// bar, .Capture walks the pages for the self-check.
public sealed partial class MailWindow : Window, IMailShell
{
    private readonly IntPtr _hwnd;
    private IMailHubSource _source = new SampleMailHubSource(SampleScenario.NormalDay);

    public MailWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        SetRecordingState(false);

        // Tall gives the bar room for the search box, as in SettingsWindow.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // The control stamps its caption padding in raw physical pixels, an
        // upstream px/DIP bug that inflates the reserve above 100 % scale.
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);

        SystemBackdrop = new MicaBackdrop();

        Title = MailText.Get("Window_Title");

        // Window sizes are PHYSICAL pixels: scale the intended DIPs, clamped to
        // the work area. 1280 x 860 is the size the frames are drawn at.
        double dpiScale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(1280 * dpiScale), workArea.Width),
            Math.Min((int)(860 * dpiScale), workArea.Height)));

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable = true;
        // 400 DIPs: the narrowest width where the title bar still holds back,
        // pane toggle, icon, search and caption buttons (SettingsWindow's floor).
        presenter.PreferredMinimumWidth = (int)(400 * dpiScale);
        presenter.PreferredMinimumHeight = (int)(400 * dpiScale);
        AppWindow.SetPresenter(presenter);

        Nav.DisplayModeChanged += (_, _) => SyncNavigationPane();
        Nav.Loaded += (_, _) => SyncNavigationPane();

        InstallBackInputs();
        Session = LoadSession(_source);
        RebuildRail();
        ShowFirstPage();
        RefreshProblems();

        if (Content is FrameworkElement root)
        {
            _lastTheme = root.ActualTheme;
            root.ActualThemeChanged += OnRootActualThemeChanged;
        }
        Closed += (_, _) => Session.Changed -= OnSessionChanged;
    }

    public TriageSession Session { get; private set; }

    // Replaces what stands behind the window and starts again on Triage: the
    // self-check walks the scenarios through this, and the engine will come
    // in the same way.
    public void UseSource(IMailHubSource source)
    {
        _source = source;
        Session.Changed -= OnSessionChanged;
        Session = LoadSession(source);
        _folded.Clear();
        RebuildRail();
        ShowFirstPage();
        RefreshProblems();
    }

    public void UseScenario(SampleScenario scenario) => UseSource(new SampleMailHubSource(scenario));

    private TriageSession LoadSession(IMailHubSource source)
    {
        var session = new TriageSession(source);
        session.Changed += OnSessionChanged;
        DeckleMailHubSource.Log.DataLoaded(
            source.GetType().Name, session.Mailboxes.Count, session.WaitingCount(MailFilter.All), session.InfoLines.Count);
        return session;
    }

    // The navigation and the problems button follow the session; each page
    // follows it on its own.
    private void OnSessionChanged(SessionChange change)
    {
        if ((change & (SessionChange.Mailboxes | SessionChange.Activity)) != 0) RebuildRail();
        else if ((change & SessionChange.Rows) != 0) RefreshRailCounts();

        if ((change & (SessionChange.Mailboxes | SessionChange.Problems | SessionChange.Rows)) != 0)
            RefreshProblems();
    }

    // ── Theme tracing ────────────────────────────────────────────────────────
    private ElementTheme _lastTheme;

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        var to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged("mail", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    // ── Window icon ──────────────────────────────────────────────────────────
    // The .ico goes to the window, the 32 px PNG to the title bar, as
    // SettingsWindow does: a BitmapImage of the .ico decodes its 16 px frame.
    public void SetRecordingState(bool isRecording)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyRecordingState(isRecording);
        else DispatcherQueue.TryEnqueueObserved(
            operation: "engine-state-sync", caller: "mail-window",
            callback: () => ApplyRecordingState(isRecording),
            rejectSource: "MAIL", rejectWhat: "recording state sync");
    }

    private void ApplyRecordingState(bool isRecording)
    {
        string? icoPath = IconAssets.ResolvePath(isRecording);
        if (icoPath is not null) AppWindow.SetIcon(icoPath);
        string? pngPath = IconAssets.ResolveImagePath(isRecording);
        if (pngPath is not null)
            AppTitleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(pngPath)) };
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Minimized)
            op.Restore();

        AppWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        WindowingProbe.EmitWindowPositioned(_hwnd, "mail", "Center");
    }

    // ── Pane ─────────────────────────────────────────────────────────────────

    private void OnTitleBarPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void SyncNavigationPane() => Nav.IsPaneOpen = Nav.DisplayMode == NavigationViewDisplayMode.Expanded;
}
