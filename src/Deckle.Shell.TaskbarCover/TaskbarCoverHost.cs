using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.Foreground;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// Dedicated thread owning the cover band, an opaque black topmost window
// sized to the exact taskbar rect, and the whole cover state machine: the
// band fades as the cursor approaches, a fullscreen foreground on its monitor
// suppresses it, sleep and session lock park it. Event driven only.
//
// The cursor WinEvent hook delivers on the thread that registered it, which
// therefore owns a message pump; the same thread owns the window, the hooks
// and every state field, so the state machine runs without a lock.
public sealed partial class TaskbarCoverHost : IDisposable
{
    private const string ClassName = "DeckleTaskbarCover";

    private readonly object _stateLock = new();

    // Shared foreground snapshot, owned and started by the App on the UI
    // thread. The host never hooks the foreground itself.
    private readonly ForegroundWatcher _foreground;

    public TaskbarCoverHost(ForegroundWatcher foreground)
    {
        _foreground = foreground;
    }

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
    private uint _wmTaskbarCreated;

    // Rooted for the GC while native code holds their function pointers.
    private NativeMethods.WndProc? _wndProcDelegate;
    private WinEventProc? _cursorHookDelegate;

    private volatile bool _running;

    // Level asked for by the App, written from its thread and read by the
    // cover thread at each layout rebuild. Kept across Stop/Start, so a
    // restart builds its zone at the last level relayed.
    private volatile ApproachDistance _requestedApproach = ApproachDistance.Standard;

    // ── State machine — worker thread only ───────────────────────────────
    // _coverVisible    : current band window state (sole writer: UpdateCover)
    // _coverAlpha      : band opacity from the cursor (sole writer:
    //                    ApplyCoverAlpha); 0 = taskbar revealed, the
    //                    pessimistic boot value, corrected from the real
    //                    cursor once layout is known; 255 = fully covered
    // _appSuppressed   : a fullscreen/presentation app is foreground
    // _systemSuspended : sleep or locked session — machine parked
    private bool _coverVisible;
    private byte _coverAlpha;
    private bool _appSuppressed;
    private bool _systemSuspended;

    private bool _layoutKnown;
    private bool _layoutFailureLogged;
    private TaskbarEdge _edge;
    private NativeMethods.RECT _band;
    private NativeMethods.RECT _zone;
    // Level _zone was built at; differs from _requestedApproach until the
    // cover thread rebuilds the layout.
    private ApproachDistance _approach = ApproachDistance.Standard;
    // Monitor the band sits on, re-read on every successful layout query;
    // fullscreen suppression only applies to a window on this monitor.
    private IntPtr _bandMonitor;

}
