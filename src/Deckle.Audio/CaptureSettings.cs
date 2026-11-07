using System.Text.Json.Serialization;
using Deckle.Audio;

namespace Deckle.Audio;

// Microphone capture settings.
public sealed class CaptureSettings
{
    // Version of the persisted shape. 0 means the file was written before the
    // settings were versioned; the post-load migration in
    // CaptureSettingsService stamps CurrentSchemaVersion once it has run.
    //   1  the recording limit's former default became "no limit".
    //   2  the microphone is kept as an MMDevice endpoint id, no longer as a
    //      waveIn index.
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = 0;

    // The microphone, as its MMDevice endpoint id ({0.0.1.00000000}.{guid}).
    // Empty = the system default capture device, resolved when each take starts,
    // so changing the default in Windows is followed. The id names the device
    // itself, so it survives other devices being plugged or unplugged, which a
    // position in a list did not.
    public string AudioInputEndpointId { get; set; } = "";

    // The microphone as the waveIn capture kept it: a device index, -1 for the
    // system default. Read only to migrate old files (see
    // CaptureSettingsService.ApplyPostLoadMigrations), which turn it into
    // AudioInputEndpointId and clear it; it is never written back.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AudioInputDeviceId { get; set; }

    // Hold the microphone alone while recording (WASAPI exclusive mode): the
    // stream goes to the device directly, past the Windows audio engine, and
    // no other application can use the microphone meanwhile. Off by default
    // for that reason. When the device refuses it (another application is
    // using it, or Windows does not allow exclusive control), the take falls
    // back to sharing it and says so in the log.
    public bool ExclusiveMode { get; set; } = false;

    // Maximum length of a single recording, in seconds. 0 = no limit, the
    // default; a limit is the user's opt-in on the Recording page. When the
    // capture loop reaches it, the capture stops as if the hotkey had been
    // pressed, and the audio still runs through the full transcription
    // pipeline. Memory of an unlimited take is not bounded: about 230 MB per
    // hour of 16 kHz mono float in the capture buffer, plus a copy at Stop. The
    // user's own limit is the bound.
    public int MaxRecordingDurationSeconds { get; set; } = 0;

    // The voice level, in dBFS: the one level above which Deckle calls a 50 ms
    // frame "voice" (VoiceDetector treats it as its exit level). Read by the
    // energy segmenter for every cut (dictation, file transcription, recovery
    // of a take), by the low-audio warning of the capture loop and by the HUD
    // outline (AudioLevelMapper). It also governs the cuts of file
    // transcription, whose gain chain is not the microphone's: known and
    // accepted for now. Moved here from the transcription module's
    // streaming.segmenter.thresholdDbfs by SettingsBootstrap.
    public double VoiceLevelDbfs { get; set; } = -45.0;

    // Transcription pre-processing DSP: a terminal float[] → float[] signal
    // transform applied between capture and the ASR backend, per utterance by
    // the streaming pipeline, once per take by the monolithic one. It lifts and
    // homogenises a quiet/dynamic mic for machine intelligibility. Off by
    // default; the only knob in Settings ▸ Recording is the opt-in toggle, and
    // only a hand-edited settings file changes the fine params.
    public PreprocessingSettings Preprocessing { get; set; } = new();
}
