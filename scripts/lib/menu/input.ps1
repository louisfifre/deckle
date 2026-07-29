# Keyboard and pointer input for interactive menu surfaces.
$script:MenuNativeInputAvailable = $false

if ($IsWindows) {
    try {
        if (-not ('DeckleMenuInputReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public enum DeckleMenuInputKind
{
    Key,
    Wheel
}

public sealed class DeckleMenuQuitException : Exception
{
    public DeckleMenuQuitException() : base("Menu quit requested.") { }
}

public sealed class DeckleMenuInputEvent
{
    public DeckleMenuInputKind Kind { get; set; }
    public ConsoleKeyInfo KeyInfo { get; set; }
    public int WheelDelta { get; set; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct DeckleMenuCoord
{
    internal short X;
    internal short Y;
}

[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
internal struct DeckleMenuKeyEvent
{
    [FieldOffset(0)] internal int KeyDown;
    [FieldOffset(4)] internal ushort RepeatCount;
    [FieldOffset(6)] internal ushort VirtualKeyCode;
    [FieldOffset(8)] internal ushort VirtualScanCode;
    [FieldOffset(10)] internal char UnicodeChar;
    [FieldOffset(12)] internal uint ControlKeyState;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DeckleMenuMouseEvent
{
    internal DeckleMenuCoord MousePosition;
    internal uint ButtonState;
    internal uint ControlKeyState;
    internal uint EventFlags;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DeckleMenuInputRecord
{
    [FieldOffset(0)] internal ushort EventType;
    [FieldOffset(4)] internal DeckleMenuKeyEvent KeyEvent;
    [FieldOffset(4)] internal DeckleMenuMouseEvent MouseEvent;
}

public static class DeckleMenuInputReader
{
    private const int StdInputHandle = -10;
    private const ushort KeyEvent = 0x0001;
    private const ushort MouseEvent = 0x0002;
    private const uint MouseWheeled = 0x0004;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInputW(
        IntPtr consoleInput,
        [Out] DeckleMenuInputRecord[] buffer,
        uint bufferLength,
        out uint eventsRead);

    public static DeckleMenuInputEvent Read()
    {
        IntPtr input = GetStdHandle(StdInputHandle);
        if (input == IntPtr.Zero || input == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var records = new DeckleMenuInputRecord[1];
        while (true)
        {
            uint eventsRead;
            if (!ReadConsoleInputW(input, records, 1, out eventsRead))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (eventsRead == 0)
            {
                continue;
            }

            DeckleMenuInputRecord record = records[0];
            if (record.EventType == KeyEvent && record.KeyEvent.KeyDown != 0)
            {
                uint state = record.KeyEvent.ControlKeyState;
                bool shift = (state & ShiftPressed) != 0;
                bool alt = (state & (LeftAltPressed | RightAltPressed)) != 0;
                bool control = (state & (LeftCtrlPressed | RightCtrlPressed)) != 0;
                return new DeckleMenuInputEvent
                {
                    Kind = DeckleMenuInputKind.Key,
                    KeyInfo = new ConsoleKeyInfo(
                        record.KeyEvent.UnicodeChar,
                        (ConsoleKey)record.KeyEvent.VirtualKeyCode,
                        shift,
                        alt,
                        control)
                };
            }

            if (record.EventType == MouseEvent && (record.MouseEvent.EventFlags & MouseWheeled) != 0)
            {
                int delta = (short)((record.MouseEvent.ButtonState >> 16) & 0xffff);
                return new DeckleMenuInputEvent
                {
                    Kind = DeckleMenuInputKind.Wheel,
                    WheelDelta = delta
                };
            }
        }
    }
}
'@ | Out-Null
        }
        $script:MenuNativeInputAvailable = $true
    } catch {
        # Keyboard navigation remains available through Console.ReadKey.
        $script:MenuNativeInputAvailable = $false
    }
}

function Test-MenuPointerInputAvailable {
    return $script:MenuNativeInputAvailable
}

function Read-MenuInputEvent {
    if ($script:MenuNativeInputAvailable -and $script:MenuPointerInputDepth -gt 0) {
        try {
            return [DeckleMenuInputReader]::Read()
        } catch {
            $script:MenuNativeInputAvailable = $false
        }
    }

    return [pscustomobject]@{
        Kind = 'Key'
        KeyInfo = [Console]::ReadKey($true)
        WheelDelta = 0
    }
}

function Get-MenuWheelPageDirection {
    param([int]$Delta)

    if ($Delta -gt 0) { return 'Previous' }
    if ($Delta -lt 0) { return 'Next' }
    return 'Current'
}
