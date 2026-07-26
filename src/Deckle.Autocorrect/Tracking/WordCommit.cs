namespace Deckle.Autocorrect;

// A word the user finished typing — a boundary character committed it.
// PreviousWord is the token committed just before on the same surface, and
// PreviousPreviousWord the one before that (elision prefixes keep their
// apostrophe: « l' »); both null after a reset, the second also null one word
// into a sentence. The two together feed the trigram disambiguator.
// Reopened is set when this commit came from a word the user backspaced into
// and retyped: the deliberate keystroke asserts intent, so the commit stage
// must leave it literal — only the sentence stage, with full context, keeps
// the right to revise it.
// PrecedingSeparators is the on-screen run between PreviousWord and this word
// as the tracker saw it typed (";" in « qu;il », ", " after a spaced comma) —
// the mistouch boundary families read it. Empty when unknown: after a reset, a
// re-opened word, or any backspace that ate into the run.
public sealed record WordCommit(
    string Word, char Boundary, string? PreviousWord, string? PreviousPreviousWord, double TimestampMs,
    bool Reopened = false, string PrecedingSeparators = "");

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
    ExternalMutation,
}
