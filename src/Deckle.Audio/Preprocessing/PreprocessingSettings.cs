namespace Deckle.Audio;

// ── PreprocessingSettings ──────────────────────────────────────────────────
//
// Tuning state for the transcription pre-processing DSP stage (see
// CONTEXT.md « transcription pre-processing »). Nested under
// CaptureSettings.Preprocessing, persisted with the rest of the audio
// module settings under modules/audio/settings.json.
//
// Two surfaces, deliberately separated:
//   - `Enabled` is the only knob exposed in Settings ▸ Recording — the
//     black-box opt-in toggle. Off by default.
//   - every other field is a fixed per-stage parameter, not exposed in
//     the UI: the whole point of the black box is that there are no knobs
//     to turn. The defaults below are an engineer's starting point, NOT
//     measured optima — provisional until grounded by measurement.
//
// Auto-properties (not public fields) so System.Text.Json round-trips
// them under the module's default serializer options.
public sealed class PreprocessingSettings
{
    // The opt-in toggle, and the whole control. Off by default. On = the DSP
    // runs on every recording and self-adjusts (the makeup lands near 0 dB on a
    // mic already at target, so it does nothing there). The mic level check on
    // the Recording page advises whether turning it on is worth it; the user
    // decides — there is no deferral and no automatic on/off.
    public bool Enabled { get; set; } = false;

    // ── High-pass — removes rumble (mains hum, HVAC), plosive thumps and
    //    the DC offset. Shared brick with the future energy VAD of the
    //    windowing workstream. Kept on by default: it only strips energy
    //    below the speech band, never touches intelligibility.
    public bool  HighPassEnabled { get; set; } = true;
    public float HighPassHz      { get; set; } = 90f;

    // ── Noise gate — soft downward expander below the threshold. OFF by
    //    default on purpose: the real silence handling belongs to the VAD
    //    upstream (windowing workstream), and an aggressive gate eats weak
    //    phonemes. Off by default; available but not run.
    public bool  GateEnabled       { get; set; } = false;
    public float GateThresholdDbfs { get; set; } = -55f;
    public float GateRatio         { get; set; } = 2f;
    public float GateAttackMs      { get; set; } = 5f;
    public float GateReleaseMs     { get; set; } = 150f;

    // ── Compressor — tames the intra-take dynamic range (whisper vs raised
    //    voice). Gentle on purpose: a hard broadcast ratio would lift the
    //    inter-word noise floor, which is fuel for Whisper's silence
    //    hallucinations. 2:1 soft-knee, not 4:1.
    public bool  CompressorEnabled { get; set; } = true;
    public float CompThresholdDbfs { get; set; } = -24f;
    public float CompRatio         { get; set; } = 2f;
    public float CompKneeDb        { get; set; } = 6f;
    public float CompAttackMs      { get; set; } = 8f;
    public float CompReleaseMs     { get; set; } = 150f;

    // ── Makeup gain — lifts the processed signal to an absolute RMS target
    //    (not peak — a single transient would skew a peak target). This is
    //    the « normalization » half: it fixes the absolute level of a quiet
    //    mic, complementary to compression which fixes the dynamics. The
    //    two-pass design lets the orchestrator hit the target exactly.
    //    MaxMakeupGainDb caps the boost so a near-silent take does not
    //    explode its noise floor.
    public float TargetRmsDbfs   { get; set; } = -20f;
    public float MaxMakeupGainDb { get; set; } = 24f;

    // ── Limiter — soft peak guard after makeup, prevents clipping. Fast,
    //    look-ahead-free, instantaneous attack so no output sample ever
    //    crosses the ceiling.
    public bool  LimiterEnabled     { get; set; } = true;
    public float LimiterCeilingDbfs { get; set; } = -1f;
    public float LimiterReleaseMs   { get; set; } = 50f;
}
