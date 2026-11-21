using Deckle.Core;
using Deckle.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Paste.Views;

// ── The Paste to folder window ────────────────────────────────────────────────
//
// A tiny window that saves what the clipboard holds as a file in one chosen
// folder: the person clicks it, presses Ctrl+V, and the inset lists what was
// saved, or why not. This file holds the chrome and the lifetime; the paste,
// the list, the thumbnails, the folder, the drag that moves the window and
// the self-check's states each have their partial file beside it.
//
// Closing closes: the window holds no work that outlives it (a save runs for
// milliseconds), so the host drops it on Closed and builds a fresh one on the
// next open, as it does for Deckle Folders. The list goes with it.
public sealed partial class PasteWindow : Window
{
    // Three or four rows show before the list scrolls, at any scale: sizes
    // are DIPs, made pixels in ApplyChrome.
    private const int WidthDip = 320;
    private const int HeightDip = 260;

    private readonly nint _hwnd;

    public PasteWindow(string dataDirectory)
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        _folderStore = new PasteFolderStore(dataDirectory);

        ApplyChrome();
        ApplyLabels();
        AttachDrag();
        AttachList();
        ShowFolder();

        // Ctrl+V works the moment the window is active: something inside it
        // must hold the keyboard focus, and the zone is what a paste is for.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) return;
            if (Content.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is null)
                PasteZone.Focus(FocusState.Programmatic);
        };
    }

    public void ShowAndActivate()
    {
        AppWindow.Show();
        Activate();
        // Activate() does not always bring the window to front from a tray
        // callback. SetForegroundWindow is allowed: the tray host is same-process.
        NativeMethods.SetForegroundWindow(_hwnd);
        PasteZone.Focus(FocusState.Programmatic);
        WindowingProbe.EmitWindowPositioned(_hwnd, "paste", "Center");
    }

    // A small surface the person keeps beside their work, the way the
    // paragraph offer window is built (RewriteOfferWindow): no title bar,
    // DWM's rounded corners without its stroke, Acrylic, above other windows, and no
    // taskbar button or Alt+Tab entry. The title is drawn nowhere; it still
    // names the window to Narrator and to the windowing APIs.
    private void ApplyChrome()
    {
        Title = PasteText.Get("Paste_WindowTitle");
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var presenter = OverlappedPresenter.Create();
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        uint corners = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(uint));
        // No stroke: the system's one reads as a bright outline around so
        // small a surface, and the Acrylic and the shadow already set it off.
        uint border = NativeMethods.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(uint));

        long exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(
            _hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle | NativeMethods.WS_EX_TOOLWINDOW));

        // Its size in DIPs; window sizes are physical pixels, so scaled.
        // Centred on the work area at first; the host may restore the place
        // the person left it.
        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min((int)Math.Round(WidthDip * scale), work.Width);
        int height = Math.Min((int)Math.Round(HeightDip * scale), work.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2,
            width,
            height));
    }

    // Tooltips and accessible names from the catalog: an icon-only button
    // says what it does on hover and to a screen reader.
    private void ApplyLabels()
    {
        string close = PasteText.Get("Paste_Close");
        ToolTipService.SetToolTip(CloseButton, close);
        AutomationProperties.SetName(CloseButton, close);

        PasteZone.PlaceholderText = PasteText.Get("Paste_Zone_Placeholder");
        AutomationProperties.SetName(PasteZone, PasteText.Get("Paste_Zone_Name"));
        AutomationProperties.SetHelpText(PasteZone, PasteText.Get("Paste_Zone_HelpText"));
        PasteMenuItem.Text = PasteText.Get("Paste_Zone_PasteItem");
        PasteMenuItem.KeyboardAcceleratorTextOverride = PasteText.Get("Paste_Zone_PasteShortcut");

        AutomationProperties.SetName(PasteList, PasteText.Get("Paste_List_Name"));
        SavingText.Text = PasteText.Get("Paste_List_Saving");

        ChooseFolderButton.Content = PasteText.Get("Paste_Folder_Choose");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // Escape closes, after any control that wanted it (an open flyout keeps
    // its own Escape).
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        Close();
    }
}
