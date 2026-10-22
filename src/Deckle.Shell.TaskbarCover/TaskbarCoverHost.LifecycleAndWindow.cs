using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    public bool IsRunning => _running;

    /// <summary>
    /// Returns false (and logs) when the native setup failed; the app keeps
    /// running without the cover.
    /// </summary>
    public bool Start()
    {
        lock (_stateLock)
        {
            if (_running) return true;

            if (_defunctThread is { } defunct)
            {
                // Join(0) succeeding means the stuck worker completed its own
                // TearDown and exited: the shared native fields are clean
                // again. Until then, restarting would corrupt them.
                if (!defunct.Join(TimeSpan.Zero))
                {
                    DeckleShellTaskbarCoverSource.Log.HostStartFailed("previous_worker_alive", "", "");
                    return false;
                }
                _defunctThread = null;
            }

            using var ready = new ManualResetEventSlim(false);
            Exception? startError = null;
            bool started = false;

            _thread = new Thread(() =>
            {
                try
                {
                    SetupWindow();
                    started = true;
                }
                catch (Exception ex)
                {
                    startError = ex;
                    TearDown();
                }
                finally
                {
                    ready.Set();
                }

                if (started)
                {
                    BootstrapState();
                    RunPump();
                    TearDown();
                }
            })
            {
                Name = "Deckle TaskbarCover",
                IsBackground = true,
            };
            _thread.Start();
            ready.Wait();

            if (!started)
            {
                DeckleShellTaskbarCoverSource.Log.HostStartFailed(
                    "setup_failed",
                    startError?.GetType().Name ?? "(unknown)", startError?.Message ?? "(no message)");
                _thread = null;
                return false;
            }

            _running = true;
            // Subscribed on the caller's thread once the anchor exists: the
            // handler only posts to it. A switch landing before this line is
            // not lost — the boot layout reads the watcher's snapshot itself.
            _foreground.Changed += OnForegroundChanged;
            // A level relayed while the worker booted found _running false and
            // posted nothing, and the boot rebuild may have read the one
            // before it. This post reconciles the two; it lands after
            // BootstrapState, once the pump runs.
            PostMessage(_hwnd, WM_APP_APPROACH, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;
        lock (_stateLock)
        {
            if (!_running || _thread is null) return;
            _running = false;
            _foreground.Changed -= OnForegroundChanged;
            thread = _thread;
            threadId = _threadId;
            _thread = null;
        }

        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (!thread.Join(TimeSpan.FromSeconds(3)))
        {
            // Stuck worker. No call on the cover thread is known to block
            // since the layout stopped querying Explorer through
            // SHAppBarMessage, but a worker that fails to exit still owns the
            // native state: the queued WM_QUIT will kill it when it next
            // pumps, and until then Start() must not reuse that state.
            lock (_stateLock) { _defunctThread = thread; }
            DeckleShellTaskbarCoverSource.Log.HostStopTimedOut();
            return;
        }
        DeckleShellTaskbarCoverSource.Log.HostStopped();
    }

    public void Dispose() => Stop();

    // ── Worker thread ─────────────────────────────────────────────────────

    private void SetupWindow()
    {
        _threadId = GetCurrentThreadId();

        _hInstance = NativeMethods.GetModuleHandle(null);
        if (_hInstance == IntPtr.Zero)
            throw new InvalidOperationException(
                $"GetModuleHandle failed (Win32 err {Marshal.GetLastWin32Error()})");

        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        _wndProcDelegate = WndProc;
        _brush = CreateSolidBrush(0x000000);

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = _hInstance,
            hbrBackground = _brush,
            lpszClassName = ClassName,
        };

        _classAtom = NativeMethods.RegisterClassEx(ref wc);
        if (_classAtom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx('{ClassName}') failed (Win32 err {Marshal.GetLastWin32Error()})");

        // The anchor is a band-class window that is never shown; the bands
        // themselves are created by the layout, one per taskbar found.
        if (!TryCreateCoverWindow(out _hwnd))
            throw new InvalidOperationException(
                $"CreateWindowEx failed (Win32 err {Marshal.GetLastWin32Error()})");

        // Without the cursor signal the bands would cover the taskbars and
        // never reveal them — a dead hook fails the whole start.
        _cursorHookDelegate = OnLocationChange;
        _cursorHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _cursorHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_cursorHook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) failed (Win32 err {Marshal.GetLastWin32Error()})");
        }

        // The desktop reorder signal catches the taskbar restacking itself
        // above the band outside every other reconciliation point. Without it
        // the cover still works, it only reconciles later — a dead hook is
        // logged, not fatal.
        _reorderHookDelegate = OnDesktopReorder;
        _reorderHook = SetWinEventHook(
            EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER,
            IntPtr.Zero, _reorderHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_reorderHook == IntPtr.Zero)
            DeckleShellTaskbarCoverSource.Log.ReorderHookFailed(Marshal.GetLastWin32Error());

        if (!WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION))
            DeckleShellTaskbarCoverSource.Log.SessionNotifyFailed(Marshal.GetLastWin32Error());

        DeckleShellTaskbarCoverSource.Log.HostStarted();
        DeckleShellTaskbarCoverSource.Log.HostStartedDetail(_hwnd.ToInt64(), (int)_threadId);
    }

    // One window of the band class, hidden and sized 0×0 until a layout
    // places it. Used for the anchor and for every band.
    //
    // NOACTIVATE + TOOLWINDOW: a band never takes focus and never appears in
    // Alt-Tab. Not click-through — a click on a band is deliberately
    // swallowed, the taskbar below stays masked.
    // TOPMOST at creation, not via a later SetWindowPos: promoting an
    // existing window to topmost needs SetForegroundWindow permission, which
    // the process lacks while another app owns the foreground (the common
    // case at boot and whenever a layout adds a band). The call succeeds yet
    // silently leaves the band below the taskbar. Born topmost, it isn't
    // subject to that gate; the SetWindowPos(HWND_TOPMOST) calls then only
    // restack an already-topmost window, which carries no permission
    // requirement.
    // LAYERED: a band's opacity follows the cursor in its approach zone
    // through SetLayeredWindowAttributes(LWA_ALPHA).
    private bool TryCreateCoverWindow(out IntPtr hwnd)
    {
        hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE
                        | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_LAYERED,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        if (hwnd == IntPtr.Zero) return false;

        // A layered window renders nothing until its attributes are set once
        // (Microsoft Learn, Window Features: layered windows).
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
        return true;
    }
}
