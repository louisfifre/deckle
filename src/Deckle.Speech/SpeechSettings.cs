namespace Deckle.Speech;

// Container POCO for the Speech (read-aloud / TTS) module, persisted at
// <UserDataRoot>/modules/speech/settings.json. Twin of AmbientSettings —
// each module owns its own settings POCO; consumers read from
// SpeechSettingsService.Instance.Current.
//
// Skeleton scope: the read-aloud gesture is on-demand (Alt+Win+`), so the
// hotkey fires regardless of Enabled. Voice and Temperature are the future
// tunables — already wired through to ISpeechBackend.SynthesizeAsync — but
// the placeholder Chatterbox stub ignores them until the real ONNX decode
// lands. The tuning UI ships alongside that backend.
public sealed class SpeechSettings
{
    // Master toggle, false by default per module doctrine. No consumer in
    // the skeleton (the hotkey is the explicit trigger); reserved for when
    // the module grows a settings page and an Enabled-gated surface.
    public bool Enabled { get; set; } = false;

    // Reference voice for the zero-shot clone. Pierre / Jessica are the two
    // FR voices auditioned (Piper UPMC reference clips). Default Pierre.
    public SpeechVoice Voice { get; set; } = SpeechVoice.Pierre;

    // LM sampling temperature — the real naturalness lever on the multilingual
    // model (0.5 flat .. 0.7 livelier; below 0.5 robotic). Default 0.6, a
    // middle ground. Range of practical interest [0.5, 0.7].
    public double Temperature { get; set; } = 0.6;
}

// The two FR reference voices auditioned for Chatterbox-Multilingual. A closed
// set for the skeleton; if a future backend exposes different voices this enum
// is revisited (flagged for the ONNX palier).
public enum SpeechVoice
{
    Pierre,
    Jessica,
}
