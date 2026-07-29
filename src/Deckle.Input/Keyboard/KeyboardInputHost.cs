using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

// The process's shared keyboard-and-mouse Raw Input host: its own
// message-only window, its own GetMessage pump, registration for the
// Generic Desktop keyboard (0x01:0x06) and mouse (0x01:0x02) usages with
// RIDEV_INPUTSINK (events regardless of focus), plus a WH_MOUSE_LL hook for
// button and wheel messages that Windows may synthesize from Precision Touchpad
// interactions instead of surfacing as raw mouse reports. No
// RIDEV_DEVNOTIFY (presence is irrelevant here — we observe transitions,
// not which device produced them).
//
// The mouse is a single Raw Input resource per process — only one window
// may receive it (the last one registered wins), so this host is the sole
// owner and the app shares one instance across consumers: autocorrect
// (keys, pointer-down, focus), wheel recording (WheelObserved), and precision
// scrolling (the synchronous wheel policy). Hook and Raw Input wheel reports
// are correlated on this thread so observers receive one device-aware event;
// hook-only transitions remain visible after a short bounded wait. Start/Stop
// therefore reference-count — the native window and registration come up on
// the first consumer and go down on the last — so neither consumer can pull
// the resource from under the other.
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
public sealed partial class KeyboardInputHost : IDisposable, IKeyboardInputHost
{
    private const string ClassName = "DeckleKeyboardHost";
    private const double RollupPeriodMs = 30_000;

    // Bare thread messages (hwnd == 0) relay private work through the pump. WM_APP
    // is reserved for application messages, so these never collide with Windows.
    private const uint WM_APP_DRAIN = 0x8000; // WM_APP
    private const uint WM_APP_POINTER_DOWN = 0x8001;
    private const uint WM_APP_WHEEL_OBSERVATION = 0x8002;

    private readonly object _stateLock = new();
    private readonly MouseInteractionRouter _mouseInteractions;
    private readonly WheelEventQueue _hookWheelEvents = new();
    private readonly WheelObservationBuffer _wheelObservations = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private NativeMethods.WndProc? _wndProcDelegate;          // rooted for the GC, same rule as RawInputHost
    private WinEventInterop.WinEventDelegate? _winEventDelegate; // rooted for the GC, same rule as the WndProc
    private LowLevelMouseHookInterop.LowLevelMouseProc? _mouseHookDelegate;
    private IntPtr _foregroundHook;
    private IntPtr _focusHook;
    private IntPtr _mouseHook;
    private IWheelInterceptor? _wheelInterceptor;
    private bool _wheelObservationTimerScheduled;
    private readonly FocusEventCoalescer _focusEvents = new();

    private IntPtr _rawBuffer;
    private int _rawBufferSize;
    private bool _rawInputRegistered;

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

    public KeyboardInputHost()
    {
        _mouseInteractions = new MouseInteractionRouter(
            QueuePointerInteraction,
            PublishPointerInteraction,
            PublishWheelInteraction);
    }

    /// <summary>Raised on the input thread for every non-overrun keyboard transition.</summary>
    public event Action<KeyboardKeyEvent>? KeyReceived;

    /// <summary>Raised on the input thread when any mouse or touchpad button transitions to down.</summary>
    public event Action? PointerInteraction;

    /// <summary>Raised on the input thread for every mouse-wheel transition (vertical or horizontal).</summary>
    public event Action<MouseWheelEvent>? WheelObserved;

    /// <summary>Raised on the input thread when the foreground window or focused element changes.</summary>
    public event Action? FocusChanged;

    /// <summary>Raised on the input thread when a drain request reaches the pump (see <see cref="RequestDrain"/>).</summary>
    public event Action? DrainRequested;

    public void SetWheelInterceptor(IWheelInterceptor? interceptor) =>
        Volatile.Write(ref _wheelInterceptor, interceptor);

}
