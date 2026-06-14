using System.Runtime.InteropServices;

namespace Deckle.Core;

// ── POINT ─────────────────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

// ── RAWINPUTDEVICE ────────────────────────────────────────────────────────────
// Describes a source device for RegisterRawInputDevices.
// For the mouse: usUsagePage=0x01 (Generic Desktop), usUsage=0x02 (Mouse).
// dwFlags=RIDEV_INPUTSINK: receive events even without focus (hwndTarget required).
[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint   dwFlags;
    public IntPtr hwndTarget;
}

// ── NOTIFYICONDATA ────────────────────────────────────────────────────────────
//
// Shell32 structure for managing the notification area icon.
// CharSet.Unicode: szTip/szInfo fields are WCHAR[].
// cbSize must be set to Marshal.SizeOf<NOTIFYICONDATA>() before any call.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public uint   cbSize;
    public IntPtr hWnd;
    public uint   uID;
    public uint   uFlags;
    public uint   uCallbackMessage;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint   dwState;
    public uint   dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public uint   uTimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint   dwInfoFlags;
    public Guid   guidItem;
    public IntPtr hBalloonIcon;
}

// ── NOTIFYICONIDENTIFIER ──────────────────────────────────────────────────────
//
// Tray icon identifier for Shell_NotifyIconGetRect (Vista+). cbSize must be
// Marshal.SizeOf<NOTIFYICONIDENTIFIER>() before any call. An icon is addressed
// either by (hWnd, uID) or by guidItem; Deckle uses the (hWnd, uID) pair as in
// NOTIFYICONDATA.
[StructLayout(LayoutKind.Sequential)]
public struct NOTIFYICONIDENTIFIER
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID;
    public Guid guidItem;
}

// ── WNDCLASSEX ────────────────────────────────────────────────────────────────
//
// Window class descriptor passed to RegisterClassEx. cbSize must be set to
// Marshal.SizeOf<WNDCLASSEX>() before the call. lpfnWndProc is an IntPtr to a
// function pointer obtained via Marshal.GetFunctionPointerForDelegate — the
// delegate itself must be rooted in a managed field to keep it alive.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WNDCLASSEX
{
    public uint   cbSize;
    public uint   style;
    public IntPtr lpfnWndProc;
    public int    cbClsExtra;
    public int    cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string? lpszMenuName;
    public string  lpszClassName;
    public IntPtr hIconSm;
}

// Flattened INPUT: represents a keyboard event for SendInput.
//
// The Windows INPUT struct contains a C union (keyboard, mouse, hardware).
// Total size on 64-bit Windows = 40 bytes (MOUSEINPUT fixes the union size).
// The union is sized by MOUSEINPUT (the largest member).
// The _pad field at offset 32 forces Marshal.SizeOf to return 40.
[StructLayout(LayoutKind.Explicit)]
public struct INPUT
{
    [FieldOffset(0)]  public uint   type;
    [FieldOffset(8)]  public ushort ki_wVk;
    [FieldOffset(10)] public ushort ki_wScan;
    [FieldOffset(12)] public uint   ki_dwFlags;
    [FieldOffset(16)] public uint   ki_time;
    [FieldOffset(24)] public IntPtr ki_dwExtraInfo;
    [FieldOffset(32)] public long   _pad;            // padding to reach 40 bytes
}

[StructLayout(LayoutKind.Sequential)]
public struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint   nSamplesPerSec;
    public uint   nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential)]
public struct WAVEHDR
{
    public IntPtr lpData;           // pointer to audio data buffer
    public uint   dwBufferLength;   // total buffer size (bytes)
    public uint   dwBytesRecorded;  // bytes actually written by the driver
    public IntPtr dwUser;           // free user data (not used here)
    public uint   dwFlags;          // flags: WHDR_DONE = buffer filled by driver
    public uint   dwLoops;          // loop count (playback only)
    public IntPtr lpNext;           // internal driver use
    public IntPtr reserved;         // internal driver use
}

