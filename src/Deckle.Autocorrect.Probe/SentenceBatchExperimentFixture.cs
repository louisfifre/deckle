namespace Deckle.Autocorrect.Probe;

internal static class SentenceBatchExperimentFixture
{
    public const int WarmupPairs = 2;
    public const int LatencyBlocks = 5;
    public const double MaximumMedianBlockRatio = 0.75;
    public const int MinimumFasterBlocks = 4;
    public const double SecondaryLatencyReferenceMilliseconds = 300.0;

    public static IReadOnlyList<SentenceBatchExperimentMethod> WarmupMethods(int pair) =>
        pair switch
        {
            0 => [SentenceBatchExperimentMethod.Sequential, SentenceBatchExperimentMethod.Batch],
            1 => [SentenceBatchExperimentMethod.Batch, SentenceBatchExperimentMethod.Sequential],
            _ => throw new ArgumentOutOfRangeException(nameof(pair)),
        };

    public static bool WarmupUsesReversedPresentation(int pair) =>
        pair switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentOutOfRangeException(nameof(pair)),
        };

    public static IReadOnlyList<SentenceBatchExperimentMethod> LatencyMethods(int block)
    {
        if (block < 0 || block >= LatencyBlocks)
            throw new ArgumentOutOfRangeException(nameof(block));

        return block % 2 == 0
            ? [
                SentenceBatchExperimentMethod.Sequential,
                SentenceBatchExperimentMethod.Batch,
                SentenceBatchExperimentMethod.Batch,
                SentenceBatchExperimentMethod.Sequential,
            ]
            : [
                SentenceBatchExperimentMethod.Batch,
                SentenceBatchExperimentMethod.Sequential,
                SentenceBatchExperimentMethod.Sequential,
                SentenceBatchExperimentMethod.Batch,
            ];
    }
}

internal enum SentenceBatchExperimentMethod
{
    Sequential,
    Batch,
}
