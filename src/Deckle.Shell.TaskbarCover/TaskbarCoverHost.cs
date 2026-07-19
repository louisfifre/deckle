using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// Dedicated thread owning the cover band — an opaque black topmost window
// sized to the exact taskbar rect — and the whole reveal state machine:
//
//   • the band covers the taskbar whatever edge it is anchored to
//     (ABM_GETTASKBARPOS rect, rebuilt on WM_SETTINGCHANGE /
//     WM_DISPLAYCHANGE / TaskbarCreated);
//   • the cursor entering the reveal zone hides the band instantly;
//     leaving it re-covers after RecoverDelayMs;
//   • a fullscreen / presentation foreground app suppresses the band;
//     two WinEvent signals reconcile it — and the band's z-order above the
//     taskbar — the instant it happens: a foreground change (another app
//     comes forward) and a location change on the foreground window itself
//     (an in-place F11 toggle, which raises no foreground event). No poll;
//   • sleep and session lock park the machine entirely.
//
// Cursor movement arrives through a WinEvent hook
// (EVENT_OBJECT_LOCATIONCHANGE filtered on OBJID_CURSOR), not Raw Input:
// the RIDEV_INPUTSINK mouse registration is per-process-per-usage and
// CursorMovementSignal (Deckle.Shell) owns the only one. The hook is asynchronous
// and additive — no input-chain latency, no contention. It delivers on
// the thread that registered it, which therefore owns a message pump;
// the same thread owns the window, the timers and every state field, so
// the machine runs without a single lock. Start/Stop mirror
// RawInputHost's thread lifecycle.
public sealed partial class TaskbarCoverHost : IDisposable
{
    private const string ClassName = "DeckleTaskbarCover";

    // Delay before the band re-covers the taskbar once the cursor has left
    // the reveal zone. Ported from the standalone utility (HIDE_DELAY),
    // calibrated in daily use; a constant, not a setting.
    public const uint RecoverDelayMs = 5000;

    private const uint TIMER_RECOVER_ID = 1;
    private static readonly UIntPtr TIMER_RECOVER = new(TIMER_RECOVER_ID);

    private readonly object _stateLock = new();

    private Thread? _thread;
    // Worker that outlived its Join in Stop() — it still owns the window
    // class, the delegates and the native handles, so Start() refuses to
    // run over it until it has fully exited and torn itself down.
    private Thread? _defunctThread;
    private uint _threadId;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private IntPtr _brush;
    private IntPtr _cursorHook;
    private IntPtr _foregroundHook;
    private uint _wmTaskbarCreated;

    // Rooted for the GC while native code holds their function pointers.
    private NativeMethods.WndProc? _wndProcDelegate;
    private WinEventProc? _cursorHookDelegate;
    private WinEventProc? _foregroundHookDelegate;

    private volatile bool _running;

    // ── State machine — worker thread only ───────────────────────────────
    // _coverVisible       : current band window state (sole writer: UpdateCover)
    // _cursorInRevealZone : the taskbar is revealed; pessimistic at boot,
    //                       corrected from the real cursor once layout is known
    // _recoverTimerArmed  : between zone exit and RecoverDelayMs expiry
    // _appSuppressed      : a fullscreen/presentation app is foreground
    // _systemSuspended    : sleep or locked session — machine parked
    private bool _coverVisible;
    private bool _cursorInRevealZone = true;
    private bool _recoverTimerArmed;
    private bool _appSuppressed;
    private bool _systemSuspended;

    // Last foreground window — tracked by the foreground hook so the
    // location-change hook can recognise an in-place resize of *that* window
    // (the F11 fullscreen toggle) with a pointer compare, no syscall on the
    // input-cadence path.
    private IntPtr _foregroundHwnd;

    private bool _layoutKnown;
    private bool _layoutFailureLogged;
    private bool _recoverArmFailureLogged;
    private TaskbarEdge _edge;
    private NativeMethods.RECT _band;
    private NativeMethods.RECT _zone;

}
