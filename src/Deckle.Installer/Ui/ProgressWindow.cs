using System.Runtime.InteropServices;

namespace Deckle.Installer;

// ── ProgressWindow ────────────────────────────────────────────────────────────
//
// The whole visible surface of the silent stub: a small fixed, centred window —
// title bar with a close button only, no resize / minimise / maximise — carrying
// one line of status text over a msctls_progress32 bar. It replaces the old console
// entirely; under WinExe there is no console to print to.
//
// Threading is the load-bearing part. Win32 requires that a window be serviced on
// the thread that created it, so the message loop runs on the main thread (the
// process entry point) while the install/uninstall work runs on a background Task.
// The worker never touches an HWND directly: it stashes the desired state under a
// lock and PostMessages WM_APP_UPDATE, and the window's own WndProc — running on
// the UI thread as it pumps — reads that state and mutates the controls. Every
// control write therefore happens on the owning thread, which is what keeps the
// cross-thread updates race- and deadlock-free. Two more app messages close the
// loop: WM_APP_DONE means "work finished, tear down"; the title-bar X arrives as
// WM_CLOSE and additionally raises Cancelled so the flow can cancel its token.
//
// All interop is source-generated ([LibraryImport]) and the WndProc is an
// [UnmanagedCallersOnly] static reached through a function pointer — the AOT-safe
// way to hand Win32 a callback (a runtime-marshalled delegate is not). There is
// exactly one window per process run, so the WndProc routes through a single static
// instance rather than threading a GCHandle through GWLP_USERDATA.
internal sealed partial class ProgressWindow
{
    // The live window the static WndProc dispatches to. One per process run.
    private static ProgressWindow? s_instance;

    // The window class is registered process-wide once; a second construction (never
    // happens today) would reuse it.
    private const string ClassName = "DeckleSetupProgress";
    private static bool s_classRegistered;

    // RegisterClassExW keeps the class-name pointer, so it must outlive the call —
    // a one-time unmanaged allocation held for the process lifetime, never freed.
    private static readonly nint s_classNamePtr = Marshal.StringToHGlobalUni(ClassName);

    private readonly nint _hwnd;
    private readonly nint _statusLabel;
    private readonly nint _progressBar;
    private readonly nint _font;

    // The state the worker thread hands to the UI thread. Guarded because the two
    // threads read and write it; applied wholesale on each WM_APP_UPDATE.
    private readonly object _sync = new();
    private string _status = string.Empty;
    private bool _wantMarquee;
    private int _percent;

    // The bar's current mode, tracked so a run of determinate updates doesn't thrash
    // the window style. Bars start determinate (range set at creation).
    private bool _barMarquee;

    // Raised on the UI thread when the user closes the window (title-bar X). The
    // install flow subscribes to cancel its CancellationTokenSource.
    public event Action? Cancelled;

    public nint Handle => _hwnd;

    public unsafe ProgressWindow(string title)
    {
        s_instance = this;
        nint instance = GetModuleHandleW(null);

        if (!s_classRegistered)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = instance,
                hCursor = LoadCursorW(nint.Zero, (nint)IDC_ARROW),
                // The classic dialog face; STATIC controls paint their own background
                // with the same system colour, so the label sits flush with no extra
                // WM_CTLCOLOR handling.
                hbrBackground = (nint)(COLOR_BTNFACE + 1),
                lpszClassName = s_classNamePtr,
            };
            RegisterClassExW(in wc);
            s_classRegistered = true;
        }

        // The progress class must be pulled in before the bar is created, or the
        // window class is unknown and CreateWindowExW returns null.
        var icc = new INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(INITCOMMONCONTROLSEX), dwICC = ICC_PROGRESS_CLASS };
        InitCommonControlsEx(in icc);

        // PerMonitorV2 is declared in the manifest, so nothing scales the pixels for
        // us: size every dimension off the system DPI at creation. Good enough for a
        // window that never moves between monitors.
        uint dpi = GetDpiForSystem();
        double scale = dpi / 96.0;
        int Scaled(int logical) => (int)Math.Round(logical * scale);

        int clientWidth = Scaled(432);
        int clientHeight = Scaled(104);
        var rect = new RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };
        AdjustWindowRectExForDpi(ref rect, WindowStyle, bMenu: false, 0, dpi);
        int windowWidth = rect.Right - rect.Left;
        int windowHeight = rect.Bottom - rect.Top;
        int left = (GetSystemMetrics(SM_CXSCREEN) - windowWidth) / 2;
        int top = (GetSystemMetrics(SM_CYSCREEN) - windowHeight) / 2;

        _hwnd = CreateWindowExW(0, ClassName, title, WindowStyle,
            left, top, windowWidth, windowHeight, nint.Zero, nint.Zero, instance, nint.Zero);

        int margin = Scaled(18);
        int labelHeight = Scaled(40);
        int barTop = margin + labelHeight + Scaled(6);
        int barHeight = Scaled(18);
        int innerWidth = clientWidth - margin * 2;

        _statusLabel = CreateWindowExW(0, "STATIC", "Starting Deckle Setup…",
            WS_CHILD | WS_VISIBLE | SS_LEFT, margin, margin, innerWidth, labelHeight,
            _hwnd, nint.Zero, instance, nint.Zero);
        _progressBar = CreateWindowExW(0, "msctls_progress32", null,
            WS_CHILD | WS_VISIBLE, margin, barTop, innerWidth, barHeight,
            _hwnd, nint.Zero, instance, nint.Zero);

        // Segoe UI at 9pt — the shell's own UI font, so the label doesn't fall back to
        // the ancient bitmap system font. Height is negative to request a character
        // (not cell) height, converted from points at the window DPI.
        int fontHeight = -(int)Math.Round(9.0 * dpi / 72.0);
        _font = CreateFontW(fontHeight, 0, 0, 0, FW_NORMAL, 0, 0, 0,
            DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI");
        SendMessageW(_statusLabel, WM_SETFONT, _font, 1);

        SendMessageW(_progressBar, PBM_SETRANGE32, 0, 100);
    }

    // Shows and raises the window. Called on the main thread before the loop.
    public void Show()
    {
        ShowWindow(_hwnd, SW_SHOWNORMAL);
        UpdateWindow(_hwnd);
        SetForegroundWindow(_hwnd);
    }

    // Pumps until WM_QUIT (posted by WM_DESTROY). Runs on the main thread and blocks
    // there for the whole install/uninstall; the worker Task drives the actual work.
    public void RunMessageLoop()
    {
        int result;
        while ((result = GetMessageW(out MSG msg, nint.Zero, 0, 0)) != 0)
        {
            if (result == -1) break; // GetMessage error — bail rather than spin
            TranslateMessage(in msg);
            DispatchMessageW(in msg);
        }
    }

    // ── Worker-thread entry points ─────────────────────────────────────────────────
    // Each stashes state and pokes the UI thread; none touches a control directly.

    public void ReportMarquee(string status)
    {
        lock (_sync) { _status = status; _wantMarquee = true; }
        PostMessageW(_hwnd, WM_APP_UPDATE, nint.Zero, nint.Zero);
    }

    public void ReportProgress(string status, long current, long total)
    {
        int percent = total > 0 ? (int)Math.Clamp(current * 100 / total, 0, 100) : 0;
        lock (_sync) { _status = status; _wantMarquee = false; _percent = percent; }
        PostMessageW(_hwnd, WM_APP_UPDATE, nint.Zero, nint.Zero);
    }

    // "The work is over" — tear the window down (and end the loop) without the
    // cancellation WM_CLOSE would signal. Posts because it may come off the worker.
    public void RequestClose() => PostMessageW(_hwnd, WM_APP_DONE, nint.Zero, nint.Zero);

    // ── UI-thread handlers ─────────────────────────────────────────────────────────

    private void OnUserClose() => Cancelled?.Invoke();

    private void ApplyState()
    {
        string status;
        bool marquee;
        int percent;
        lock (_sync) { status = _status; marquee = _wantMarquee; percent = _percent; }

        SetWindowTextW(_statusLabel, status);
        if (marquee)
        {
            SetBarMarquee(true);
        }
        else
        {
            SetBarMarquee(false);
            SendMessageW(_progressBar, PBM_SETPOS, percent, nint.Zero);
        }
    }

    // Toggles the bar between the endless marquee and a determinate fill. The style
    // bit must be flipped alongside PBM_SETMARQUEE — the message alone doesn't change
    // how a non-marquee bar draws.
    private void SetBarMarquee(bool on)
    {
        if (on == _barMarquee) return;

        nint style = GetWindowLongPtrW(_progressBar, GWL_STYLE);
        if (on)
        {
            SetWindowLongPtrW(_progressBar, GWL_STYLE, style | PBS_MARQUEE);
            SendMessageW(_progressBar, PBM_SETMARQUEE, 1, 30); // animate, 30 ms/step
        }
        else
        {
            SendMessageW(_progressBar, PBM_SETMARQUEE, nint.Zero, nint.Zero);
            SetWindowLongPtrW(_progressBar, GWL_STYLE, style & ~(nint)PBS_MARQUEE);
            SendMessageW(_progressBar, PBM_SETRANGE32, 0, 100);
        }
        _barMarquee = on;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_APP_UPDATE:
                s_instance?.ApplyState();
                return nint.Zero;
            case WM_APP_DONE:
                DestroyWindow(hwnd);
                return nint.Zero;
            case WM_CLOSE:
                s_instance?.OnUserClose();
                DestroyWindow(hwnd);
                return nint.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return nint.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ── Win32 constants ────────────────────────────────────────────────────────────

    private const uint CS_VREDRAW = 0x0001;
    private const uint CS_HREDRAW = 0x0002;

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_SYSMENU = 0x00080000;
    // Caption + system menu only: a close button, no resize / minimise / maximise.
    private const uint WindowStyle = WS_CAPTION | WS_SYSMENU;
    private const uint SS_LEFT = 0x00000000;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SETFONT = 0x0030;
    private const uint WM_APP = 0x8000;
    private const uint WM_APP_UPDATE = WM_APP + 1;
    private const uint WM_APP_DONE = WM_APP + 2;

    private const uint PBM_SETPOS = 0x0402;
    private const uint PBM_SETRANGE32 = 0x0406;
    private const uint PBM_SETMARQUEE = 0x040A;
    private const nint PBS_MARQUEE = 0x08;

    private const int GWL_STYLE = -16;
    private const int SW_SHOWNORMAL = 1;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint ICC_PROGRESS_CLASS = 0x20;
    private const int IDC_ARROW = 32512;
    private const int COLOR_BTNFACE = 15;
    private const uint FW_NORMAL = 400;
    private const uint DEFAULT_CHARSET = 1;
    private const uint CLEARTYPE_QUALITY = 5;

    // ── Interop ────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public uint dwICC;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ushort RegisterClassExW(in WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int width, int height,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int cmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowTextW(nint hWnd, string lpString);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowLongPtrW(nint hWnd, int index, nint newLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetWindowLongPtrW(nint hWnd, int index);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectExForDpi(
        ref RECT lpRect, uint dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitCommonControlsEx(in INITCOMMONCONTROLSEX picce);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(
        int height, int width, int escapement, int orientation, uint weight,
        uint italic, uint underline, uint strikeOut, uint charSet,
        uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily,
        string faceName);
}
