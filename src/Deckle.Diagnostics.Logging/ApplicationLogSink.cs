using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// The optional on-disk mirror of the admitted operational stream. Its fixed
// destination is diagnostics/app.jsonl; telemetry datasets have their own
// roots and sinks. The enabled reader and recording filter are evaluated for
// every future entry so settings changes apply without rebuilding the sink.
public sealed class ApplicationLogSink : ILogSink
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
            rotation: new JsonlRotationPolicy(maxLines: 8000));
    }

    public bool Wants(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != ObservationKind.Operational) return false;

        try
        {
            return _isEnabled() && _recordingFilter(entry);
        }
        catch
        {
            return false;
        }
    }

    public void Write(EventEntry entry) => _sink.Write(entry);
}
