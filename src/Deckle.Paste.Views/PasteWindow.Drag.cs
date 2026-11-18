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
// past the system drag threshold the window captures the pointer and moves
// itself to follow the cursor until the button is released
// (WindowDragGesture holds the arithmetic).
//
// Not Windows' own move loop (WM_NCLBUTTONDOWN on HTCAPTION after
// ReleaseCapture): started from a XAML pointer handler, that loop runs inside
// the handler and the button's release never reaches it, so the window stayed
// stuck to the cursor until another click. Not InputNonClientPointerSource
// with a Caption region over the client area either: a Caption region is
// non-client, so the zone under it would lose its pointer states, its focus
// on click and its context menu, and a double click there would reach the
// caption's maximize. Here no move starts without travel, so a double click
// is two clicks. The cost: no Aero Snap, which a small fixed-size window does
// not use.
public sealed partial class PasteWindow
{
    private const int SM_CXDRAG = 68;
    private const int SM_CYDRAG = 69;

    private readonly WindowDragGesture _drag = new();

    private void AttachDrag()
    {
        // handledEventsToo: the TextBox marks its own presses handled.
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSurfacePressed), true);
        RootGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnSurfaceMoved), true);
        RootGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSurfaceReleased), true);
        RootGrid.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnSurfaceReleased), true);
        RootGrid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnSurfaceCaptureLost), true);
    }

    private void OnSurfacePressed(object sender, PointerRoutedEventArgs e)
    {
        _drag.End();
        if (KeepsItsOwnPress(e.OriginalSource as DependencyObject)) return;

        // Mouse and pen. A touch press keeps the zone's own behaviour.
        if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Mouse or PointerDeviceType.Pen)) return;
        if (!e.GetCurrentPoint(RootGrid).Properties.IsLeftButtonPressed) return;

        // The cursor's screen position, not the event's: the event's is
        // relative to the window, which is about to move under it.
        if (!NativeMethods.GetCursorPos(out POINT cursor)) return;
        var place = AppWindow.Position;
        _drag.Press(cursor.X, cursor.Y, place.X, place.Y);
        PasteZone.Focus(FocusState.Pointer);
    }

    private void OnSurfaceMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out POINT cursor)) return;

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        bool wasMoving = _drag.IsMoving;
        if (!_drag.TryMove(
                cursor.X, cursor.Y,
                GetSystemMetricsForDpi(SM_CXDRAG, dpi), GetSystemMetricsForDpi(SM_CYDRAG, dpi),
                out int x, out int y)) return;

        // Captured once the move starts, so the moves keep coming when a fast
        // pointer leaves the window before it has caught up.
        if (!wasMoving) RootGrid.CapturePointer(e.Pointer);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void OnSurfaceReleased(object sender, PointerRoutedEventArgs e)
    {
        bool wasMoving = _drag.IsMoving;
        _drag.End();
        if (wasMoving) RootGrid.ReleasePointerCapture(e.Pointer);
    }

    // The window's own capture ending ends the move. A child that loses its
    // capture because the window just took it (the event then comes from that
    // child) must not end the move it started.
    private void OnSurfaceCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_drag.IsMoving && !ReferenceEquals(e.OriginalSource, RootGrid)) return;
        _drag.End();
    }

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
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
}
