using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class NativeEngineMessageTests
{
    [Fact]
    public void AStatusCarriesEachRulesNumbersAndTheProblemsOfTheDocument()
    {
        var message = NativeMessage.Parse(
            """
            {"t":"status","generation":"3","settled":"true",
             "rules":[{"id":"tray.icon.minHeight","state":"applied","matched":"10","active":"10","reasserted":"2","contested":"0","error":""},
                      {"id":"bad","state":"error","matched":"0","active":"0","reasserted":"0","contested":"0","error":"target: the selector is empty"}],
             "behaviors":[{"id":"clock","state":"applied","matched":"2","active":"2","reasserted":"6","contested":"0","error":""}],
             "problems":["clock.upper: a line has either a date or a time picture"]}
            """);

        var status = Assert.IsType<NativeStatus>(message);
        Assert.Equal(3, status.Generation);
        Assert.True(status.Settled);
        Assert.Equal(2, status.Rules.Count);
        Assert.Equal(new NativeRuleState("tray.icon.minHeight", "applied", 10, 10, 2, 0, ""), status.Rules[0]);
        Assert.Equal("error", status.Rules[1].State);
        Assert.Equal("target: the selector is empty", status.Rules[1].Error);
        Assert.Equal("clock", Assert.Single(status.Behaviors).Id);
        Assert.Equal("clock.upper: a line has either a date or a time picture", Assert.Single(status.Problems));
    }

    [Fact]
    public void AStatusWithoutRulesReadsAsEmptyAndNotSettled()
    {
        var status = Assert.IsType<NativeStatus>(NativeMessage.Parse("""{"t":"status","generation":"1"}"""));

        Assert.False(status.Settled);
        Assert.Empty(status.Rules);
        Assert.Empty(status.Behaviors);
        Assert.Empty(status.Problems);
    }

    [Fact]
    public void ATreeRecordKeepsItsTextWithItsLineBreaks()
    {
        var tree = Assert.IsType<NativeTreeDump>(NativeMessage.Parse(
            """{"t":"tree","tid":"21932","text":"Taskbar.TaskbarFrame#TaskbarFrame  [~actual=48x1080]\n  Windows.UI.Xaml.Controls.Grid#RootGrid\n"}"""));

        Assert.Equal(21932, tree.Tid);
        Assert.Equal(2, tree.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.StartsWith("Taskbar.TaskbarFrame#TaskbarFrame", tree.Text);
    }

    [Fact]
    public void ADumpEndsWithTheNumberOfRootsItSent()
    {
        var end = Assert.IsType<NativeTreeEnd>(NativeMessage.Parse("""{"t":"tree_end","roots":"2"}"""));

        Assert.Equal(2, end.Roots);
    }

    [Fact]
    public void TheDumpCommandIsOneCompactLine()
    {
        Assert.Equal("""{"t":"dump"}""", NativeCommand.Dump);
    }
}
