using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using WinRT;

namespace Deckle.Vision;

// The Win32 and WinRT entry points WindowSnapshot needs: the capture item for
// a window handle, and what the window manager says about a window (shown,
// minimized, cloaked, its title, the windows of this process).
internal static class WindowSnapshotInterop
{
    // ── GraphicsCaptureItem for an HWND ──────────────────────────────────────
    //
    // GraphicsCaptureItem has no managed constructor from a window handle: the
    // factory's IGraphicsCaptureItemInterop.CreateForWindow is the documented
    // route for a desktop app. The factory is fetched with RoGetActivationFactory
    // and called through its vtable, as ScreenCaptureInterop calls D3D11, never
    // through a [ComImport] interface that would clash with the CsWinRT
    // projection (see ScreenCaptureInterop.Direct3D).

    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("combase.dll", PreserveSig = false, ExactSpelling = true)]
    private static extern void RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [DllImport("combase.dll", PreserveSig = false, ExactSpelling = true)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint hstring);

    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(className, className.Length, out nint classId);
        try
        {
            RoGetActivationFactory(classId, in IID_IGraphicsCaptureItemInterop, out nint interop);
            try
            {
                unsafe
                {
                    // vtable: IUnknown (3 slots), then CreateForWindow (slot 3)
                    // and CreateForMonitor (slot 4).
                    var vtbl = *(nint**)interop;
                    var createForWindow = (delegate* unmanaged<nint, nint, Guid*, nint*, int>)vtbl[3];
                    Guid iid = IID_IGraphicsCaptureItem;
                    nint itemPtr;
                    Marshal.ThrowExceptionForHR(createForWindow(interop, hwnd, &iid, &itemPtr));
                    try
                    {
                        return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
                    }
                    finally
                    {
                        Marshal.Release(itemPtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }
        finally
        {
            WindowsDeleteString(classId);
        }
    }

    // ── What the window manager says about a window ──────────────────────────

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    private const int DWMWA_CLOAKED = 14;

    public static string GetTitle(nint hwnd)
    {
        var text = new StringBuilder(256);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    // A cloaked window is kept by DWM but not drawn: another virtual desktop,
    // or a window WinUI hides by cloaking it. It has nothing to capture.
    public static bool IsCloaked(nint hwnd)
        => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    // The top-level windows of this process that the user can see: shown, not
    // minimized, not cloaked. Hidden helper windows (the tray's message window,
    // a HUD between two takes) are left out, since they show nothing.
    public static IReadOnlyList<nint> VisibleWindowsOfThisProcess()
    {
        uint self = (uint)Environment.ProcessId;
        var found = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self && IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsCloaked(hwnd))
                found.Add(hwnd);
            return true;
        }, 0);
        return found;
    }
}
