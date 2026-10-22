using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// The optional on-disk mirror of the admitted operational stream. Its fixed
// destination is diagnostics/app.jsonl; telemetry datasets have their own
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
        Func<EventEntry, bool>? recordingFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsDirectory);

        _isEnabled = isEnabled
            ?? (static () => LoggingSettingsService.Instance.Current.ApplicationLogToDisk);
        _recordingFilter = recordingFilter ?? (static _ => true);
        _sink = new JsonlSink(
            filePath: Path.Combine(diagnosticsDirectory, "app.jsonl"),
            kindLabel: "log",
            predicate: static _ => true,
            schema: JsonlSchema.SelfDescribing,
            // Ten generations: a week of use with every activity on stays
            // readable, and the folder no longer grows without end.
            rotation: new JsonlRotationPolicy(maxLines: 8000, maxArchives: 10));
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
