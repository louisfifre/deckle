using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // ── Injection clavier (SendInput) ─────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── Raw Input (WM_INPUT) ──────────────────────────────────────────────────
    // Event-driven approach: subscribe to global mouse movement through
    // RegisterRawInputDevices with RIDEV_INPUTSINK (receives even when the
    // window has no focus). For our proximity need, we do not need to parse
    // RAWINPUT; call GetCursorPos to get the current absolute position.

    public const uint WM_INPUT       = 0x00FF;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    // Unregisters a usage previously registered with RIDEV_INPUTSINK. The
    // matching call must pass hwndTarget = NULL.
    public const uint RIDEV_REMOVE    = 0x00000001;

    // WM_NCCALCSIZE lets us claim the entire window rect as client area.
    // Returning 0 with wParam=TRUE leaves rgrc[0] unchanged, so Windows
    // concludes there is no non-client area to paint — no caption, no
    // frame, no 3D edge — regardless of what WS_DLGFRAME / WS_EX_WINDOWEDGE
    // bits are still on the HWND. Canonical pattern for borderless custom-
    // chrome windows (used by Chromium, Electron, PowerToys).
    public const uint WM_NCCALCSIZE  = 0x0083;

    // WM_DPICHANGED is sent to a PerMonitorV2-aware top-level HWND (see
    // Deckle.App\app.manifest) when its effective DPI changes — dragged to a
    // monitor at a different scale, or the host monitor's scale changed live.
    // LOWORD(wParam) carries the new X-axis DPI (identical to the Y-axis); the
    // scale factor is that value / 96. The one event that invalidates a cached
    // window scale.
    public const uint WM_DPICHANGED  = 0x02E0;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

}
