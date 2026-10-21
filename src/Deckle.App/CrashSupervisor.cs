using System.Diagnostics;
using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.App;

// The launcher half of Deckle's crash recovery. The process the shell starts
// stays alive as a thin parent: it starts the real Deckle as its child, waits,
// and starts it again when the child dies of a crash (CrashRelaunchPolicy says
// which exits are crashes and how many relaunches are allowed).
//
// The parent is deliberately minimal: no WinUI, no diagnostics stack, no
// settings. It can only hold up if it does almost nothing, and it has no sink
// to log to, so the relaunched child reports the crash it follows.
//
// The same parent arms the crash dump capture (CrashDumpCapture explains why
// the environment is the only lever): the dump variables and the supervision
// marker are set in its environment, which every child inherits. The child
// starts through ShellExecute like every other Deckle restart, so it keeps the
// shell-launched foreground state the HUD's topmost depends on.
internal static class CrashSupervisor
{
    // Set in the child, from the environment the parent handed it. Null when
    // this process was not started after a crash.
    internal static (uint ExitCode, int Relaunch)? FollowsCrash { get; private set; }

    // True in a child a launcher watches. False for the launcher that had to
    // run Deckle itself, a debugger session and the transactional modes: a
    // crash there ends the app, and the run identity line says so up front.
    internal static bool Supervised { get; private set; }

    // True when this process finished its work as a launcher and Main must
    // return. False when it is the Deckle that should run: a supervised child,
    // a transactional mode, a debugger session, or a launcher that could not
    // start a child.
    //
    // Hand-over at a deliberate restart (QuitApp, update, relocation) needs no
    // code of its own. A deliberate exit is 0, so this parent returns, and
    // the successor the child spawned finds no marker in its environment (the
    // child removed it at startup) and becomes the launcher of its own child.
    // The old parent never relaunches after a deliberate exit, and two parents
    // never watch the same Deckle.
    internal static bool TryRun(string[] args)
    {
        if (App.IsTransactionalMode(args)) return false;

        if (Environment.GetEnvironmentVariable(CrashRelaunchPolicy.SupervisedVariable) is not null)
        {
            BecomeSupervisedChild();
            return false;
        }

        // A debugger attached to this process would be left on the parent.
        if (Debugger.IsAttached) return false;

        string? exePath = Environment.ProcessPath;
        if (exePath is null) return false;

        return Supervise(exePath, args);
    }

    private static void BecomeSupervisedChild()
    {
        // Removed at once so no process this one starts inherits it: a restart
        // successor has to set up its own launcher.
        string? afterCrash = Environment.GetEnvironmentVariable(CrashRelaunchPolicy.AfterCrashVariable);
        Environment.SetEnvironmentVariable(CrashRelaunchPolicy.SupervisedVariable, null);
        Environment.SetEnvironmentVariable(CrashRelaunchPolicy.AfterCrashVariable, null);
        Supervised = true;

        if (CrashRelaunchPolicy.TryParseAfterCrash(afterCrash, out uint code, out int relaunch))
            FollowsCrash = (code, relaunch);
    }

    private static bool Supervise(string exePath, string[] args)
    {
        bool started = false;
        try
        {
            // Left alone when someone turned the capture off on purpose
            // (Decision.LeftOff); the supervision does not depend on it.
            if (CrashDumpCapture.Decide(Environment.GetEnvironmentVariable(CrashDumpCapture.EnableVariable))
                == CrashDumpCapture.Decision.ArmAndRelaunch)
            {
                Directory.CreateDirectory(AppPaths.CrashDumpsDirectory);
                foreach (var (name, value) in CrashDumpCapture.Variables(AppPaths.CrashDumpsDirectory))
                    Environment.SetEnvironmentVariable(name, value);
            }
            Environment.SetEnvironmentVariable(CrashRelaunchPolicy.SupervisedVariable, "1");

            var budget = new CrashRelaunchBudget();
            string? afterCrash = null;
            while (true)
            {
                Environment.SetEnvironmentVariable(CrashRelaunchPolicy.AfterCrashVariable, afterCrash);

                // A child that cannot start lands in the catch below: the
                // first one makes this process run Deckle itself rather than
                // leave the user app-less, a later one makes the launcher stop.
                using Process child = StartChild(exePath, args)
                    ?? throw new InvalidOperationException("Process.Start returned no process.");
                started = true;

                child.WaitForExit();
                int exitCode = child.ExitCode;

                if (!CrashRelaunchPolicy.IsCrash(exitCode)) return true;

                Thread.Sleep(CrashRelaunchPolicy.RelaunchDelay);

                // Checked after the pause: the flag can rise a moment after
                // the child dies of the shutdown itself.
                if (IsSessionEnding()) return true;
                if (!budget.TrySpend(DateTimeOffset.UtcNow)) return true;

                afterCrash = CrashRelaunchPolicy.FormatAfterCrash(exitCode, budget.Granted);
            }
        }
        catch
        {
            // No diagnostics sink exists in the parent. Once a child ran, the
            // launcher just stops. Before that, this process runs Deckle itself;
            // the marker and dump variables come off so it behaves like a plain
            // launch (and the startup log says capture is off).
            if (started) return true;
            Environment.SetEnvironmentVariable(CrashRelaunchPolicy.SupervisedVariable, null);
            Environment.SetEnvironmentVariable(CrashRelaunchPolicy.AfterCrashVariable, null);
            foreach (var name in CrashDumpCapture.Variables(AppPaths.CrashDumpsDirectory).Keys)
                Environment.SetEnvironmentVariable(name, null);
            return false;
        }
    }

    private static Process? StartChild(string exePath, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        return Process.Start(psi);
    }

    // GetSystemMetrics(SM_SHUTTINGDOWN) is nonzero while the current session
    // is shutting down (documented on Learn). A child that dies in a shutdown
    // is not a crash to answer with a new process.
    private static bool IsSessionEnding() => GetSystemMetrics(SmShuttingDown) != 0;

    private const int SmShuttingDown = 0x2000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
