using Deckle.Contributions.Tests.Fakes;
using Microsoft.UI.Xaml;
using Xunit;
using static Deckle.Contributions.Tests.Fakes.FakeContribution;
namespace Deckle.Contributions.Tests;

// How the host handles a contributed window: built on first use, once while
// it lives, dropped when closed, and opened only through its gates.
[Trait("Category", "unit")]
public sealed class ContributionWindowTests
{
    private readonly FakeHostServices _services = new();
    private readonly Probe _probe = new();

    private ContributionHost Host(
        string id = "mail", bool developerOnly = false, bool withRecordingState = false,
        Deckle.Modules.ModuleDescriptor? descriptor = null)
        => new(
            [new FakeContribution
            {
                Id = id,
                Descriptor = descriptor,
                Windows = [_probe.Window("mail", "TrayMenu_Mail", developerOnly, withRecordingState)],
            }],
            _services);

    [Fact]
    public void NothingIsBuiltUntilTheFirstOpen()
    {
        ContributionHost host = Host();

        Assert.Equal(0, _probe.Created);
        Assert.False(host.IsOpen("mail"));
    }

    [Fact]
    public void OpeningBuildsPlacesThemesAndShowsTheWindowOnce()
    {
        ContributionHost host = Host();

        Assert.True(host.Open("mail"));
        Assert.True(host.Open("mail"));

        Assert.Equal(1, _probe.Created);
        Window window = Assert.Single(_probe.Built);
        AssertSame([window, window], _probe.Shown);
        Assert.Equal(["mail"], _services.Placed);
        AssertSame([window], _services.Themed);
        Assert.Equal(["mail", "mail"], _services.Opened);
        AssertSame([window], host.OpenWindows);
    }

    [Fact]
    public void EnsureBuildsWithoutShowingAndWithoutTheGates()
    {
        ContributionHost host = Host(developerOnly: true);

        Window window = host.Ensure("mail");

        Assert.Same(window, host.Ensure("mail"));
        Assert.Equal(1, _probe.Created);
        Assert.Empty(_probe.Shown);
        Assert.Empty(_services.Opened);
    }

    [Fact]
    public void EnsureOfAnUnknownWindowIsAProgrammingError()
    {
        Assert.Throws<ArgumentException>(() => Host().Ensure("nobody"));
    }

    [Fact]
    public void ClosingDropsTheWindowSoTheNextOpenBuildsAFreshOneAndTheContributionIsTold()
    {
        ContributionHost host = Host();
        host.Open("mail");
        Window first = _probe.Built[0];

        _services.Close(first);

        Assert.False(host.IsOpen("mail"));
        AssertSame([first], _probe.Closed);

        host.Open("mail");
        Assert.Equal(2, _probe.Created);
        Assert.NotSame(first, _probe.Built[1]);
    }

    [Fact]
    public void ALateCloseOfAReplacedWindowDoesNotDropTheNewOne()
    {
        ContributionHost host = Host();
        host.Open("mail");
        Window first = _probe.Built[0];
        _services.Close(first);
        host.Open("mail");
        Window second = _probe.Built[1];

        _services.Close(first);

        Assert.True(host.IsOpen("mail"));
        AssertSame([second], host.OpenWindows);
    }

    [Fact]
    public void ADeveloperOnlyWindowDoesNotOpenWhileDeveloperModeIsOff()
    {
        ContributionHost host = Host(developerOnly: true);

        Assert.False(host.Open("mail"));
        Assert.Equal(0, _probe.Created);

        _services.DeveloperModeEnabled = true;
        Assert.True(host.Open("mail"));
        Assert.Equal(1, _probe.Created);
    }

    [Fact]
    public void AWindowOfAModuleThePersonDidNotInstallDoesNotOpen()
    {
        ContributionHost host = Host(id: "atelier", descriptor: Module("atelier"));
        _services.AbsentModules.Add("atelier");

        Assert.False(host.Open("mail"));
        Assert.Equal(0, _probe.Created);
    }

    [Fact]
    public void AnUnknownWindowDoesNotOpen()
    {
        Assert.False(Host().Open("nobody"));
    }

    [Fact]
    public void TheRecordingStateReachesOpenWindowsAndStartsALaterOneFromTheLastState()
    {
        ContributionHost host = Host(withRecordingState: true);

        host.SetRecordingState(true);
        Assert.Empty(_probe.RecordingStates);

        host.Open("mail");
        Window window = _probe.Built[0];
        Assert.Equal([true], _probe.RecordingStates.Select(r => r.State));
        AssertSame([window], _probe.RecordingStates.Select(r => r.Window));

        host.SetRecordingState(false);
        Assert.Equal([true, false], _probe.RecordingStates.Select(r => r.State));
        AssertSame([window, window], _probe.RecordingStates.Select(r => r.Window));
    }

    [Fact]
    public void AWindowWithoutARecordingStateHookIsLeftAlone()
    {
        ContributionHost host = Host(withRecordingState: false);
        host.Open("mail");

        host.SetRecordingState(true);

        Assert.Empty(_probe.RecordingStates);
    }
}
