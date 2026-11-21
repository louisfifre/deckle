using Deckle.Contributions.Tests.Fakes;
using Xunit;
using static Deckle.Contributions.DeckleContributionsSource;
using static Deckle.Contributions.Tests.Fakes.FakeContribution;

namespace Deckle.Contributions.Tests;

// The rules a composition must satisfy before the host builds anything from
// it. Each test hands the host a composition and asserts what it refuses, by
// the reason it names.
[Trait("Category", "unit")]
public sealed class ContributionRegistrationTests
{
    private readonly FakeHostServices _services = new();
    private readonly Probe _probe = new();

    private ContributionHost Host(IReadOnlyList<IContribution> list, params string[] reserved) =>
        new(list, _services, reserved);

    private static string Refused(Action build)
        => Assert.Throws<ContributionRegistrationException>(build).Reason;

    [Fact]
    public void AnEmptyCompositionIsValidAndHoldsNothing()
    {
        ContributionHost host = Host([]);

        Assert.Empty(host.Descriptors);
        Assert.Empty(host.WindowNames);
        Assert.Empty(host.Walks);
        Assert.Empty(host.TrayEntries());
    }

    [Fact]
    public void ATwiceListedIdIsRefused()
    {
        var list = new IContribution[] { new FakeContribution { Id = "mail" }, new FakeContribution { Id = "mail" } };

        Assert.Equal(RegistrationRefusals.DuplicateContribution, Refused(() => Host(list)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mail")]
    [InlineData("my mail")]
    [InlineData("1mail")]
    [InlineData("mäil")]
    public void AnIdOutsideTheModuleIdShapeIsRefused(string id)
    {
        var list = new IContribution[] { new FakeContribution { Id = id } };

        Assert.Equal(RegistrationRefusals.InvalidId, Refused(() => Host(list)));
    }

    [Fact]
    public void ADescriptorOfAnotherIdIsRefused()
    {
        var list = new IContribution[] { new FakeContribution { Id = "mail", Descriptor = Module("atelier") } };

        Assert.Equal(RegistrationRefusals.DescriptorIdMismatch, Refused(() => Host(list)));
    }

    [Fact]
    public void ATwiceUsedWindowNameIsRefusedAcrossContributions()
    {
        var list = new IContribution[]
        {
            new FakeContribution { Id = "mail", Windows = [_probe.Window("main")] },
            new FakeContribution { Id = "atelier", Windows = [_probe.Window("main")] },
        };

        Assert.Equal(RegistrationRefusals.DuplicateWindow, Refused(() => Host(list)));
    }

    [Fact]
    public void AWindowNameTheExecutableOwnsIsRefused()
    {
        var list = new IContribution[] { new FakeContribution { Id = "mail", Windows = [_probe.Window("settings")] } };

        Assert.Equal(RegistrationRefusals.ReservedWindowName, Refused(() => Host(list, "settings", "log")));
    }

    [Fact]
    public void AWindowNameOutsideTheShapeIsRefused()
    {
        var list = new IContribution[] { new FakeContribution { Id = "mail", Windows = [_probe.Window("Main Window")] } };

        Assert.Equal(RegistrationRefusals.InvalidWindowName, Refused(() => Host(list)));
    }

    [Fact]
    public void AWalkOfAWindowTheContributionDoesNotOpenIsRefused()
    {
        var list = new IContribution[]
        {
            new FakeContribution
            {
                Id = "mail",
                Windows = [_probe.Window("mail")],
                SelfCheckWalks = [new ContributedWalk { WindowName = "atelier", Run = _ => Task.CompletedTask }],
            },
        };

        Assert.Equal(RegistrationRefusals.WalkWithoutWindow, Refused(() => Host(list)));
    }

    [Fact]
    public void ALoopInDependsOnAmongTheContributionsIsRefused()
    {
        var list = new IContribution[]
        {
            new FakeContribution { Id = "a", Descriptor = Module("a", 1, "b") },
            new FakeContribution { Id = "b", Descriptor = Module("b", 2, "a") },
        };

        Assert.Equal(RegistrationRefusals.DependencyCycle, Refused(() => Host(list)));
    }

    [Fact]
    public void ADependencyOnAModuleOfTheHostIsNotALoop()
    {
        var list = new IContribution[]
        {
            new FakeContribution { Id = "atelier", Descriptor = Module("atelier", 1, "rewrite") },
        };

        ContributionHost host = Host(list);

        Assert.Single(host.Descriptors);
    }

    [Fact]
    public void DescriptorsComeInSelectorOrderAndSkipAContributionThatIsNotAModule()
    {
        var list = new IContribution[]
        {
            new FakeContribution { Id = "atelier", Descriptor = Module("atelier", 50) },
            new FakeContribution { Id = "preview" },
            new FakeContribution { Id = "mail", Descriptor = Module("mail", 10) },
        };

        Assert.Equal(["mail", "atelier"], Host(list).Descriptors.Select(d => d.Id));
    }

    [Fact]
    public void WindowNamesAndWalksFollowTheCompositionOrder()
    {
        var walkA = new ContributedWalk { WindowName = "a-main", Views = ["x", "y"], Run = _ => Task.CompletedTask };
        var walkB = new ContributedWalk { WindowName = "b-main", Run = _ => Task.CompletedTask };
        var list = new IContribution[]
        {
            new FakeContribution { Id = "a", Windows = [_probe.Window("a-main"), _probe.Window("a-aux")], SelfCheckWalks = [walkA] },
            new FakeContribution { Id = "b", Windows = [_probe.Window("b-main")], SelfCheckWalks = [walkB] },
        };

        ContributionHost host = Host(list);

        Assert.Equal(["a-main", "a-aux", "b-main"], host.WindowNames);
        Assert.Equal([walkA, walkB], host.Walks);
        Assert.Equal(["x", "y"], host.ViewsOf("a-main"));
        Assert.Empty(host.ViewsOf("b-main"));
        Assert.Empty(host.ViewsOf("nobody"));
    }

    [Fact]
    public void ComposeRunsEachContributionOnceWithTheContextAndSurvivesAFailingOne()
    {
        string? directory = null;
        var broken = new FakeContribution { Id = "broken", OnCompose = _ => throw new InvalidOperationException("boom") };
        var fine = new FakeContribution
        {
            Id = "fine",
            OnCompose = context =>
            {
                directory = context.ModuleDirectory("fine");
                context.OpenSettingsPage("rewrite");
            },
        };
        ContributionHost host = Host([broken, fine]);

        host.Compose();

        Assert.Equal(1, broken.ComposeCalls);
        Assert.Equal(1, fine.ComposeCalls);
        Assert.Equal(Path.Combine("data", "modules", "fine"), directory);
        Assert.Equal(["rewrite"], _services.SettingsPages);
    }
}
