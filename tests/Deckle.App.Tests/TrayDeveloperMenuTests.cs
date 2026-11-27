using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The developer tray menu: the developer tools, then Logs and Restart at the
// bottom, behind a separator.
[Trait("Category", "unit")]
public sealed class TrayDeveloperMenuTests
{
    private static IReadOnlyList<TrayMenuEntry> Compose()
        => TrayDeveloperMenu.Compose(key => key, () => { }, () => { }, () => { });

    private static IEnumerable<string> CommandIds(IReadOnlyList<TrayMenuEntry> entries)
        => entries.OfType<TrayMenuCommand>().Select(c => c.Id);

    [Fact]
    public void TheMenuHoldsTheCaptureThenLogsAndRestart()
    {
        Assert.Equal(["capture-interface", "logs", "restart"], CommandIds(Compose()));
    }

    [Fact]
    public void TheMenuHasNoProjectsEntry()
    {
        Assert.DoesNotContain("projects", CommandIds(Compose()));
    }

    [Fact]
    public void ALabelIsReadFromItsResourceKeyWhenTheMenuOpens()
    {
        IReadOnlyList<TrayMenuEntry> entries = Compose();

        Assert.Equal("TrayMenu_CaptureInterface", ((TrayMenuCommand)entries[0]).Label());
    }

    [Fact]
    public void LogsAndRestartCloseTheMenuBehindTheirOwnSeparator()
    {
        IReadOnlyList<TrayMenuEntry> entries = Compose();

        Assert.Single(entries.OfType<TrayMenuSeparator>());
        Assert.IsType<TrayMenuSeparator>(entries[^3]);
        Assert.Equal(TrayDeveloperMenu.LogsId, ((TrayMenuCommand)entries[^2]).Id);
        Assert.Equal(TrayDeveloperMenu.RestartId, ((TrayMenuCommand)entries[^1]).Id);
    }

    [Fact]
    public void TheEverydayCommandsAreNotInThisMenu()
    {
        IEnumerable<string> ids = CommandIds(Compose());

        Assert.DoesNotContain("settings", ids);
        Assert.DoesNotContain("quit", ids);
    }

    [Fact]
    public void EachEntryRunsItsOwnAction()
    {
        var ran = new List<string>();
        IReadOnlyList<TrayMenuEntry> entries = TrayDeveloperMenu.Compose(
            key => key,
            () => ran.Add("capture-interface"), () => ran.Add("logs"), () => ran.Add("restart"));

        foreach (TrayMenuCommand command in entries.OfType<TrayMenuCommand>())
            command.Invoke();

        Assert.Equal(["capture-interface", "logs", "restart"], ran);
    }
}
