namespace Deckle.Diagnostics;

// Crash dump capture rides on the runtime's own createdump: when
// DOTNET_DbgEnableMiniDump is in the process environment, CoreCLR writes a
// minidump on a fatal runtime error (the 0x80131506 exit that reaches no
// managed handler) and on an unhandled exception. The runtime reads the
// variable once, at startup: set from Main it is ignored, and
// runtimeconfig.json has no equivalent (verified on .NET 10.0.11 with a
// fail-fast probe). So the only lever is the environment the process starts
// in, and the host arms it by relaunching itself once when it is missing.
//
// This class holds the decision and the variables; the host owns the
// process part, so the decision stays testable without spawning anything.
public static class CrashDumpCapture
{
    public const string EnableVariable = "DOTNET_DbgEnableMiniDump";
    public const string TypeVariable   = "DOTNET_DbgMiniDumpType";
    public const string NameVariable   = "DOTNET_DbgMiniDumpName";

    // Mini: module list, thread list, exception record and every stack. It
    // places a native fault and weighs a few megabytes. Heap and Full would
    // also carry the dictation text and the unsealed vault in memory, so they
    // stay a deliberate choice, never the default.
    public enum DumpType { Mini = 1, Heap = 2, Triage = 3, Full = 4 }

    public const DumpType DefaultType = DumpType.Mini;

    // Older dumps go at startup; the newest ones are the ones worth reading.
    public const int RetainedDumps = 3;

    public const string FileExtension = ".dmp";

    // %t is the epoch second, %p the process id: both expanded by createdump.
    public const string FileNameTemplate = "Deckle-crash-%t-%p" + FileExtension;

    public enum Decision
    {
        // The variable already says 1: this process will dump on a fatal error.
        AlreadyArmed,
        // The variable is absent: set it and start a fresh process that reads it.
        ArmAndRelaunch,
        // The variable is set to anything else: someone turned capture off on
        // purpose, respect it.
        LeftOff,
    }

    public static Decision Decide(string? enableVariableValue)
    {
        if (string.IsNullOrWhiteSpace(enableVariableValue)) return Decision.ArmAndRelaunch;
        return enableVariableValue.Trim() == "1" ? Decision.AlreadyArmed : Decision.LeftOff;
    }

    // The three variables a relaunched process must find in its environment.
    public static IReadOnlyDictionary<string, string> Variables(string dumpDirectory, DumpType type = DefaultType)
        => new Dictionary<string, string>
        {
            [EnableVariable] = "1",
            [TypeVariable]   = ((int)type).ToString(),
            [NameVariable]   = Path.Combine(dumpDirectory, FileNameTemplate),
        };

    // Deletes every dump beyond the newest `keep`, and returns what remains,
    // newest first. Failures leave the file in place: a dump that resists
    // deletion is not worth failing the boot over.
    public static IReadOnlyList<FileInfo> Prune(string dumpDirectory, int keep = RetainedDumps)
    {
        if (!Directory.Exists(dumpDirectory)) return Array.Empty<FileInfo>();

        var dumps = new DirectoryInfo(dumpDirectory)
            .EnumerateFiles("*" + FileExtension)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var kept = new List<FileInfo>(keep);
        foreach (var dump in dumps)
        {
            if (kept.Count < keep) { kept.Add(dump); continue; }
            try { dump.Delete(); } catch { kept.Add(dump); }
        }
        return kept;
    }
}
