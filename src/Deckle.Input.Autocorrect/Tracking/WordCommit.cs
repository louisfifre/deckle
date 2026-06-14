namespace Deckle.Input.Autocorrect;

// A word the user finished typing — a boundary character committed it.
// PreviousWord is the token committed just before on the same surface
// (elision prefixes keep their apostrophe: « l' »), null after a reset.
public sealed record WordCommit(string Word, char Boundary, string? PreviousWord, double TimestampMs);

// The user backspaced into the word committed just before and retyped
// it differently — the raw material of correction harvesting.
public sealed record WordEdit(string Original, string Replacement, double TimestampMs);

public enum ResetReason
{
    FocusChanged,
    PointerInteraction,
    Navigation,
    Escape,
    Shortcut,
    Enter,
    Delete,
    DeadKey,
    BufferLimit,
    PasswordSurface,
}
