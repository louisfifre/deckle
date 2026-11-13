using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Operational-log settings, persisted by LoggingSettingsService: producer-side
// admission plus the trace.jsonl opt-in; the trace's retention is fixed
// (TraceRetentionPolicy). The LogWindow view
// filter is a separate, process-lifetime selection and never belongs here.
//
// One level model, the same for every activity (2026-11-13). An activity
// always leaves its run story and its summary, the heartbeat, rollup or
// per-take figures, at Summary; Detailed logs adds its per-step detail on top,
// source by source. There is no Off: a summary costs one line per period and
// is what says an activity runs, and a reader who does not want it hides it
// with the LogWindow's Heartbeat filter without destroying it. The levels
// Off/Summary/Details had a different meaning on each card and could not be
// read without a description.
public sealed class LoggingSettings
{
    // Optional disk mirror of admitted operational observations. Its path is
    // fixed by the Logging module at diagnostics/trace.jsonl; telemetry settings
    // and storage overrides never redirect it.
    public bool ApplicationLogToDisk { get; set; } = false;

    // The one switch for everything a developer turns on to chase a bug: each
    // activity's per-step detail and the diagnostic capture that hangs under
    // it (the microphone level, whose own toggle lives in the telemetry
    // store). Off, none of them runs, whatever its own toggle says.
    public bool DetailedLogs { get; set; } = false;

    // Each source's share of Detailed logs. On by default, so turning the
    // master on gives every source's detail at once, and a reader narrows it
    // from there.
    public bool AmbientDetails       { get; set; } = true;
    public bool TranscriptionDetails { get; set; } = true;
    public bool AutocorrectDetails   { get; set; } = true;
    public bool InputDetails         { get; set; } = true;
    public bool WindowingDetails     { get; set; } = true;
    public bool LlmDetails           { get; set; } = true;

    public bool Details(OperationalLogActivity activity) => activity switch
    {
        OperationalLogActivity.Ambient       => AmbientDetails,
        OperationalLogActivity.Transcription => TranscriptionDetails,
        OperationalLogActivity.Autocorrect   => AutocorrectDetails,
        OperationalLogActivity.Input         => InputDetails,
        OperationalLogActivity.Windowing     => WindowingDetails,
        OperationalLogActivity.Llm           => LlmDetails,
        _                                    => false,
    };

    public OperationalLogVerbosity Verbosity(OperationalLogActivity activity)
        => DetailedLogs && Details(activity)
            ? OperationalLogVerbosity.Details
            : OperationalLogVerbosity.Summary;
}

// The trace archive's cap, decided for the person (2026-11-13): no setting.
// The rotation counts lines (8,000 a file) and a line weighs about 720 bytes
// on a real run (measured 2026-11-13: 680 to 740), so a file is about 6 MB.
// Ten archives, about 60 MB, held about one day of the maintainer's use;
// developer mode keeps 170, about 1 GB, about three weeks, for whoever chases
// a bug across days. The cap is read at each roll, so leaving developer mode
// trims the archive at the next one.
public static class TraceRetentionPolicy
{
    public const int LinesPerFile = 8000;

    public const int NormalArchives = 10;
    public const int DeveloperArchives = 170;

    // Archived files kept beside the active one.
    public static int MaxArchives(bool developerMode)
        => developerMode ? DeveloperArchives : NormalArchives;
}
