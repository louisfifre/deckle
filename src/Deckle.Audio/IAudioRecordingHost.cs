namespace Deckle.Audio;

// Contract injected by the orchestrator to expose its live capture-related
// settings to MicrophoneCapture without the latter depending on the host's
// full settings shape. Each property is read during every Record() call, so
// the host is free to forward to a settings service that may have changed
// since the previous call.
public interface IAudioRecordingHost
{
    // MMDevice endpoint id of the microphone ({0.0.1.00000000}.{guid}). Empty =
    // the system default capture device, resolved when the take starts.
    string AudioInputEndpointId { get; }

    // Hard cap on a single recording's duration, in seconds. Snapshotted at
    // the start of each recording. 0 = no cap.
    int MaxRecordingDurationSeconds { get; }

    // The Recording page's microphone-telemetry opt-in. Gates the emission of
    // the per-recording MicrophoneTelemetryPayload EventSource event.
    // The payload is always *computed* (auto-calibration depends on it);
    // this flag only decides whether it's broadcast.
    bool MicrophoneTelemetryEnabled { get; }

    // The voice level in dBFS (CaptureSettings.VoiceLevelDbfs): the level the
    // low-audio tracker tests for sustained voice, the same value the segmenter
    // cuts on. Deliberately not defaulted: a default would quietly hold a
    // second copy of the level for any host that forgot it.
    double VoiceLevelDbfs { get; }

    // What the take is for, as the recording milestones name it: dictation,
    // level_test, listening_test or voice_meter. A free string that
    // only tags log lines, no closed set. Without it a Settings test
    // reads in the log exactly like a dictation. Defaulted so a dictation host
    // needs no change.
    string RecordingPurpose => "dictation";

    // False for a take that only feeds a live meter through AudioLevel and
    // Frame and returns no audio: the loop then keeps no PCM, so a take left
    // running for hours holds no more memory than one buffer of it, and the
    // result's Pcm is empty. The low-audio warning is a dictation diagnostic
    // and stays off as well: a monitor left running in a quiet room would
    // write it to the log as a broken microphone. Defaulted so a
    // dictation host needs no change.
    bool RetainsAudio => true;

    // Hold the microphone alone (CaptureSettings.ExclusiveMode). Defaulted to
    // false so a live meter or a test shares the device whatever the setting:
    // only the dictation host forwards it.
    bool ExclusiveMode => false;
}
