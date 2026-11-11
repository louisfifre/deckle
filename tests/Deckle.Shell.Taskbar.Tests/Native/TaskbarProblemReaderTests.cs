using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class TaskbarProblemReaderTests
{
    private static NativeRuleState Rule(string id, string state) => new(id, state, 1, 1, 0, 0, "");

    private static NativeStatus Status(params NativeRuleState[] rules) => new(1, true, rules, [], []);

    private static readonly TaskbarCustomization _everything = new()
    {
        Background = TaskbarBackground.Black,
        TrayIconPitch = 24,
        TightenQuickSettings = true,
        Clock = ClockCustomization.DateAboveTime,
        StartMenu = new StartMenuCustomization { FullHeight = true },
    };

    [Theory]
    [InlineData("background.fill", TaskbarPart.Background)]
    [InlineData("background.stroke", TaskbarPart.Background)]
    [InlineData("tray.icon.minHeight", TaskbarPart.CompactTray)]
    [InlineData("quickSettings.spacing", TaskbarPart.CompactTray)]
    [InlineData("tray.mainStack.icon.minHeight", TaskbarPart.CompactTray)]
    [InlineData("taskButtons.app.height", TaskbarPart.CompactTaskButtons)]
    [InlineData("taskButtons.experience.padding", TaskbarPart.CompactTaskButtons)]
    [InlineData("clock.upper.fontSize", TaskbarPart.CompactClock)]
    [InlineData("clock.button.padding", TaskbarPart.CompactClock)]
    [InlineData("clock", TaskbarPart.CompactClock)]
    [InlineData("startMenuHeight", TaskbarPart.StartMenuHeight)]
    public void AnEngineIdBelongsToThePartThatAskedForIt(string id, TaskbarPart part)
    {
        Assert.Equal(part, TaskbarProblemReader.PartOf(id));
    }

    [Fact]
    public void AnIdDeckleDoesNotKnowBelongsToNoPart()
    {
        Assert.Null(TaskbarProblemReader.PartOf("something.else"));
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("pending")]
    [InlineData("idle")]
    public void AStateThatIsFineOrPassingSaysNothing(string state)
    {
        var problems = TaskbarProblemReader.Read(_everything,
            Status(Rule("background.fill", state)), false, null, false);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("never_matched", TaskbarProblem.NotApplied)]
    [InlineData("contested", TaskbarProblem.Contested)]
    [InlineData("error", TaskbarProblem.Error)]
    public void AStateThatIsWrongIsTheProblemOfItsPart(string state, TaskbarProblem expected)
    {
        var problems = TaskbarProblemReader.Read(_everything,
            Status(Rule("tray.icon.minHeight", state)), false, null, false);

        Assert.Equal(expected, Assert.Single(problems).Value);
        Assert.Equal(TaskbarPart.CompactTray, Assert.Single(problems).Key);
    }

    // The microphone-in-use icon is absent most of the time: its rule finding
    // nothing is not a fault of the compact tray, but its failing still is.
    [Fact]
    public void AnOptionalRuleThatFindsNothingSaysNothing()
    {
        var quiet = TaskbarProblemReader.Read(_everything,
            Status(Rule("tray.mainStack.icon.minHeight", "never_matched"), Rule("tray.nonActivatableStack.icon.minHeight", "never_matched")),
            false, null, false);
        var failed = TaskbarProblemReader.Read(_everything,
            Status(Rule("tray.mainStack.icon.minHeight", "error")), false, null, false);

        Assert.Empty(quiet);
        Assert.Equal(TaskbarProblem.Error, failed[TaskbarPart.CompactTray]);
    }

    [Fact]
    public void TheWorstStateOfAPartWins()
    {
        var problems = TaskbarProblemReader.Read(_everything,
            Status(Rule("clock.spacing", "never_matched"), Rule("clock.upper.color", "error"), Rule("clock.lower.color", "contested")),
            false, null, false);

        Assert.Equal(TaskbarProblem.Error, problems[TaskbarPart.CompactClock]);
    }

    // A status still on its way from before the person turned a part off.
    [Fact]
    public void APartThatIsNotAskedForIsNeverReported()
    {
        var onlyBackground = new TaskbarCustomization { Background = TaskbarBackground.Black };

        var problems = TaskbarProblemReader.Read(onlyBackground,
            Status(Rule("clock.spacing", "error")), false, null, false);

        Assert.Empty(problems);
    }

    [Fact]
    public void AnEngineThatCouldNotBeStartedPutsItsPartsInError()
    {
        var problems = TaskbarProblemReader.Read(_everything, null, true, null, false);

        Assert.Equal(3, problems.Count);
        Assert.DoesNotContain(TaskbarPart.StartMenuHeight, problems.Keys);
        Assert.All(problems.Values, problem => Assert.Equal(TaskbarProblem.Error, problem));
    }

    [Fact]
    public void TheStartMenuStatusIsReadForTheStartMenuPart()
    {
        var problems = TaskbarProblemReader.Read(_everything,
            null, false, new NativeStatus(1, true, [], [Rule("startMenuHeight", "never_matched")], []), false);

        Assert.Equal(TaskbarProblem.NotApplied, problems[TaskbarPart.StartMenuHeight]);
    }

    // The height and the width are two cards: a width that fails speaks on the
    // width card, and an unreachable Start menu process fails both.
    [Fact]
    public void TheStartMenuWidthIsAPartOfItsOwn()
    {
        var both = _everything with { StartMenu = new StartMenuCustomization { FullHeight = true, Width = StartMenuWidthRequest.Of(640) } };

        var failed = TaskbarProblemReader.Read(both,
            null, false, new NativeStatus(1, true, [], [Rule("startMenuHeight", "applied"), Rule("startMenuWidth", "error")], []), false);
        var unreachable = TaskbarProblemReader.Read(both, null, false, null, true);

        Assert.Equal(TaskbarPart.StartMenuWidth, TaskbarProblemReader.PartOf("startMenuWidth"));
        Assert.Equal(TaskbarProblem.Error, Assert.Single(failed).Value);
        Assert.Equal(TaskbarPart.StartMenuWidth, Assert.Single(failed).Key);
        Assert.Equal([TaskbarPart.StartMenuHeight, TaskbarPart.StartMenuWidth], unreachable.Keys.Order());
    }
}
