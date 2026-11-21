using Deckle.Contributions;
using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The developer tray menu: the alpha surfaces (the contributed windows in
// composition order, then Deckle Projects), then the developer tools, then
// Logs and Restart at the bottom, each group behind a separator. A contributed
// line is absent when its module is (the host drops it, ContributionHost); the
// Projects line shows only while its module is present.
[Trait("Category", "unit")]
public sealed class TrayDeveloperMenuTests
{
    // The lines the composition's windows ask for, as the host hands them over.
    private static IReadOnlyList<ContributionTrayEntry> Contributed(bool atelier = true, Action? onPreview = null)
    {
        var entries = new List<ContributionTrayEntry>
        {
            new("preview", "TrayMenu_Preview", onPreview ?? (() => { })),
        };
        if (atelier) entries.Add(new("atelier", "TrayMenu_Atelier", () => { }));
        return entries;
    }

    private static IReadOnlyList<TrayMenuEntry> Compose(bool showAtelier, bool showProjects = true)
        => TrayDeveloperMenu.Compose(
            Contributed(showAtelier), showProjects, key => key,
            () => { }, () => { }, () => { }, () => { });

    private static IEnumerable<string> CommandIds(IReadOnlyList<TrayMenuEntry> entries)
        => entries.OfType<TrayMenuCommand>().Select(c => c.Id);

    [Fact]
    public void TheMenuHoldsTheAlphaSurfacesThenTheCaptureThenLogsAndRestart()
    {
        Assert.Equal(
            ["preview", "atelier", "projects", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showAtelier: true)));
    }

    [Fact]
    public void AContributedEntryIsAbsentWhenTheHostDroppedIt()
    {
        Assert.Equal(
            ["preview", "projects", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showAtelier: false)));
    }

    [Fact]
    public void TheProjectsEntryIsAbsentWhenItsModuleIs()
    {
        Assert.Equal(
            ["preview", "atelier", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showAtelier: true, showProjects: false)));
    }

    [Fact]
    public void ALabelIsReadFromItsResourceKeyWhenTheMenuOpens()
    {
        IReadOnlyList<TrayMenuEntry> entries = Compose(showAtelier: true);

        Assert.Equal("TrayMenu_Preview", ((TrayMenuCommand)entries[0]).Label());
        Assert.Equal("TrayMenu_Atelier", ((TrayMenuCommand)entries[1]).Label());
    }

    [Fact]
    public void LogsAndRestartCloseTheMenuBehindTheirOwnSeparator()
    {
        IReadOnlyList<TrayMenuEntry> entries = Compose(showAtelier: true);

        Assert.Equal(2, entries.OfType<TrayMenuSeparator>().Count());
        Assert.IsType<TrayMenuSeparator>(entries[^3]);
        Assert.Equal(TrayDeveloperMenu.LogsId, ((TrayMenuCommand)entries[^2]).Id);
        Assert.Equal(TrayDeveloperMenu.RestartId, ((TrayMenuCommand)entries[^1]).Id);
    }

    [Fact]
    public void TheEverydayCommandsAreNotInThisMenu()
    {
        IEnumerable<string> ids = CommandIds(Compose(showAtelier: true));

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
                new("atelier", "TrayMenu_Atelier", () => ran.Add("atelier")),
            ],
            true, key => key,
            () => ran.Add("projects"),
            () => ran.Add("capture-interface"), () => ran.Add("logs"), () => ran.Add("restart"));

        foreach (TrayMenuCommand command in entries.OfType<TrayMenuCommand>())
            command.Invoke();

        Assert.Equal(["preview", "atelier", "projects", "capture-interface", "logs", "restart"], ran);
    }
}
