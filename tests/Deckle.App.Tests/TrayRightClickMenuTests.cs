using Deckle.Shell.TrayMenu;
using Xunit;

namespace Deckle.App.Tests;

// The right-click tray menu and its conditional entries: the Atelier shows
// only while its module is present, the Playground and the Deckle Mail preview
// only while the developer setting is on.
[Trait("Category", "unit")]
public sealed class TrayRightClickMenuTests
{
    private static IReadOnlyList<TrayMenuEntry> Compose(bool showDeveloperSurfaces, bool showAtelier = false)
        => TrayRightClickMenu.Compose(
            showAtelier, showDeveloperSurfaces, key => key,
            () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });

    private static IEnumerable<string> CommandIds(IReadOnlyList<TrayMenuEntry> entries)
        => entries.OfType<TrayMenuCommand>().Select(c => c.Id);

    [Fact]
    public void TheDeveloperEntriesAreAbsentWhenTheDeveloperSettingIsOff()
    {
        Assert.DoesNotContain(TrayRightClickMenu.PlaygroundId, CommandIds(Compose(false)));
        Assert.DoesNotContain(TrayRightClickMenu.MailId, CommandIds(Compose(false)));
    }

    [Fact]
    public void TheDeveloperEntriesComeAfterSettingsWhenTheDeveloperSettingIsOn()
    {
        Assert.Equal(
            ["logs", "settings", "playground", "mail", "restart", "quit"],
            CommandIds(Compose(true)));
    }

    [Fact]
    public void TheMailEntryOpensTheMailWindow()
    {
        bool opened = false;
        IReadOnlyList<TrayMenuEntry> entries = TrayRightClickMenu.Compose(
            false, true, key => key,
            () => { }, () => { }, () => { }, () => { }, () => opened = true, () => { }, () => { });

        entries.OfType<TrayMenuCommand>().Single(c => c.Id == TrayRightClickMenu.MailId).Invoke();

        Assert.True(opened);
    }

    [Fact]
    public void TheAtelierEntryComesAfterSettingsWhenItsModuleIsPresent()
    {
        Assert.Equal(
            ["logs", "settings", "atelier", "playground", "mail", "restart", "quit"],
            CommandIds(Compose(true, showAtelier: true)));
        Assert.DoesNotContain(TrayRightClickMenu.AtelierId, CommandIds(Compose(true, showAtelier: false)));
    }

    [Fact]
    public void TheAtelierEntryFollowsItsModuleAndNotTheDeveloperSetting()
    {
        Assert.Equal(
            ["logs", "settings", "atelier", "restart", "quit"],
            CommandIds(Compose(false, showAtelier: true)));
    }

    [Fact]
    public void TheOtherEntriesAndTheirSeparatorAreTheSameWhateverTheSetting()
    {
        Assert.Equal(["logs", "settings", "restart", "quit"], CommandIds(Compose(false)));
        Assert.Single(Compose(false).OfType<TrayMenuSeparator>());
        Assert.Single(Compose(true).OfType<TrayMenuSeparator>());
    }
}
