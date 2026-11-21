using Deckle.Contributions.Tests.Fakes;
using Xunit;
using static Deckle.Contributions.Tests.Fakes.FakeContribution;

namespace Deckle.Contributions.Tests;

// The developer tray lines of a composition: which ones show, in what order,
// and that a click goes through the same gates as any other open.
[Trait("Category", "unit")]
public sealed class ContributionTrayTests
{
    private readonly FakeHostServices _services = new();
    private readonly Probe _probe = new();

    private ContributionHost Host() => new(
        [
            new FakeContribution
            {
                Id = "atelier",
                Descriptor = Module("atelier", 50),
                Windows = [_probe.Window("atelier", "TrayMenu_Atelier")],
            },
            new FakeContribution
            {
                Id = "mail",
                Windows = [_probe.Window("mail", "TrayMenu_Mail", developerOnly: true), _probe.Window("mail-aux")],
            },
        ],
        _services);

    [Fact]
    public void EntriesFollowTheCompositionAndSkipAWindowWithoutALabelKey()
    {
        _services.DeveloperModeEnabled = true;

        IReadOnlyList<ContributionTrayEntry> entries = Host().TrayEntries();

        Assert.Equal(["atelier", "mail"], entries.Select(e => e.Id));
        Assert.Equal(["TrayMenu_Atelier", "TrayMenu_Mail"], entries.Select(e => e.LabelKey));
    }

    [Fact]
    public void ADeveloperOnlyEntryIsHiddenWhileDeveloperModeIsOff()
    {
        _services.DeveloperModeEnabled = false;

        Assert.Equal(["atelier"], Host().TrayEntries().Select(e => e.Id));
    }

    [Fact]
    public void TheDeveloperMenuListsADeveloperOnlyEntryWhateverTheModeIsAtCompose()
    {
        _services.DeveloperModeEnabled = false;
        ContributionHost host = Host();

        IReadOnlyList<ContributionTrayEntry> entries = host.TrayEntries(forDeveloperMenu: true);

        Assert.Equal(["atelier", "mail"], entries.Select(e => e.Id));
        entries.Single(e => e.Id == "mail").Open();
        Assert.False(host.IsOpen("mail"));
    }

    [Fact]
    public void TheDeveloperMenuStillSkipsAnAbsentModule()
    {
        _services.AbsentModules.Add("atelier");

        Assert.Equal(["mail"], Host().TrayEntries(forDeveloperMenu: true).Select(e => e.Id));
    }

    [Fact]
    public void AnEntryOfAnAbsentModuleIsHidden()
    {
        _services.DeveloperModeEnabled = true;
        _services.AbsentModules.Add("atelier");

        Assert.Equal(["mail"], Host().TrayEntries().Select(e => e.Id));
    }

    [Fact]
    public void ClickingAnEntryOpensItsWindow()
    {
        _services.DeveloperModeEnabled = true;
        ContributionHost host = Host();

        host.TrayEntries().Single(e => e.Id == "mail").Open();

        Assert.True(host.IsOpen("mail"));
        Assert.Equal(["mail"], _services.Opened);
    }

    [Fact]
    public void AnEntryClickedAfterDeveloperModeWentOffDoesNotOpen()
    {
        _services.DeveloperModeEnabled = true;
        ContributionHost host = Host();
        ContributionTrayEntry entry = host.TrayEntries().Single(e => e.Id == "mail");
        _services.DeveloperModeEnabled = false;

        entry.Open();

        Assert.False(host.IsOpen("mail"));
    }
}
