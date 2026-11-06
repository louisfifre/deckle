using Deckle.Audio;

namespace Deckle.Audio;

// Microphone capture settings. AudioInputDeviceId = waveIn device index;
// -1 = WAVE_MAPPER (system default device).
public sealed class CaptureSettings
{
    // Version of the persisted shape. 0 means the file was written before the
    // settings were versioned; the post-load migration in
    // CaptureSettingsService stamps CurrentSchemaVersion once it has run.
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = 0;

    public int AudioInputDeviceId { get; set; } = -1;

    // Maximum length of a single recording, in seconds. 0 = no limit, the
    // default; a limit is the user's opt-in on the Recording page. When the
    // capture loop reaches it, the capture stops as if the hotkey had been
    // pressed, and the audio still runs through the full transcription
    // pipeline. Memory of an unlimited take is not bounded: about 115 MB per
    // hour of PCM16 in the capture buffer, plus a float copy at Stop. The
    // user's own limit is the bound.
    public int MaxRecordingDurationSeconds { get; set; } = 0;

    // Transcription pre-processing DSP: a terminal float[] → float[] signal
    // transform applied between capture and the ASR backend, per utterance by
    // the streaming pipeline, once per take by the monolithic one. It lifts and
    // homogenises a quiet/dynamic mic for machine intelligibility. Off by
    // default; the only knob in Settings ▸ Recording is the opt-in toggle, and
    // only a hand-edited settings file changes the fine params.
    public PreprocessingSettings Preprocessing { get; set; } = new();
}
