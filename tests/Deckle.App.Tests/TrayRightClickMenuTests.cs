using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The right-click tray menu and its one developer entry: the Playground shows
// only when the developer setting is on.
[Trait("Category", "unit")]
public sealed class TrayRightClickMenuTests
{
    private static IReadOnlyList<TrayMenuEntry> Compose(bool showPlayground)
        => TrayRightClickMenu.Compose(
            showPlayground, key => key, () => { }, () => { }, () => { }, () => { }, () => { });

    private static IEnumerable<string> CommandIds(IReadOnlyList<TrayMenuEntry> entries)
        => entries.OfType<TrayMenuCommand>().Select(c => c.Id);

    [Fact]
    public void ThePlaygroundEntryIsAbsentWhenTheDeveloperSettingIsOff()
    {
        Assert.DoesNotContain(TrayRightClickMenu.PlaygroundId, CommandIds(Compose(false)));
    }

    [Fact]
    public void ThePlaygroundEntryComesAfterSettingsWhenTheDeveloperSettingIsOn()
    {
        Assert.Equal(
            ["logs", "settings", "playground", "restart", "quit"],
            CommandIds(Compose(true)));
    }

    [Fact]
    public void TheOtherEntriesAndTheirSeparatorAreTheSameWhateverTheSetting()
    {
        Assert.Equal(["logs", "settings", "restart", "quit"], CommandIds(Compose(false)));
        Assert.Single(Compose(false).OfType<TrayMenuSeparator>());
        Assert.Single(Compose(true).OfType<TrayMenuSeparator>());
    }
}
