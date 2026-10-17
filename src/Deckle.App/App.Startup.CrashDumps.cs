using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.App;

// Program armed (or found armed) the crash dump capture before any sink
// existed. Once the operational sinks are up, this says what state the
// process runs in, trims the dump folder and points at a dump an earlier run
// left behind, so the log a person reads after a crash names the file.
public partial class App
{
    private static void ReportCrashDumpCapture()
    {
        string? enabled = Environment.GetEnvironmentVariable(CrashDumpCapture.EnableVariable);
        if (CrashDumpCapture.Decide(enabled) == CrashDumpCapture.Decision.AlreadyArmed)
        {
            DeckleAppSource.Log.CrashDumpCaptureArmed();
            DeckleAppSource.Log.CrashDumpCaptureArmedDetail(
                AppPaths.CrashDumpsDirectory,
                Environment.GetEnvironmentVariable(CrashDumpCapture.TypeVariable) ?? "(unset)",
                CrashDumpCapture.RetainedDumps);
        }
        else
        {
            DeckleAppSource.Log.CrashDumpCaptureOff();
            DeckleAppSource.Log.CrashDumpCaptureOffDetail(CrashDumpCapture.EnableVariable, enabled ?? "(unset)");
        }

        var kept = CrashDumpCapture.Prune(AppPaths.CrashDumpsDirectory);
        if (kept.Count == 0) return;

        DeckleAppSource.Log.CrashDumpFound();
        foreach (var dump in kept)
            DeckleAppSource.Log.CrashDumpFoundDetail(dump.FullName, dump.Length, dump.LastWriteTime.ToString("O"));
    }
}
