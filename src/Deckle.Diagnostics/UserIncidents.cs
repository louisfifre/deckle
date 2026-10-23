namespace Deckle.Diagnostics;

// Closed vocabulary of the user incidents UserIncidentRegistry holds: a part of
// Deckle runs degraded and a gesture of the person restores it. A module opens
// and closes its incident by one of these ids, beside the log line it already
// writes for that open and that close; the App declares, for each id, the module
// it belongs to and the Settings page that shows the gesture.
//
// An id is a source, not a module: Ambient Light has two (the push and the
// event stream to the bridge), Transcription two (the Whisper model and the
// speech-trim model). The App folds them into one line per module.
public static class UserIncidents
{
    // Precision scrolling stopped twice in a short run, or could not restart,
    // and fell back to the native wheel until the person turns it on again.
    public const string PrecisionScroll = "precision_scroll";

    // The microphone could not be opened for a recording.
    public const string Microphone = "microphone";

    // The Whisper model or its native runtime is missing or failed to load.
    public const string WhisperModel = "whisper_model";

    // The speech-trim (VAD) model is missing, corrupt or failed.
    public const string VadModel = "vad_model";

    // The Anytype backend stopped, stopped answering, or cannot be supervised.
    public const string AnytypeBackend = "anytype_backend";

    // Ambient Light kept failing to push colours to the Hue bridge.
    public const string HuePush = "hue_push";

    // Ambient Light lost the Hue bridge's event stream.
    public const string HueEvents = "hue_events";

    // Deckle runs as administrator, so Windows does not show its notifications.
    public const string ToastsElevated = "toasts_elevated";

    // Every id above, so a host can check it declared each one.
    public static IReadOnlyList<string> All { get; } =
    [
        PrecisionScroll, Microphone, WhisperModel, VadModel,
        AnytypeBackend, HuePush, HueEvents, ToastsElevated,
    ];
}
