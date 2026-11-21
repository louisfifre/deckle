using System.Diagnostics.Tracing;
using Deckle.Contributions.Tests.Fakes;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;
using static Deckle.Contributions.DeckleContributionsSource;

namespace Deckle.Contributions.Tests;

// What the log says when the host refuses a composition, an open, or a
// contribution's hooks: the line names the rule or the reason, never a path.
[Trait("Category", "observability")]
public sealed class ContributionObservabilityTests
{
    private readonly FakeHostServices _services = new();
    private readonly FakeContribution.Probe _probe = new();

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    [Fact]
    public void ARefusedCompositionIsAnErrorNamingTheContributionAndTheRule()
    {
        using var listener = new TestEventListener("Deckle-Contributions");
        var list = new IContribution[] { new FakeContribution { Id = "mail" }, new FakeContribution { Id = "mail" } };

        Assert.Throws<ContributionRegistrationException>(() => new ContributionHost(list, _services));

        EventWrittenEventArgs error = Assert.Single(listener.Events, e => e.EventId == EvtRegistrationRefused);
        Assert.Equal(EventLevel.Error, error.Level);
        Assert.Equal("mail", Field(error, "id"));
        Assert.Equal(RegistrationRefusals.DuplicateContribution, Field(error, "reason"));
    }

    [Fact]
    public void ARefusedOpenSaysWhyAsAMilestone()
    {
        using var listener = new TestEventListener("Deckle-Contributions");
        var host = new ContributionHost(
            [new FakeContribution { Id = "mail", Windows = [_probe.Window("mail", developerOnly: true)] }], _services);

        host.Open("mail");

        EventWrittenEventArgs line = Assert.Single(listener.Events, e => e.EventId == EvtWindowNotOpened);
        Assert.Equal(EventLevel.Informational, line.Level);
        Assert.True(ObservationTags.IsMilestone(line.Tags));
        Assert.Equal("mail", Field(line, "window"));
        Assert.Equal(WindowRefusals.DeveloperModeOff, Field(line, "reason"));
    }

    [Fact]
    public void AFailingStartHookIsAWarningNamingTheExceptionTypeAndNotItsMessage()
    {
        using var listener = new TestEventListener("Deckle-Contributions");
        var host = new ContributionHost(
            [new FakeContribution { Id = "mail", OnCompose = _ => throw new InvalidOperationException("secret detail") }],
            _services);

        host.Compose();

        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.EventId == EvtComposeFailed);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Equal("InvalidOperationException", Field(warning, "ex_type"));
        Assert.DoesNotContain(
            listener.Events, e => e.Payload!.Any(p => Convert.ToString(p)?.Contains("secret detail") == true));
    }
}
