using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_INPUT)
            HandleInput(lParam);
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── WM_INPUT → key transitions and pointer-down signals ──────────────

    private void HandleInput(IntPtr lParam)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>();
        if (RawInputInterop.GetRawInputData(
                lParam, RawInputInterop.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            return;

        if (_rawBuffer == IntPtr.Zero || _rawBufferSize < size)
        {
            if (_rawBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_rawBuffer);
            _rawBufferSize = (int)Math.Max(size, 256);
            _rawBuffer = Marshal.AllocHGlobal(_rawBufferSize);
        }

        if (RawInputInterop.GetRawInputData(
                lParam, RawInputInterop.RID_INPUT, _rawBuffer, ref size, headerSize) != size)
            return;

        var header = Marshal.PtrToStructure<RawInputInterop.RAWINPUTHEADER>(_rawBuffer);
        int dataOffset = (int)headerSize;

        switch (header.dwType)
        {
            case RawInputInterop.RIM_TYPEMOUSE:
                HandleMouse(dataOffset, header);
                break;

            case RawInputInterop.RIM_TYPEKEYBOARD:
                HandleKeyboard(dataOffset, header);
                break;
        }
    }

    private void HandleMouse(int dataOffset, RawInputInterop.RAWINPUTHEADER header)
    {
        // This path fires at mouse report rate. Two kinds of report earn
        // work — a wheel transition and a button-down; pure movement is the
        // common case and is dropped below.
        ushort buttonFlags = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.MouseButtonFlagsOffset);

        // Wheel reports ride the same button-flags word but set no button
        // bit; the signed detent sits in usButtonData (+6). A report carries
        // one wheel axis at a time. When the low-level hook is installed it is
        // the single wheel source; Raw Input stays as fallback if the hook was
        // unavailable.
        bool vertical   = (buttonFlags & RawInputInterop.RI_MOUSE_WHEEL)  != 0;
        bool horizontal = (buttonFlags & RawInputInterop.RI_MOUSE_HWHEEL) != 0;
        if (vertical || horizontal)
        {
            if (_mouseHook == IntPtr.Zero)
            {
                short delta = Marshal.ReadInt16(
                    _rawBuffer, dataOffset + RawInputInterop.MouseButtonDataOffset);
                bool rawWheelRollupEnabled = IsKeyboardRollupEnabled();
                if (rawWheelRollupEnabled) _rollupWheel++;
                WheelObserved?.Invoke(new MouseWheelEvent(
                    Axis:        vertical ? WheelAxis.Vertical : WheelAxis.Horizontal,
                    Delta:       delta,
                    TimestampMs: RawInputHost.NowMs,
                    Device:      header.hDevice,
                    Source:      WheelEventSource.RawInput));
                if (rawWheelRollupEnabled) TrackRollup(RawInputHost.NowMs);
            }
            return;
        }

        if ((buttonFlags & RawInputInterop.RI_MOUSE_ANY_BUTTON_DOWN) == 0) return;

        bool rollupEnabled = IsKeyboardRollupEnabled();
        if (rollupEnabled) _rollupPointerDowns++;
        PointerInteraction?.Invoke();
        if (rollupEnabled) TrackRollup(RawInputHost.NowMs);
    }

    private void HandleKeyboard(int dataOffset, RawInputInterop.RAWINPUTHEADER header)
    {
        ushort vkey = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardVKeyOffset);
        if (vkey == RawInputInterop.VKEY_OVERRUN) return; // fake/overrun key

        ushort makeCode = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardMakeCodeOffset);
        ushort flags = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardFlagsOffset);

        uint extraInfo = (uint)Marshal.ReadInt32(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardExtraInfoOffset);

        var evt = new KeyboardKeyEvent(
            VirtualKey:  vkey,
            ScanCode:    makeCode,
            IsKeyDown:   (flags & RawInputInterop.RI_KEY_BREAK) == 0,
            IsExtended:  (flags & RawInputInterop.RI_KEY_E0) != 0,
            // SendInput-synthesized events carry no source device.
            IsInjected:  header.hDevice == IntPtr.Zero,
            TimestampMs: RawInputHost.NowMs,
            ExtraInfo:   extraInfo);

        bool rollupEnabled = IsKeyboardRollupEnabled();
        if (rollupEnabled)
        {
            _rollupKeys++;
            if (evt.IsInjected) _rollupInjectedFiltered++;
        }
        KeyReceived?.Invoke(evt);
        if (rollupEnabled) TrackRollup(evt.TimestampMs);
    }

}
