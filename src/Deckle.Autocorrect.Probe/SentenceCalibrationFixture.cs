namespace Deckle.Autocorrect.Probe;

internal static class SentenceCalibrationFixture
{
    public const int OrdinaryRounds = 20;
    public const int CalibrationBlocksPerStratum = 16;

    public static IReadOnlyList<int> CalibrationCandidateCounts { get; } = [2];

    // This frozen assignment was chosen independently from the Latin stratum
    // schedule and candidate rotations. Across sixteen blocks it balances both
    // halves, parity, every modulo-four Latin position, and paired modulo-eight
    // rotations. The inner sequence is the exact complement.
    private static IReadOnlySet<int> ProfiledOuterBlocks { get; } =
        new HashSet<int> { 0, 2, 5, 7, 9, 11, 12, 14 };

    // A separate frozen permutation breaks the affine coupling between the
    // Latin call position and candidate rotation. Its joint balance is tested
    // for every retained stratum rather than assumed from marginal counts.
    private static IReadOnlyList<int> OrdinaryRotationOrdinals { get; } =
        [7, 3, 9, 10, 2, 17, 16, 11, 5, 6, 18, 13, 12, 15, 19, 4, 1, 0, 14, 8];

    public static IReadOnlyList<int> OrdinaryStrataForRound(int round) =>
        SentenceProfileFixture.StrataForRound(round);

    public static int OrdinaryRotation(int round, int candidateCount)
    {
        if (round is < 0 or >= OrdinaryRounds)
            throw new ArgumentOutOfRangeException(nameof(round));

        return SentenceProfileFixture.CandidateRotation(
            OrdinaryRotationOrdinals[round],
            candidateCount);
    }

    internal static IReadOnlyList<int> RotationOrdinalsForTests() =>
        OrdinaryRotationOrdinals;

    public static int CalibrationRotation(int block, int candidateCount) =>
        SentenceProfileFixture.CandidateRotation(block + 97, candidateCount);

    public static bool IsProfiledOuter(int block)
    {
        if (block is < 0 or >= CalibrationBlocksPerStratum)
            throw new ArgumentOutOfRangeException(nameof(block));

        return ProfiledOuterBlocks.Contains(block);
    }

    public static IReadOnlyList<SentenceCalibrationMethod> MethodsForBlock(int block) =>
        IsProfiledOuter(block)
            ?
            [
                SentenceCalibrationMethod.Profiled,
                SentenceCalibrationMethod.Ordinary,
                SentenceCalibrationMethod.Ordinary,
                SentenceCalibrationMethod.Profiled,
            ]
            :
            [
                SentenceCalibrationMethod.Ordinary,
                SentenceCalibrationMethod.Profiled,
                SentenceCalibrationMethod.Profiled,
                SentenceCalibrationMethod.Ordinary,
            ];
}

internal enum SentenceCalibrationMethod
{
    Ordinary,
    Profiled,
}
