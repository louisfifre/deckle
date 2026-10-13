using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// Mines mistouch families off the typed-sentence corpus: recurrent MECHANICAL
// keyboard slips (a wrong key hit near the intended one, a dropped space after
// a comma), as opposed to spelling faults, which stay the corrector's domain.
// Two evidence lanes feed one signature space: the repaired lane (the errors the
// user backspaced into and retyped) and the residue lane (non-words still
// standing in the final text). A family is a signature with its accumulated
// evidence; distinct days are its recurrence signal. The miner measures and
// activates nothing: a reviewer approves a family before the engine uses it.
public static class MistouchMiner
{
    // FromIsWord: the faulty form is itself a valid word, a slip the commit
    // stage's non-word trigger cannot see. Ambiguous: several hypotheses
    // repaired it.
    public readonly record struct MistouchEvidence(
        string From, string To, string Process, string Day,
        bool Repaired, bool FromIsWord, bool Ambiguous);

    // FromWordCount is the review's word-choice alarm: evidence whose faulty
    // form is itself a valid word (« le→la ») smells of grammar or rewording,
    // not of a key slip.
    public sealed record MistouchFamily(
        string Signature,
        string Kind,
        int RepairedCount,
        int ResidueCount,
        int DistinctDays,
        int AmbiguousCount,
        int FromWordCount,
        IReadOnlyList<MistouchEvidence> Examples)
    {
        public int Evidence => RepairedCount + ResidueCount;
    }

    // Unclassified keeps the user repairs no signature reads, so the report
    // shows what mining missed.
    public sealed record MiningResult(
        IReadOnlyList<MistouchFamily> Families,
        IReadOnlyList<MistouchEvidence> Unclassified);

    private const int ExamplesPerFamily = 8;

    public static MiningResult Mine(
        IEnumerable<CorpusEntry> entries,
        IFrequencyLexicon french,
        IFrequencyLexicon? english)
    {
        var evidence = new List<(string Signature, string Kind, MistouchEvidence Occurrence)>();
        var unclassified = new List<MistouchEvidence>();
        bool IsWord(string form) =>
            french.Contains(form.ToLowerInvariant())
            || english?.Contains(form.ToLowerInvariant()) == true;

        foreach (CorpusEntry entry in entries)
        {
            foreach ((string from, string to) in UserRepairs(entry.Record.History))
            {
                var occurrence = new MistouchEvidence(
                    from, to, entry.Process, entry.Day,
                    Repaired: true, FromIsWord: IsWord(from), Ambiguous: false);
                if (ClassifySingleEdit(from, to) is (string signature, string kind))
                    evidence.Add((signature, kind, occurrence));
                else
                    unclassified.Add(occurrence);
            }

            MineResidue(entry, IsWord, evidence);
        }

        var families = evidence
            .GroupBy(e => e.Signature)
            .Select(g => new MistouchFamily(
                g.Key,
                g.First().Kind,
                RepairedCount: g.Count(e => e.Occurrence.Repaired),
                ResidueCount: g.Count(e => !e.Occurrence.Repaired),
                DistinctDays: g.Select(e => e.Occurrence.Day).Where(d => d.Length > 0).Distinct().Count(),
                AmbiguousCount: g.Count(e => e.Occurrence.Ambiguous),
                FromWordCount: g.Count(e => e.Occurrence.FromIsWord),
                Examples: g.Select(e => e.Occurrence).Take(ExamplesPerFamily).ToList()))
            .OrderByDescending(f => f.Evidence)
            .ThenBy(f => f.Signature, StringComparer.Ordinal)
            .ToList();

        return new MiningResult(families, unclassified);
    }

    // ── Repaired lane ────────────────────────────────────────────────────

    // Yields (before, retyped) for every « user: » transition in a history
    // string. Later stage transitions on the same slot (the retype's own commit
    // repair) are the corrector's work, not evidence.
    internal static IEnumerable<(string From, string To)> UserRepairs(string history)
    {
        if (string.IsNullOrEmpty(history))
            yield break;

        foreach (string slot in history.Split('|'))
        {
            int equals = slot.IndexOf('=');
            if (equals < 2 || slot[0] != '#')
                continue;

            string[] steps = slot[(equals + 1)..].Split('»');
            // steps[0] is the first-typed form; each later step is "stage:form".
            string previous = steps[0];
            for (int i = 1; i < steps.Length; i++)
            {
                int colon = steps[i].IndexOf(':');
                if (colon <= 0 || colon == steps[i].Length - 1)
                    continue;
                string form = steps[i][(colon + 1)..];
                if (steps[i][..colon] == "user" && previous.Length > 0 && form.Length > 0)
                    yield return (previous, form);
                previous = form;
            }
        }
    }

    // Null for a diacritics fix (the restorer's domain) and for anything wider
    // than one edit (a rewording). Case is folded: the mechanics live on the
    // key, not the shift.
    internal static (string Signature, string Kind)? ClassifySingleEdit(string from, string to)
    {
        string a = from.ToLowerInvariant(), b = to.ToLowerInvariant();
        if (a == b) return null;
        if (AccentFolding.Fold(a) == AccentFolding.Fold(b)) return null;

        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        int suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix
               && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        string ma = a[prefix..^suffix], mb = b[prefix..^suffix];

        if (ma.Length == 1 && mb.Length == 1)
            return ($"sub {ma}→{mb}", "substitution");

        if (ma.Length == 2 && mb.Length == 2 && ma[0] == mb[1] && ma[1] == mb[0])
            return ($"transposed {ma}", "transposition");

        if (ma.Length == 1 && mb.Length == 0)
        {
            char extra = ma[0];
            char before = prefix > 0 ? a[prefix - 1] : '^';
            return extra == before
                ? ($"doubled {extra}", "doubling")
                : ($"extra {Show(extra)} after {Show(before)}", "extra");
        }

        if (ma.Length == 0 && mb.Length == 1)
        {
            char missing = mb[0];
            char before = prefix > 0 ? b[prefix - 1] : '^';
            return ($"dropped {Show(missing)} after {Show(before)}", "omission");
        }

        return null;
    }

    // ── Residue lane ─────────────────────────────────────────────────────

    // Errors nobody fixed, still standing in the final text. The boundary
    // patterns (the « ; »-for-apostrophe elision, the missing space behind a
    // comma or period) are read off the raw string: the tracker split them
    // across slots, where no token-level view can see them.
    private static void MineResidue(
        CorpusEntry entry,
        Func<string, bool> isWord,
        List<(string, string, MistouchEvidence)> evidence)
    {
        string final = entry.Record.Final;
        MistouchEvidence Occurrence(string from, string to, bool fromIsWord, bool ambiguous) =>
            new(from, to, entry.Process, entry.Day,
                Repaired: false, FromIsWord: fromIsWord, Ambiguous: ambiguous);

        foreach (string token in WordBoundaries.Tokenize(final))
        {
            string lower = token.ToLowerInvariant();
            if (lower.Length < 2 || !lower.All(char.IsAsciiLetter) || isWord(lower))
                continue;

            var repairs = new List<(string Signature, string Kind, string To)>();
            for (int i = 0; i < lower.Length; i++)
            {
                foreach (char n in QwertyAdjacency.Neighbours(lower[i]))
                {
                    string candidate = lower[..i] + n + lower[(i + 1)..];
                    if (isWord(candidate))
                        repairs.Add(($"sub {lower[i]}→{n}", "substitution", candidate));
                }
                if (i > 0 && lower[i] == lower[i - 1])
                {
                    string candidate = lower[..i] + lower[(i + 1)..];
                    if (isWord(candidate))
                        repairs.Add(($"doubled {lower[i]}", "doubling", candidate));
                }
            }

            bool ambiguous = repairs.Select(r => r.To).Distinct().Count() > 1;
            foreach (var (signature, kind, to) in repairs.DistinctBy(r => (r.Signature, r.To)))
                evidence.Add((signature, kind, Occurrence(lower, to, fromIsWord: false, ambiguous)));
        }

        for (int i = 1; i < final.Length - 1; i++)
        {
            char p = final[i];
            if (p is not (';' or ',' or '.')) continue;
            if (!char.IsLetter(final[i - 1]) || !char.IsLetter(final[i + 1])) continue;

            string left = LetterRunBefore(final, i).ToLowerInvariant();
            string right = LetterRunAfter(final, i).ToLowerInvariant();

            if (p == ';' && WordBoundaries.IsElisionPrefix(left) && isWord(right))
            {
                // « qu;il »: ';' is the key left of the apostrophe on QWERTY-US.
                evidence.Add(("sub ;→'", "substitution",
                    Occurrence($"{left};{right}", $"{left}'{right}", fromIsWord: false, ambiguous: false)));
            }
            else if (p is ',' or '.' && isWord(left) && isWord(right))
            {
                // Both sides must be words, so a file name such as app.jsonl
                // never qualifies.
                evidence.Add(($"dropped space after {p}", "omission",
                    Occurrence($"{left}{p}{right}", $"{left}{p} {right}", fromIsWord: false, ambiguous: false)));
            }
        }
    }

    private static string LetterRunBefore(string s, int at)
    {
        int start = at;
        while (start > 0 && char.IsLetter(s[start - 1])) start--;
        return s[start..at];
    }

    private static string LetterRunAfter(string s, int at)
    {
        int end = at + 1;
        while (end < s.Length && char.IsLetter(s[end])) end++;
        return s[(at + 1)..end];
    }

    private static string Show(char c) => c switch
    {
        ' ' => "space",
        '^' => "start",
        _ => c.ToString(),
    };
}
