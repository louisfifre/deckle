using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// The single, process-wide source of "the mouse cursor moved". Surfaces that
// fade with cursor proximity (the HUD, its overlay cards) subscribe to Moved
// and read GetCursorPos themselves; the signal carries no position, since every
// subscriber runs on the UI thread.
//
// One owner: raw input registers once per process per HID usage, so a
// second RIDEV_INPUTSINK registration for the mouse (0x01:0x02) would
// clobber this one.
//
// The sink targets the MessageOnlyHost window: HWND_MESSAGE, never shown or
// hidden, alive from boot to quit. A surface HWND is the wrong target: the HUD
// is hidden between uses while an overlay card can still fade with proximity.
//
// The host window lives on the UI thread, so Moved is raised there. WM_INPUT
// is only a wake-up: its payload is never read, and the message goes on to
// DefSubclassProc so the system can clean up.
public sealed class CursorMovementSignal : IDisposable
{
    // "CURS" — distinct from the other window subclass ids in the process.
    private static readonly UIntPtr SubclassId = new(0x43555253);

    private readonly IntPtr _hwnd;
    private NativeMethods.SubclassProc? _subclassDelegate;
    private bool _rawInputRegistered;
    private bool _disposed;

    public event Action? Moved;

    public CursorMovementSignal(IntPtr hostHwnd)
    {
        _hwnd = hostHwnd;

        // Subclass first so the sink's WM_INPUT lands in our callback.
        _subclassDelegate = SubclassCallback;
        if (!NativeMethods.SetWindowSubclass(
                _hwnd, _subclassDelegate, SubclassId, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            DeckleShellSource.Log.CursorSignalRegistrationFailed("subclass", error);
            return;
        }

        var rid = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = 0x01, // Generic Desktop
                usUsage     = 0x02, // Mouse
                dwFlags     = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget  = _hwnd,
            },
        };
        _rawInputRegistered = NativeMethods.RegisterRawInputDevices(
            rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        if (_rawInputRegistered)
        {
            DeckleShellSource.Log.CursorSignalArmed(_hwnd.ToInt64());
        }
        else
        {
            // The sink never armed: Moved will never fire and proximity fade
            // stays off. Surface it — the failure is otherwise invisible.
            int error = Marshal.GetLastWin32Error();
            DeckleShellSource.Log.CursorSignalRegistrationFailed("raw_input", error);
        }
    }

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_INPUT)
        {
            Moved?.Invoke();
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_rawInputRegistered)
        {
            DeckleShellSource.Log.CursorSignalDisarmed(_hwnd.ToInt64());

            // RIDEV_REMOVE requires hwndTarget = NULL.
            var remove = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage     = 0x02,
                    dwFlags     = NativeMethods.RIDEV_REMOVE,
                    hwndTarget  = IntPtr.Zero,
                },
            };
            NativeMethods.RegisterRawInputDevices(
                remove, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _rawInputRegistered = false;
        }

        if (_subclassDelegate is not null)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
            _subclassDelegate = null;
        }
    }
}
