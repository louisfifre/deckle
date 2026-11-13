using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// The optional on-disk mirror of the admitted operational stream. Its fixed
// destination is diagnostics/trace.jsonl; telemetry datasets have their own
// roots and sinks. The enabled reader and recording filter are evaluated for
// every future entry so settings changes apply without rebuilding the sink.
public sealed class ApplicationLogSink : IFlushableLogSink
{
    private readonly JsonlSink _sink;
    private readonly Func<bool> _isEnabled;
    private readonly Func<EventEntry, bool> _recordingFilter;

    public ApplicationLogSink(
        string diagnosticsDirectory,
        Func<bool>? isEnabled = null,
        Func<EventEntry, bool>? recordingFilter = null,
        Func<int?>? maxArchives = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsDirectory);
        // Read lazily, at each roll and at start: a sink built by a test with
        // its own reader never touches the person's settings file.
        maxArchives ??= static () => TraceRetentionPolicy.MaxArchives(
            LoggingSettingsService.Instance.Current.TraceRetention);

        _isEnabled = isEnabled
            ?? (static () => LoggingSettingsService.Instance.Current.ApplicationLogToDisk);
        _recordingFilter = recordingFilter ?? (static _ => true);
        _sink = new JsonlSink(
            filePath: Path.Combine(diagnosticsDirectory, DiagnosticsFiles.Trace),
            kindLabel: "log",
            predicate: static _ => true,
            schema: JsonlSchema.SelfDescribing,
            // The person's retention, read at each roll (TraceRetention): a
            // lowered cap trims at the next roll.
            rotation: new JsonlRotationPolicy(TraceRetentionPolicy.LinesPerFile, maxArchives));
    }

    public bool Wants(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != ObservationKind.Operational) return false;

        // A reader or filter that throws is not "disabled": the dispatcher
        // contains the exception and reports this sink as an incident, so a
        // log that stopped recording says why instead of just going quiet.
        return _isEnabled() && _recordingFilter(entry);
    }

    public void Write(EventEntry entry) => _sink.Write(entry);

    public void Flush() => _sink.Flush();

    public bool TryFlush(TimeSpan timeout) => _sink.TryFlush(timeout);

    public void Dispose() => _sink.Dispose();
}
