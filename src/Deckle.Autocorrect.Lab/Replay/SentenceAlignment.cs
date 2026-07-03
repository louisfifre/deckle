using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// Turns one corpus record back into the two slot-aligned word-lists the replay
// core consumes: what was typed at each slot, and what the sentence ended with
// there. The alignment is the inverse of SentenceCorpus.Flush.
//
// The typed side is re-tokenized with WordBoundaries.Tokenize — the SAME
// tokenizer the live tracker used to cut the slots, so the split reproduces the
// original slots exactly (the parity guarantee WordBoundariesTests pins). The
// final side is NOT re-tokenized: a correction can insert an elision apostrophe
// (« cetait » → « c'était ») which would re-split one slot into two and break the
// index alignment. Instead the final forms are read off the History field, whose
// « #index » entries are keyed to the same slots — each changed slot's final form
// is its last recorded transition. Unchanged slots keep their typed form.
public static class SentenceAlignment
{
    public static (IReadOnlyList<string> Typed, IReadOnlyList<string> Final) Align(
        SentenceCorpus.SentenceRecord record)
    {
        var typed = WordBoundaries.Tokenize(record.Typed).ToList();
        var final = new List<string>(typed); // unchanged slots stay as typed

        foreach ((int index, string form) in ParseHistory(record.History))
            if (index >= 0 && index < final.Count) // ignore an out-of-range slot ref defensively
                final[index] = form;

        return (typed, final);
    }

    // Yields (slotIndex, finalForm) for every changed slot in a history string:
    // "#<i>=<firsttyped>»<stage>:<form>[»<stage>:<form>…]" entries, pipe-joined
    // (see SentenceCorpus.AppendHistory). A slot's final form is its LAST
    // transition, so the walk takes the piece after the last « » ». Word forms
    // and stage tags never contain '|', '»' or ':', so the split is unambiguous.
    internal static IEnumerable<(int Index, string Form)> ParseHistory(string history)
    {
        if (string.IsNullOrEmpty(history))
            yield break;

        foreach (string entry in history.Split('|'))
        {
            int equals = entry.IndexOf('=');
            if (equals < 2 || entry[0] != '#')
                continue;
            if (!int.TryParse(entry[1..equals], out int index))
                continue;

            int lastArrow = entry.LastIndexOf('»');
            if (lastArrow < 0)
                continue;

            string tagged = entry[(lastArrow + 1)..]; // "<stage>:<form>"
            int colon = tagged.IndexOf(':');
            if (colon < 0 || colon == tagged.Length - 1)
                continue;

            yield return (index, tagged[(colon + 1)..]);
        }
    }
}
