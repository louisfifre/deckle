namespace Deckle.Input.Autocorrect.Injection;

// Applies a correction to the focused control by rewriting the divergent tail.
// TextInjector is the production implementation, replaying the diff as one
// atomic SendInput burst. The interface is the engine's port to the keystroke
// channel: it lets the engine be tested without synthesizing real input, and
// lets the requested edits be recorded and asserted. Narrowed to what the
// engine consumes — Replace; the bare-text path (TypeText) stays on the
// concrete type for the dev CLI alone.
public interface ITextInjector
{
    // Corrects `current` into `target` by the minimal diff. A no-op (identical)
    // returns true; a partial/failed send returns false.
    bool Replace(string current, string target);
}
