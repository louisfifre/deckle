using System.Text.Json.Serialization;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Operational-log settings, persisted by LoggingSettingsService: producer-side
// admission levels plus the trace.jsonl opt-in. The LogWindow view filter is a
// separate, process-lifetime selection and never belongs here.
public sealed class LoggingSettings
{
    // Optional disk mirror of admitted operational observations. Its path is
    // fixed by the Logging module at diagnostics/trace.jsonl; telemetry settings
    // and storage overrides never redirect it.
    public bool ApplicationLogToDisk { get; set; } = false;

    // One verbosity control per activity (OperationalLogVerbosity). They ship
    // at Summary: an activity leaves its heartbeat, rollup or per-take figures,
    // and its per-step detail waits for Details. Milestones, warnings and
    // errors pass at every level. The values are written as words so the file
    // reads by itself.

    // Summary: the push loop's heartbeat, one line per period. Details: the
    // sparse per-change push lines and the engine's other Verbose detail,
    // never one line per frame.
    [JsonConverter(typeof(JsonStringEnumConverter<OperationalLogVerbosity>))]
    public OperationalLogVerbosity AmbientVerbosity { get; set; } = OperationalLogVerbosity.Summary;

    // Summary: the 1 Hz streaming heartbeat and each take's figures (capture,
    // utterance totals, latencies, output). Details: per utterance and per
    // segment, decode parameters, model load internals.
    [JsonConverter(typeof(JsonStringEnumConverter<OperationalLogVerbosity>))]
    public OperationalLogVerbosity TranscriptionVerbosity { get; set; } = OperationalLogVerbosity.Summary;

    // Summary: the 30 s activity rollup and the keyboard rollup. Details: the
    // SurfaceChanged probe and the learning signals. An applied correction's
    // detail (reason and lengths, never the word) and injection failures
    // remain at every level. There is no capture window: the engine runs
    // continuously, so the level applies whenever it is below Details.
    [JsonConverter(typeof(JsonStringEnumConverter<OperationalLogVerbosity>))]
    public OperationalLogVerbosity AutocorrectVerbosity { get; set; } = OperationalLogVerbosity.Summary;

    // Summary: the Raw Input contact-frame rollup. Details: Trackpad's
    // per-gesture detail. Device presence, lifecycle, incidents, warnings and
    // recoveries remain admitted at every level.
    [JsonConverter(typeof(JsonStringEnumConverter<OperationalLogVerbosity>))]
    public OperationalLogVerbosity InputVerbosity { get; set; } = OperationalLogVerbosity.Summary;

    // Windowing probes have no summary record yet: below Details the provider,
    // Verbose only, stays silent, so the control ships Off and offers Off and
    // Details alone.
    [JsonConverter(typeof(JsonStringEnumConverter<OperationalLogVerbosity>))]
    public OperationalLogVerbosity WindowingVerbosity { get; set; } = OperationalLogVerbosity.Off;

    public OperationalLogVerbosity Verbosity(OperationalLogActivity activity) => activity switch
    {
        OperationalLogActivity.Ambient       => AmbientVerbosity,
        OperationalLogActivity.Transcription => TranscriptionVerbosity,
        OperationalLogActivity.Autocorrect   => AutocorrectVerbosity,
        OperationalLogActivity.Input         => InputVerbosity,
        OperationalLogActivity.Windowing     => WindowingVerbosity,
        _                                    => OperationalLogVerbosity.Off,
    };
}
