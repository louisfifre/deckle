using System.Runtime.InteropServices;

namespace Deckle.Input.Interop;

// Raw Input plumbing local to Deckle.Input. Deckle.Core.Interop already
// carries the registration primitives shared with the HUD's mouse
// proximity (RegisterRawInputDevices, RAWINPUTDEVICE, WM_INPUT,
// RIDEV_INPUTSINK) and the message-only window primitives; this file adds
// what only this module consumes — device enumeration, the WM_INPUT data
// read, device-change notifications, and the message pump for the
// dedicated input thread.
public static class RawInputInterop
{
    // ── HID usages — Windows Precision Touchpad collection ───────────────
    // learn.microsoft.com/windows-hardware/design/component-guidelines/
    //   touchpad-windows-precision-touchpad-collection

    public const ushort UsagePageDigitizer = 0x0D;
    public const ushort UsageTouchpad      = 0x05;

    // ── HID usages — Generic Desktop keyboard and mouse ──────────────────
    // Generic Desktop page (0x01); keyboard observation registers usage
    // 0x06, mouse-button observation usage 0x02.
    public const ushort UsagePageGeneric = 0x01;
    public const ushort UsageKeyboard    = 0x06;
    public const ushort UsageMouse       = 0x02;

    // ── Registration flags ────────────────────────────────────────────────

    // Receive WM_INPUT_DEVICE_CHANGE (GIDC_ARRIVAL / GIDC_REMOVAL) for the
    // registered usage — how the module sees the Bluetooth trackpad come
    // and go. Only safe with the standard WM_INPUT read (WndProc +
    // GetRawInputData), never with the buffered GetRawInputBuffer loop:
    // device-change messages ride the raw input queue and a range-filtered
    // PeekMessage loop would never drain them.
    public const uint RIDEV_DEVNOTIFY = 0x00002000;
    // Stop receiving input for the usage; hwndTarget must be null.
    public const uint RIDEV_REMOVE    = 0x00000001;

    public const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int  GIDC_ARRIVAL = 1;
    public const int  GIDC_REMOVAL = 2;

    // ── Device enumeration ────────────────────────────────────────────────

    public const uint RIM_TYPEMOUSE    = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID      = 2;

    // ── RAWKEYBOARD layout and flags ──────────────────────────────────────
    // learn.microsoft.com/windows/win32/api/winuser/ns-winuser-rawkeyboard
    // Read by pointer arithmetic off the RAWINPUT buffer, right after the
    // header: MakeCode (USHORT) @ +0, Flags (USHORT) @ +2, Reserved @ +4,
    // VKey (USHORT) @ +6, Message (UINT) @ +8, ExtraInformation (ULONG)
    // @ +12 — the dwExtraInfo a sender stamped via SendInput, surfaced
    // back on the receive side. Offsets are relative to RAWKEYBOARD start.
    public const int KeyboardMakeCodeOffset  = 0;
    public const int KeyboardFlagsOffset     = 2;
    public const int KeyboardVKeyOffset      = 6;
    public const int KeyboardExtraInfoOffset = 12;

    // RAWKEYBOARD.Flags bits. RI_KEY_MAKE (0) is key-down by absence of
    // RI_KEY_BREAK. RI_KEY_E0 marks the E0-prefixed (extended) scan code.
    public const ushort RI_KEY_BREAK = 1;
    public const ushort RI_KEY_E0    = 2;

    // KEYBOARD_OVERRUN_MAKE_CODE companion: VKey for a fake/overrun key.
    public const ushort VKEY_OVERRUN = 0xFF;

    // ── RAWMOUSE layout and button-down flags ─────────────────────────────
    // learn.microsoft.com/windows/win32/api/winuser/ns-winuser-rawmouse
    // RAWMOUSE = usFlags (USHORT) @ +0, then a ULONG-aligned union whose
    // usButtonFlags (USHORT) member lands @ +4 (2 bytes of padding after
    // usFlags). usButtonData @ +6. Offset is relative to RAWMOUSE start.
    public const int MouseButtonFlagsOffset = 4;

    // RAWMOUSE.usButtonFlags transition bits we treat as a button press.
    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN   = 0x0001;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN  = 0x0004;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_BUTTON_4_DOWN      = 0x0040;
    public const ushort RI_MOUSE_BUTTON_5_DOWN      = 0x0100;
    public const ushort RI_MOUSE_ANY_BUTTON_DOWN =
        RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_RIGHT_BUTTON_DOWN |
        RI_MOUSE_MIDDLE_BUTTON_DOWN | RI_MOUSE_BUTTON_4_DOWN | RI_MOUSE_BUTTON_5_DOWN;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint   dwType;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    // ── Device info ───────────────────────────────────────────────────────

    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICENAME    = 0x20000007;
    public const uint RIDI_DEVICEINFO    = 0x2000000B;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        ref RID_DEVICE_INFO pData,
        ref uint pcbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        public RID_DEVICE_INFO_HID hid; // native union sized by the keyboard member
                                        // (24 bytes); hid is the member we read (16),
                                        // padding below keeps Marshal.SizeOf at the
                                        // native 32 bytes (8 header + 24 union).
        public uint _pad0;
        public uint _pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_HID
    {
        public uint   dwVendorId;
        public uint   dwProductId;
        public uint   dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    // ── WM_INPUT data read ────────────────────────────────────────────────

    public const uint RID_INPUT = 0x10000003;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint   dwType;
        public uint   dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    // Variable-length payload: bRawData is dwCount HID reports of dwSizeHid
    // bytes each, laid out back-to-back right after the two uints. Read via
    // pointer arithmetic on the full RAWINPUT buffer, never via this struct
    // alone.
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    // ── Message pump (dedicated input thread) ─────────────────────────────

    public const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public int    ptX;
        public int    ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
