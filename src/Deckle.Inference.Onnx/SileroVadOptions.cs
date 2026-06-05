namespace Deckle.Inference.Onnx;

// Tunables for the Silero VAD speech-timestamp extraction, mirroring the
// reference get_speech_timestamps defaults (snakers4/silero-vad v5). These stay
// internal defaults — the only knob exposed upstream is whether the VAD runs at
// all. Durations are milliseconds; the state machine converts them to 16 kHz
// sample counts.
public sealed record SileroVadOptions
{
    // Speech probability at/above which a window counts as speech. The release
    // threshold is Threshold - 0.15 (hysteresis), floored at 0.01.
    public float Threshold { get; init; } = 0.5f;

    // A speech span shorter than this is discarded as a blip.
    public int MinSpeechDurationMs { get; init; } = 250;

    // Trailing silence shorter than this does not close an open span, so an
    // intra-phrase pause does not split a sentence.
    public int MinSilenceDurationMs { get; init; } = 100;

    // Each kept span is padded by this much on both sides (clamped to the
    // buffer), so a word onset or tail is not clipped.
    public int SpeechPadMs { get; init; } = 30;

    public static SileroVadOptions Default { get; } = new();
}
