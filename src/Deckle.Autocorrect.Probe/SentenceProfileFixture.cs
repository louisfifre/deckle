namespace Deckle.Autocorrect.Probe;

internal static class SentenceProfileFixture
{
    public const int Seed = 20260730;

    public static IReadOnlyList<int> CandidateCounts { get; } = [2, 4, 8, 13];

    public static ClosedSentenceTransaction Transaction { get; } = CreateTransaction();

    public static ProfileCandidateSet Candidates(int candidateCount, int rotation)
    {
        if (!CandidateCounts.Contains(candidateCount))
            throw new ArgumentOutOfRangeException(nameof(candidateCount));

        var canonical = new List<(int Index, string Text)>(candidateCount)
        {
            (0, Transaction.Literal),
        };
        for (int index = 0; index < candidateCount - 1; index++)
            canonical.Add((index + 1, Apply(Transaction.Literal, Transaction.Edits[index])));

        int offset = Math.Abs(rotation % candidateCount);
        (int Index, string Text)[] rotated = canonical
            .Skip(offset)
            .Concat(canonical.Take(offset))
            .ToArray();
        return new ProfileCandidateSet(
            rotated.Select(static item => item.Text).ToArray(),
            rotated.Select(static item => item.Index).ToArray());
    }

    public static IReadOnlyList<int> StrataForRound(int round)
    {
        if (round < 0)
            throw new ArgumentOutOfRangeException(nameof(round));

        int offset = (round + Seed) % CandidateCounts.Count;
        return CandidateCounts
            .Skip(offset)
            .Concat(CandidateCounts.Take(offset))
            .ToArray();
    }

    public static int CandidateRotation(int round, int candidateCount)
    {
        if (round < 0)
            throw new ArgumentOutOfRangeException(nameof(round));
        if (!CandidateCounts.Contains(candidateCount))
            throw new ArgumentOutOfRangeException(nameof(candidateCount));

        // Five is coprime with every retained stratum (2, 4, 8, 13), so the
        // rotation covers each candidate offset before repeating. It is
        // intentionally independent from the Latin stratum position.
        return (Seed + (round * 5) + (candidateCount * 3)) % candidateCount;
    }

    private static ClosedSentenceTransaction CreateTransaction()
    {
        const string literal =
            "Cette petite phrase locale contient plusieurs mots simples pour mesurer exactement notre juge rapide.";
        string[] words = literal.TrimEnd('.').Split(' ');
        (string Original, string Replacement)[] variants =
        [
            ("Cette", "Cete"),
            ("petite", "petites"),
            ("phrase", "phrases"),
            ("locale", "local"),
            ("contient", "contiens"),
            ("plusieurs", "plusieur"),
            ("mots", "mot"),
            ("simples", "simple"),
            ("pour", "afin"),
            ("mesurer", "mesuré"),
            ("exactement", "précisément"),
            ("notre", "nôtre"),
        ];

        var edits = new SentenceEditCandidate[variants.Length];
        int searchStart = 0;
        for (int index = 0; index < variants.Length; index++)
        {
            (string original, string replacement) = variants[index];
            int start = literal.IndexOf(original, searchStart, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException($"Profile token not found: {original}");
            edits[index] = new SentenceEditCandidate(
                index,
                start,
                original.Length,
                replacement);
            searchStart = start + original.Length;
        }

        return new ClosedSentenceTransaction(literal, words, edits);
    }

    private static string Apply(string literal, SentenceEditCandidate edit)
    {
        if (edit.Start < 0 || edit.Length < 0 || edit.Start + edit.Length > literal.Length)
            throw new InvalidOperationException("Profile edit is outside the exact literal.");
        string candidate = string.Concat(
            literal.AsSpan(0, edit.Start),
            edit.Replacement,
            literal.AsSpan(edit.Start + edit.Length));
        if (!literal.AsSpan(0, edit.Start).SequenceEqual(candidate.AsSpan(0, edit.Start)))
            throw new InvalidOperationException("Profile edit changed the literal prefix.");
        return candidate;
    }
}

internal sealed record ProfileCandidateSet(
    IReadOnlyList<string> Texts,
    IReadOnlyList<int> CanonicalIndices);
