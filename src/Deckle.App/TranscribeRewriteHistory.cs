namespace Deckle.App;

// The Undo and Redo of the Transcribe and rewrite window. Each replacement of
// the field's whole text (a transcription, a rewrite) is one step; the text
// before it is kept, hand edits included, since it is read from the field at
// the moment of the replacement. That round trip is what makes a comparison
// screen unnecessary: rewrite, Undo, try another profile (Louis, 2026-11-14).
// Typing inside the field keeps the field's own Ctrl+Z.
internal sealed class TranscribeRewriteHistory
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    // Called with the field's text just before it is replaced. A new step
    // forgets what Redo could bring back, as in any editor.
    public void Record(string before)
    {
        _undo.Push(before);
        _redo.Clear();
    }

    // A replacement that delivered nothing (a failed run): the field goes back
    // to the text recorded for it, and the step is forgotten.
    public void Withdraw()
    {
        if (_undo.Count > 0) _undo.Pop();
    }

    // Returns the text to show, or null when there is nothing to undo.
    public string? Undo(string current)
    {
        if (_undo.Count == 0) return null;
        _redo.Push(current);
        return _undo.Pop();
    }

    public string? Redo(string current)
    {
        if (_redo.Count == 0) return null;
        _undo.Push(current);
        return _redo.Pop();
    }
}
