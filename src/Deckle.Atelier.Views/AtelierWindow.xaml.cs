using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

namespace Deckle.Atelier.Views;

// ── The Atelier window ────────────────────────────────────────────────────────
//
// One window through which the person looks at the folders they added, with
// several tools (spec, Intention). This file holds the window's chrome and
// lifetime; the place and its history, the tools, the added folders and what
// the self-check drives each have their partial file beside it.
//
// The window works in its module's own data folder, handed in by the app:
// the added folders persist there and nowhere else.
public sealed partial class AtelierWindow : Window
{
    private readonly nint _hwnd;
    private readonly NavigationPane _pane;

    public AtelierWindow(string moduleDirectory)
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        _folders = new AddedFolderStore(moduleDirectory);

        ApplyChrome();
        ApplyLabels();
        Address.Folders = _folders;
        Address.NavigateRequested += place => _ = NavigateTo(place);
        Address.IconChosen += OnFolderIconChosen;

        BuildToolButtons();
        // The rail the first time (spec, Window: the pane reduced to icons),
        // then as the user left it (specs/deckle-navigation-pane.md). Attached
        // before the items, which read whether the pane is open.
        _pane = NavigationPane.Attach(Nav, windowKey: "atelier", openAtFirst: false);
        SyncFolderItems();
        // The icon provider's shell threads end with the window.
        Closed += (_, _) => _icons.Dispose();

        if (_folders.Folders.Count > 0) _ = NavigateTo(FolderPlace.AtRoot(_folders.Folders[0]));
        else ShowEmptyState();
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } op) op.Restore();
        AppWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        WindowingProbe.EmitWindowPositioned(_hwnd, "atelier", "Center");
    }

    private void ApplyChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Tall: the caption buttons take the full height of the one row
        // (spec, Window), 48 px as in File Explorer.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);
        SystemBackdrop = new MicaBackdrop();

        if (IconAssets.ResolvePath(recording: false) is { } ico) AppWindow.SetIcon(ico);
        if (IconAssets.ResolveImagePath(recording: false) is { } png)
            AppTitleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(png)) };

        // The size the boards are drawn at, in DIPs; window sizes are physical
        // pixels, so scaled, and clamped to the work area.
        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(1280 * scale), work.Width),
            Math.Min((int)(800 * scale), work.Height)));

        var presenter = OverlappedPresenter.Create();
        // The narrowest the top row holds whole: the buttons, the active
        // tool with its name, search, and an address bar of its icon and a
        // few letters (AtelierWindow.xaml).
        presenter.PreferredMinimumWidth = (int)(840 * scale);
        presenter.PreferredMinimumHeight = (int)(480 * scale);
        AppWindow.SetPresenter(presenter);
    }

    // Tooltips and accessible names read from the catalog: an icon-only
    // button says what it does on hover and to a screen reader.
    private void ApplyLabels()
    {
        Label(BackButton, "Nav_Back");
        Label(ForwardButton, "Nav_Forward");
        Label(UpButton, "Nav_Up");
        // The active tool may say what Refresh does for it (AtelierWindow.Tools.cs).
        SetRefreshLabel(null);
        Label(SearchButton, "Search");
        AutomationProperties.SetName(ToolButtons, AtelierText.Get("Tools_Group"));
        AutomationProperties.SetName(DetailsPanel, AtelierText.Get("Details_Panel"));
        AutomationProperties.SetName(Nav, AtelierText.Get("Folders_Pane"));
        AddFolderItem.Content = AtelierText.Get("Folders_Add");
        EmptyAddLabel.Text = AtelierText.Get("Folders_Add");
    }

    private static void Label(FrameworkElement element, string key)
    {
        string text = AtelierText.Get(key);
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => _pane.Toggle();
}
