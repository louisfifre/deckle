using System;
using System.Collections.Generic;
using System.Text;

namespace Deckle.Autocorrect;

// The text-corpus accumulator: rebuilds a sentence from the word-commit stream as
// two parallel strings — what the user TYPED and what ended up FINAL after the
// corrector — so the pair feeds error-pattern mining (the keyboard substitutions
// the corrector never even sees, e.g. a ';' typed for an apostrophe). Pure and
// synchronous, like CorrectionTrace: words and edits go in, completed sentences
// come out through Completed; no OS calls, no EventSource — the engine owns
// emission and the consent gate.
//
// A sentence is the run of committed words up to a sentence-ending boundary
// ('.', '!', '?') or an Enter. Any OTHER reset (focus change, navigation, a
// Ctrl-chord that may have pasted, …) DROPS the partial run: a sentence the
// accumulator could not observe cleanly is never emitted. Paste and dictation
// never reach the word stream in the first place (clipboard inserts raise no key
// events; injected text is filtered upstream), so the corpus stays « what I
// actually typed at the keyboard ».
//
// Reconstruction. Each committed word carries its boundary char; the sentence is
// the words rejoined by those separators, so punctuation (the telling ';') and
// spacing survive verbatim on both sides. The lone exception is the elision
// apostrophe, which the tracker attaches to the word itself (« l' »); its
// boundary collapses to an empty separator so the rejoin does not double it.
//
// One known v1 artifact: re-editing a word in the very gesture that ended a
// sentence (the rare backspace-fix after the closing '.') emits before the merge
// can fold it, leaving a duplicate token in that one sentence — mining noise, not
// corruption.
public sealed class SentenceCorpus
{
    private readonly List<Slot> _slots = new();

    private readonly record struct Slot(string Typed, string Final, string Separator);

    /// <summary>Raised when a sentence completes — (typed, final). The engine
    /// emits it on the dedicated, consent-gated dataset.</summary>
    public Action<string, string>? Completed;

    // A committed word: its typed form, the form left on screen (the correction,
    // or the same word when the gate stood aside), and the boundary that ended it.
    public void Word(string typed, string final, char boundary)
    {
        _slots.Add(new Slot(typed, final, Separator(boundary)));
        if (IsSentenceEnd(boundary))
            Flush();
    }

    // The user backspaced into the word just committed and retyped it (tracker
    // WordEdit). The re-commit already appended a slot; fold it back into the one
    // it edited — the TYPED side keeps the first attempt (the error worth mining),
    // the FINAL side takes the retyped result.
    public void Edit(string original, string replacement)
    {
        if (_slots.Count < 2) return;
        if (_slots[^2].Typed != original) return; // not the slot we modelled — leave it
        var redo = _slots[^1];
        _slots.RemoveAt(_slots.Count - 1);
        _slots[^1] = _slots[^1] with { Final = redo.Final, Separator = redo.Separator };
    }

    // Enter ends a sentence (emit); every other reset interrupts it before an
    // ending boundary, so the partial run is dropped.
    public void Reset(ResetReason reason)
    {
        if (reason == ResetReason.Enter)
            Flush();
        else
            _slots.Clear();
    }

    private void Flush()
    {
        if (_slots.Count == 0) return;
        var typed = new StringBuilder();
        var final = new StringBuilder();
        foreach (var s in _slots)
        {
            typed.Append(s.Typed).Append(s.Separator);
            final.Append(s.Final).Append(s.Separator);
        }
        _slots.Clear();
        Completed?.Invoke(typed.ToString(), final.ToString());
    }

    // The elision apostrophe is already part of the word (« l' »); collapse its
    // separator so the rejoin does not double it. Every other boundary is verbatim.
    private static string Separator(char boundary) =>
        boundary == '\'' ? "" : boundary.ToString();

    private static bool IsSentenceEnd(char boundary) =>
        boundary is '.' or '!' or '?';
}
