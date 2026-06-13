namespace Deckle.Input.Keyboard;

// One keyboard transition from WM_INPUT (RIM_TYPEKEYBOARD), normalized:
// VirtualKey/ScanCode straight from RAWKEYBOARD, IsExtended from the E0
// flag. IsInjected is true when the event carries no source device
// (hDevice == 0) — the signature of SendInput-synthesized keystrokes,
// which is how autocorrect's own repairs are filtered out of its view.
// ExtraInfo is RAWKEYBOARD.ExtraInformation — the dwExtraInfo stamped by
// the sender of a synthetic event; Deckle's own injections carry the
// InjectionTag there, so an injected event can be attributed (ours vs a
// third-party synthesizer: remapper, RDP, on-screen keyboard).
public readonly record struct KeyboardKeyEvent(
    ushort VirtualKey,
    ushort ScanCode,
    bool IsKeyDown,
    bool IsExtended,
    bool IsInjected,
    double TimestampMs,
    uint ExtraInfo = 0);
