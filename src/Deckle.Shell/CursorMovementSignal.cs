using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// The single, process-wide source of "the mouse cursor moved". Surfaces that
// fade with cursor proximity (the HUD, its overlay cards) subscribe to Moved;
// each reads the cursor position itself and computes its own distance. The
// signal carries no position payload — GetCursorPos is the source of truth and
// every subscriber runs on the UI thread, so a shared point would buy nothing.
//
// Why one owner. RIDEV_INPUTSINK is registered once per process per HID usage;
// a second registration for the mouse usage (0x01:0x02) clobbers the first.
// Before this type the HUD owned the sink on its own HWND, and the overlay
// cards — unable to register a second one — fell back to a 60 Hz GetCursorPos
// poll. Centralizing the sink here lets every surface share the one
// registration: the poll is gone, and a single place knows how the cursor
// signal is produced.
//
// Why the MessageOnlyHost HWND. The sink must target a window that lives for
// the whole process and is never hidden. The HUD HWND is hidden between uses
// (SW_HIDE), yet an overlay card can fade with proximity while the HUD is
// hidden — so the HUD HWND is the wrong target. The MessageOnlyHost window is
// HWND_MESSAGE: invisible by construction, never shown or hidden, alive from
// boot to quit. WM_INPUT therefore arrives regardless of any surface's
// visibility. The touchpad's RawInputHost proves an HWND_MESSAGE window is a
// valid RIDEV_INPUTSINK target; this is the same pattern on the UI thread.
//
// Threading. The host window lives on the UI thread, so Moved is raised there
// and subscribers touch their windows without marshaling — identical to the
// former HUD subclass. WM_INPUT is only a wake-up: its payload is never read,
// the message is forwarded to DefSubclassProc so the system can clean up.
public sealed class CursorMovementSignal : IDisposable
{
    // "CURS" — distinct from the other window subclass ids in the process.
    private static readonly UIntPtr SubclassId = new(0x43555253);

    private readonly IntPtr _hwnd;
    private NativeMethods.SubclassProc? _subclassDelegate;
    private bool _rawInputRegistered;
    private bool _disposed;

    // Raised on the UI thread on every mouse move (WM_INPUT). Subscribers read
    // the current cursor position via GetCursorPos; the event carries no data.
    public event Action? Moved;

    public CursorMovementSignal(IntPtr hostHwnd)
    {
        _hwnd = hostHwnd;

        // Subclass first so the sink's WM_INPUT lands in our callback.
        _subclassDelegate = SubclassCallback;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero);

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
            DeckleShellSource.Log.CursorSignalRegistrationFailed();
            DeckleShellSource.Log.CursorSignalRegistrationFailedDetail(Marshal.GetLastWin32Error());
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
