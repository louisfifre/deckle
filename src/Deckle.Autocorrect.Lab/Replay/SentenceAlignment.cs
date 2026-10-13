using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// How one corpus record aligned back into slots. RepairedFromFinal: a record
// without the history property, whose final side was recovered by re-tokenizing
// the Final string. Unusable: the record's slot indexing could not be trusted, so
// it is skipped rather than mis-judged.
public enum AlignmentStatus { Aligned, RepairedFromFinal, Unusable }

public readonly record struct AlignmentResult(
    AlignmentStatus Status,
    IReadOnlyList<string> Typed,
    IReadOnlyList<string> Final)
{
    public bool Usable => Status != AlignmentStatus.Unusable;
}

// Turns one corpus record back into the two slot-aligned word lists the replay
// consumes: what was typed at each slot, and what the sentence ended with there.
// The alignment is the inverse of SentenceCorpus.Flush.
//
// The typed side is re-tokenized with WordBoundaries.Tokenize, the SAME tokenizer
// the live tracker used to cut the slots, so the split reproduces the original
// slots exactly (WordBoundariesTests pins the parity).
//
// The final side comes from one of two paths, never a silent copy of typed:
//   • History present: overlay each changed slot's final form off the History
//     field, whose « #index » entries are keyed to the same slots.
//   • History property ABSENT (an older record): re-tokenize the Final string,
//     but only when it yields exactly as many tokens as the typed side. An
//     elision-apostrophe re-split, or any other token-count drift, breaks the
//     slot map and the record is dropped rather than judged against a
//     fabricated final = typed.
public static class SentenceAlignment
{
    public static AlignmentResult Align(CorpusEntry entry)
    {
        SentenceCorpus.SentenceRecord record = entry.Record;
        var typed = WordBoundaries.Tokenize(record.Typed).ToList();
        if (typed.Count == 0)
            return Unusable();

        if (entry.HistoryPresent)
        {
            var final = new List<string>(typed); // unchanged slots stay as typed
            foreach ((int index, string firstTyped, string form) in ParseHistoryEntries(record.History))
            {
                // The slot indexing holds only while the history's first-typed form
                // still matches the token at that index. A mismatch means a
                // corrupted typed string (a user re-edit into an elided form fuses
                // two slots, « deavoir ») and every later index is off by one.
                if (index < 0 || index >= typed.Count ||
                    !string.Equals(typed[index], firstTyped, StringComparison.Ordinal))
                    return Unusable();
                final[index] = form;
            }

            return new AlignmentResult(AlignmentStatus.Aligned, typed, final);
        }

        var finalTokens = WordBoundaries.Tokenize(record.Final).ToList();
        if (finalTokens.Count != typed.Count)
            return Unusable();
        return new AlignmentResult(AlignmentStatus.RepairedFromFinal, typed, finalTokens);
    }

    private static AlignmentResult Unusable() =>
        new(AlignmentStatus.Unusable, Array.Empty<string>(), Array.Empty<string>());

    // Yields (slotIndex, firstTyped, finalForm) for every changed slot in a history
    // string: "#<i>=<firsttyped>»<stage>:<form>[»<stage>:<form>…]" entries,
    // pipe-joined (see SentenceCorpus.AppendHistory). The first-typed form sits
    // between '=' and the first « » »; the final form is the LAST transition, the
    // piece after the last « » » and its ':'. Word forms and stage tags never
    // contain '|', '»' or ':', so the split is unambiguous.
    internal static IEnumerable<(int Index, string FirstTyped, string Form)> ParseHistoryEntries(string history)
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

            int firstArrow = entry.IndexOf('»', equals + 1);
            if (firstArrow < 0)
                continue;
            string firstTyped = entry[(equals + 1)..firstArrow];

            int lastArrow = entry.LastIndexOf('»');
            string tagged = entry[(lastArrow + 1)..]; // "<stage>:<form>"
            int colon = tagged.IndexOf(':');
            if (colon < 0 || colon == tagged.Length - 1)
                continue;

            yield return (index, firstTyped, tagged[(colon + 1)..]);
        }
    }

    internal static IEnumerable<(int Index, string Form)> ParseHistory(string history)
    {
        foreach ((int index, _, string form) in ParseHistoryEntries(history))
            yield return (index, form);
    }
}
