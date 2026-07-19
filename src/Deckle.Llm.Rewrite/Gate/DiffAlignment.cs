using System.Text;

namespace Deckle.Llm.Rewrite;

// ─── Monotone alignment ──────────────────────────────────────────────────────
//
// Dynamic-programming alignment of the original and rewritten token
// sequences. Strictly monotone by construction: the DP only ever advances
// through both sequences in order, so no alignment it can produce moves a
// span — reordering surfaces as a replacement/deletion pair that no rule
// admits, and the paragraph is rejected. That is the framing decision
// "ordre monotone strict" made mechanical.
//
// Every transition is one ruling of the gate. Allowed transitions carry
// small costs that prefer the tightest script (matches over replacements,
// small groups over large); disallowed ones carry a penalty larger than any
// possible all-allowed path (tokenCount + 10 — an all-allowed path costs
// under 1 per consumed token). The optimum therefore contains a violation
// only when NO violation-free alignment exists, which makes "all edits
// allowed" equivalent to "the gate's rules can explain the whole diff" —
// the all-or-nothing verdict, with the explaining script recovered by
// backtracking either way.
//
// Group replacements (up to 3→3 word tokens) compare the concatenated
// normalized forms, which is what lets phonetic re-segmentation through:
// "samarreter" → "sans m'arrêter" concatenates to a bounded form distance,
// while "voiture" → "véhicule" does not. Tokens containing digits never
// pass a non-identical replacement — numbers must not drift.

internal static class DiffAlignment
{
    // Group horizons. Replacements merge/split at most 3 tokens a side;
    // deletions extend to the longest filler phrase.
    const int ReplaceGroupMax = 3;

    internal static List<DiffEdit> Align(IReadOnlyList<GateToken> a, IReadOnlyList<GateToken> b)
    {
        int n = a.Count;
        int m = b.Count;
        int deleteGroupMax = Math.Max(ReplaceGroupMax, GateLexicon.MaxFillerPhraseLength);
        double violationPenalty = n + m + 10;

        var cost = new double[n + 1, m + 1];
        var parent = new Parent[n + 1, m + 1];
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= m; j++)
                cost[i, j] = double.PositiveInfinity;
        cost[0, 0] = 0;

        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                double here = cost[i, j];
                if (double.IsPositiveInfinity(here)) continue;

                // Insertion of B[j].
                if (j < m)
                {
                    var ins = ClassifyInsertion(b[j]);
                    Relax(cost, parent, i, j + 1, here + Cost(ins, 1, violationPenalty), new Parent(0, 1, ins));
                }

                // Deletion of A[i..i+k).
                for (int k = 1; k <= deleteGroupMax && i + k <= n; k++)
                {
                    var del = ClassifyDeletion(a, i, k);
                    if (del is null) continue;
                    Relax(cost, parent, i + k, j, here + Cost(del.Value, k, violationPenalty), new Parent((byte)k, 0, del.Value));
                }

                // Match / replacement of A[i..i+ga) with B[j..j+gb).
                for (int ga = 1; ga <= ReplaceGroupMax && i + ga <= n; ga++)
                {
                    for (int gb = 1; gb <= ReplaceGroupMax && j + gb <= m; gb++)
                    {
                        var rep = ClassifyReplacement(a, i, ga, b, j, gb, out int dist);
                        if (rep is null) continue;
                        double stepCost = rep.Value switch
                        {
                            DiffEditRuling.Match => 0,
                            DiffEditRuling.AllowedReplacement => 0.25 + 0.25 * dist + 0.1 * (ga + gb - 2),
                            _ => violationPenalty + 0.25,
                        };
                        Relax(cost, parent, i + ga, j + gb, here + stepCost, new Parent((byte)ga, (byte)gb, rep.Value));
                    }
                }
            }
        }

        return Backtrack(a, b, parent, n, m);
    }

    readonly record struct Parent(byte ConsumedA, byte ConsumedB, DiffEditRuling Ruling);

    static void Relax(double[,] cost, Parent[,] parent, int i, int j, double candidate, Parent via)
    {
        if (candidate < cost[i, j])
        {
            cost[i, j] = candidate;
            parent[i, j] = via;
        }
    }

    static double Cost(DiffEditRuling ruling, int tokenCount, double violationPenalty) => ruling switch
    {
        DiffEditRuling.AllowedInsertion => 0.5,
        DiffEditRuling.AllowedDeletion => 0.5 * tokenCount,
        DiffEditRuling.RejectedInsertion => violationPenalty + 0.5,
        DiffEditRuling.RejectedDeletion => violationPenalty + 0.5,
        _ => throw new ArgumentOutOfRangeException(nameof(ruling), ruling, null),
    };

    static DiffEditRuling ClassifyInsertion(GateToken token)
    {
        if (!token.IsWord)
            return GateLexicon.IsInsertablePunctuation(token.Text)
                ? DiffEditRuling.AllowedInsertion
                : DiffEditRuling.RejectedInsertion; // markdown/formatting characters
        return GateLexicon.IsFunctionWord(token.Normalized)
            ? DiffEditRuling.AllowedInsertion
            : DiffEditRuling.RejectedInsertion;
    }

    /// <summary>Null means "no such transition" — multi-token deletions exist
    /// only in their allowed forms; a disallowed deletion is expressed token
    /// by token so the script shows exactly which words could not go.</summary>
    static DiffEditRuling? ClassifyDeletion(IReadOnlyList<GateToken> a, int start, int count)
    {
        if (count == 1)
        {
            var token = a[start];
            if (token.IsWord && GateLexicon.IsFiller(token.Normalized))
                return DiffEditRuling.AllowedDeletion;
            if (IsAdjacentDuplicate(a, start))
                return DiffEditRuling.AllowedDeletion;
            return DiffEditRuling.RejectedDeletion;
        }

        // Filler phrase ("du coup", "je veux dire").
        var span = new GateToken[count];
        for (int k = 0; k < count; k++) span[k] = a[start + k];
        if (GateLexicon.IsFillerPhrase(span)) return DiffEditRuling.AllowedDeletion;

        // Phrase duplicate: the span repeats the tokens just before it
        // ("tu vois tu vois").
        if (start - count >= 0)
        {
            bool repeats = true;
            for (int k = 0; k < count; k++)
            {
                if (!SameForm(a[start - count + k], a[start + k])) { repeats = false; break; }
            }
            if (repeats) return DiffEditRuling.AllowedDeletion;
        }

        return null;
    }

    static bool IsAdjacentDuplicate(IReadOnlyList<GateToken> a, int index)
    {
        var token = a[index];
        if (index > 0 && SameForm(a[index - 1], token)) return true;
        if (index + 1 < a.Count && SameForm(a[index + 1], token)) return true;
        return false;
    }

    static bool SameForm(GateToken x, GateToken y)
        => x.IsWord == y.IsWord
        && string.Equals(x.Normalized, y.Normalized, StringComparison.Ordinal);

    /// <summary>Null means "no such transition". 1→1 pairs always classify
    /// (allowed or rejected — a rejected substitution should read as one
    /// replacement, not a delete/insert pair); larger groups exist only in
    /// their allowed form.</summary>
    static DiffEditRuling? ClassifyReplacement(
        IReadOnlyList<GateToken> a, int ai, int ga,
        IReadOnlyList<GateToken> b, int bj, int gb,
        out int dist)
    {
        dist = 0;

        if (ga == 1 && gb == 1)
        {
            var x = a[ai];
            var y = b[bj];

            if (x.IsWord != y.IsWord) return null; // expressed as delete + insert

            if (string.Equals(x.Text, y.Text, StringComparison.Ordinal))
                return DiffEditRuling.Match;

            if (!x.IsWord)
                return GateLexicon.IsInsertablePunctuation(y.Text)
                    ? DiffEditRuling.AllowedReplacement  // re-punctuation
                    : DiffEditRuling.RejectedReplacement; // punctuation → formatting character

            if (string.Equals(x.Normalized, y.Normalized, StringComparison.Ordinal))
                return DiffEditRuling.AllowedReplacement; // accent / case / elision repair

            if (x.HasDigit || y.HasDigit)
                return DiffEditRuling.RejectedReplacement; // numbers never drift

            dist = Levenshtein(x.Normalized, y.Normalized);
            return dist <= RewriteDiffGate.FormDistanceBound(Math.Max(x.Normalized.Length, y.Normalized.Length))
                ? DiffEditRuling.AllowedReplacement
                : DiffEditRuling.RejectedReplacement;
        }

        // Merge/split groups: words only, compared on concatenated forms.
        for (int k = 0; k < ga; k++) if (!a[ai + k].IsWord) return null;
        for (int k = 0; k < gb; k++) if (!b[bj + k].IsWord) return null;

        // A group replacement is a re-segmentation of DIFFERENT forms: a
        // word that survives verbatim must be consumed as a match, never
        // inside a group. Without this, an identical neighbor dilutes the
        // concatenated distance and smuggles a substitution through —
        // measured on the 2026-07-19 eval ("pas lisible" → "peu lisible"
        // passing as one 2→2 group).
        for (int k = 0; k < ga; k++)
            for (int l = 0; l < gb; l++)
                if (SameForm(a[ai + k], b[bj + l])) return null;

        string left = ConcatNormalized(a, ai, ga);
        string right = ConcatNormalized(b, bj, gb);

        bool hasDigit = false;
        for (int k = 0; k < ga && !hasDigit; k++) hasDigit = a[ai + k].HasDigit;
        for (int k = 0; k < gb && !hasDigit; k++) hasDigit = b[bj + k].HasDigit;
        if (hasDigit)
            return string.Equals(left, right, StringComparison.Ordinal)
                ? DiffEditRuling.AllowedReplacement
                : null;

        dist = Levenshtein(left, right);
        return dist <= RewriteDiffGate.FormDistanceBound(Math.Max(left.Length, right.Length))
            ? DiffEditRuling.AllowedReplacement
            : null;
    }

    static string ConcatNormalized(IReadOnlyList<GateToken> tokens, int start, int count)
    {
        var sb = new StringBuilder();
        for (int k = 0; k < count; k++) sb.Append(tokens[start + k].Normalized);
        return sb.ToString();
    }

    static int Levenshtein(string x, string y)
    {
        int lx = x.Length;
        int ly = y.Length;
        if (lx == 0) return ly;
        if (ly == 0) return lx;

        Span<int> previous = ly <= 128 ? stackalloc int[ly + 1] : new int[ly + 1];
        Span<int> current = ly <= 128 ? stackalloc int[ly + 1] : new int[ly + 1];
        for (int j = 0; j <= ly; j++) previous[j] = j;

        for (int i = 1; i <= lx; i++)
        {
            current[0] = i;
            for (int j = 1; j <= ly; j++)
            {
                int substitution = previous[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }
            var swap = previous; previous = current; current = swap;
        }
        return previous[ly];
    }

    static List<DiffEdit> Backtrack(
        IReadOnlyList<GateToken> a, IReadOnlyList<GateToken> b,
        Parent[,] parent, int n, int m)
    {
        // Walk parents from the end, then reverse and merge match runs so the
        // script reads "…equal span, one edit, equal span…" instead of one
        // entry per untouched word.
        var reversed = new List<(Parent Via, int I, int J)>();
        int ci = n, cj = m;
        while (ci > 0 || cj > 0)
        {
            var via = parent[ci, cj];
            ci -= via.ConsumedA;
            cj -= via.ConsumedB;
            reversed.Add((via, ci, cj));
        }

        var edits = new List<DiffEdit>(reversed.Count);
        for (int k = reversed.Count - 1; k >= 0; k--)
        {
            var (via, i, j) = reversed[k];
            string original = JoinTokens(a, i, via.ConsumedA);
            string rewritten = JoinTokens(b, j, via.ConsumedB);

            if (via.Ruling == DiffEditRuling.Match
                && edits.Count > 0
                && edits[^1].Ruling == DiffEditRuling.Match)
            {
                var last = edits[^1];
                edits[^1] = last with
                {
                    Original = last.Original + " " + original,
                    Rewritten = last.Rewritten + " " + rewritten,
                };
                continue;
            }

            edits.Add(new DiffEdit(via.Ruling, original, rewritten));
        }
        return edits;
    }

    static string JoinTokens(IReadOnlyList<GateToken> tokens, int start, int count)
    {
        if (count == 0) return "";
        if (count == 1) return tokens[start].Text;
        var sb = new StringBuilder();
        for (int k = 0; k < count; k++)
        {
            if (k > 0) sb.Append(' ');
            sb.Append(tokens[start + k].Text);
        }
        return sb.ToString();
    }
}
