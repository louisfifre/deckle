using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Input;

namespace Deckle.Input;

// The process's shared keyboard-and-mouse Raw Input host: its own
// message-only window, its own GetMessage pump, registration for the
// Generic Desktop keyboard (0x01:0x06) and mouse (0x01:0x02) usages with
// RIDEV_INPUTSINK (events regardless of focus), no RIDEV_DEVNOTIFY
// (presence is irrelevant here — we observe transitions, not which device
// produced them).
//
// The mouse is a single Raw Input resource per process — only one window
// may receive it (the last one registered wins), so this host is the sole
// owner and the app shares one instance across consumers. Today two of
// them: the autocorrect engine (keys, pointer-down, focus) and the wheel
// recorder (WheelObserved). Start/Stop therefore reference-count — the
// native window and registration come up on the first consumer and go down
// on the last — so neither consumer can pull the resource from under the
// other.
//
// Separate from RawInputHost by design: that host carries the touchpad
// contact stream at report cadence feeding an injection path; this one
// observes keyboard, pointer and wheel activity. Mixing them on one window
// would couple two unrelated lifecycles. The structure (HWND_MESSAGE
// window, WndProc rooted in a field, dedicated thread, startup handshake)
// mirrors RawInputHost exactly; it reuses RawInputHost.NowMs so every event
// in the module shares one host clock.
//
// Focus signals come from two WinEvent hooks installed on this same
// thread (SetWinEventHook is WINEVENT_OUTOFCONTEXT, so its callbacks ride
// this thread's message pump). FocusChanged carries no payload — the
// consumer probes UIA itself.
//
// Events are raised on the input thread. Consumers do microseconds of
// work per event; anything heavier must marshal itself off the thread.
public sealed class KeyboardInputHost : IDisposable, IKeyboardInputHost
{
    private const string ClassName = "DeckleKeyboardHost";
    private const double RollupPeriodMs = 30_000;

    private readonly object _stateLock = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private NativeMethods.WndProc? _wndProcDelegate;          // rooted for the GC, same rule as RawInputHost
    private WinEventInterop.WinEventDelegate? _winEventDelegate; // rooted for the GC, same rule as the WndProc
    private IntPtr _foregroundHook;
    private IntPtr _focusHook;

    private IntPtr _rawBuffer;
    private int _rawBufferSize;

    // Rollup accumulators — input thread only.
    private double _rollupStartMs = -1;
    private int _rollupKeys;
    private int _rollupInjectedFiltered;
    private int _rollupPointerDowns;
    private int _rollupWheel;
    private int _rollupFocusChanges;

    // Consumers currently holding the host up. The native window and Raw
    // Input registration exist exactly while this is > 0. Guarded by
    // _stateLock, like the thread handle.
    private int _refCount;

    /// <summary>Raised on the input thread for every non-overrun keyboard transition.</summary>
    public event Action<KeyboardKeyEvent>? KeyReceived;

    /// <summary>Raised on the input thread when any mouse button transitions to down.</summary>
    public event Action? PointerInteraction;

    /// <summary>Raised on the input thread for every mouse-wheel transition (vertical or horizontal).</summary>
    public event Action<MouseWheelEvent>? WheelObserved;

    /// <summary>Raised on the input thread when the foreground window or focused element changes.</summary>
    public event Action? FocusChanged;

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
            NativeMethods.UnregisterClass(ClassName, _hInstance);
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
            TearDownWindow();
            throw new InvalidOperationException($"RegisterRawInputDevices failed (Win32 err {err})");
        }

        // Both hooks installed from this thread so their out-of-context
        // callbacks arrive on this pump. Best-effort: a failed hook leaves
        // the host running on raw input alone.
        _winEventDelegate = WinEventProc;
        uint flags = WinEventInterop.WINEVENT_OUTOFCONTEXT | WinEventInterop.WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = WinEventInterop.SetWinEventHook(
            WinEventInterop.EVENT_SYSTEM_FOREGROUND, WinEventInterop.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags);
        _focusHook = WinEventInterop.SetWinEventHook(
            WinEventInterop.EVENT_OBJECT_FOCUS, WinEventInterop.EVENT_OBJECT_FOCUS,
            IntPtr.Zero, _winEventDelegate, 0, 0, flags);

        DeckleInputSource.Log.KeyboardHostStarted(_hwnd.ToInt64(), (int)_threadId);
    }

    private void RunPump()
    {
        while (RawInputInterop.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            RawInputInterop.TranslateMessage(ref msg);
            RawInputInterop.DispatchMessage(ref msg);
        }

        // WM_QUIT — unwind everything this thread owns.
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
        NativeMethods.RegisterRawInputDevices(unregister, 2, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

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

        TearDownWindow();

        if (_rawBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBuffer = IntPtr.Zero;
            _rawBufferSize = 0;
        }
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

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_INPUT)
            HandleInput(lParam);
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void WinEventProc(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        _rollupFocusChanges++;
        FocusChanged?.Invoke();
        TrackRollup(RawInputHost.NowMs);
    }

    // ── WM_INPUT → key transitions and pointer-down signals ──────────────

    private void HandleInput(IntPtr lParam)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTHEADER>();
        if (RawInputInterop.GetRawInputData(
                lParam, RawInputInterop.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            return;

        if (_rawBuffer == IntPtr.Zero || _rawBufferSize < size)
        {
            if (_rawBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_rawBuffer);
            _rawBufferSize = (int)Math.Max(size, 256);
            _rawBuffer = Marshal.AllocHGlobal(_rawBufferSize);
        }

        if (RawInputInterop.GetRawInputData(
                lParam, RawInputInterop.RID_INPUT, _rawBuffer, ref size, headerSize) != size)
            return;

        var header = Marshal.PtrToStructure<RawInputInterop.RAWINPUTHEADER>(_rawBuffer);
        int dataOffset = (int)headerSize;

        switch (header.dwType)
        {
            case RawInputInterop.RIM_TYPEMOUSE:
                HandleMouse(dataOffset, header);
                break;

            case RawInputInterop.RIM_TYPEKEYBOARD:
                HandleKeyboard(dataOffset, header);
                break;
        }
    }

    private void HandleMouse(int dataOffset, RawInputInterop.RAWINPUTHEADER header)
    {
        // This path fires at mouse report rate. Two kinds of report earn
        // work — a wheel transition and a button-down; pure movement is the
        // common case and is dropped below.
        ushort buttonFlags = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.MouseButtonFlagsOffset);

        // Wheel reports ride the same button-flags word but set no button
        // bit; the signed detent sits in usButtonData (+6). A report carries
        // one wheel axis at a time.
        bool vertical   = (buttonFlags & RawInputInterop.RI_MOUSE_WHEEL)  != 0;
        bool horizontal = (buttonFlags & RawInputInterop.RI_MOUSE_HWHEEL) != 0;
        if (vertical || horizontal)
        {
            short delta = Marshal.ReadInt16(
                _rawBuffer, dataOffset + RawInputInterop.MouseButtonDataOffset);
            _rollupWheel++;
            WheelObserved?.Invoke(new MouseWheelEvent(
                Axis:        vertical ? WheelAxis.Vertical : WheelAxis.Horizontal,
                Delta:       delta,
                TimestampMs: RawInputHost.NowMs,
                Device:      header.hDevice));
            TrackRollup(RawInputHost.NowMs);
            return;
        }

        if ((buttonFlags & RawInputInterop.RI_MOUSE_ANY_BUTTON_DOWN) == 0) return;

        _rollupPointerDowns++;
        PointerInteraction?.Invoke();
        TrackRollup(RawInputHost.NowMs);
    }

    private void HandleKeyboard(int dataOffset, RawInputInterop.RAWINPUTHEADER header)
    {
        ushort vkey = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardVKeyOffset);
        if (vkey == RawInputInterop.VKEY_OVERRUN) return; // fake/overrun key

        ushort makeCode = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardMakeCodeOffset);
        ushort flags = (ushort)Marshal.ReadInt16(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardFlagsOffset);

        uint extraInfo = (uint)Marshal.ReadInt32(
            _rawBuffer, dataOffset + RawInputInterop.KeyboardExtraInfoOffset);

        var evt = new KeyboardKeyEvent(
            VirtualKey:  vkey,
            ScanCode:    makeCode,
            IsKeyDown:   (flags & RawInputInterop.RI_KEY_BREAK) == 0,
            IsExtended:  (flags & RawInputInterop.RI_KEY_E0) != 0,
            // SendInput-synthesized events carry no source device.
            IsInjected:  header.hDevice == IntPtr.Zero,
            TimestampMs: RawInputHost.NowMs,
            ExtraInfo:   extraInfo);

        _rollupKeys++;
        if (evt.IsInjected) _rollupInjectedFiltered++;
        KeyReceived?.Invoke(evt);
        TrackRollup(evt.TimestampMs);
    }

    private void TrackRollup(double nowMs)
    {
        if (_rollupStartMs < 0) _rollupStartMs = nowMs;

        if (nowMs - _rollupStartMs < RollupPeriodMs) return;

        DeckleInputSource.Log.KeyboardRollup(
            _rollupKeys, _rollupInjectedFiltered, _rollupPointerDowns, _rollupWheel, _rollupFocusChanges);

        _rollupStartMs = nowMs;
        _rollupKeys = 0;
        _rollupInjectedFiltered = 0;
        _rollupPointerDowns = 0;
        _rollupWheel = 0;
        _rollupFocusChanges = 0;
    }
}
