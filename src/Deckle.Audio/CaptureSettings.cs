using Deckle.Audio;

namespace Deckle.Audio;

// Microphone capture settings. AudioInputDeviceId = waveIn device index;
// -1 = WAVE_MAPPER (system default device).
//
// Renamed from RecordingSettings on 2026-05-02 when the capture subsystem
// was extracted into its own project. The page Settings ▸ Recording label
// stays unchanged on the UI side — only the C# property and the JSON key
// change. Migration from the legacy "Recording" JSON key is handled
// silently in SettingsService.Load.
public sealed class CaptureSettings
{
    // Version of the persisted shape. 0 means the file was written before the
    // settings were versioned; the post-load migration in
    // CaptureSettingsService stamps CurrentSchemaVersion once it has run.
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = 0;

    public int AudioInputDeviceId { get; set; } = -1;

    // Maximum length of a single recording, in seconds. 0 = no limit, the
    // default since 2026-10 (it used to be 20 minutes). A limit is the
    // user's opt-in on the Recording page. When the capture loop reaches it,
    // the capture stops as if the hotkey had been pressed, and the audio
    // still runs through the full transcription pipeline.
    // Memory of an unlimited take is documented, not bounded: about 115 MB
    // per hour of PCM16 in the capture buffer, plus a float copy at Stop.
    // The user's own limit is the bound.
    public int MaxRecordingDurationSeconds { get; set; } = 0;

    public LevelWindowSettings LevelWindow { get; set; } = new();

    // Transcription pre-processing DSP (see CONTEXT.md). A terminal
    // float[] → float[] signal transform applied between capture and the
    // ASR backend: per utterance by the streaming pipeline, once per take by
    // the monolithic one. It lifts and homogenises a quiet/dynamic
    // mic for machine intelligibility. Off by default; the only knob in
    // Settings ▸ Recording is the opt-in toggle, and no surface edits the
    // fine params (only a hand-edited settings file changes them).
    // Distinct from LevelWindow above, which drives the HUD display level
    // and never alters the audio.
    public PreprocessingSettings Preprocessing { get; set; } = new();
}

// Persisted dBFS window used to map raw microphone RMS
// onto the [0, 1] perceptual level driving the Recording stroke. Exposed
// as Settings so the user can calibrate against their own mic+DSP chain
// without rebuilding — the values land in AudioLevelMapper at app startup
// and on every change.
//
// Defaults match the shipping calibration:
//   Min  -55 dBFS — below typical p25 silence band, well above the
//                   -97 dBFS digital floor / DSP gate.
//   Max  -32 dBFS — measured peak ceiling for normal voice.
//   Exp   1.0    — linear response. Higher = compress low end / expand
//                  high end; lower = the opposite. The HUD reads "there
//                  or not there" with a linear ramp.
//
// AutoCalibration runs a rolling heuristic over the last N microphone
// telemetry samples: median(p25) - 5 dB -> MinDbfs, median(p90) + 5 dB
// -> MaxDbfs, with clamps in the calculator. Off by default — the user
// has to opt in to recurring visual-feedback tweaks. Manual sliders
// override the auto values until auto runs again.
public sealed class LevelWindowSettings
{
    public float MinDbfs                = -55f;
    public float MaxDbfs                = -32f;
    public float DbfsCurveExponent      = 1.0f;
    public bool  AutoCalibrationEnabled = false;
    public int   AutoCalibrationSamples = 5;
}
