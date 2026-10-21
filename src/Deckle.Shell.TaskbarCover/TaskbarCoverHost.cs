using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.Foreground;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// Dedicated thread owning the cover bands, one opaque black topmost window
// per taskbar sized to its exact rect, and the whole cover state machine:
// a band fades as the cursor approaches its taskbar, a fullscreen foreground
// on a band's monitor suppresses that band, sleep and session lock park them
// all. Event driven only.
//
// The cursor WinEvent hook delivers on the thread that registered it, which
// therefore owns a message pump; the same thread owns the windows, the hooks
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
    // The anchor: a window of the band class that is never shown. It receives
    // the broadcasts (TaskbarCreated, WM_SETTINGCHANGE, WM_DISPLAYCHANGE,
    // WM_POWERBROADCAST), the session notifications and the App's posts, so
    // the bands can come and go with the taskbars while the host keeps one
    // stable mailbox. The band windows receive the same broadcasts and ignore
    // them.
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private IntPtr _brush;
    private IntPtr _cursorHook;
    private IntPtr _reorderHook;
    private uint _wmTaskbarCreated;

    // Rooted for the GC while native code holds their function pointers.
    private NativeMethods.WndProc? _wndProcDelegate;
    private WinEventProc? _cursorHookDelegate;
    private WinEventProc? _reorderHookDelegate;

    private volatile bool _running;

    // Level asked for by the App, written from its thread and read by the
    // cover thread at each layout rebuild. Kept across Stop/Start, so a
    // restart builds its zones at the last level relayed.
    private volatile ApproachDistance _requestedApproach = ApproachDistance.Standard;

    // ── State machine — worker thread only ───────────────────────────────
    // _bands           : one per taskbar the last layout found, the primary's
    //                    first; each carries its own opacity, visibility and
    //                    fullscreen suppression (CoverBand)
    // _systemSuspended : sleep or locked session — machine parked
    // _layoutKnown     : the last layout found the primary taskbar
    private readonly List<CoverBand> _bands = new();
    private bool _systemSuspended;

    private bool _layoutKnown;
    private bool _layoutFailureLogged;
    private bool _bandCreateFailureLogged;
    // Level the bands were last placed at; differs from _requestedApproach
    // until the cover thread rebuilds the layout.
    private ApproachDistance _approach = ApproachDistance.Standard;
}
