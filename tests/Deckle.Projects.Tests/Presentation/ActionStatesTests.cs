using Xunit;

namespace Deckle.Projects.Tests;

// What a pinned action's card and the page's header say, in the spec's own
// cases (Pinned actions, P1 and P3).
[Trait("Category", "unit")]
public sealed class ActionStatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly PinnedAction SelfCheck = new(new ActionRef("self-check.0", "Debug"), "Self-check", "self-check.ps1");
    private static readonly PinnedAction UpdateProd = new(new ActionRef("deploy-prod.0"), "Update prod", "deploy-prod.ps1");
    private static readonly PinnedAction BuildRun = new(new ActionRef("build-run.1"), "Build & run", "build-run.ps1");

    private static ActionRun RunOf(PinnedAction action, TimeSpan age) =>
        new(1, action.Action, action.Label, Now - age, "title");

    private static ActionResult Result(RunOutcome outcome, int? code, TimeSpan age) =>
        new(outcome, code, Now - age);

    [Fact]
    public void AnActionThatNeverRanSaysSoAndOffersRun()
    {
        PinnedCardState state = ActionStates.ForCard(SelfCheck, null, null, menuGivesIt: true, canStart: true, Now);

        Assert.Equal("Not run yet", state.Line.Text);
        Assert.Equal(ActionButtonMode.Run, state.Button);
        Assert.True(state.ButtonEnabled);
    }

    [Fact]
    public void ARunningActionShowsItsTimeAndOffersTheTerminal()
    {
        ActionRun run = RunOf(SelfCheck, TimeSpan.FromSeconds(72));

        PinnedCardState state = ActionStates.ForCard(SelfCheck, run, null, menuGivesIt: true, canStart: true, Now);

        Assert.Equal("Running · 1 min 12 s", state.Line.Text);
        Assert.Equal(RunLineKind.Running, state.Line.Kind);
        Assert.Equal(ActionButtonMode.ShowTerminal, state.Button);
        Assert.True(state.ButtonEnabled);
    }

    [Fact]
    public void EveryOtherActionWaitsForTheRunningOneAndCannotRun()
    {
        ActionRun run = RunOf(SelfCheck, TimeSpan.FromSeconds(72));

        PinnedCardState state = ActionStates.ForCard(UpdateProd, run, null, menuGivesIt: true, canStart: true, Now);

        Assert.Equal("Waits for Self-check to end", state.Line.Text);
        Assert.Equal(ActionButtonMode.Run, state.Button);
        Assert.False(state.ButtonEnabled);
    }

    [Fact]
    public void AFailureShowsItsExitCodeAndTheActionCanRunAgain()
    {
        var failed = Result(RunOutcome.Failed, 1, TimeSpan.FromMinutes(4));

        PinnedCardState state = ActionStates.ForCard(SelfCheck, null, failed, menuGivesIt: true, canStart: true, Now);

        Assert.Equal("Failed 4 min ago · exit code 1", state.Line.Text);
        Assert.Equal(RunLineKind.Failure, state.Line.Kind);
        Assert.True(state.ButtonEnabled);
    }

    [Fact]
    public void ASuccessCarriesNoMarkOfFailure()
    {
        var done = Result(RunOutcome.Succeeded, 0, TimeSpan.FromMinutes(4));

        PinnedCardState state = ActionStates.ForCard(SelfCheck, null, done, menuGivesIt: true, canStart: true, Now);

        Assert.Equal("Succeeded 4 min ago · exit code 0", state.Line.Text);
        Assert.Equal(RunLineKind.Success, state.Line.Kind);
    }

    [Fact]
    public void AnEntryTheMenuNoLongerGivesKeepsItsCardWithRunOff()
    {
        PinnedCardState state = ActionStates.ForCard(SelfCheck, null, null, menuGivesIt: false, canStart: true, Now);

        Assert.Equal(RunStateLine.MenuNoLongerHasIt, state.Line.Text);
        Assert.False(state.ButtonEnabled);
    }

    [Fact]
    public void ARunCannotStartWhenTheProjectCannotStartOne()
    {
        PinnedCardState state = ActionStates.ForCard(SelfCheck, null, null, menuGivesIt: true, canStart: false, Now);

        Assert.False(state.ButtonEnabled);
    }

    [Fact]
    public void TheHeaderOffersTheMainActionAndSaysNothingBeforeItRan()
    {
        HeaderActionState state = ActionStates.ForHeader(BuildRun, null, null, canStart: true, Now);

        Assert.Equal("Build & run", state.Label);
        Assert.Equal(ActionButtonMode.Run, state.Button);
        Assert.True(state.ButtonEnabled);
        Assert.Null(state.Line);
    }

    [Fact]
    public void WhileAnotherActionRunsTheHeaderSaysWhatTheAccentButtonWaitsFor()
    {
        // P3: "Build & run waits for Self-check to end" under a disabled button; the
        // failure comes back when nothing blocks it.
        var failed = Result(RunOutcome.Failed, 1, TimeSpan.FromMinutes(4));
        ActionRun other = RunOf(SelfCheck, TimeSpan.FromSeconds(72));

        HeaderActionState state = ActionStates.ForHeader(BuildRun, other, failed, canStart: true, Now);

        Assert.Equal("Build & run waits for Self-check to end", state.Line!.Text);
        Assert.Equal(RunLineKind.Neutral, state.Line.Kind);
        Assert.Equal(ActionButtonMode.Run, state.Button);
        Assert.False(state.ButtonEnabled);

        HeaderActionState after = ActionStates.ForHeader(BuildRun, null, failed, canStart: true, Now);
        Assert.Equal("Build & run failed 4 min ago · exit code 1", after.Line!.Text);
    }

    [Fact]
    public void WhileTheMainActionRunsTheAccentButtonGivesWayToShowTerminal()
    {
        ActionRun run = RunOf(BuildRun, TimeSpan.FromSeconds(72));

        HeaderActionState state = ActionStates.ForHeader(BuildRun, run, null, canStart: true, Now);

        Assert.Equal("Build & run running · 1 min 12 s", state.Line!.Text);
        Assert.Equal(RunLineKind.Running, state.Line.Kind);
        Assert.Equal(ActionButtonMode.ShowTerminal, state.Button);
        Assert.True(state.ButtonEnabled);
    }

    [Fact]
    public void ASuccessfulMainActionReadsWithoutAnIcon()
    {
        var done = Result(RunOutcome.Succeeded, 0, TimeSpan.FromMinutes(4));

        HeaderActionState state = ActionStates.ForHeader(BuildRun, null, done, canStart: true, Now);

        Assert.Equal("Build & run succeeded 4 min ago · exit code 0", state.Line!.Text);
        Assert.Equal(RunLineKind.Success, state.Line.Kind);
    }

    [Fact]
    public void TheAccentButtonCannotRunWhenTheProjectCannotStartAnAction()
    {
        HeaderActionState state = ActionStates.ForHeader(BuildRun, null, null, canStart: false, Now);

        Assert.False(state.ButtonEnabled);
    }
}
