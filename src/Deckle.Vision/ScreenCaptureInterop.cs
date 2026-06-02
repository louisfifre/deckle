using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Deckle.Vision;

// Interop helpers for the capture pipeline (DXGI Output Duplication
// since the WGC-to-DXGI migration — see ScreenCaptureService class
// header for the rationale).
//
// What lives here :
//   1. CreateDirect3DDevice — wraps a fresh ID3D11Device as the WinRT
//      IDirect3DDevice the FrameSampler holds. Accepts an optional
//      adapter pointer (mandatory for the DXGI Duplication path, where
//      the D3D11 device must be created on the adapter driving the
//      target output).
//   2. EnumerateMonitors / FindMonitorByDeviceName / GetPrimaryMonitor
//      — HMONITOR resolution helpers shared by capture and the
//      monitor-selector UI.
//   3. GetD3D11Device / GetD3D11Texture — IDirect3DDxgiInterfaceAccess
//      QI bridge to recover the native COM objects behind WinRT
//      wrappers (used at FrameSampler init).
//   4. DetectHdrState / FindDxgiOutputForMonitor — DXGI walk that
//      matches an HMONITOR to its IDXGIAdapter + IDXGIOutput5 +
//      reported HDR state.
//   5. DXGI Output Duplication primitives (DuplicateOutput1,
//      AcquireNextFrame, ReleaseFrame, GetDuplicationDesc) and the
//      backing structs / vtable indices.
//   6. D3D11 vtable indices + textur-related structs used by
//      FrameSampler's GPU downsample path.
//
// Errors from PreserveSig=false P/Invoke surface as Marshal-thrown
// COMException with the HRESULT preserved. ScreenCaptureService
// catches and logs.
internal static partial class ScreenCaptureInterop
{
    // ── HMONITOR ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // MONITORINFOEXW — 32-byte fixed header + 32-char device name buffer.
    // LayoutKind.Sequential + CharSet.Unicode + fixed-size buffer matches
    // the C struct exactly. CbSize must be filled with sizeof(MONITORINFOEXW)
    // before the call (108 bytes including the 64-byte device-name buffer).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct MONITORINFOEXW
    {
        public int   CbSize;
        public RECT  RcMonitor;
        public RECT  RcWork;
        public uint  DwFlags;
        public fixed char SzDevice[32]; // CCHDEVICENAME = 32
    }

    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    private const uint MONITORINFOF_PRIMARY     = 0x00000001;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEXW lpmi);

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    public static nint GetPrimaryMonitor()
    {
        // (0,0) + MONITOR_DEFAULTTOPRIMARY canonically returns the primary
        // monitor regardless of taskbar orientation or workspace layout.
        return MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
    }

    /// <summary>Describes one monitor returned by <see cref="EnumerateMonitors"/>.</summary>
    /// <param name="Handle">HMONITOR (opaque to callers — pass back to capture).</param>
    /// <param name="DeviceName">Win32 device name, e.g. "\\\\.\\DISPLAY1". Stable
    /// across reboots for a given physical port + GPU. Persisted in
    /// <c>AmbientSettings.SelectedMonitorDeviceName</c>.</param>
    /// <param name="IsPrimary">True when this is the user's primary monitor.</param>
    /// <param name="X">Virtual-desktop x-origin in pixels.</param>
    /// <param name="Y">Virtual-desktop y-origin in pixels.</param>
    /// <param name="Width">Pixel width of the monitor.</param>
    /// <param name="Height">Pixel height of the monitor.</param>
    public sealed record MonitorInfo(
        nint    Handle,
        string  DeviceName,
        bool    IsPrimary,
        int     X,
        int     Y,
        int     Width,
        int     Height);

    /// <summary>
    /// Enumerates every monitor attached to the current desktop, in the
    /// order Windows reports them (typically primary first, then by
    /// GPU output index). The list is a snapshot — call again after
    /// a display configuration change to refresh. Scaffolding for the
    /// J9 monitor selector ; the V0 capture service still uses the
    /// primary unconditionally.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<MonitorInfo>(4);

        bool Callback(nint hMon, nint hdc, ref RECT rect, nint data)
        {
            var info = new MONITORINFOEXW { CbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (!GetMonitorInfoW(hMon, ref info))
            {
                // Monitor enumerated but info call failed (rare ;
                // typically a driver hiccup). Skip ; the rest of the
                // enumeration continues.
                return true;
            }

            string deviceName;
            unsafe { deviceName = new string(info.SzDevice); }

            monitors.Add(new MonitorInfo(
                Handle:     hMon,
                DeviceName: deviceName,
                IsPrimary:  (info.DwFlags & MONITORINFOF_PRIMARY) != 0,
                X:          info.RcMonitor.Left,
                Y:          info.RcMonitor.Top,
                Width:      info.RcMonitor.Right - info.RcMonitor.Left,
                Height:     info.RcMonitor.Bottom - info.RcMonitor.Top));
            return true; // keep enumerating
        }

        EnumDisplayMonitors(0, 0, Callback, 0);
        return monitors;
    }

    /// <summary>
    /// Resolves a monitor by its Win32 device name (the same string
    /// <see cref="MonitorInfo.DeviceName"/> exposes). Returns 0 if no
    /// monitor with that name is currently attached — the caller is
    /// expected to fall back to <see cref="GetPrimaryMonitor"/>.
    /// </summary>
    public static nint FindMonitorByDeviceName(string deviceName)
    {
        foreach (var m in EnumerateMonitors())
        {
            if (string.Equals(m.DeviceName, deviceName, StringComparison.Ordinal))
                return m.Handle;
        }
        return 0;
    }

}
