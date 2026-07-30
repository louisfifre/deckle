using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Onnx;

internal sealed record ProfiledSentenceScoringOutcome(
    SentenceScoringOutcome Outcome,
    OnnxSentenceScoringProfile Profile);

internal sealed record OnnxSentenceScoringProfile(
    long StopwatchFrequency,
    int CandidateCount,
    long TotalTicks,
    long ScoreCombinationTicks,
    long FinalDecisionTicks,
    IReadOnlyList<OnnxSentenceOrderProfile> Orders);

internal sealed record OnnxSentenceOrderProfile(
    string Order,
    IReadOnlyList<int> OriginalCandidateIndices,
    int PromptTokens,
    IReadOnlyList<int> CompletionTokens,
    long PromptRenderTicks,
    long PromptTokenizationTicks,
    long CompletionTokenizationTicks,
    long CompletionPlanTicks,
    IReadOnlyList<OnnxSentenceCandidateProfile> Candidates);

internal sealed record OnnxSentenceCandidateProfile(
    int PresentedIndex,
    int OriginalIndex,
    int CompletionTokens,
    int PlannedScoredTokens,
    int ActualScoredTokens,
    long InputPreparationTicks,
    long GeneratorSetupTicks,
    long AppendTokensTicks,
    long GetOutputTicks,
    long LogitsReadbackTicks,
    long LogSoftmaxTicks,
    long DisposalTicks,
    long NativeEnvelopeTicks,
    double Score,
    double LogProbability,
    string? AbstainReason);

internal sealed class OnnxSentenceScoringProfileBuilder(int candidateCount)
{
    private readonly List<OnnxSentenceOrderProfileBuilder> _orders = new();

    public long ScoreCombinationTicks { get; set; }
    public long FinalDecisionTicks { get; set; }

    public OnnxSentenceOrderProfileBuilder BeginOrder(
        string order,
        IReadOnlyList<int> originalCandidateIndices)
    {
        var builder = new OnnxSentenceOrderProfileBuilder(order, originalCandidateIndices);
        _orders.Add(builder);
        return builder;
    }

    public OnnxSentenceScoringProfile Build(long totalTicks) => new(
        System.Diagnostics.Stopwatch.Frequency,
        candidateCount,
        totalTicks,
        ScoreCombinationTicks,
        FinalDecisionTicks,
        _orders.Select(static order => order.Build()).ToArray());
}

internal sealed class OnnxSentenceOrderProfileBuilder(
    string order,
    IReadOnlyList<int> originalCandidateIndices)
{
    private readonly List<OnnxSentenceCandidateProfile> _candidates = new();

    public int PromptTokens { get; set; }
    public IReadOnlyList<int> CompletionTokens { get; set; } = Array.Empty<int>();
    public long PromptRenderTicks { get; set; }
    public long PromptTokenizationTicks { get; set; }
    public long CompletionTokenizationTicks { get; set; }
    public long CompletionPlanTicks { get; set; }

    public int OriginalIndex(int presentedIndex) => originalCandidateIndices[presentedIndex];

    public void AddCandidate(OnnxSentenceCandidateProfile candidate) =>
        _candidates.Add(candidate);

    public OnnxSentenceOrderProfile Build() => new(
        order,
        originalCandidateIndices,
        PromptTokens,
        CompletionTokens,
        PromptRenderTicks,
        PromptTokenizationTicks,
        CompletionTokenizationTicks,
        CompletionPlanTicks,
        _candidates.ToArray());
}
