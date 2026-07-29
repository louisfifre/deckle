using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
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
                DeckleInputSource.Log.KeyboardHostStartFailed();
                DeckleInputSource.Log.KeyboardHostStartFailedDetail(
                    startError?.GetType().Name ?? "(unknown)", startError?.Message ?? "(no message)");
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
        thread.Join(TimeSpan.FromSeconds(3));
        DeckleInputSource.Log.KeyboardHostStopped();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Posts a drain request to the input thread. Safe from any thread — PostThreadMessage is
    /// the documented cross-thread primitive. A no-op before the pump's thread id is known or
    /// after the thread has quit (the message is simply never retrieved).
    /// </summary>
    public void RequestDrain()
    {
        uint threadId = _threadId;
        if (threadId != 0)
            RawInputInterop.PostThreadMessage(threadId, WM_APP_DRAIN, IntPtr.Zero, IntPtr.Zero);
    }

    // ── Input thread ─────────────────────────────────────────────────────

    private void SetupWindow()
    {
        _threadId = RawInputInterop.GetCurrentThreadId();

        _hInstance = NativeMethods.GetModuleHandle(null);
        if (_hInstance == IntPtr.Zero)
            throw new InvalidOperationException(
                $"GetModuleHandle failed (Win32 err {Marshal.GetLastWin32Error()})");

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
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"CreateWindowEx(HWND_MESSAGE) failed (Win32 err {err})");
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
            int err = Marshal.GetLastWin32Error();
            DeckleInputSource.Log.RegistrationFailed();
            DeckleInputSource.Log.RegistrationFailedDetail(err);
            throw new InvalidOperationException($"RegisterRawInputDevices failed (Win32 err {err})");
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
            throw new InvalidOperationException(
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
            DeckleInputSource.Log.MouseWheelHookFailed();
            DeckleInputSource.Log.MouseWheelHookFailedDetail(Marshal.GetLastWin32Error());
        }

        DeckleInputSource.Log.KeyboardHostStarted(_hwnd.ToInt64(), (int)_threadId);
    }

    private void RunPump()
    {
        while (RawInputInterop.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
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

        // WM_QUIT — unwind everything this thread owns.
        TearDownInputThread();
    }

    private void TearDownInputThread()
    {
        FlushAllWheelObservations();

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
