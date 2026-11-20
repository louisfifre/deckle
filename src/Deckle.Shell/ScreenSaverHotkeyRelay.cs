using System.Runtime.InteropServices;
using static Deckle.Shell.ScreenSaverNativeMethods;

namespace Deckle.Shell;

// Holds the hotkeys on the screen saver's desktop while it shows. A thread of
// its own is placed on that desktop, since a thread that owns a window cannot
// change desktop, and a hotkey fires only for a window of the input desktop.
//
// The chord table is shared by every desktop of the session: a chord still
// held on Default is refused here with 1409. HotkeyManager releases its set
// first, and takes it back once this relay stops.
//
// A press is consumed by the hotkey, so the screen saver never sees it and
// would keep showing over the recording that just stopped. The relay then
// moves the mouse by one pixel and back, from this desktop, which is how the
// screen saver learns that the person is back (measured: Helios closed within
// half a second; a WM_CLOSE posted to its window did not close it).
// Measured too: a chord still held on Default fires for a press made over the
// screen saver, so the move matters more than the desktop the chord lives on;
// only a thread of the screen saver's desktop may move the mouse there
// (SendInput from Default: ERROR_ACCESS_DENIED).
//
// Each press reaches the owner as a posted message carrying the hotkey id, so
// the action runs on the owner's thread exactly as a WM_HOTKEY there would.
internal sealed class ScreenSaverHotkeyRelay : IDisposable
{
    // Chord ready to register: the VK is resolved by the owner, under the
    // keyboard layout of its own thread.
    public readonly record struct Chord(int Id, uint Modifiers, uint VirtualKey);

    public readonly record struct StartResult(bool Started, string FailedStep, int Win32Error,
        IReadOnlyList<HotkeyRegistrationFailure> Failures);

    private readonly IntPtr _owner;
    private readonly uint _relayMessage;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _desktop;

    public ScreenSaverHotkeyRelay(IntPtr owner, uint relayMessage)
    {
        _owner = owner;
        _relayMessage = relayMessage;
    }

    public uint ThreadId => _threadId;

    // Blocks until the thread holds its chords or gave up; a few milliseconds.
    public StartResult Start(IReadOnlyList<Chord> chords)
    {
        _desktop = OpenDesktop(HotkeyDesktopPolicy.ScreenSaverDesktop, 0, false,
            DESKTOP_READOBJECTS | DESKTOP_CREATEWINDOW | DESKTOP_WRITEOBJECTS | DESKTOP_JOURNALPLAYBACK);
        if (_desktop == IntPtr.Zero)
            return new(false, "open_desktop", Marshal.GetLastWin32Error(), []);

        StartResult result = default;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(chords, ready, r => result = r))
        {
            IsBackground = true,
            Name = "Deckle screen saver hotkeys",
        };
        _thread.Start();
        ready.Wait();

        if (!result.Started)
        {
            _thread.Join();
            _thread = null;
            CloseDesktop(_desktop);
            _desktop = IntPtr.Zero;
        }
        return result;
    }

    private void Run(IReadOnlyList<Chord> chords, ManualResetEventSlim ready, Action<StartResult> report)
    {
        if (!SetThreadDesktop(_desktop))
        {
            report(new(false, "set_thread_desktop", Marshal.GetLastWin32Error(), []));
            ready.Set();
            return;
        }

        // A plain hidden window of a system class: it only carries the
        // hotkeys, whose WM_HOTKEY is read straight off the queue below.
        IntPtr window = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            report(new(false, "create_window", Marshal.GetLastWin32Error(), []));
            ready.Set();
            return;
        }

        var held = new List<int>();
        var failures = new List<HotkeyRegistrationFailure>();
        foreach (Chord chord in chords)
        {
            if (chord.VirtualKey == 0) { failures.Add(new(chord.Id, 0)); continue; }
            if (Core.NativeMethods.RegisterHotKey(window, chord.Id, chord.Modifiers, chord.VirtualKey))
                held.Add(chord.Id);
            else
                failures.Add(new(chord.Id, Marshal.GetLastWin32Error()));
        }

        _threadId = GetCurrentThreadId();
        report(new(true, "", 0, failures));
        ready.Set();

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message != Core.NativeMethods.WM_HOTKEY) continue;
            NudgeMouse();
            PostMessage(_owner, _relayMessage, msg.wParam, IntPtr.Zero);
        }

        foreach (int id in held) Core.NativeMethods.UnregisterHotKey(window, id);
        DestroyWindow(window);
    }

    // A single move, not a move and its return: Windows builds WM_MOUSEMOVE
    // from where the cursor is when the screen saver reads its queue, so a
    // move undone in the same batch can read as no move at all (measured: a
    // +1/-1 pair left Helios showing). Ten mickeys clear the small jitter
    // threshold screen savers apply. The owner puts the cursor back once the
    // screen saver has closed, from NudgeOrigin.
    private const int NudgeMickeys = 10;

    // Where the cursor was before the first nudge; written on the relay
    // thread before the press is posted, read by the owner after Dispose.
    public Core.POINT? NudgeOrigin { get; private set; }

    private void NudgeMouse()
    {
        if (NudgeOrigin is null && Core.NativeMethods.GetCursorPos(out Core.POINT origin))
            NudgeOrigin = origin;

        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = NudgeMickeys, dwFlags = MOUSEEVENTF_MOVE } },
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            DeckleShellSource.Log.ScreenSaverDismissFailed(Marshal.GetLastWin32Error());
    }

    // Releases the chords on the relay thread and waits for it to end.
    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        if (_desktop != IntPtr.Zero) CloseDesktop(_desktop);
        _desktop = IntPtr.Zero;
    }
}
