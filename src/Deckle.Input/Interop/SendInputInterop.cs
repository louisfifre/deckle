using System.Runtime.InteropServices;

namespace Deckle.Input.Interop;

// SendInput mouse plumbing. Deckle.Core.Interop carries a keyboard-
// flattened INPUT for the paste injection; the mouse variant needs the
// union's MOUSEINPUT shape instead, so it lives here with its own
// SendInput overload rather than contorting the shared struct.
public static class SendInputInterop
{
    public const uint INPUT_MOUSE = 0;

    public const uint MOUSEEVENTF_MOVE     = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP   = 0x0004;

    // Stamped into dwExtraInfo on every injected event so Deckle's own
    // synthesis is identifiable downstream (GetMessageExtraInfo) if a
    // feedback loop ever needs breaking.
    public static readonly IntPtr InjectionTag = new(0x00DECC1E);

    // Mouse-flattened INPUT: type, then the MOUSEINPUT union member.
    // Total size on 64-bit Windows = 40 bytes, same union sizing rule as
    // the keyboard variant in Deckle.Core.Interop.Structs.
    [StructLayout(LayoutKind.Explicit)]
    public struct MOUSE_INPUT
    {
        [FieldOffset(0)]  public uint   type;
        [FieldOffset(8)]  public int    mi_dx;
        [FieldOffset(12)] public int    mi_dy;
        [FieldOffset(16)] public uint   mi_mouseData;
        [FieldOffset(20)] public uint   mi_dwFlags;
        [FieldOffset(24)] public uint   mi_time;
        [FieldOffset(32)] public IntPtr mi_dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, MOUSE_INPUT[] pInputs, int cbSize);
}
