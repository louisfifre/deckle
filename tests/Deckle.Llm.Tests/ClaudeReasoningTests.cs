using Anthropic.Models.Messages;
using Xunit;

namespace Deckle.Llm.Tests;

// What the Messages API is sent for each reasoning level. Turning thinking off
// takes a different field on each line of models, so Off is checked per model.
[Trait("Category", "unit")]
public sealed class ClaudeReasoningTests
{
    [Fact]
    public void NoLevelKeepsLowEffortAndAdaptiveThinking()
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-sonnet-5-5", null);

        Assert.Equal(Effort.Low, reasoning.Effort);
        Assert.Null(reasoning.Thinking);
    }

    [Theory]
    [InlineData(ReasoningLevel.Low, Effort.Low)]
    [InlineData(ReasoningLevel.Medium, Effort.Medium)]
    public void ALevelSetsTheMatchingEffortWithAdaptiveThinking(ReasoningLevel level, Effort effort)
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-haiku-5-5", level);

        Assert.Equal(effort, reasoning.Effort);
        Assert.Null(reasoning.Thinking);
    }

    [Fact]
    public void OffDisablesThinkingOnHaiku()
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-haiku-5-5", ReasoningLevel.Off);

        Assert.True(reasoning.Thinking!.TryPickDisabled(out _));
        Assert.Equal(Effort.Low, reasoning.Effort);
    }

    [Fact]
    public void OffUsesBetweenToolsOnSonnet55WhichRejectsDisabled()
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-sonnet-5-5", ReasoningLevel.Off);

        Assert.True(reasoning.Thinking!.TryPickBetweenTools(out _));
        Assert.Equal(Effort.Low, reasoning.Effort);
    }

    // Opus 5.5 and Fable always think; a model the list does not know yet is
    // never sent a thinking field it might reject.
    [Theory]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-fable-5-1")]
    [InlineData("claude-sonnet-6")]
    public void OffFallsBackToLowEffortWhereTheModelIsNotKnownToStopThinking(string model)
    {
        var reasoning = ClaudeEngine.ReasoningFor(model, ReasoningLevel.Off);

        Assert.Null(reasoning.Thinking);
        Assert.Equal(Effort.Low, reasoning.Effort);
        Assert.Contains("off_unsupported", reasoning.Detail);
    }

    [Theory]
    [InlineData("claude-haiku-5-5", true)]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData(" claude-sonnet-5-5 ", true)]
    [InlineData("claude-opus-5-5", false)]
    [InlineData("claude-sonnet-6", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OffIsOfferedOnlyWhereTheModelHonoursIt(string? model, bool accepted)
    {
        Assert.Equal(accepted, ClaudeEngine.AcceptsOff(model));
    }
}
