namespace Deckle.Autocorrect;

// A word the user finished typing — a boundary character committed it.
// PreviousWord is the token committed just before on the same surface, and
// PreviousPreviousWord the one before that (elision prefixes keep their
// apostrophe: « l' »); both null after a reset, the second also null one word
// into a sentence. The two together feed the trigram disambiguator.
public sealed record WordCommit(
    string Word, char Boundary, string? PreviousWord, string? PreviousPreviousWord, double TimestampMs);

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
