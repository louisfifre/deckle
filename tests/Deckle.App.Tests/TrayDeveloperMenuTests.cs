using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The developer tray menu: the alpha surfaces, then the developer tools, then
// Logs and Restart at the bottom, each group behind a separator. The Atelier
// shows only while its module is present.
[Trait("Category", "unit")]
public sealed class TrayDeveloperMenuTests
{
    private static IReadOnlyList<TrayMenuEntry> Compose(bool showAtelier)
        => TrayDeveloperMenu.Compose(
            showAtelier, key => key, () => { }, () => { }, () => { }, () => { }, () => { });

    private static IEnumerable<string> CommandIds(IReadOnlyList<TrayMenuEntry> entries)
        => entries.OfType<TrayMenuCommand>().Select(c => c.Id);

    [Fact]
    public void TheMenuHoldsTheAlphaSurfacesThenTheCaptureThenLogsAndRestart()
    {
        Assert.Equal(
            ["atelier", "mail", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showAtelier: true)));
    }

    [Fact]
    public void TheAtelierEntryIsAbsentWhenItsModuleIs()
    {
        Assert.Equal(
            ["mail", "capture-interface", "logs", "restart"],
            CommandIds(Compose(showAtelier: false)));
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
            true, key => key,
            () => ran.Add("atelier"), () => ran.Add("mail"), () => ran.Add("capture-interface"),
            () => ran.Add("logs"), () => ran.Add("restart"));

        foreach (TrayMenuCommand command in entries.OfType<TrayMenuCommand>())
            command.Invoke();

        Assert.Equal(["atelier", "mail", "capture-interface", "logs", "restart"], ran);
    }
}
