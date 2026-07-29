using Deckle.Catalog;
using Deckle.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.Graphics;

namespace Deckle.Llm.Rewrite;

// Interactive transient: shown without activation so typing stays in the
// target, but intentionally activatable when selected so its native Buttons,
// focus visuals, Escape handling, and accessibility peers remain available.
public sealed partial class RewriteOfferWindow : Window
{
    private const int WidthDip = 440;
    private const int HeightDip = 280;
    private const int AnchorGapDip = 8;

    private readonly IntPtr _hwnd;
    private ParagraphRewriteOffer? _offer;

    public RewriteOfferWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        Title = Loc.GetFrom("Deckle.Llm.Rewrite", "RewriteOffer_WindowTitle");
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
            _hwnd,
            NativeMethods.GWL_EXSTYLE,
            new IntPtr(exStyle | NativeMethods.WS_EX_TOOLWINDOW));

        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            Hide();
        };
    }

    public event Action<ParagraphRewriteOffer>? ApplyRequested;
    public event Action? KeptOriginal;

    public IntPtr Hwnd => _hwnd;

    public void ShowOffer(ParagraphRewriteOffer offer, IntPtr targetHwnd, ScreenRect anchor)
    {
        _offer = offer;
        ProposalText.Text = offer.Rewritten;
        ChangesList.ItemsSource = offer.Verdict.Edits
            .Where(static edit => edit.Ruling != DiffEditRuling.Match)
            .Select(static edit => new RewriteChangeView(edit.Original, edit.Rewritten))
            .ToArray();
        AutomationProperties.SetName(ProposalText, offer.Rewritten);

        double scale = Math.Max(1.0, NativeMethods.GetDpiForWindow(targetHwnd) / 96.0);
        int width = (int)Math.Round(WidthDip * scale);
        int height = (int)Math.Round(HeightDip * scale);
        int gap = (int)Math.Round(AnchorGapDip * scale);

        var display = DisplayArea.GetFromPoint(
            new PointInt32(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2),
            DisplayAreaFallback.Nearest);
        RectInt32 work = display.WorkArea;

        int x = Math.Clamp(anchor.Right - width, work.X, work.X + work.Width - width);
        int preferredY = anchor.Y - height - gap;
        int y = preferredY >= work.Y
            ? preferredY
            : Math.Clamp(anchor.Bottom + gap, work.Y, work.Y + work.Height - height);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    public bool ContainsPointer()
    {
        if (!NativeMethods.GetCursorPos(out POINT point)) return false;
        if (!NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT rect)) return false;
        return point.X >= rect.left && point.X < rect.right
            && point.Y >= rect.top && point.Y < rect.bottom;
    }

    public void Hide()
    {
        _offer = null;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ParagraphRewriteOffer? offer = _offer;
        if (offer is null) return;

        // Keep the offer window alive and foreground until the host has restored
        // the captured target and injected the replacement. Hiding first lets
        // Windows publish the target's focus transition before ApplyRequested;
        // the paragraph observer can then invalidate this exact offer between
        // these two lines, making the click intermittently do nothing.
        ApplyRequested?.Invoke(offer);
        Hide();
    }

    private void OnKeepOriginalClick(object sender, RoutedEventArgs e)
    {
        Hide();
        KeptOriginal?.Invoke();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        Hide();
        KeptOriginal?.Invoke();
    }
}
