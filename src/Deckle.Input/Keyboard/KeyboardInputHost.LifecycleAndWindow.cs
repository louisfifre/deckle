using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private const int StopTimeoutMs = 3000;

    public bool IsRunning { get { lock (_stateLock) return _refCount > 0; } }

    /// <summary>
    /// Registers a consumer. The first call spawns the input thread, creates
    /// the window, registers for keyboard and mouse raw input and installs
    /// the focus hooks; later calls just take a reference and return true.
    /// Returns false (and logs) when the native setup failed — the app keeps
    /// running without keyboard, pointer or wheel observation. Every Start
    /// that returns true must be balanced by one <see cref="Stop"/>.
    /// </summary>
    public bool Start()
    {
        lock (_stateLock)
        {
            if (_refCount > 0)
            {
                _refCount++;
                return true;
            }

            using var ready = new ManualResetEventSlim(false);
            Exception? startError = null;
            bool started = false;
            _pumpFailure = "";

            _thread = new Thread(() =>
            {
                try
                {
                    SetupWindow();
                    started = true;
                }
                catch (Exception ex)
                {
                    TearDownInputThread();
                    startError = ex;
                }
                finally
                {
                    ready.Set();
                }

                if (started) RunPump();
            })
            {
                Name = "Deckle Keyboard Input",
                IsBackground = true,
            };
            _thread.Start();
            ready.Wait();

            if (!started)
            {
                DeckleInputSource.Log.KeyboardHostStartFailed(
                    startError?.GetType().Name ?? "(unknown)",
                    startError?.Message ?? "(no message)",
                    startError is Win32Exception win32 ? win32.NativeErrorCode : 0);
                _thread = null;
                return false;
            }

            _refCount = 1;
            return true;
        }
    }

    /// <summary>
    /// Releases a consumer. The last release posts WM_QUIT to the input
    /// thread and joins it; earlier releases just drop a reference. Balanced
    /// against <see cref="Start"/>; calling it once more than Start is a no-op.
    /// </summary>
    public void Stop()
    {
        Thread? thread;
        lock (_stateLock)
        {
            if (_refCount == 0) return;
            if (--_refCount > 0) return;
            if (_thread is null) return;
            thread = _thread;
            _thread = null;
        }

        RawInputInterop.PostThreadMessage(_threadId, RawInputInterop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (!thread.Join(StopTimeoutMs))
        {
            // The thread still owns the window, the registration and the
            // hooks; the host is reported stopped because no consumer holds it.
            DeckleInputSource.Log.HostStopTimedOut(DeckleInputSource.KeyboardHost, StopTimeoutMs);
            DeckleInputSource.Log.KeyboardHostStopped("failed", "stop_timed_out");
            return;
        }

        // Join orders the input thread's last write of _pumpFailure before this read.
        string failure = _pumpFailure;
        _pumpFailure = "";
        DeckleInputSource.Log.KeyboardHostStopped(failure.Length == 0 ? "completed" : "failed", failure);
    }

    public void Dispose() => Stop();

    public void RequestDrain()
    {
        uint threadId = _threadId;
        if (threadId != 0
            && !RawInputInterop.PostThreadMessage(threadId, WM_APP_DRAIN, IntPtr.Zero, IntPtr.Zero))
            NotePostFailure();
    }

    // ── Input thread ─────────────────────────────────────────────────────

    private void SetupWindow()
    {
        _threadId = RawInputInterop.GetCurrentThreadId();

        // A drain posted to the previous run's dying thread must not open an
        // incident on this one.
        Interlocked.Exchange(ref _postFailures, 0);

        // Setup failures throw Win32Exception so Start can log the native code
        // as its own field instead of burying it in the message.
        _hInstance = NativeMethods.GetModuleHandle(null);
        if (_hInstance == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandle failed");

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
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"RegisterClassEx('{ClassName}') failed");

        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    0,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      0,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   NativeMethods.HWND_MESSAGE,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx(HWND_MESSAGE) failed");
        }

        var registration = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = RawInputInterop.UsagePageGeneric,
                usUsage     = RawInputInterop.UsageKeyboard,
                dwFlags     = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget  = _hwnd,
            },
            new RAWINPUTDEVICE
            {
                usUsagePage = RawInputInterop.UsagePageGeneric,
                usUsage     = RawInputInterop.UsageMouse,
                dwFlags     = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget  = _hwnd,
            },
        };
        if (!NativeMethods.RegisterRawInputDevices(
                registration, 2, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed");
        }
        _rawInputRegistered = true;

        // Both hooks are required: without either one the focused surface can
        // go stale and the password gate is no longer trustworthy.
        _winEventDelegate = WinEventProc;
        uint flags = WinEventInterop.WINEVENT_OUTOFCONTEXT | WinEventInterop.WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = WinEventInterop.SetWinEventHook(
            WinEventInterop.EVENT_SYSTEM_FOREGROUND, WinEventInterop.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags);
        int foregroundError = _foregroundHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        _focusHook = WinEventInterop.SetWinEventHook(
            WinEventInterop.EVENT_OBJECT_FOCUS, WinEventInterop.EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags);
        int focusError = _focusHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        if (_foregroundHook == IntPtr.Zero || _focusHook == IntPtr.Zero)
        {
            // Two hooks, two codes: the first non-zero one becomes the
            // exception's native code, both stay in the message.
            throw new Win32Exception(
                foregroundError != 0 ? foregroundError : focusError,
                $"SetWinEventHook failed (foreground_error={foregroundError}, "
                + $"focus_error={focusError})");
        }

        _mouseHookDelegate = MouseHookProc;
        _mouseHook = LowLevelMouseHookInterop.SetWindowsHookEx(
            LowLevelMouseHookInterop.WH_MOUSE_LL,
            _mouseHookDelegate,
            _hInstance,
            0);
        if (_mouseHook == IntPtr.Zero)
        {
            // Read before any other call: an EventSource write can itself
            // overwrite the thread's last error.
            int hookError = Marshal.GetLastWin32Error();
            DeckleInputSource.Log.MouseHookFailed(hookError);
        }

        StartHeartbeat();
        DeckleInputSource.Log.KeyboardHostStarted(_mouseHook != IntPtr.Zero);
        DeckleInputSource.Log.KeyboardHostStartedDetail(_hwnd.ToInt64(), (int)_threadId);
    }

    private void RunPump()
    {
        try
        {
            int result;
            while ((result = RawInputInterop.GetMessage(out var msg, IntPtr.Zero, 0, 0)) > 0)
            {
                // A bare thread message (hwnd == 0) never reaches WndProc via DispatchMessage —
                // WndProc only handles WM_INPUT — so the drain must be relayed here, in the loop body.
                if (msg.message == WM_APP_DRAIN)
                {
                    DrainRequested?.Invoke();
                    continue;
                }
                if (msg.message == WM_APP_POINTER_DOWN)
                {
                    _mouseInteractions.PublishQueuedButtonDown();
                    continue;
                }
                if (msg.message == WM_APP_WHEEL_OBSERVATION)
                {
                    PublishQueuedHookWheels();
                    continue;
                }
                RawInputInterop.TranslateMessage(ref msg);
                RawInputInterop.DispatchMessage(ref msg);
            }

            // -1 is GetMessage's error return; 0 is the WM_QUIT Stop posted.
            if (result < 0)
            {
                int err = Marshal.GetLastWin32Error();
                _pumpFailure = "get_message_failed";
                DeckleInputSource.Log.InputPumpFailed(
                    DeckleInputSource.KeyboardHost, _pumpFailure, err, "", "");
            }
        }
        catch (Exception ex)
        {
            LogPumpException(ex);
            throw;
        }

        TearDownInputThread();
    }

    // A fault inside the pump is still fatal, as before; logging it here first
    // names the host that died, which the process-wide crash handler cannot.
    // The flag keeps a fault seen in a callback from being logged again when
    // it unwinds through RunPump.
    private void LogPumpException(Exception ex)
    {
        if (_pumpFailure.Length != 0) return;
        _pumpFailure = "exception";
        DeckleInputSource.Log.InputPumpFailed(
            DeckleInputSource.KeyboardHost, _pumpFailure, 0, ex.GetType().Name, ex.Message);
    }

    private void TearDownInputThread()
    {
        FlushAllWheelObservations();
        CloseHealthIncidents();

        if (_rawInputRegistered)
        {
            var unregister = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = RawInputInterop.UsagePageGeneric,
                    usUsage     = RawInputInterop.UsageKeyboard,
                    dwFlags     = RawInputInterop.RIDEV_REMOVE,
                    hwndTarget  = IntPtr.Zero,
                },
                new RAWINPUTDEVICE
                {
                    usUsagePage = RawInputInterop.UsagePageGeneric,
                    usUsage     = RawInputInterop.UsageMouse,
                    dwFlags     = RawInputInterop.RIDEV_REMOVE,
                    hwndTarget  = IntPtr.Zero,
                },
            };
            NativeMethods.RegisterRawInputDevices(
                unregister, 2, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _rawInputRegistered = false;
        }

        if (_foregroundHook != IntPtr.Zero)
        {
            WinEventInterop.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (_focusHook != IntPtr.Zero)
        {
            WinEventInterop.UnhookWinEvent(_focusHook);
            _focusHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            LowLevelMouseHookInterop.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        TearDownWindow();

        if (_rawBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBuffer = IntPtr.Zero;
            _rawBufferSize = 0;
        }

        _threadId = 0;
    }

    private void TearDownWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_classAtom != 0)
        {
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            _classAtom = 0;
        }
    }

}
