using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// Writes the end of a Windows session (logoff, shutdown, restart) into the log
// and drains the sinks, so a log that stops there says why.
//
// Why a window of its own: WM_QUERYENDSESSION and WM_ENDSESSION are broadcast
// to top-level windows only, and MessageOnlyHost is message-only, which never
// receives a broadcast. This one is a hidden WS_POPUP, like the tray's owner
// window, and sits on the UI thread's pump.
//
// It never objects: the answer to the question is always TRUE, so Deckle does
// not hold the session up. QuitApp is not involved; whether an orderly quit
// should run at a session end is a separate decision, and until then the flush
// below is what keeps the last lines.
public sealed class SessionEndWatcher : IDisposable
{
    private const string ClassName = "WhispSessionEndWatcher";

    private readonly Action _flushSinks;
    private readonly IntPtr _hInstance;
    private readonly ushort _classAtom;
    private readonly IntPtr _hwnd;

    // Held in a field so the GC does not collect it while native code holds
    // the function pointer.
    private readonly NativeMethods.WndProc _wndProcDelegate;

    private bool _disposed;

    // flushSinks drains the log writers within a deadline of its own; it runs
    // on the UI thread, inside Windows' wait for the answer.
    public SessionEndWatcher(Action flushSinks)
    {
        ArgumentNullException.ThrowIfNull(flushSinks);
        _flushSinks = flushSinks;

        _hInstance = NativeMethods.GetModuleHandle(null);
        if (_hInstance == IntPtr.Zero)
            throw new InvalidOperationException(
                $"GetModuleHandle failed (Win32 err {Marshal.GetLastWin32Error()})");

        _wndProcDelegate = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = _hInstance,
            lpszClassName = ClassName,
        };

        _classAtom = NativeMethods.RegisterClassEx(ref wc);
        if (_classAtom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx('{ClassName}') failed (Win32 err {Marshal.GetLastWin32Error()})");

        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      NativeMethods.WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            throw new InvalidOperationException(
                $"CreateWindowEx('{ClassName}') failed (Win32 err {error})");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        SessionEndSignal signal = SessionEndSignal.Read(msg, unchecked((nuint)(nint)wParam), lParam);
        switch (signal.Step)
        {
            case SessionEndStep.Asked:
                DeckleShellSource.Log.SessionEndAsked(signal.Reason, signal.Critical);
                return (IntPtr)1; // TRUE: Deckle does not hold the session up.

            case SessionEndStep.Ending:
                DeckleShellSource.Log.SessionEnding(signal.Reason, signal.Critical);
                try { _flushSinks(); }
                catch (Exception ex)
                {
                    // The window procedure must not throw into native code, and
                    // the session ends whatever the drain did.
                    DeckleShellSource.Log.SessionEndFlushFailed(ex.GetType().Name, ex.Message);
                }
                return IntPtr.Zero;

            case SessionEndStep.Cancelled:
                DeckleShellSource.Log.SessionEndCancelled(signal.Reason);
                return IntPtr.Zero;

            default:
                return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero) NativeMethods.DestroyWindow(_hwnd);
        if (_classAtom != 0) NativeMethods.UnregisterClass(ClassName, _hInstance);
    }
}
