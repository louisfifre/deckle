namespace Deckle.Autocorrect;

// Applies a correction to the focused control by rewriting the divergent tail.
// TextInjector is the production implementation, replaying the diff as one
// atomic SendInput burst. The interface is the engine's port to the keystroke
// channel: it lets the engine be tested without synthesizing real input, and
// lets the requested edits be recorded and asserted.
public interface ITextInjector
{
    // Corrects `current` into `target` by the minimal diff. A no-op (identical)
    // returns true; a partial/failed send returns false.
    bool Replace(string current, string target);

    // The Win32 error behind the last short send, 0 when the last send went
    // through or the implementation has none to report. A default member, so
    // test and benchmark injectors need not grow one.
    int LastWin32Error => 0;
}
