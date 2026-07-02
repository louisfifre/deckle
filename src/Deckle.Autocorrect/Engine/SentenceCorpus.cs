using System;
using System.Collections.Generic;
using System.Text;

namespace Deckle.Autocorrect;

// The text-corpus accumulator: rebuilds each typed sentence as an ordered list of
// slots, every slot carrying its own history — the first-typed form, then each
// transition tagged by the stage that produced it (commit / sentence / user) — so
// the pair (what was typed, what ended final) AND the path between them feed
// error-pattern mining. Pure and synchronous, like CorrectionTrace: words and edits
// go in, completed sentences come out through Completed; no OS calls, no
// EventSource — the engine owns emission and the consent gate.
//
// A sentence is the run of committed words up to a sentence-ending boundary
// ('.', '!', '?') or an Enter. Any OTHER reset (focus change, navigation, a
// Ctrl-chord that may have pasted, …) DROPS the partial run: a sentence the
// accumulator could not observe cleanly is never emitted. Paste and dictation
// never reach the word stream in the first place (clipboard inserts raise no key
// events; injected text is filtered upstream), so the corpus stays « what I
// actually typed at the keyboard ».
//
// Reconstruction. Each slot carries the boundary char it closed on; the sentence
// is the slots rejoined by those separators, so punctuation (the telling ';') and
// spacing survive verbatim on both sides. The lone exception is the elision
// apostrophe, which the tracker attaches to the word itself (« l' »); its boundary
// collapses to an empty separator so the rejoin does not double it.
//
// History. `typed` and `final` are the flat pair for direct mining; `history`
// spells the ordered path of every slot that changed — first-typed then each
// stage's transition — so a commit-stage repair, a sentence-stage rewrite from
// behind, and a manual re-edit are told apart after the fact. The sentence stage
// can rewrite a word while the sentence is still open (a la/là resolved by right
// context); that transition is recorded on its slot. A rewrite that lands after
// the sentence already flushed is a post-close edit — invisible by design (the
// accumulator has no caret), accepted.
public sealed class SentenceCorpus
{
    private readonly List<Slot> _slots = new();

    // The stage that produced a transition — the technical function, never its
    // interpretation. `Commit` the instantaneous gate, `Sentence` the deferred
    // contextual stage rewriting from behind, `User` a manual backspace-and-retype.
    public enum Stage { Commit, Sentence, User }

    public readonly record struct Transition(Stage By, string Form);

    // A completed sentence handed to the engine: the two parallel strings for
    // direct mining plus the per-slot ordered history (empty when nothing changed).
    public readonly record struct SentenceRecord(string Typed, string Final, string History);

    // One committed word's life inside the sentence: the verbatim first-typed form,
    // the separator that closed it, and each transition since. Final is the last
    // transition's form, or the first-typed form when nothing touched it.
    private sealed class Slot
    {
        public string FirstTyped = string.Empty;
        public string Separator = string.Empty;
        public readonly List<Transition> Transitions = new();
        public string Final => Transitions.Count == 0 ? FirstTyped : Transitions[^1].Form;
    }

    /// <summary>Raised when a sentence completes. The engine emits it on the
    /// dedicated, consent-gated dataset.</summary>
    public Action<SentenceRecord>? Completed;

    // A committed word: its typed form, the form left on screen after the commit
    // stage (the same word when the gate stood aside), and the boundary that ended
    // it. A commit-stage repair is the slot's first transition.
    public void Word(string typed, string final, char boundary)
    {
        var slot = new Slot { FirstTyped = typed, Separator = WordBoundaries.DisplaySeparator(boundary) };
        if (!string.Equals(final, typed, StringComparison.Ordinal))
            slot.Transitions.Add(new Transition(Stage.Commit, final));
        _slots.Add(slot);
        if (IsSentenceEnd(boundary))
            Flush();
    }

    // The sentence stage rewrote a committed word from behind (original → new) while
    // the sentence was still open. Record it as a Sentence transition on the matching
    // still-open slot; a rewrite after flush finds none and is dropped (post-close,
    // invisible by design). Scans from the end so the most recent match wins.
    public void SentenceEdit(string original, string replacement)
    {
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_slots[i].Final, original, StringComparison.Ordinal))
            {
                _slots[i].Transitions.Add(new Transition(Stage.Sentence, replacement));
                return;
            }
        }
    }

    // The user backspaced into the word just committed and retyped it (tracker
    // WordEdit). The re-commit already appended a slot; fold it back into the one it
    // edited as a User transition — the first-typed stays the error worth mining, and
    // the retype's own commit repairs (if any) carry over so the history stays whole.
    public void Edit(string original, string replacement)
    {
        if (_slots.Count < 2) return;
        if (!string.Equals(_slots[^2].FirstTyped, original, StringComparison.Ordinal)) return; // not the slot we modelled
        Slot redo = _slots[^1];
        _slots.RemoveAt(_slots.Count - 1);
        Slot edited = _slots[^1];
        edited.Transitions.Add(new Transition(Stage.User, redo.FirstTyped));
        foreach (Transition t in redo.Transitions) // the retype's own commit repairs, in order
            edited.Transitions.Add(t);
        edited.Separator = redo.Separator;
    }

    // Enter ends a sentence (emit); every other reset interrupts it before an ending
    // boundary, so the partial run is dropped.
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
        var history = new StringBuilder();
        for (int i = 0; i < _slots.Count; i++)
        {
            Slot s = _slots[i];
            typed.Append(s.FirstTyped).Append(s.Separator);
            final.Append(s.Final).Append(s.Separator);
            if (s.Transitions.Count > 0)
                AppendHistory(history, i, s);
        }
        _slots.Clear();
        Completed?.Invoke(new SentenceRecord(typed.ToString(), final.ToString(), history.ToString()));
    }

    // "#<index>=<firsttyped>»<stage>:<form>[»<stage>:<form>…]" per changed slot,
    // pipe-joined — flat and self-reading, like the decision dataset's candidate
    // rendering. A grep on "»user:" finds every manual re-edit across the corpus.
    private static void AppendHistory(StringBuilder history, int index, Slot slot)
    {
        if (history.Length > 0) history.Append('|');
        history.Append('#').Append(index).Append('=').Append(slot.FirstTyped);
        foreach (Transition t in slot.Transitions)
            history.Append('»').Append(Tag(t.By)).Append(':').Append(t.Form);
    }

    private static string Tag(Stage by) => by switch
    {
        Stage.Commit => "commit",
        Stage.Sentence => "sentence",
        Stage.User => "user",
        _ => "?",
    };

    private static bool IsSentenceEnd(char boundary) =>
        boundary is '.' or '!' or '?';
}
