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
    /// Spawns the cover thread, creates the band window, hooks the cursor.
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
                    DeckleShellTaskbarCoverSource.Log.HostStartFailed();
                    DeckleShellTaskbarCoverSource.Log.HostStartFailedDetail(
                        "DefunctWorker", "previous cover thread has not exited yet");
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
                DeckleShellTaskbarCoverSource.Log.HostStartFailed();
                DeckleShellTaskbarCoverSource.Log.HostStartFailedDetail(
                    startError?.GetType().Name ?? "(unknown)", startError?.Message ?? "(no message)");
                _thread = null;
                return false;
            }

            _running = true;
            return true;
        }
    }

    /// <summary>Posts WM_QUIT to the cover thread and joins it.</summary>
    public void Stop()
    {
        Thread? thread;
        uint threadId;
        lock (_stateLock)
        {
            if (!_running || _thread is null) return;
            _running = false;
            thread = _thread;
            threadId = _threadId;
            _thread = null;
        }

        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (!thread.Join(TimeSpan.FromSeconds(3)))
        {
            // Stuck worker — realistically SHAppBarMessage sent to a hung
            // Explorer. The queued WM_QUIT will kill it the moment the send
            // returns; until then Start() must not reuse its native state.
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

        // NOACTIVATE + TOOLWINDOW: the band never takes focus and never
        // appears in Alt-Tab. Not click-through — a click on the band is
        // deliberately swallowed, the taskbar below stays masked.
        // TOPMOST at creation, not via a later SetWindowPos: promoting an
        // existing window to topmost needs SetForegroundWindow permission,
        // which the process lacks while another app owns the foreground (the
        // common case at boot) — the call succeeds yet silently leaves the band
        // below the taskbar. Born topmost, it isn't subject to that gate; the
        // SetWindowPos(HWND_TOPMOST) calls then only restack an already-topmost
        // window, which carries no permission requirement.
        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateWindowEx failed (Win32 err {Marshal.GetLastWin32Error()})");

        // Without the cursor signal the band would cover the taskbar and
        // never reveal it — a dead hook fails the whole start.
        _cursorHookDelegate = OnLocationChange;
        _cursorHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _cursorHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_cursorHook == IntPtr.Zero)
        {
            throw new InvalidOperationException("SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) failed");
        }

        // Foreground changes drive the immediate reconciliation of fullscreen
        // suppression and z-order. Non-fatal, unlike the cursor hook: on
        // failure the 5 s poll alone still does the job, just lazily — so the
        // band never gets stuck, it just reacts slower.
        _foregroundHookDelegate = OnForegroundEvent;
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_foregroundHook == IntPtr.Zero)
            DeckleShellTaskbarCoverSource.Log.ForegroundHookFailed();

        if (!WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION))
            DeckleShellTaskbarCoverSource.Log.SessionNotifyFailed();

        DeckleShellTaskbarCoverSource.Log.HostStarted();
        DeckleShellTaskbarCoverSource.Log.HostStartedDetail(_hwnd.ToInt64(), (int)_threadId);
    }

}
