using Xunit;

namespace Deckle.Projects.Tests;

[Trait("Category", "unit")]
public sealed class ElapsedFormatTests
{
    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(45, "45 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min 00 s")]
    [InlineData(72, "1 min 12 s")]
    [InlineData(3599, "59 min 59 s")]
    [InlineData(3600, "1 h 00 min")]
    [InlineData(3720, "1 h 02 min")]
    [InlineData(-5, "0 s")]
    public void ARunningTimeReadsAtItsBoundaries(int seconds, string expected)
    {
        Assert.Equal(expected, ElapsedFormat.Running(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(240, "4 min ago")]
    [InlineData(3599, "59 min ago")]
    [InlineData(3600, "1 h ago")]
    [InlineData(7200, "2 h ago")]
    [InlineData(86399, "23 h ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(259200, "3 days ago")]
    public void AnAgeReadsAtItsBoundariesWithTheSingularWhereItBelongs(int seconds, string expected)
    {
        Assert.Equal(expected, ElapsedFormat.Ago(TimeSpan.FromSeconds(seconds)));
    }
}

[Trait("Category", "unit")]
public sealed class RunStateLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static ActionRun Running(bool fallback = false) =>
        new(1, new ActionRef("a.0"), "Self-check", Now.AddSeconds(-72), "t", fallback);

    [Fact]
    public void AnActionThatNeverRanSaysSo()
    {
        RunLine line = RunStateLine.Idle(null, Now);

        Assert.Equal("Not run yet", line.Text);
        Assert.Equal(RunLineKind.Neutral, line.Kind);
    }

    [Fact]
    public void ASuccessAndAFailureNameTheirAgeAndExitCode()
    {
        var ok = new ActionResult(RunOutcome.Succeeded, 0, Now.AddMinutes(-4));
        var bad = new ActionResult(RunOutcome.Failed, 1, Now.AddMinutes(-4));

        Assert.Equal(new RunLine("Succeeded 4 min ago · exit code 0", RunLineKind.Success), RunStateLine.Idle(ok, Now));
        Assert.Equal(new RunLine("Failed 4 min ago · exit code 1", RunLineKind.Failure), RunStateLine.Idle(bad, Now));
    }

    [Fact]
    public void ATerminalClosedEarlyAndOneThatNeverStartedAreFailuresWithoutACode()
    {
        var closed = new ActionResult(RunOutcome.Closed, null, Now.AddMinutes(-4));
        var notStarted = new ActionResult(RunOutcome.NotStarted, null, Now);

        Assert.Equal(new RunLine("Closed 4 min ago before it ended", RunLineKind.Failure), RunStateLine.Idle(closed, Now));
        Assert.Equal(new RunLine("The terminal did not start", RunLineKind.Failure), RunStateLine.Idle(notStarted, Now));
    }

    [Fact]
    public void ARunningActionShowsItsElapsedTime()
    {
        Assert.Equal(new RunLine("Running · 1 min 12 s", RunLineKind.Running), RunStateLine.Running(Running(), Now));
    }

    [Fact]
    public void TheFallbackToTheConsoleIsASeparateNoteOnTheLine()
    {
        RunLine running = RunStateLine.Running(Running(fallback: true), Now);
        RunLine idle = RunStateLine.Idle(new ActionResult(RunOutcome.Succeeded, 0, Now, UsedFallback: true), Now);

        Assert.Equal(RunStateLine.FallbackNote, running.Note);
        Assert.Equal(RunStateLine.FallbackNote, idle.Note);
        Assert.Null(RunStateLine.Running(Running(), Now).Note);
    }

    [Fact]
    public void AnotherActionRunningMakesThisOneWaitAndAVanishedEntryIsSaid()
    {
        Assert.Equal("Waits for Self-check to end", RunStateLine.Waiting("Self-check").Text);
        Assert.Equal("The script menu no longer has this action", RunStateLine.Gone().Text);
    }

    [Fact]
    public void TheMainActionsLineReadsLikeTheSpecsExamples()
    {
        var failed = new ActionResult(RunOutcome.Failed, 1, Now.AddMinutes(-4));
        var succeeded = new ActionResult(RunOutcome.Succeeded, 0, Now.AddMinutes(-4));
        ActionRun running = new(1, new ActionRef("b.0"), "Build & run", Now.AddSeconds(-72), "t");

        Assert.Equal(new RunLine("Build & run failed 4 min ago · exit code 1", RunLineKind.Failure),
            RunStateLine.Main("Build & run", failed, null, Now));
        Assert.Equal(new RunLine("Build & run succeeded 4 min ago · exit code 0", RunLineKind.Success),
            RunStateLine.Main("Build & run", succeeded, null, Now));
        Assert.Equal(new RunLine("Build & run running · 1 min 12 s", RunLineKind.Running),
            RunStateLine.Main("Build & run", succeeded, running, Now));
    }

    [Fact]
    public void ANeverRunMainActionSaysNothing()
    {
        Assert.Null(RunStateLine.Main("Build & run", null, null, Now));
    }
}
