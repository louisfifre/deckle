using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The right-click tray menu and its two conditional entries: the Atelier shows
// only while its module is present, the Playground only while the developer
// setting is on.
[Trait("Category", "unit")]
public sealed class TrayRightClickMenuTests
{
    private static IReadOnlyList<TrayMenuEntry> Compose(bool showPlayground, bool showAtelier = false)
        => TrayRightClickMenu.Compose(
            showAtelier, showPlayground, key => key, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });

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
    public void TheAtelierEntryComesAfterSettingsWhenItsModuleIsPresent()
    {
        Assert.Equal(
            ["logs", "settings", "atelier", "playground", "restart", "quit"],
            CommandIds(Compose(true, showAtelier: true)));
        Assert.DoesNotContain(TrayRightClickMenu.AtelierId, CommandIds(Compose(true, showAtelier: false)));
    }

    [Fact]
    public void TheOtherEntriesAndTheirSeparatorAreTheSameWhateverTheSetting()
    {
        Assert.Equal(["logs", "settings", "restart", "quit"], CommandIds(Compose(false)));
        Assert.Single(Compose(false).OfType<TrayMenuSeparator>());
        Assert.Single(Compose(true).OfType<TrayMenuSeparator>());
    }
}
