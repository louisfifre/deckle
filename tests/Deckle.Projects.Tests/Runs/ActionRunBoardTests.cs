using Xunit;

namespace Deckle.Projects.Tests;

[Trait("Category", "unit")]
public sealed class ActionRunBoardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static ActionRun Run(long id, string action, string label = "Label") =>
        new(id, new ActionRef(action), label, T0, $"{label} (0000)");

    [Fact]
    public void AProjectRunsOneActionAtATime()
    {
        var board = new ActionRunBoard();

        Assert.NotNull(board.Begin("p1", Run(1, "build-run.0")));
        Assert.Null(board.Begin("p1", Run(2, "self-check.0")));
        Assert.Equal(1, board.ActiveRun("p1")!.RunId);
    }

    [Fact]
    public void AnotherProjectRunsFreely()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "build-run.0"));

        Assert.NotNull(board.Begin("p2", Run(2, "build-run.0")));
        Assert.Equal(2, board.ActiveRun("p2")!.RunId);
    }

    [Fact]
    public void AResultIsKeptForItsActionAndFreesTheProject()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "build-run.0"));

        board.End("p1", RunOutcome.Failed, 1, T0.AddMinutes(1));

        Assert.Null(board.ActiveRun("p1"));
        ActionResult result = board.LastResult("p1", new ActionRef("build-run.0"))!;
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.NotNull(board.Begin("p1", Run(2, "self-check.0")));
    }

    [Fact]
    public void TheFailureOfOneActionStaysWhileAnotherRunsAndEnds()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "build-run.0"));
        board.End("p1", RunOutcome.Failed, 1, T0.AddMinutes(1));

        board.Begin("p1", Run(2, "self-check.0"));
        board.End("p1", RunOutcome.Succeeded, 0, T0.AddMinutes(2));

        Assert.Equal(RunOutcome.Failed, board.LastResult("p1", new ActionRef("build-run.0"))!.Outcome);
        Assert.Equal(RunOutcome.Succeeded, board.LastResult("p1", new ActionRef("self-check.0"))!.Outcome);
    }

    [Fact]
    public void AResultGoesWhenItsActionRunsAgain()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "build-run.0"));
        board.End("p1", RunOutcome.Failed, 1, T0);

        board.Begin("p1", Run(2, "build-run.0"));

        Assert.Null(board.LastResult("p1", new ActionRef("build-run.0")));
    }

    [Fact]
    public void TwoVariantsOfOneEntryKeepTheirOwnResults()
    {
        var board = new ActionRunBoard();
        var debug = new ActionRun(1, new ActionRef("self-check.0", "Debug"), "Self-check", T0, "t");
        var release = new ActionRun(2, new ActionRef("self-check.0", "Release"), "Self-check", T0, "t");
        board.Begin("p1", debug);
        board.End("p1", RunOutcome.Failed, 2, T0);
        board.Begin("p1", release);
        board.End("p1", RunOutcome.Succeeded, 0, T0);

        Assert.Equal(RunOutcome.Failed, board.LastResult("p1", debug.Action)!.Outcome);
        Assert.Equal(RunOutcome.Succeeded, board.LastResult("p1", release.Action)!.Outcome);
    }

    [Fact]
    public void OneChangeIsRaisedPerTransition()
    {
        var board = new ActionRunBoard();
        var changes = new List<string>();
        board.Changed += changes.Add;

        board.Begin("p1", Run(1, "a.0"));
        board.Begin("p1", Run(2, "b.0"));
        board.NoteFallback("p1");
        board.NoteFallback("p1");
        board.End("p1", RunOutcome.Succeeded, 0, T0);
        board.End("p1", RunOutcome.Succeeded, 0, T0);

        Assert.Equal(["p1", "p1", "p1"], changes);
    }

    [Fact]
    public void AFallbackIsRememberedOnTheRunAndOnItsResult()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "a.0"));

        board.NoteFallback("p1");
        Assert.True(board.ActiveRun("p1")!.UsedFallback);
        board.End("p1", RunOutcome.Succeeded, 0, T0);

        Assert.True(board.LastResult("p1", new ActionRef("a.0"))!.UsedFallback);
    }

    [Fact]
    public void AbortingFreesTheProjectAndRecordsNothing()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "a.0"));

        board.Abort("p1");

        Assert.Null(board.ActiveRun("p1"));
        Assert.Null(board.LastResult("p1", new ActionRef("a.0")));
    }

    [Fact]
    public void AForgottenProjectKeepsNothing()
    {
        var board = new ActionRunBoard();
        board.Begin("p1", Run(1, "a.0"));
        board.End("p1", RunOutcome.Failed, 1, T0);
        board.Begin("p1", Run(2, "b.0"));

        board.Forget("p1");

        Assert.Null(board.ActiveRun("p1"));
        Assert.Null(board.LastResult("p1", new ActionRef("a.0")));
    }
}
