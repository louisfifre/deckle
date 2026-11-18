using Deckle.Core;
using Deckle.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

namespace Deckle.Paste.Views;

// ── The Paste to folder window ────────────────────────────────────────────────
//
// A tiny window that saves what the clipboard holds as a file in one chosen
// folder: the person clicks it, presses Ctrl+V, and the status line says
// whether it worked. This file holds the chrome, the lifetime and the status
// line; the paste itself, the folder and the drag that moves the window each
// have their partial file beside it.
//
// Closing closes: the window holds no work that outlives it (a save runs for
// milliseconds), so the host drops it on Closed and builds a fresh one on the
// next open, as it does for the Atelier.
public sealed partial class PasteWindow : Window
{
    private const int WidthDip = 320;
    private const int HeightDip = 220;

    private readonly nint _hwnd;

    public PasteWindow(string dataDirectory)
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        _folderStore = new PasteFolderStore(dataDirectory);

        ApplyChrome();
        ApplyLabels();
        AttachDrag();
        ShowFolder();
        ShowStatus(PasteStatus.None, null);

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
    // DWM's rounded corners and stroke, Acrylic, above other windows, and no
    // taskbar button or Alt+Tab entry.
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
        uint border = NativeMethods.DWMWA_COLOR_DEFAULT;
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

    // ── The status line ───────────────────────────────────────────────────────

    private enum PasteStatus { None, Saving, Done, Failed }

    // The mark, the word and the detail of one state, and the sentence
    // Narrator reads when it changes.
    private void ShowStatus(PasteStatus status, string? detail)
    {
        SavingRing.IsActive = status == PasteStatus.Saving;
        DoneMark.Visibility = status == PasteStatus.Done ? Visibility.Visible : Visibility.Collapsed;
        FailedMark.Visibility = status == PasteStatus.Failed ? Visibility.Visible : Visibility.Collapsed;

        string word = status switch
        {
            PasteStatus.Saving => PasteText.Get("Paste_Status_Saving"),
            PasteStatus.Done => PasteText.Get("Paste_Status_Done"),
            PasteStatus.Failed => PasteText.Get("Paste_Status_Failed"),
            _ => string.Empty,
        };
        StatusText.Text = word;
        StatusDetail.Text = detail ?? string.Empty;
        ToolTipService.SetToolTip(StatusLine, string.IsNullOrEmpty(detail) ? null : detail);

        string spoken = string.IsNullOrEmpty(detail)
            ? word
            : PasteText.Format("Paste_Status_Announce_Format", word, detail);
        AutomationProperties.SetName(StatusText, spoken);
        if (status != PasteStatus.None)
            FrameworkElementAutomationPeer.FromElement(StatusText)?
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // ── What the self-check captures ──────────────────────────────────────────

    public static IReadOnlyList<string> CaptureStates { get; } = ["empty", "done", "failed"];

    // Puts the window in one named state without reading the clipboard or
    // writing to disk. The file name shown is an example, not a saved file.
    public void ShowStateForCapture(string state)
    {
        switch (state)
        {
            case "empty":
                ShowStatus(PasteStatus.None, null);
                break;
            case "done":
                ShowStatus(PasteStatus.Done, "paste-2026-11-18-143005.png");
                break;
            case "failed":
                ShowStatus(PasteStatus.Failed, PasteText.Get("Paste_Error_NoFolder"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown capture state.");
        }
    }
}
