using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Closed activity vocabulary for producer-side operational-log admission.
// Provider boundaries do not appear here: one user workflow can span several
// emitting modules.
public enum OperationalLogActivity
{
    Ambient,
    Transcription,
    Autocorrect,
    Input,
    Windowing,
    // Calls to a language model, local or hosted: the per-call usage line.
    Llm,
}

// How much of an activity's own detail the verbosity control admits, in
// increasing order. Milestones, warnings and errors pass at every value.
//
//   Off       nothing beyond them.
//   Summary   plus one record per unit of work or per period: a take's
//             figures, a loop's heartbeat or rollup. A loop never writes one
//             line per frame at any value; a frame-by-frame series is a
//             telemetry dataset.
//   Details   plus the per-step detail: per utterance, per segment, per push,
//             per probe.
public enum OperationalLogVerbosity
{
    Off,
    Summary,
    Details,
}

// Dependency-neutral access point used by producers before log-only work.
// Logging owns the persisted policy; the composition root injects its reader
// after settings migration. Governed activities fail closed until then.
public static class OperationalLogAdmission
{
    private static Func<OperationalLogActivity, OperationalLogVerbosity>? _reader;
    private static readonly bool[] _active =
        new bool[Enum.GetValues<OperationalLogActivity>().Length];

    public static void Configure(Func<OperationalLogActivity, OperationalLogVerbosity> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Volatile.Write(ref _reader, reader);
    }

    // The on/off form the controls had before they took levels, kept for the
    // suites that only need "everything" or "nothing": true admits Details,
    // false admits nothing beyond what always passes.
    public static void Configure(Func<OperationalLogActivity, bool> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Configure(activity => reader(activity)
            ? OperationalLogVerbosity.Details
            : OperationalLogVerbosity.Off);
    }

    public static OperationalLogVerbosity Verbosity(OperationalLogActivity activity)
    {
        var reader = Volatile.Read(ref _reader);
        if (reader is null) return OperationalLogVerbosity.Off;
        try { return reader(activity); }
        catch { return OperationalLogVerbosity.Off; }
    }

    // Whether the activity's per-step detail is admitted.
    public static bool IsEnabled(OperationalLogActivity activity)
        => Verbosity(activity) >= OperationalLogVerbosity.Details;

    // Owned activity detail: both the persisted policy and the listener must
    // admit the event. Producers call this before any measurement, allocation
    // or formatting performed only for diagnostics.
    public static bool IsDetailEnabled(
        OperationalLogActivity activity,
        EventSource provider,
        EventLevel level,
        EventKeywords keywords)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return IsEnabled(activity) && provider.IsEnabled(level, keywords);
    }

    // The activity's summary record, a heartbeat, a rollup or a take's figures:
    // admitted from Summary up, so an activity whose detail is off still
    // leaves its trace in the stream.
    public static bool IsSummaryEnabled(
        OperationalLogActivity activity,
        EventSource provider,
        EventLevel level,
        EventKeywords keywords)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return Verbosity(activity) >= OperationalLogVerbosity.Summary
            && provider.IsEnabled(level, keywords);
    }

    // Supporting providers use this form when their detail belongs to an
    // activity only while its workflow is running. Outside that scope their
    // own diagnostics remain unaffected.
    public static bool AllowsScopedDetail(OperationalLogActivity activity)
        => !IsActive(activity) || IsEnabled(activity);

    // Combines scoped policy and listener admission in one query so callers
    // cannot pay log-only costs for an event that will be rejected later.
    public static bool IsScopedDetailEnabled(
        OperationalLogActivity activity,
        EventSource provider,
        EventLevel level,
        EventKeywords keywords)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return AllowsScopedDetail(activity) && provider.IsEnabled(level, keywords);
    }

    // The scoped form of IsSummaryEnabled, for a supporting provider's summary
    // of the activity's unit of work, such as the capture figures of a take.
    public static bool IsScopedSummaryEnabled(
        OperationalLogActivity activity,
        EventSource provider,
        EventLevel level,
        EventKeywords keywords)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return (!IsActive(activity) || Verbosity(activity) >= OperationalLogVerbosity.Summary)
            && provider.IsEnabled(level, keywords);
    }

    public static bool IsActive(OperationalLogActivity activity)
        => Volatile.Read(ref _active[(int)activity]);

    public static void SetActive(OperationalLogActivity activity, bool active)
        => Volatile.Write(ref _active[(int)activity], active);
}
