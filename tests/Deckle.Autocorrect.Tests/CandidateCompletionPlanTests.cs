using Deckle.Autocorrect.Onnx;
using Xunit;

namespace Deckle.Autocorrect.Tests;

public sealed class CandidateCompletionPlanTests
{
    [Fact]
    public void CreateScoresOnlyTokensBetweenCommonPrefixAndSuffix()
    {
        CandidateCompletionPlan[] plans = CandidateCompletionPlan.Create(new[]
        {
            new[] { 10, 20, 30, 40 },
            new[] { 10, 21, 30, 40 },
        });

        Assert.Equal(new CandidateCompletionPlan(1, 1), plans[0]);
        Assert.Equal(new CandidateCompletionPlan(1, 1), plans[1]);
    }

    [Fact]
    public void CreateKeepsMiddleCommonTokensWhenThereAreMultipleDifferences()
    {
        CandidateCompletionPlan[] plans = CandidateCompletionPlan.Create(new[]
        {
            new[] { 10, 20, 30, 40, 50 },
            new[] { 10, 21, 30, 41, 50 },
        });

        Assert.Equal(new CandidateCompletionPlan(1, 3), plans[0]);
        Assert.Equal(new CandidateCompletionPlan(1, 3), plans[1]);
    }

    [Fact]
    public void CreateFallsBackToFullCompletionWhenPrefixCandidateWouldHaveNoScoredToken()
    {
        CandidateCompletionPlan[] plans = CandidateCompletionPlan.Create(new[]
        {
            new[] { 10, 20 },
            new[] { 10, 20, 30 },
        });

        Assert.Equal(new CandidateCompletionPlan(0, 2), plans[0]);
        Assert.Equal(new CandidateCompletionPlan(0, 3), plans[1]);
    }
}
