using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// Invisible native Win32 host window (HWND_MESSAGE parent) for RegisterHotKey,
// the cursor signal and the tray menus' anchor windows. WinUI 3 has no
// canonical "tray-only, no flash" window (PowerToys #11010, microsoft-ui-xaml
// #6723); a hidden XAML Window moved off-screen is a hack.
//
// Created with HWND_MESSAGE as its parent, it is invisible by construction: no
// z-order, no enumeration, no broadcast messages, just a message dispatcher.
// RegisterHotKey posts WM_HOTKEY to the thread that created it. HotkeyManager
// and CursorMovementSignal attach through SetWindowSubclass. The tray icon
// does not live here: it needs the TaskbarCreated broadcast, which a
// message-only window never receives, so TrayIconManager owns a hidden
// top-level window of its own.
//
// Created on the UI thread so WM_HOTKEY arrives on the DispatcherQueue's
// thread, invoked directly without marshaling.
//
// References:
//   learn.microsoft.com/windows/win32/winmsg/window-features#window-types
//   learn.microsoft.com/windows/win32/inputdev/about-keyboard-input#hot-key-support
public sealed class MessageOnlyHost : IDisposable
{
    private const string ClassName = "WhispMessageHost";

    private readonly IntPtr _hInstance;
    private readonly ushort _classAtom;
    private readonly IntPtr _hwnd;

    // The WndProc delegate must live in a field for the GC not to collect it
    // while native code holds its function pointer.
    private readonly NativeMethods.WndProc _wndProcDelegate;

    private bool _disposed;

    public IntPtr Hwnd => _hwnd;

    public MessageOnlyHost()
    {
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
            dwExStyle:   0,
            lpClassName: ClassName,
            lpWindowName: null,
            dwStyle:     0,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:  NativeMethods.HWND_MESSAGE,
            hMenu:       IntPtr.Zero,
            hInstance:   _hInstance,
            lpParam:     IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            throw new InvalidOperationException(
                $"CreateWindowEx(HWND_MESSAGE) failed (Win32 err {err})");
        }

        DeckleShellSource.Log.MessageOnlyHostCreated(_hwnd.ToInt64());
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            DeckleShellSource.Log.MessageOnlyHostDestroyed(_hwnd.ToInt64());
        }

        if (_classAtom != 0)
            NativeMethods.UnregisterClass(ClassName, _hInstance);
    }
}
