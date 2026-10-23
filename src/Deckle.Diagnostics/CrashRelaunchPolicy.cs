namespace Deckle.Diagnostics;

// When a supervising launcher relaunches Deckle after its child dies. The
// launcher (Deckle.App's CrashSupervisor) owns the process part; this class
// holds the rules, so they stay testable without spawning anything.
//
// Why a parent process and not RegisterApplicationRestart: Windows only
// restarts a process that lived at least 60 seconds, goes through a Windows
// Error Reporting dialog, and does not document a CLR fatal error as a case it
// covers. A parent has none of those limits.
public static class CrashRelaunchPolicy
{
    // Set by the launcher in the environment of the child it starts. A process
    // that finds it is the supervised one; it removes it at once (see
    // CrashSupervisor), so whatever it spawns later, a restart, an update, is
    // not mistaken for a supervised child and becomes a launcher of its own.
    public const string SupervisedVariable = "DECKLE_SUPERVISED";

    // Carried to a relaunched child so it can log that it follows a crash:
    // the previous exit code in hex, then the relaunch ordinal, "C0000005:2".
    public const string AfterCrashVariable = "DECKLE_AFTER_CRASH";

    // At most this many relaunches inside the window, then the launcher gives
    // up. A crash at startup (a corrupt settings file, a driver that faults at
    // load) would otherwise loop for ever, burning CPU and filling the dump
    // folder. Three tries cover an unlucky one-off; a fourth crash in ten
    // minutes is not luck, and a person has to look.
    public const int MaxRelaunches = 3;
    public static readonly TimeSpan RelaunchWindow = TimeSpan.FromMinutes(10);

    // A pause before the relaunch lets Windows Error Reporting and the
    // runtime's dump writer finish and lets a session that is ending raise its
    // shutdown flag, which the launcher checks after this pause.
    public static readonly TimeSpan RelaunchDelay = TimeSpan.FromSeconds(2);

    // True when the exit code reads as a failure status: the high bit set, as
    // read from the exit code as an unsigned 32-bit value. Windows documents
    // that a process killed by an unhandled exception exits with that
    // exception's value, and the fatal ones all have the high bit set:
    // 0xC0000005 access violation, 0xC0000409 stack buffer overrun, 0xE0434352
    // unhandled managed exception, 0x80131506 CLR execution engine error.
    //
    // Zero and every small code are deliberate exits: Deckle's own quit and
    // restarts exit with 0. TerminateProcess leaves whatever code its caller
    // chose, and Windows does not document the one Task Manager's End task
    // picks; it is a small number in practice, so a person ending Deckle does
    // not trigger a relaunch. That last point is observed, not documented.
    //
    // 0xFFFFFFFF (-1) is not a crash either. It has the high bit, but it is the
    // code .NET's Process.Kill passes to TerminateProcess, so it is what
    // Stop-Process -Force and every script that kills Deckle through the
    // runtime leave behind: a deliberate end, which must not spend relaunch
    // budget. No fatal exception uses that value. (That Process.Kill exits
    // with -1 is the runtime's behavior as known, not stated in this repository.)
    public static bool IsCrash(int exitCode)
        => exitCode != -1 && ((uint)exitCode & 0x80000000u) != 0;

    public static string FormatAfterCrash(int exitCode, int relaunchOrdinal)
        => $"{(uint)exitCode:X8}:{relaunchOrdinal}";

    public static bool TryParseAfterCrash(string? value, out uint exitCode, out int relaunchOrdinal)
    {
        exitCode = 0;
        relaunchOrdinal = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] parts = value.Split(':');
        return parts.Length == 2
            && uint.TryParse(parts[0], System.Globalization.NumberStyles.AllowHexSpecifier,
                             System.Globalization.CultureInfo.InvariantCulture, out exitCode)
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out relaunchOrdinal);
    }
}
