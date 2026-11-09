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
        StartMenu = new StartMenuCustomization(),
    };

    [Theory]
    [InlineData("background.fill", TaskbarPart.Background)]
    [InlineData("background.stroke", TaskbarPart.Background)]
    [InlineData("tray.icon.minHeight", TaskbarPart.CompactTray)]
    [InlineData("quickSettings.spacing", TaskbarPart.CompactTray)]
    [InlineData("clock.upper.fontSize", TaskbarPart.CompactClock)]
    [InlineData("clock", TaskbarPart.CompactClock)]
    [InlineData("startMenuHeight", TaskbarPart.StartMenu)]
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
        Assert.DoesNotContain(TaskbarPart.StartMenu, problems.Keys);
        Assert.All(problems.Values, problem => Assert.Equal(TaskbarProblem.Error, problem));
    }

    [Fact]
    public void TheStartMenuStatusIsReadForTheStartMenuPart()
    {
        var problems = TaskbarProblemReader.Read(_everything,
            null, false, new NativeStatus(1, true, [], [Rule("startMenuHeight", "never_matched")], []), false);

        Assert.Equal(TaskbarProblem.NotApplied, problems[TaskbarPart.StartMenu]);
    }
}
