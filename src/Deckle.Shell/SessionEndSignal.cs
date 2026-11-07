namespace Deckle.Shell;

// What the two Windows messages of a session end mean, as a pure reading so a
// test can hold it without a window.
//
// Windows broadcasts WM_QUERYENDSESSION to every top-level window when the user
// logs off, shuts down or restarts, then WM_ENDSESSION with the verdict: wParam
// is TRUE when the session really ends, FALSE when another program (or the
// user) cancelled it. After a TRUE verdict the process can be killed as soon as
// the window procedure returns, so that is the last moment to write to disk.
//
// lParam of both messages carries the flags below; the logoff bit tells a
// logoff from a shutdown/restart (the message does not separate those two).
//
// Reference: learn.microsoft.com/windows/win32/shutdown/wm-queryendsession
//            learn.microsoft.com/windows/win32/shutdown/wm-endsession
public enum SessionEndStep
{
    // Not a session-end message.
    None,

    // Windows asks whether the session may end. Deckle never objects.
    Asked,

    // The session is ending: nothing runs after this returns.
    Ending,

    // The session end was cancelled after the question.
    Cancelled,
}

// reason (closed vocabulary): logoff | shutdown | close_app, the last for a
// restart-manager request to close the application for an update.
public readonly record struct SessionEndSignal(SessionEndStep Step, string Reason, bool Critical)
{
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION      = 0x0016;

    private const long EndSessionCloseApp = 0x00000001;
    private const long EndSessionCritical = 0x40000000;
    private const long EndSessionLogoff   = 0x80000000;

    public static SessionEndSignal Read(uint message, nuint wParam, nint lParam)
    {
        if (message != WM_QUERYENDSESSION && message != WM_ENDSESSION)
            return new SessionEndSignal(SessionEndStep.None, "", false);

        // lParam is a 32-bit flag set; read it unsigned so the logoff bit
        // (the sign bit) does not turn the value negative on a 64-bit nint.
        long flags = unchecked((uint)lParam);
        string reason = (flags & EndSessionCloseApp) != 0 ? "close_app"
            : (flags & EndSessionLogoff) != 0 ? "logoff"
            : "shutdown";
        bool critical = (flags & EndSessionCritical) != 0;

        SessionEndStep step = message == WM_QUERYENDSESSION
            ? SessionEndStep.Asked
            : wParam != 0 ? SessionEndStep.Ending : SessionEndStep.Cancelled;

        return new SessionEndSignal(step, reason, critical);
    }
}
