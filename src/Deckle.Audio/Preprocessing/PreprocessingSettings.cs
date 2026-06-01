namespace Deckle.Audio.Preprocessing;

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
//   - every other field is a per-stage parameter tuned from the
//     Playground (where the gain is heard + measured). The defaults
//     below are an engineer's starting point, NOT measured optima —
//     they are provisional until the WER bench grounds them.
//
// Auto-properties (not public fields) so System.Text.Json round-trips
// them under the module's default serializer options.
public sealed class PreprocessingSettings
{
    // The opt-in toggle. Off by default — the feature is available and
    // wired, but never transforms a recording until the user enables it
    // and the activation model has confirmed the mic benefits (see
    // PreprocessingActivation + PreprocessingActivationCalculator).
    public bool Enabled { get; set; } = false;

    // Deferred-activation state. Meaningful only when Enabled. On opt-in
    // the orchestrator resets this to Calibrating; the DSP transforms
    // audio only once the state reaches Active. See the activation model
    // for the rationale (« activer ≠ actif tout de suite »).
    public PreprocessingActivation Activation { get; set; } = PreprocessingActivation.Calibrating;

    // ── High-pass — removes rumble (mains hum, HVAC), plosive thumps and
    //    the DC offset. Shared brick with the future energy VAD of the
    //    windowing workstream. Kept on by default: it only strips energy
    //    below the speech band, never touches intelligibility.
    public bool  HighPassEnabled { get; set; } = true;
    public float HighPassHz      { get; set; } = 90f;

    // ── Noise gate — soft downward expander below the threshold. OFF by
    //    default on purpose: the real silence handling belongs to the VAD
    //    upstream (windowing workstream), and an aggressive gate eats weak
    //    phonemes. Present so the Playground can experiment, not to run.
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

// State of the deferred-activation model. Off is represented by
// PreprocessingSettings.Enabled == false (no enum member needed): when the
// feature is disabled the activation state is simply irrelevant.
//
//   Calibrating — opted in, collecting microphone telemetry over the first
//                 N recordings. The DSP does NOT transform audio yet; the
//                 UI says « enabled, calibrating, not yet in service ».
//   Active      — calibration confirmed the mic sits below the makeup
//                 target by a meaningful margin; the DSP now transforms.
//   Dormant     — calibration concluded the mic is already adequate; the
//                 DSP stays opted-in but does not transform (the user is
//                 told their mic does not need it).
public enum PreprocessingActivation
{
    Calibrating,
    Active,
    Dormant,
}
