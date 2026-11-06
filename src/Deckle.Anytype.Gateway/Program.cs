using Deckle.Anytype.Mcp;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Anytype.Gateway;

// Entry point of the Anytype gateway (ADR-0008). One instance per user session,
// started and supervised by Deckle, outliving it. The exit code tells the
// supervisor what to do next (GatewayContract.ExitCodes).
internal static class Program
{
    private static int Main()
    {
        GatewayDiagnostics.Initialize(AppPaths.DiagnosticsDirectory);
        string workId = WorkId.Next();

        using var instance = new Mutex(initiallyOwned: false, GatewayContract.InstanceMutexName);
        if (!TryOwn(instance))
        {
            DeckleAnytypeMcpSource.Log.GatewayStartDeclined("already_running");
            GatewayDiagnostics.Flush();
            return GatewayContract.ExitCodes.AlreadyRunning;
        }

        // The versions folder is named after the build's fingerprint.
        string fingerprint = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        DeckleAnytypeMcpSource.Log.GatewayStarting(Environment.ProcessId, fingerprint, workId);

        int exitCode;
        string outcome;
        try
        {
            (exitCode, outcome) = GatewayRuntime.Run();
        }
        catch (Exception ex)
        {
            DeckleAnytypeMcpSource.Log.GatewayFaulted(ex.GetType().Name, ex.Message, workId);
            (exitCode, outcome) = (GatewayContract.ExitCodes.Faulted, "faulted");
        }

        DeckleAnytypeMcpSource.Log.GatewayEnded(outcome, exitCode, workId);
        GatewayDiagnostics.Flush();
        try { instance.ReleaseMutex(); }
        catch (ApplicationException) { }
        return exitCode;
    }

    private static bool TryOwn(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // A previous gateway died holding it: ownership passed to us.
            return true;
        }
    }
}
