namespace Deckle.Input.Autocorrect.Tracking;

// What a raw key-down means for the text under the caret — the
// KeyDecoder output consumed by the TypedWordTracker.
public enum KeystrokeKind
{
    /// <summary>Printable output; <see cref="Keystroke.Text"/> carries 1-2 UTF-16 chars.</summary>
    Text,
    Backspace,
    Delete,
    Enter,
    Tab,
    /// <summary>Arrows, Home/End, PageUp/PageDown — the caret moved, the buffer is stale.</summary>
    Navigation,
    Escape,
    /// <summary>Any key chorded with Ctrl or Win — the application may have done anything.</summary>
    Shortcut,
    /// <summary>Dead-key composition pending — conservative reset.</summary>
    DeadKey,
    /// <summary>Function keys, Insert, lone modifiers… irrelevant to the buffer.</summary>
    Other,
}

public readonly record struct Keystroke(KeystrokeKind Kind, string Text, double TimestampMs)
{
    public static Keystroke Of(KeystrokeKind kind, double timestampMs) =>
        new(kind, string.Empty, timestampMs);
}
