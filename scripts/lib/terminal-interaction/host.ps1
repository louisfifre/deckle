# Windows terminal host: capability probing, reversible session, and physical input.

$script:TerminalHostBridgeAvailable = $false

function Test-TerminalWindows {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Initialize-TerminalHostBridge {
    if ($script:TerminalHostBridgeAvailable) { return $true }
    if (-not (Test-TerminalWindows)) { return $false }

    try {
        if (-not ('DeckleTerminalHostSession' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public enum DeckleTerminalInputKind
{
    Key,
    Wheel,
    Resize
}

public sealed class DeckleTerminalInputEvent
{
    public DeckleTerminalInputKind Kind { get; set; }
    public ConsoleKeyInfo KeyInfo { get; set; }
    public int WheelDelta { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct DeckleTerminalCoord
{
    internal short X;
    internal short Y;
}

[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
internal struct DeckleTerminalKeyEvent
{
    [FieldOffset(0)] internal int KeyDown;
    [FieldOffset(4)] internal ushort RepeatCount;
    [FieldOffset(6)] internal ushort VirtualKeyCode;
    [FieldOffset(8)] internal ushort VirtualScanCode;
    [FieldOffset(10)] internal char UnicodeChar;
    [FieldOffset(12)] internal uint ControlKeyState;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DeckleTerminalMouseEvent
{
    internal DeckleTerminalCoord MousePosition;
    internal uint ButtonState;
    internal uint ControlKeyState;
    internal uint EventFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DeckleTerminalWindowBufferSizeEvent
{
    internal DeckleTerminalCoord Size;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DeckleTerminalInputRecord
{
    [FieldOffset(0)] internal ushort EventType;
    [FieldOffset(4)] internal DeckleTerminalKeyEvent KeyEvent;
    [FieldOffset(4)] internal DeckleTerminalMouseEvent MouseEvent;
    [FieldOffset(4)] internal DeckleTerminalWindowBufferSizeEvent WindowBufferSizeEvent;
}

public sealed class DeckleTerminalHostSession : IDisposable
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const ushort KeyEvent = 0x0001;
    private const ushort MouseEvent = 0x0002;
    private const ushort WindowBufferSizeEvent = 0x0004;
    private const uint MouseWheeled = 0x0004;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;

    private readonly IntPtr input;
    private readonly IntPtr output;
    private readonly uint originalInputMode;
    private readonly uint originalOutputMode;
    private readonly bool inputConfigured;
    private readonly bool outputConfigured;
    private bool disposed;

    public bool PointerInputSupported { get { return inputConfigured; } }
    public bool ResizeInputSupported { get { return inputConfigured; } }
    public bool VirtualTerminalSupported { get { return outputConfigured; } }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInputW(
        IntPtr consoleInput,
        [Out] DeckleTerminalInputRecord[] buffer,
        uint bufferLength,
        out uint eventsRead);

    public DeckleTerminalHostSession()
    {
        input = GetStdHandle(StdInputHandle);
        output = GetStdHandle(StdOutputHandle);

        uint inputMode;
        if (IsHandleValid(input) && GetConsoleMode(input, out inputMode))
        {
            originalInputMode = inputMode;
            uint requested = inputMode | EnableExtendedFlags | EnableMouseInput | EnableWindowInput;
            requested &= ~EnableQuickEditMode;
            requested &= ~EnableProcessedInput;
            inputConfigured = SetConsoleMode(input, requested);
        }

        uint outputMode;
        if (IsHandleValid(output) && GetConsoleMode(output, out outputMode))
        {
            originalOutputMode = outputMode;
            outputConfigured = SetConsoleMode(output, outputMode | EnableVirtualTerminalProcessing);
        }
    }

    private static bool IsHandleValid(IntPtr handle)
    {
        return handle != IntPtr.Zero && handle != new IntPtr(-1);
    }

    public DeckleTerminalInputEvent Read()
    {
        if (!inputConfigured)
        {
            throw new InvalidOperationException("Console input records are unavailable.");
        }

        DeckleTerminalInputRecord[] records = new DeckleTerminalInputRecord[1];
        while (true)
        {
            uint eventsRead;
            if (!ReadConsoleInputW(input, records, 1, out eventsRead))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (eventsRead == 0) { continue; }

            DeckleTerminalInputRecord record = records[0];
            if (record.EventType == KeyEvent && record.KeyEvent.KeyDown != 0)
            {
                uint state = record.KeyEvent.ControlKeyState;
                bool shift = (state & ShiftPressed) != 0;
                bool alt = (state & (LeftAltPressed | RightAltPressed)) != 0;
                bool control = (state & (LeftCtrlPressed | RightCtrlPressed)) != 0;
                return new DeckleTerminalInputEvent
                {
                    Kind = DeckleTerminalInputKind.Key,
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
                return new DeckleTerminalInputEvent
                {
                    Kind = DeckleTerminalInputKind.Wheel,
                    WheelDelta = delta,
                    X = record.MouseEvent.MousePosition.X,
                    Y = record.MouseEvent.MousePosition.Y
                };
            }

            if (record.EventType == WindowBufferSizeEvent)
            {
                return new DeckleTerminalInputEvent
                {
                    Kind = DeckleTerminalInputKind.Resize,
                    Width = record.WindowBufferSizeEvent.Size.X,
                    Height = record.WindowBufferSizeEvent.Size.Y
                };
            }
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (inputConfigured) { SetConsoleMode(input, originalInputMode); }
        if (outputConfigured) { SetConsoleMode(output, originalOutputMode); }
    }
}
'@ | Out-Null
        }
        $script:TerminalHostBridgeAvailable = $true
    } catch {
        $script:TerminalHostBridgeAvailable = $false
    }
    return $script:TerminalHostBridgeAvailable
}

function Write-TerminalHostSequence {
    param([Parameter(Mandatory)][string]$Sequence)

    if ([Console]::IsOutputRedirected) { return }
    [Console]::Write($Sequence)
}

function Start-TerminalHost {
    [CmdletBinding()]
    param()

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        throw 'Interactive terminal input and output are required. Use snapshot mode when output is redirected.'
    }

    $state = [pscustomobject][ordered]@{
        OriginalForeground = [Console]::ForegroundColor
        OriginalBackground = [Console]::BackgroundColor
        OriginalCursorVisible = $true
        OriginalTreatControlCAsInput = [Console]::TreatControlCAsInput
        OriginalOutputEncoding = [Console]::OutputEncoding
        NativeSession = $null
        UsesAlternateBuffer = $false
        UsesReadKeyFallback = $false
        UsesUtf8Output = $false
        PointerInput = 'Unsupported'
        ResizeInput = 'Unsupported'
        VirtualTerminal = 'Unsupported'
        CursorAddressing = 'Unknown'
        UnicodeOutput = 'Unknown'
        Color = 'Supported'
    }
    try { $state.OriginalCursorVisible = [Console]::CursorVisible } catch { }

    try {
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        if ([Console]::OutputEncoding.CodePage -eq 65001) {
            $state.UsesUtf8Output = $true
            $state.UnicodeOutput = 'Supported'
        } else {
            $state.UnicodeOutput = 'Unsupported'
        }
    } catch {
        $state.UnicodeOutput = 'Unsupported'
    }

    if (Initialize-TerminalHostBridge) {
        try {
            $state.NativeSession = [DeckleTerminalHostSession]::new()
            if ($state.NativeSession.PointerInputSupported) { $state.PointerInput = 'Supported' }
            if ($state.NativeSession.ResizeInputSupported) { $state.ResizeInput = 'Supported' }
            if ($state.NativeSession.VirtualTerminalSupported) { $state.VirtualTerminal = 'Supported' }
        } catch {
            $state.NativeSession = $null
        }
    }

    if ($null -eq $state.NativeSession -or -not $state.NativeSession.PointerInputSupported) {
        $state.UsesReadKeyFallback = $true
        [Console]::TreatControlCAsInput = $true
    }

    if ($state.VirtualTerminal -eq 'Supported') {
        $state.CursorAddressing = 'Supported'
    } else {
        try {
            $left = [Console]::CursorLeft
            $top = [Console]::CursorTop
            [Console]::SetCursorPosition($left, $top)
            $state.CursorAddressing = 'Supported'
        } catch {
            $state.CursorAddressing = 'Unsupported'
        }
    }
    if ($state.CursorAddressing -ne 'Supported') {
        if ($null -ne $state.NativeSession) { $state.NativeSession.Dispose() }
        if ($state.UsesReadKeyFallback) {
            [Console]::TreatControlCAsInput = $state.OriginalTreatControlCAsInput
        }
        if ($state.UsesUtf8Output) {
            [Console]::OutputEncoding = $state.OriginalOutputEncoding
        }
        throw 'This terminal cannot position the preview. Use snapshot mode or a terminal with cursor addressing.'
    }

    if ($state.VirtualTerminal -eq 'Supported') {
        $escape = [char]27
        Write-TerminalHostSequence "$escape[?1049h$escape[?25l"
        $state.UsesAlternateBuffer = $true
    } else {
        [Console]::Clear()
        try { [Console]::CursorVisible = $false } catch { }
    }
    return $state
}

function Stop-TerminalHost {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$State)

    try {
        [Console]::ForegroundColor = $State.OriginalForeground
        [Console]::BackgroundColor = $State.OriginalBackground
        if ($State.UsesAlternateBuffer) {
            $escape = [char]27
            Write-TerminalHostSequence "$escape[0m$escape[?25h$escape[?1049l"
        } else {
            try { [Console]::CursorVisible = $State.OriginalCursorVisible } catch { }
        }
    } finally {
        if ($null -ne $State.NativeSession) { $State.NativeSession.Dispose() }
        if ($State.UsesReadKeyFallback) {
            [Console]::TreatControlCAsInput = $State.OriginalTreatControlCAsInput
        }
        if ($State.UsesUtf8Output) {
            [Console]::OutputEncoding = $State.OriginalOutputEncoding
        }
        [Console]::ForegroundColor = $State.OriginalForeground
        [Console]::BackgroundColor = $State.OriginalBackground
    }
}

function Get-TerminalHostMetrics {
    [CmdletBinding()]
    param()

    return [pscustomobject]@{
        Width = [Math]::Max(20, [Console]::WindowWidth)
        Height = [Math]::Max(8, [Console]::WindowHeight)
    }
}

function Read-TerminalHostEvent {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$State)

    if ($null -ne $State.NativeSession -and $State.NativeSession.PointerInputSupported) {
        return $State.NativeSession.Read()
    }
    return [pscustomobject]@{
        Kind = 'Key'
        KeyInfo = [Console]::ReadKey($true)
        WheelDelta = 0
        X = -1
        Y = -1
    }
}
