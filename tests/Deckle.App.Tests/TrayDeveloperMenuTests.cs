using Deckle.Contributions;
using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The developer tray menu: the alpha surfaces (the contributed windows in
// composition order), then the developer tools, then Logs and Restart at the
// bottom, each group behind a separator. A contributed line is absent when its
// module is (the host drops it, ContributionHost). Deckle Projects is no longer
// one of its lines: it is an app of its own.
[Trait("Category", "unit")]
public sealed class TrayDeveloperMenuTests
{
    // The lines the composition's windows ask for, as the host hands them over.
    private static IReadOnlyList<ContributionTrayEntry> Contributed(bool contributed = true, Action? onPreview = null)
    {
        var entries = new List<ContributionTrayEntry>
        {
            new("preview", "TrayMenu_Preview", onPreview ?? (() => { })),
        };
        if (contributed) entries.Add(new("contributed", "TrayMenu_Contributed", () => { }));
        return entries;
    }

    private static IReadOnlyList<TrayMenuEntry> Compose(bool showContributed)
        => TrayDeveloperMenu.Compose(
            Contributed(showContributed), key => key,
            () => { }, () => { }, () => { });

    private static IEnumerable<string> CommandIds(IReadOnlyList<TrayMenuEntry> entries)
        => entries.OfType<TrayMenuCommand>().Select(c => c.Id);

    [Fact]
    public void TheMenuHoldsTheAlphaSurfacesThenTheCaptureThenLogsAndRestart()
    {
        Assert.Equal(
            ["preview", "contributed", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showContributed: true)));
    }

    [Fact]
    public void AContributedEntryIsAbsentWhenTheHostDroppedIt()
    {
        Assert.Equal(
            ["preview", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showContributed: false)));
    }

    [Fact]
    public void TheMenuHasNoProjectsEntry()
    {
        Assert.DoesNotContain("projects", CommandIds(Compose(showContributed: true)));
    }

    [Fact]
    public void ALabelIsReadFromItsResourceKeyWhenTheMenuOpens()
    {
        IReadOnlyList<TrayMenuEntry> entries = Compose(showContributed: true);

        Assert.Equal("TrayMenu_Preview", ((TrayMenuCommand)entries[0]).Label());
        Assert.Equal("TrayMenu_Contributed", ((TrayMenuCommand)entries[1]).Label());
    }

    [Fact]
    public void LogsAndRestartCloseTheMenuBehindTheirOwnSeparator()
    {
        IReadOnlyList<TrayMenuEntry> entries = Compose(showContributed: true);

        Assert.Equal(2, entries.OfType<TrayMenuSeparator>().Count());
        Assert.IsType<TrayMenuSeparator>(entries[^3]);
        Assert.Equal(TrayDeveloperMenu.LogsId, ((TrayMenuCommand)entries[^2]).Id);
        Assert.Equal(TrayDeveloperMenu.RestartId, ((TrayMenuCommand)entries[^1]).Id);
    }

    [Fact]
    public void TheEverydayCommandsAreNotInThisMenu()
    {
        IEnumerable<string> ids = CommandIds(Compose(showContributed: true));

        Assert.DoesNotContain("settings", ids);
        Assert.DoesNotContain("quit", ids);
    }

    [Fact]
    public void EachEntryRunsItsOwnAction()
    {
        var ran = new List<string>();
        IReadOnlyList<TrayMenuEntry> entries = TrayDeveloperMenu.Compose(
            [
                new("preview", "TrayMenu_Preview", () => ran.Add("preview")),
                new("contributed", "TrayMenu_Contributed", () => ran.Add("contributed")),
            ],
            key => key,
            () => ran.Add("capture-interface"), () => ran.Add("logs"), () => ran.Add("restart"));

        foreach (TrayMenuCommand command in entries.OfType<TrayMenuCommand>())
            command.Invoke();

        Assert.Equal(["preview", "contributed", "capture-interface", "logs", "restart"], ran);
    }
}
