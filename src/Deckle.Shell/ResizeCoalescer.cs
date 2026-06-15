using System;
using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Shell;

// ─── Resize coalescer ────────────────────────────────────────────────────────
//
// Collapses the per-frame recompute a resizable window pays during an
// interactive edge drag into a single recompute once the gesture settles.
//
// Windows runs a modal move/size loop while the user drags a window's title bar
// or sizing border: WM_ENTERSIZEMOVE once, a burst of WM_SIZE (often faster than
// the compositor) while the pointer moves, WM_EXITSIZEMOVE once at release. WinUI
// relays out the tree and Win2D reissues its Draw on every one of those WM_SIZE —
// the visible lag. WinUI 3 exposes no native resize-begin/resize-end event
// (AppWindow.Changed only reports "size changed", and wiring its handler silently
// kills Window.SizeChanged after the first resize — microsoft-ui-xaml #6466), so
// the boundary has to be read off the Win32 messages directly.
//
// We subclass the HWND — the same comctl32 SetWindowSubclass primitive
// HotkeyManager and HudWindow already use, never SetWindowLongPtr(GWLP_WNDPROC)
// which would replace the WinUI message chain — and surface the gesture as:
//   • IsResizing — true between ENTER and EXIT. Expensive per-frame work (a Win2D
//     Draw, a SizeChanged rebuild) gates on !IsResizing and renders a cheap
//     placeholder, or nothing, while it is true.
//   • onResizeStarted — the rising edge, invoked once on ENTER. Lets a consumer
//     flip a reactive suspend flag the instant a drag begins, without polling
//     IsResizing or arming a debounce timer. Only a real gesture raises it; the
//     direct safety-net path below never does (a single settled frame needs no
//     suspend).
//   • onResizeSettled — the falling edge / recompute callback, invoked once when
//     the size settles, to clear the suspend flag and repaint crisply.
//
// Safety net: maximize, snap and programmatic SetWindowPos do NOT enter the modal
// loop, so they never raise ENTER/EXIT — they emit WM_SIZE alone. A WM_SIZE seen
// while IsResizing is false is therefore an already-settled size, and settles
// immediately. IsResizing never goes true on that path, so nothing is suppressed.
//
// Threading: the subclass runs on the host window's UI thread (its own message
// pump), so onResizeSettled fires on the UI thread — consumers touch XAML
// directly, no DispatcherQueue marshalling (same as HotkeyManager's WM_HOTKEY).
//
// Lifetime: construct, wire the callback, Register() once the HWND exists;
// Dispose() at window close removes the subclass. The SubclassProc delegate is
// kept in a field so the GC can't collect it while comctl32 holds its function
// pointer — exactly as HotkeyManager does.
public sealed class ResizeCoalescer : IDisposable
{
    private readonly IntPtr  _hwnd;
    private readonly string  _window;          // closed windowing vocabulary (DeckleWindowingSource)
    private readonly Action  _onResizeSettled;
    private readonly Action? _onResizeStarted;
    private readonly ResizeGesture _gesture = new();
    private readonly Stopwatch     _gestureClock = new();

    private NativeMethods.SubclassProc? _subclassDelegate;

    // Arbitrary id to retrieve our subclass at Remove time — "RSZC".
    private static readonly UIntPtr SubclassId = new(0x52535A43);

    private bool _registered;
    private bool _disposed;

    // True while a resize gesture is in flight. Per-frame consumers (Win2D Draw,
    // SizeChanged rebuilds) gate their expensive path on this.
    public bool IsResizing => _gesture.IsResizing;

    // `window` is the short logical name from the DeckleWindowingSource closed
    // vocabulary ("playground", "settings", "log", …). `onResizeSettled` runs the
    // window's one deferred recompute, `onResizeStarted` (optional) the rising-edge
    // suspend; both fire on the UI thread.
    public ResizeCoalescer(IntPtr hwnd, string window, Action onResizeSettled, Action? onResizeStarted = null)
    {
        _hwnd = hwnd;
        _window = window;
        _onResizeSettled = onResizeSettled;
        _onResizeStarted = onResizeStarted;
    }

    // Installs the subclass. Call once the window's HWND is valid (after
    // WindowNative.GetWindowHandle). Separate from the ctor so the wiring can be
    // set up first, mirroring HotkeyManager.Register. Idempotent.
    public void Register()
    {
        if (_registered) return;
        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);
        _registered = true;
    }

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (uMsg)
        {
            case NativeMethods.WM_ENTERSIZEMOVE:
                _gesture.EnterSizeMove();
                _gestureClock.Restart();
                _onResizeStarted?.Invoke();
                break;

            case NativeMethods.WM_EXITSIZEMOVE:
                Settle(_gesture.ExitSizeMove());
                break;

            case NativeMethods.WM_SIZE:
                // Ignore minimize (client area collapses to 0×0): no recompute is
                // due, and letting it through would settle a phantom 0-size layout.
                if (wParam.ToInt32() != NativeMethods.SIZE_MINIMIZED)
                    Settle(_gesture.Size());
                break;
        }

        // Always chain: WinUI's own WM_SIZE handling (its layout pass) and any
        // other subclass must still run. We observe the gesture, never consume it.
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // Turns a non-null settlement into the rolled-up trace and the recompute. One
    // Verbose event per settled resize — never per frame — on the cross-cutting
    // windowing provider, so a drag's coalescing is observable after the fact.
    private void Settle(ResizeSettlement? settlement)
    {
        if (settlement is null) return;
        var s = settlement.Value;

        long durationMs = _gestureClock.IsRunning ? _gestureClock.ElapsedMilliseconds : 0;
        _gestureClock.Reset();

        DeckleWindowingSource.Log.WindowResizeSettled(
            _window,
            s.Trigger == ResizeTrigger.Gesture ? "gesture" : "direct",
            s.Frames,
            durationMs);

        _onResizeSettled();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered && _subclassDelegate is not null)
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
    }
}
