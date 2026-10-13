using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Deckle.Vision;

// Raw interop for the capture pipeline: monitor resolution, the D3D11
// device and its WinRT wrapper, DXGI adapter / output walks, HDR detection
// and Output Duplication.
//
// PreserveSig=false P/Invokes throw COMException with the HRESULT kept;
// ScreenCaptureService catches and logs.
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

    // CbSize must hold the struct size before the call: GetMonitorInfoW
    // reads it to tell MONITORINFOEXW from MONITORINFO.
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
        // The primary monitor's origin is always (0,0) on the virtual
        // desktop, whatever the layout.
        return MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
    }

    /// <param name="DeviceName">Win32 device name, e.g. "\\\\.\\DISPLAY1". Stable
    /// across reboots for a given physical port + GPU, which is why it is the
    /// persisted key.</param>
    /// <param name="X">Virtual-desktop x-origin in pixels.</param>
    /// <param name="Y">Virtual-desktop y-origin in pixels.</param>
    public sealed record MonitorInfo(
        nint    Handle,
        string  DeviceName,
        bool    IsPrimary,
        int     X,
        int     Y,
        int     Width,
        int     Height);

    /// <summary>
    /// A snapshot in the order Windows reports the monitors; call again
    /// after a display configuration change.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<MonitorInfo>(4);

        bool Callback(nint hMon, nint hdc, ref RECT rect, nint data)
        {
            var info = new MONITORINFOEXW { CbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (!GetMonitorInfoW(hMon, ref info))
            {
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
    /// Returns 0 when no attached monitor has that device name; the caller
    /// falls back to <see cref="GetPrimaryMonitor"/>.
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
