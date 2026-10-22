using System;
using System.Linq;
using Xunit;

namespace Deckle.Playground.Tests;

// The HUD tab tells the person why its Microphone preview gave way to the
// simulation. These tests hold the sentence to what a reader needs from it:
// each problem says its own thing, and nothing the machine knows but the
// person does not need (an exception's type) reaches the window.
[Trait("Category", "unit")]
public sealed class MicrophonePreviewFailureTests
{
    // Walks the enum, so a problem added later is covered without editing.
    private static readonly MicrophoneProblem[] Problems = Enum.GetValues<MicrophoneProblem>();

    [Fact]
    public void EachProblemHasItsOwnNonEmptyReason()
    {
        var reasons = Problems.Select(problem => new MicrophonePreviewFailure(problem, 7).Reason).ToArray();

        Assert.All(reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.Equal(reasons.Length, reasons.Distinct().Count());
    }

    [Fact]
    public void EachProblemHasItsOwnLogCause()
    {
        var causes = Problems.Select(problem => new MicrophonePreviewFailure(problem).Cause).ToArray();

        Assert.All(causes, cause => Assert.False(string.IsNullOrWhiteSpace(cause)));
        Assert.Equal(causes.Length, causes.Distinct().Count());
    }

    [Fact]
    public void AnUnexplainedOpenFailureNamesItsErrorCode()
    {
        var failure = new MicrophonePreviewFailure(MicrophoneProblem.Unavailable, 32);

        Assert.Contains("32", failure.Reason);
    }

    [Fact]
    public void ARaisedExceptionShowsNoTypeInTheWindow()
    {
        var failure = new MicrophonePreviewFailure(
            MicrophoneProblem.Faulted, ExceptionType: nameof(InvalidOperationException));

        Assert.DoesNotContain(nameof(InvalidOperationException), failure.Reason);
    }
}
