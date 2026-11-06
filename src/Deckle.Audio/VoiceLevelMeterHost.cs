namespace Deckle.Audio;

// What the Recording page's voice meter takes from the microphone, through a
// MicrophoneLevelFeed: the configured device, no audio kept, no cap, no
// telemetry. The voice level is read at start; the low-audio warning that
// reads it stays off for a take that keeps no audio. The purpose tags the
// capture's own log lines, so a meter left on does not read as a dictation.
internal sealed class VoiceLevelMeterHost(int deviceId, double voiceLevelDbfs) : IAudioRecordingHost
{
    public const string Purpose = "voice_meter";

    public int AudioInputDeviceId => deviceId;
    public int MaxRecordingDurationSeconds => 0;
    public bool MicrophoneTelemetryEnabled => false;
    public double VoiceLevelDbfs => voiceLevelDbfs;
    public string RecordingPurpose => Purpose;
    public bool RetainsAudio => false;
}
