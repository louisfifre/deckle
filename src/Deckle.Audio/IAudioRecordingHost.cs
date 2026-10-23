namespace Deckle.Audio;

// Contract injected by the orchestrator to expose its live capture-related
// settings to MicrophoneCapture without the latter depending on the host's
// full settings shape. Each property is read during every Record() call, so
// the host is free to forward to a settings service that may have changed
// since the previous call.
public interface IAudioRecordingHost
{
    // waveIn device index. -1 = WAVE_MAPPER (system default device).
    int AudioInputDeviceId { get; }

    // Hard cap on a single recording's duration, in seconds. Snapshotted at
    // the start of each recording. 0 = no cap.
    int MaxRecordingDurationSeconds { get; }

    // The Recording page's microphone-telemetry opt-in. Gates the emission of
    // the per-recording MicrophoneTelemetryPayload EventSource event.
    // The payload is always *computed* (auto-calibration depends on it);
    // this flag only decides whether it's broadcast.
    bool MicrophoneTelemetryEnabled { get; }

    // What the take is for, as the recording milestones name it: dictation,
    // level_test, listening_test or playground. Without it a Settings test
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
}
