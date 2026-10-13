namespace Deckle.Speech;

// Voice and Temperature reach ISpeechBackend.SynthesizeAsync, but the
// placeholder Chatterbox stub ignores them until the real ONNX decode lands;
// the tuning UI ships alongside that backend.
public sealed class SpeechSettings
{
    // False by default, like every module. No consumer yet: reserved for a
    // settings page and an Enabled-gated surface.
    public bool Enabled { get; set; } = false;

    // A reference clip for the zero-shot clone, not a model voice; the clips
    // come from Piper UPMC.
    public SpeechVoice Voice { get; set; } = SpeechVoice.Pierre;

    // LM sampling temperature, the naturalness lever on the multilingual
    // model: lower reads flatter, higher livelier.
    public double Temperature { get; set; } = 0.6;
}

// The two FR reference voices auditioned for Chatterbox-Multilingual, a closed
// set; a backend with different voices revisits this enum.
public enum SpeechVoice
{
    Pierre,
    Jessica,
}
