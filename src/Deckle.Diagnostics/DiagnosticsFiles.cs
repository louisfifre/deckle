namespace Deckle.Diagnostics;

// The names of the two operational log files under diagnostics/. The host
// writes the log, the Logging module writes the trace, and Settings names the
// trace in its consent dialog, so each name is spelled once, here.
public static class DiagnosticsFiles
{
    // Always written: every Warning and Error, every Milestone, the crash
    // details. The file a reader on another machine opens first.
    public const string Log = "log.jsonl";

    // Written only on consent: every admitted operational entry.
    public const string Trace = "trace.jsonl";
}
