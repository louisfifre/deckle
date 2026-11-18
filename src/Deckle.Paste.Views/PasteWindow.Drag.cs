using System.Runtime.InteropServices;
using Deckle.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Paste.Views;

// The whole surface moves the window, the inset and its rows included; only
// the two buttons and the list's scrollbar keep their press. A press that
// does not travel stays a click and puts the focus in the zone, so Ctrl+V
// works at once.
//
// The route: a press is watched in XAML, and once the pointer has travelled
// past the system drag threshold the window hands the gesture to Windows'
// own move loop (WM_NCLBUTTONDOWN on HTCAPTION, after ReleaseCapture), as a
// title bar would. Not InputNonClientPointerSource with a Caption region over
// the client area: a Caption region is non-client, so the zone under it would
// lose its pointer states, its focus on click and its context menu, and a
// double click there would reach the caption's maximize. Here no move starts
// without travel, so a double click is two clicks and never maximizes.
public sealed partial class PasteWindow
{
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int SM_CXDRAG = 68;
    private const int SM_CYDRAG = 69;

    private bool _pressWatched;
    private POINT _pressOrigin;

    private void AttachDrag()
    {
        // handledEventsToo: the TextBox marks its own presses handled.
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSurfacePressed), true);
        RootGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnSurfaceMoved), true);
        RootGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSurfaceEnded), true);
        RootGrid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnSurfaceEnded), true);
    }

    private void OnSurfacePressed(object sender, PointerRoutedEventArgs e)
    {
        _pressWatched = false;
        if (KeepsItsOwnPress(e.OriginalSource as DependencyObject)) return;

        // Mouse and pen: Windows' move loop follows the mouse button. A touch
        // press keeps the zone's own behaviour.
        if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Mouse or PointerDeviceType.Pen)) return;
        if (!e.GetCurrentPoint(RootGrid).Properties.IsLeftButtonPressed) return;

        if (!NativeMethods.GetCursorPos(out _pressOrigin)) return;
        _pressWatched = true;
        PasteZone.Focus(FocusState.Pointer);
    }

    private void OnSurfaceMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressWatched) return;
        if (!NativeMethods.GetCursorPos(out POINT now)) return;

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (Math.Abs(now.X - _pressOrigin.X) < GetSystemMetricsForDpi(SM_CXDRAG, dpi)
            && Math.Abs(now.Y - _pressOrigin.Y) < GetSystemMetricsForDpi(SM_CYDRAG, dpi)) return;

        _pressWatched = false;
        // XAML holds the capture on its input window; Windows' move loop needs
        // the button free. SendMessage returns when the button is released.
        ReleaseCapture();
        nint position = (nint)((_pressOrigin.Y << 16) | (_pressOrigin.X & 0xFFFF));
        SendMessage(_hwnd, WM_NCLBUTTONDOWN, HTCAPTION, position);
    }

    private void OnSurfaceEnded(object sender, PointerRoutedEventArgs e) => _pressWatched = false;

    // A button, or the scrollbar: its thumb is no ButtonBase, and dragging it
    // must scroll the list, not move the window.
    private bool KeepsItsOwnPress(DependencyObject? element)
    {
        for (DependencyObject? node = element; node is not null && !ReferenceEquals(node, RootGrid);
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or ScrollBar) return true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
}
