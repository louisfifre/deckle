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
// A sentence is the run of committed words up to one of three closures, recorded on
// the record so the reader can weigh a run by how it ended:
//   • "sentence" — a sentence-ending boundary ('.', '!', '?') closed it;
//   • "enter"    — an Enter closed it;
//   • "interrupted" — any OTHER reset (focus change, navigation, a Ctrl-chord that
//     may have pasted, …) cut it short before an ending boundary.
// Earlier every reset but Enter DROPPED the partial run; it is now emitted tagged
// "interrupted", because the run is still verbatim keyboard input worth mining. The
// purity guarantee is what makes that safe: paste and dictation never reach the word
// stream in the first place (clipboard inserts raise no key events; injected text is
// filtered upstream), so even an interrupted run is « what I actually typed at the
// keyboard ». An interrupted run whose slot list is already empty emits nothing.
//
// Reconstruction. Each slot carries the boundary chars it closed on; the sentence is
// the slots rejoined by those separators, so punctuation (the telling ';') and
// spacing survive verbatim on both sides. The elision apostrophe, which the tracker
// attaches to the word itself (« l' »), collapses to an empty separator so the rejoin
// does not double it. The typed side and the final side keep SEPARATE closing
// separators per slot: a re-edit (Edit) can leave a slot whose typed first form ended
// on one boundary while its on-screen final form ends on another — rejoining both
// with a single separator fused two typed tokens (« de » + « avoir » → « deavoir »),
// a proven corruption. Each side rejoins with its own boundary, so both still
// tokenize back to the same slot count that offline alignment relies on.
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

    // A completed sentence handed to the engine: the two parallel strings for direct
    // mining, the per-slot ordered history (empty when nothing changed), the closure
    // that ended the run ("sentence" / "enter" / "interrupted"), and the typing rhythm
    // as comma-joined per-slot inter-commit gaps in ms (first slot "0"; empty when no
    // timestamps were available). The defaults mirror how a reader interprets a
    // legacy record that predates the two fields: closed on a sentence boundary,
    // rhythm unknown.
    public readonly record struct SentenceRecord(
        string Typed, string Final, string History, string Closure = "sentence", string Timing = "");

    // One committed word's life inside the sentence: the verbatim first-typed form,
    // the two separators that closed it on the typed and the final rendering (equal
    // until a re-edit splits them), the commit timestamp, and each transition since.
    // Final is the last transition's form, or the first-typed form when nothing
    // touched it.
    private sealed class Slot
    {
        public string FirstTyped = string.Empty;
        public string TypedSeparator = string.Empty;
        public string FinalSeparator = string.Empty;
        public long TimestampMs;
        public readonly List<Transition> Transitions = new();
        public string Final => Transitions.Count == 0 ? FirstTyped : Transitions[^1].Form;
    }

    /// <summary>Raised when a sentence completes. The engine emits it on the
    /// dedicated, consent-gated dataset.</summary>
    public Action<SentenceRecord>? Completed;

    // Set when the last reset threw away a word in flight: the next committed
    // "word" may be that word's tail (« probl|reset|ème » commits « ème »), a
    // fragment the user never typed as a word. It is dropped from the corpus —
    // one word of loss against a polluted sentence start. Cleared by the word it
    // judges or by the next reset, whose own drop signal re-arms it or not.
    private bool _nextWordSuspect;

    /// <summary>The last reset dropped a partial word; hold the next committed
    /// word suspect and keep it out of the corpus.</summary>
    public void MarkNextWordSuspect() => _nextWordSuspect = true;

    // A committed word: its typed form, the form left on screen after the commit
    // stage (the same word when the gate stood aside), the boundary that ended it,
    // and the commit time in ms (0 = unknown, e.g. from a caller without a clock).
    // A commit-stage repair is the slot's first transition. Typed and final rendering
    // start with the same separator; only a re-edit can split them.
    public void Word(string typed, string final, char boundary, long timestampMs = 0)
    {
        if (_nextWordSuspect)
        {
            _nextWordSuspect = false;
            return; // a likely fragment tail — never a slot
        }

        string separator = WordBoundaries.DisplaySeparator(boundary);
        var slot = new Slot
        {
            FirstTyped = typed,
            TypedSeparator = separator,
            FinalSeparator = separator,
            TimestampMs = timestampMs,
        };
        if (!string.Equals(final, typed, StringComparison.Ordinal))
            slot.Transitions.Add(new Transition(Stage.Commit, final));
        _slots.Add(slot);
        if (IsSentenceEnd(boundary))
            Flush("sentence");
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
    // The FINAL rendering adopts the redo's closing separator (the on-screen boundary
    // the retype landed on); the TYPED rendering keeps the edited slot's OWN original
    // separator, so the first-typed form rejoins on the boundary it was actually typed
    // with — otherwise an elision-apostrophe retype (empty display separator) would
    // fuse the typed side (« de »+« avoir » → « deavoir ») while the final side stays
    // « d'avoir ».
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
        edited.FinalSeparator = redo.FinalSeparator; // the typed side keeps its own boundary
    }

    // Enter ends a sentence (closure "enter"); every other reset interrupts it before
    // an ending boundary, so the partial run is emitted tagged "interrupted" rather
    // than dropped — it is still verbatim keyboard input. A pending suspect mark
    // does not survive the reset: each reset re-arms it (or not) from its own
    // dropped-partial signal.
    public void Reset(ResetReason reason)
    {
        _nextWordSuspect = false;
        Flush(reason == ResetReason.Enter ? "enter" : "interrupted");
    }

    private void Flush(string closure)
    {
        if (_slots.Count == 0) return;
        var typed = new StringBuilder();
        var final = new StringBuilder();
        var history = new StringBuilder();
        for (int i = 0; i < _slots.Count; i++)
        {
            Slot s = _slots[i];
            typed.Append(s.FirstTyped).Append(s.TypedSeparator);
            final.Append(s.Final).Append(s.FinalSeparator);
            if (s.Transitions.Count > 0)
                AppendHistory(history, i, s);
        }
        string timing = BuildTiming();
        _slots.Clear();
        Completed?.Invoke(new SentenceRecord(
            typed.ToString(), final.ToString(), history.ToString(), closure, timing));
    }

    // The typing rhythm: the first slot's gap is "0" and each later slot's is the ms
    // elapsed since the previous slot's commit, comma-joined ("0,340,1220"). Empty
    // when every slot's timestamp is 0 (the caller had no clock) — the whole string
    // is unavailable rather than a run of zeros.
    private string BuildTiming()
    {
        bool anyStamp = false;
        foreach (Slot s in _slots)
            if (s.TimestampMs != 0) { anyStamp = true; break; }
        if (!anyStamp) return string.Empty;

        var timing = new StringBuilder();
        for (int i = 0; i < _slots.Count; i++)
        {
            if (i > 0) timing.Append(',');
            long gap = i == 0 ? 0 : _slots[i].TimestampMs - _slots[i - 1].TimestampMs;
            timing.Append(gap);
        }
        return timing.ToString();
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
