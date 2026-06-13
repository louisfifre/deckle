using System.Diagnostics;
using System.Runtime.InteropServices;
using Deckle.Core.Interop;
using Deckle.Input.Interop;

namespace Deckle.Input;

// Dedicated Raw Input thread for the touchpad: its own message-only
// window (HWND_MESSAGE parent), its own GetMessage pump, registration
// for the Precision Touchpad usage (0x0D:0x05) with RIDEV_INPUTSINK
// (frames regardless of focus) and RIDEV_DEVNOTIFY (Bluetooth arrivals
// and removals as WM_INPUT_DEVICE_CHANGE).
//
// Why not Deckle.Shell's MessageOnlyHost on the UI thread: hotkeys are
// rare events, contact frames arrive at report cadence (~100 Hz) and
// feed an injection path whose perceived quality is latency — a busy UI
// frame must not stutter a drag. The pattern (HWND_MESSAGE window, the
// WndProc delegate rooted in a field) mirrors MessageOnlyHost; only the
// thread ownership differs. Registering with hwndTarget on a message-
// only window is the documented pattern from Microsoft's "Using Raw
// Input" sample (standard WndProc read — required anyway, the buffered
// read is incompatible with RIDEV_DEVNOTIFY).
//
// Events are raised on the input thread. Consumers (recognizer, frame
// recorder) do microseconds of work per frame; anything heavier must
// marshal itself off the thread.
public sealed class RawInputHost : IDisposable
{
    private const string ClassName = "DeckleInputHost";
    private const double RollupPeriodMs = 5000;

    // Host-side monotonic clock shared by every frame timestamp.
    private static readonly Stopwatch s_clock = Stopwatch.StartNew();

    private readonly object _stateLock = new();
    private readonly Dictionary<IntPtr, (TouchpadParser Parser, ContactFrameAssembler Assembler)> _devices = new();
    private readonly HashSet<IntPtr> _failedDevices = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private NativeMethods.WndProc? _wndProcDelegate; // rooted for the GC, same rule as MessageOnlyHost

    private IntPtr _rawBuffer;
    private int _rawBufferSize;
    private byte[] _hidPayload = new byte[256];

    // Rollup accumulators — input thread only.
    private double _rollupStartMs = -1;
    private double _lastFrameMs = -1;
    private int _rollupFrames;
    private int _rollupFragmented;
    private int _rollupMaxTips;
    private double _rollupMaxGapMs;
    private long _rollupOrphans, _rollupFlushes, _rollupMismatches;

    private volatile bool _running;
    private volatile TouchpadCapabilities? _touchpad;

    /// <summary>Raised on the input thread for every assembled contact frame.</summary>
    public event Action<ContactFrame>? FrameAssembled;

    /// <summary>Raised on the input thread when a touchpad becomes available (boot probe or arrival).</summary>
    public event Action<TouchpadCapabilities>? TouchpadConnected;

    /// <summary>Raised on the input thread when the last touchpad goes away.</summary>
    public event Action? TouchpadDisconnected;

    /// <summary>Capabilities of the current touchpad, null when none is present.</summary>
    public TouchpadCapabilities? Touchpad => _touchpad;

    public bool IsRunning => _running;

    /// <summary>Current time on the host clock frames are stamped with, in milliseconds.</summary>
    public static double NowMs => s_clock.ElapsedTicks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Spawns the input thread, creates the window and registers for
    /// touchpad raw input. Returns false (and logs) when the native
    /// setup failed; the app keeps running without touchpad input.
    /// </summary>
    public bool Start()
    {
        lock (_stateLock)
        {
            if (_running) return true;

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
                Name = "Deckle Input",
                IsBackground = true,
            };
            _thread.Start();
            ready.Wait();

            if (!started)
            {
                DeckleInputSource.Log.HostStartFailed();
                DeckleInputSource.Log.HostStartFailedDetail(
                    startError?.GetType().Name ?? "(unknown)", startError?.Message ?? "(no message)");
                _thread = null;
                return false;
            }

            _running = true;
            return true;
        }
    }

    /// <summary>Posts WM_QUIT to the input thread and joins it.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_stateLock)
        {
            if (!_running || _thread is null) return;
            _running = false;
            thread = _thread;
            _thread = null;
        }

        RawInputInterop.PostThreadMessage(_threadId, RawInputInterop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(3));
        DeckleInputSource.Log.HostStopped();
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
                usUsagePage = RawInputInterop.UsagePageDigitizer,
                usUsage     = RawInputInterop.UsageTouchpad,
                dwFlags     = NativeMethods.RIDEV_INPUTSINK | RawInputInterop.RIDEV_DEVNOTIFY,
                hwndTarget  = _hwnd,
            },
        };
        if (!NativeMethods.RegisterRawInputDevices(
                registration, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            int err = Marshal.GetLastWin32Error();
            DeckleInputSource.Log.RegistrationFailed();
            DeckleInputSource.Log.RegistrationFailedDetail(err);
            TearDownWindow();
            throw new InvalidOperationException($"RegisterRawInputDevices failed (Win32 err {err})");
        }

        DeckleInputSource.Log.HostStarted(_hwnd.ToInt64(), (int)_threadId);
        ProbeExistingTouchpads();
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
                usUsagePage = RawInputInterop.UsagePageDigitizer,
                usUsage     = RawInputInterop.UsageTouchpad,
                dwFlags     = RawInputInterop.RIDEV_REMOVE,
                hwndTarget  = IntPtr.Zero,
            },
        };
        NativeMethods.RegisterRawInputDevices(unregister, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        foreach (var (parser, _) in _devices.Values) parser.Dispose();
        _devices.Clear();
        _failedDevices.Clear();
        _touchpad = null;

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
        switch (msg)
        {
            case NativeMethods.WM_INPUT:
                HandleInput(lParam);
                break;

            case RawInputInterop.WM_INPUT_DEVICE_CHANGE:
                HandleDeviceChange((int)wParam, lParam);
                break;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── Device presence ──────────────────────────────────────────────────

    private void ProbeExistingTouchpads()
    {
        uint count = 0;
        uint listEntrySize = (uint)Marshal.SizeOf<RawInputInterop.RAWINPUTDEVICELIST>();
        if (RawInputInterop.GetRawInputDeviceList(null, ref count, listEntrySize) != 0 || count == 0)
        {
            DeckleInputSource.Log.TouchpadAbsent();
            return;
        }

        var list = new RawInputInterop.RAWINPUTDEVICELIST[count];
        if (RawInputInterop.GetRawInputDeviceList(list, ref count, listEntrySize) == unchecked((uint)-1))
        {
            DeckleInputSource.Log.TouchpadAbsent();
            return;
        }

        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (list[i].dwType != RawInputInterop.RIM_TYPEHID) continue;
            if (TryAttachDevice(list[i].hDevice)) found = true;
        }

        if (!found) DeckleInputSource.Log.TouchpadAbsent();
    }

    private bool TryAttachDevice(IntPtr hDevice)
    {
        if (_devices.ContainsKey(hDevice) || _failedDevices.Contains(hDevice)) return false;

        var parser = TouchpadParser.TryCreate(hDevice, out string? failure);
        if (parser is null)
        {
            // The enumeration sees every HID device; non-touchpad
            // collections fail here by design and stay silent. Only an
            // actual touchpad page that could not be parsed is worth a
            // warning — TryCreate distinguishes via the failure text.
            if (failure is not null && !failure.StartsWith("not a touchpad collection"))
                DeckleInputSource.Log.ParserCreateFailed();
                DeckleInputSource.Log.ParserCreateFailedDetail(failure);
            _failedDevices.Add(hDevice);
            return false;
        }

        _devices[hDevice] = (parser, new ContactFrameAssembler());
        _touchpad = parser.Capabilities;

        var c = parser.Capabilities;
        DeckleInputSource.Log.TouchpadDetected();
        DeckleInputSource.Log.TouchpadDetectedDetail(
            c.DeviceName, c.VendorId, c.ProductId,
            c.XMin, c.XMax, c.YMin, c.YMax, c.ContactSlots, c.ReportByteLength);

        TouchpadConnected?.Invoke(c);
        return true;
    }

    private void HandleDeviceChange(int change, IntPtr hDevice)
    {
        switch (change)
        {
            case RawInputInterop.GIDC_ARRIVAL:
                if (TryAttachDevice(hDevice))
                    DeckleInputSource.Log.TouchpadArrived();
                break;

            case RawInputInterop.GIDC_REMOVAL:
                _failedDevices.Remove(hDevice);
                if (_devices.Remove(hDevice, out var entry))
                {
                    entry.Parser.Dispose();
                    if (_devices.Count == 0)
                    {
                        _touchpad = null;
                        DeckleInputSource.Log.TouchpadRemoved();
                        TouchpadDisconnected?.Invoke();
                    }
                }
                break;
        }
    }

    // ── WM_INPUT → contact frames ────────────────────────────────────────

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
            _rawBufferSize = (int)Math.Max(size, 1024);
            _rawBuffer = Marshal.AllocHGlobal(_rawBufferSize);
        }

        if (RawInputInterop.GetRawInputData(
                lParam, RawInputInterop.RID_INPUT, _rawBuffer, ref size, headerSize) != size)
            return;

        var header = Marshal.PtrToStructure<RawInputInterop.RAWINPUTHEADER>(_rawBuffer);
        if (header.dwType != RawInputInterop.RIM_TYPEHID) return;

        if (!_devices.TryGetValue(header.hDevice, out var device))
        {
            if (!TryAttachDevice(header.hDevice)) return;
            device = _devices[header.hDevice];
        }

        // RAWHID sits right after the header: dwSizeHid, dwCount, then the
        // dwCount × dwSizeHid report bytes.
        int hidOffset = (int)headerSize;
        int sizeHid = Marshal.ReadInt32(_rawBuffer, hidOffset);
        int reportCount = Marshal.ReadInt32(_rawBuffer, hidOffset + 4);
        int payloadBytes = sizeHid * reportCount;
        int payloadOffset = hidOffset + 8;
        if (sizeHid <= 0 || reportCount <= 0 || payloadOffset + payloadBytes > size) return;

        if (_hidPayload.Length < payloadBytes)
            _hidPayload = new byte[Math.Max(payloadBytes, _hidPayload.Length * 2)];
        Marshal.Copy(_rawBuffer + payloadOffset, _hidPayload, 0, payloadBytes);

        double now = NowMs;
        var reports = device.Parser.Parse(_hidPayload, 0, sizeHid, reportCount);
        foreach (var report in reports)
        {
            var frame = device.Assembler.Add(report, now);
            if (frame is null) continue;

            TrackRollup(frame, device.Assembler);
            FrameAssembled?.Invoke(frame);
        }
    }

    private void TrackRollup(ContactFrame frame, ContactFrameAssembler assembler)
    {
        if (_rollupStartMs < 0)
        {
            _rollupStartMs = frame.TimestampMs;
            _lastFrameMs = frame.TimestampMs;
        }

        _rollupFrames++;
        if (frame.ReportCount > 1) _rollupFragmented++;
        int tips = frame.TipCount;
        if (tips > _rollupMaxTips) _rollupMaxTips = tips;
        double gap = frame.TimestampMs - _lastFrameMs;
        if (gap > _rollupMaxGapMs) _rollupMaxGapMs = gap;
        _lastFrameMs = frame.TimestampMs;

        double elapsed = frame.TimestampMs - _rollupStartMs;
        if (elapsed < RollupPeriodMs) return;

        DeckleInputSource.Log.FrameRollup(
            _rollupFrames,
            Math.Round(_rollupFrames * 1000.0 / elapsed, 1),
            Math.Round(_rollupMaxGapMs, 1),
            _rollupMaxTips,
            _rollupFragmented,
            assembler.OrphanContinuations - _rollupOrphans,
            assembler.IncompleteFlushes - _rollupFlushes,
            assembler.ScanTimeMismatches - _rollupMismatches);

        _rollupStartMs = frame.TimestampMs;
        _rollupFrames = 0;
        _rollupFragmented = 0;
        _rollupMaxTips = 0;
        _rollupMaxGapMs = 0;
        _rollupOrphans = assembler.OrphanContinuations;
        _rollupFlushes = assembler.IncompleteFlushes;
        _rollupMismatches = assembler.ScanTimeMismatches;
    }
}
