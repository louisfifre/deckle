namespace Deckle.Autocorrect.Probe;

internal static class SentenceOrderAblationFixture
{
    public const int Seed = 20260730;
    public const int WarmupCycles = 2;
    public const int LatencyBlocks = 20;
    public const double ContinuousHotForwardP95ReferenceMilliseconds = 300.0;

    private static readonly SentenceOrderAblationMethod[][] MethodPermutations =
    [
        [SentenceOrderAblationMethod.Forward, SentenceOrderAblationMethod.Reverse, SentenceOrderAblationMethod.Combined],
        [SentenceOrderAblationMethod.Forward, SentenceOrderAblationMethod.Combined, SentenceOrderAblationMethod.Reverse],
        [SentenceOrderAblationMethod.Reverse, SentenceOrderAblationMethod.Forward, SentenceOrderAblationMethod.Combined],
        [SentenceOrderAblationMethod.Reverse, SentenceOrderAblationMethod.Combined, SentenceOrderAblationMethod.Forward],
        [SentenceOrderAblationMethod.Combined, SentenceOrderAblationMethod.Forward, SentenceOrderAblationMethod.Reverse],
        [SentenceOrderAblationMethod.Combined, SentenceOrderAblationMethod.Reverse, SentenceOrderAblationMethod.Forward],
    ];

    private static readonly int[] LatencyPermutationOrdinals =
    [
        2, 3, 4, 5, 0, 1,
        2, 3, 4, 5, 0, 1,
        2, 3, 4, 5, 0, 1,
        2, 5,
    ];

    private static readonly int[][] RepetitionPermutationOrdinals =
    [
        [0, 3, 4],
        [1, 2, 5],
        [2, 5, 1],
        [3, 4, 0],
        [4, 0, 3],
        [5, 1, 2],
    ];

    public static IReadOnlySet<string> DisagreementCaseIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ou_question",
            "sur_certain",
            "participle_adjective_trap",
            "masculine_plural_participle",
            "masculine_plural_subject",
            "literal_la_build",
            "literal_a_variable",
            "duplicate_letter",
            "qu_a_auxiliary",
        };

    public static int QualityRepetitions(string caseId) =>
        DisagreementCaseIds.Contains(caseId) ? 3 : 1;

    public static IReadOnlyList<SentenceOrderAblationMethod> QualityMethods(
        int caseIndex,
        int repetition)
    {
        if (caseIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(caseIndex));
        if (repetition < 0 || repetition >= 3)
            throw new ArgumentOutOfRangeException(nameof(repetition));

        int basePermutation = (Seed + caseIndex) % MethodPermutations.Length;
        int permutation = RepetitionPermutationOrdinals[basePermutation][repetition];
        return MethodPermutations[permutation];
    }

    public static IReadOnlyList<SentenceOrderAblationMethod> LatencyMethods(int block)
    {
        if (block < 0 || block >= LatencyBlocks)
            throw new ArgumentOutOfRangeException(nameof(block));

        return MethodPermutations[LatencyPermutationOrdinals[block]];
    }
}

internal enum SentenceOrderAblationMethod
{
    Forward,
    Reverse,
    Combined,
}
