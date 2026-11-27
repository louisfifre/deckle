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
            // The crash net: a faded taskbar outlives a Deckle that dies.
            AppDomain.CurrentDomain.UnhandledException += OnProcessEnding;
            AppDomain.CurrentDomain.ProcessExit += OnProcessEnding;
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
        bool joined = thread.Join(TimeSpan.FromSeconds(3));

        // The worker's TearDown restored the taskbars; a stuck worker did not,
        // so they are given back from here, from the list it last published.
        if (!joined) OnProcessEnding(null, EventArgs.Empty);
        AppDomain.CurrentDomain.UnhandledException -= OnProcessEnding;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessEnding;

        if (!joined)
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

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = _hInstance,
            lpszClassName = ClassName,
        };

        _classAtom = NativeMethods.RegisterClassEx(ref wc);
        if (_classAtom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx('{ClassName}') failed (Win32 err {Marshal.GetLastWin32Error()})");

        // The anchor is a window that is never shown; the bands are created by
        // the layout, one per taskbar found, and draw nothing.
        if (!TryCreateAnchorWindow(out _hwnd))
            throw new InvalidOperationException(
                $"CreateWindowEx failed (Win32 err {Marshal.GetLastWin32Error()})");

        // Without the cursor signal the taskbars would fade and never come
        // back — a dead hook fails the whole start.
        _cursorHookDelegate = OnLocationChange;
        _cursorHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _cursorHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_cursorHook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) failed (Win32 err {Marshal.GetLastWin32Error()})");
        }

        // Without it a switch of virtual desktop would leave the strips as
        // they were until another window moves: not fatal.
        _cloakHook = SetWinEventHook(
            EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED,
            IntPtr.Zero, _cursorHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        // The strips read their apps' edges by window capture; Windows draws
        // a yellow border round a captured window unless this was granted.
        _ = Deckle.Vision.WindowEdgeReader.RequestBorderlessAsync();

        if (!WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION))
            DeckleShellTaskbarCoverSource.Log.SessionNotifyFailed(Marshal.GetLastWin32Error());

        DeckleShellTaskbarCoverSource.Log.HostStarted();
        DeckleShellTaskbarCoverSource.Log.HostStartedDetail(_hwnd.ToInt64(), (int)_threadId);
    }

    // A hidden 0×0 popup: it only receives the broadcasts and the posts.
    // NOACTIVATE + TOOLWINDOW: it never takes focus nor appears in Alt-Tab.
    private bool TryCreateAnchorWindow(out IntPtr hwnd)
    {
        hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        return hwnd != IntPtr.Zero;
    }
}
