using System;
using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Shell;

// Collapses the per-frame recompute a resizable window pays during an
// interactive edge drag into a single recompute once the gesture settles.
//
// Windows runs a modal move/size loop while the user drags a window's title bar
// or sizing border: WM_ENTERSIZEMOVE once, a burst of WM_SIZE while the pointer
// moves, WM_EXITSIZEMOVE once at release. WinUI relays out the tree and Win2D
// reissues its Draw on every one of those WM_SIZE. WinUI 3 exposes no native
// resize-begin/resize-end event (AppWindow.Changed only reports "size changed",
// and wiring its handler silently kills Window.SizeChanged after the first
// resize — microsoft-ui-xaml #6466), so the boundary is read off the Win32
// messages directly.
//
// The HWND is subclassed with comctl32 SetWindowSubclass, never
// SetWindowLongPtr(GWLP_WNDPROC), which would replace the WinUI message chain.
// The subclass runs on the window's UI thread, so both callbacks fire there and
// consumers touch XAML directly. The SubclassProc delegate lives in a field so
// the GC can't collect it while comctl32 holds its function pointer.
public sealed class ResizeCoalescer : IDisposable
{
    private readonly IntPtr  _hwnd;
    private readonly string  _window;          // closed windowing vocabulary (DeckleWindowingSource)
    private readonly Action  _onResizeSettled;
    private readonly Action? _onResizeStarted;
    private readonly ResizeGesture _gesture = new();
    private readonly Stopwatch     _gestureClock = new();
    private long                   _lastFrameMs;   // gesture-clock ms at the previous coalesced frame

    private NativeMethods.SubclassProc? _subclassDelegate;

    private static readonly UIntPtr SubclassId = new(0x52535A43);

    private bool _registered;
    private bool _disposed;

    // True while a resize gesture is in flight. Per-frame consumers (Win2D Draw,
    // SizeChanged rebuilds) gate their expensive path on this.
    public bool IsResizing => _gesture.IsResizing;

    // `window` is the short logical name from the DeckleWindowingSource closed
    // vocabulary ("playground", "settings", "log", …). `onResizeSettled` runs the
    // window's one deferred recompute, `onResizeStarted` (optional) the rising-edge
    // suspend. Only a real drag raises `onResizeStarted`; a direct settle
    // (maximize, snap, programmatic resize) never does.
    public ResizeCoalescer(IntPtr hwnd, string window, Action onResizeSettled, Action? onResizeStarted = null)
    {
        _hwnd = hwnd;
        _window = window;
        _onResizeSettled = onResizeSettled;
        _onResizeStarted = onResizeStarted;
    }

    // Installs the subclass. Call once the window's HWND is valid (after
    // WindowNative.GetWindowHandle). Separate from the ctor so the wiring can be
    // set up first. Idempotent.
    public void Register()
    {
        if (_registered) return;
        _subclassDelegate = SubclassCallback;
        if (!NativeMethods.SetWindowSubclass(
                _hwnd, _subclassDelegate, SubclassId, IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"SetWindowSubclass failed for '{_window}' (Win32 err {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
        }
        _registered = true;
    }

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        bool traceFrame = false;   // set for a coalesced gesture frame, traced after the chain

        switch (uMsg)
        {
            case NativeMethods.WM_ENTERSIZEMOVE:
                _gesture.EnterSizeMove();
                if (IsWindowingDetailEnabled()) _gestureClock.Restart();
                else _gestureClock.Reset();
                _lastFrameMs = 0;
                _onResizeStarted?.Invoke();
                break;

            case NativeMethods.WM_EXITSIZEMOVE:
                Settle(_gesture.ExitSizeMove());
                break;

            case NativeMethods.WM_SIZE:
                // Ignore minimize (client area collapses to 0×0): no recompute is
                // due, and letting it through would settle a phantom 0-size layout.
                if (wParam.ToInt32() != NativeMethods.SIZE_MINIMIZED)
                {
                    ResizeSettlement? settlement = _gesture.Size();
                    if (settlement is null)
                        traceFrame = IsWindowingDetailEnabled();
                    else
                        Settle(settlement);  // direct settle (maximize / snap / programmatic)
                }
                break;
        }

        // Always chain: WinUI's own WM_SIZE handling (its layout pass) and any
        // other subclass must still run. We observe the gesture, never consume it.
        // For a coalesced gesture frame the chain call is timed, since WinUI's
        // relayout runs synchronously inside DefSubclassProc.
        if (!traceFrame)
            return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);

        long start = Stopwatch.GetTimestamp();
        IntPtr result = NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
        long relayoutMs = (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;

        long nowMs = _gestureClock.ElapsedMilliseconds;
        long sincePrevMs = nowMs - _lastFrameMs;
        _lastFrameMs = nowMs;

        int packed = lParam.ToInt32();   // WM_SIZE packs client w/h in the low 32 bits
        DeckleWindowingSource.Log.WindowResizeFrame(
            _window, _gesture.Frames, packed & 0xFFFF, (packed >> 16) & 0xFFFF,
            sincePrevMs, relayoutMs);

        return result;
    }

    private void Settle(ResizeSettlement? settlement)
    {
        if (settlement is null) return;
        var s = settlement.Value;

        bool detailEnabled = IsWindowingDetailEnabled();
        long durationMs = detailEnabled && _gestureClock.IsRunning
            ? _gestureClock.ElapsedMilliseconds
            : 0;
        _gestureClock.Reset();

        if (detailEnabled)
        {
            DeckleWindowingSource.Log.WindowResizeSettled(
                _window,
                s.Trigger == ResizeTrigger.Gesture ? "gesture" : "direct",
                s.Frames,
                durationMs);
        }

        _onResizeSettled();
    }

    private static bool IsWindowingDetailEnabled()
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing,
            DeckleWindowingSource.Log,
            System.Diagnostics.Tracing.EventLevel.Verbose,
            (System.Diagnostics.Tracing.EventKeywords)Keywords.Windowing);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered && _subclassDelegate is not null)
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
    }
}
