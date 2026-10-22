using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Playground.Tests;

// The Playground's Warning and Error events used to say that something failed and
// nothing else, with the page, the operation and the error type in a Verbose
// detail no default log keeps. These tests hold the journal line to carrying
// its own cause. The events are picked by their payload, since other tests of
// this project emit on the same provider.
[Trait("Category", "observability")]
public sealed class PlaygroundFailureObservabilityTests
{
    private const string Provider = "Deckle-Playground";

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static string Line(EventWrittenEventArgs e)
        => string.Format(e.Message!, [.. e.Payload!]);

    [Fact]
    [Trait("Category", "regression")]
    public void ANavigationThatFailedNamesItsPageAndItsReasonInTheJournal()
    {
        using var listener = new TestEventListener(Provider);

        DecklePlaygroundSource.Log.NavigationFailed("ProbePage", "frame_returned_false");

        EventWrittenEventArgs failed = Assert.Single(
            listener.Events, e => e.EventId == DecklePlaygroundSource.EvtNavigationFailed && Field(e, "page") == "ProbePage");
        Assert.Equal(EventLevel.Error, failed.Level);
        Assert.Equal("frame_returned_false", Field(failed, "reason"));
        Assert.Contains("ProbePage", Line(failed));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ARejectedNavigationNamesItsReasonInTheJournal()
    {
        using var listener = new TestEventListener(Provider);

        DecklePlaygroundSource.Log.NavigationRejected("probe_reason");

        EventWrittenEventArgs rejected = Assert.Single(
            listener.Events, e => e.EventId == DecklePlaygroundSource.EvtNavigationRejected);
        Assert.Equal(EventLevel.Warning, rejected.Level);
        Assert.Contains("probe_reason", Line(rejected));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AHueCallThatFailedNamesItsOperationAndItsErrorTypeInTheJournal()
    {
        using var listener = new TestEventListener(Provider);

        DecklePlaygroundSource.Log.HueCallFailed("probe op", nameof(InvalidOperationException));

        EventWrittenEventArgs failed = Assert.Single(
            listener.Events, e => e.EventId == DecklePlaygroundSource.EvtHueCallFailed && Field(e, "op") == "probe op");
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Contains(nameof(InvalidOperationException), Line(failed));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AScreenCaptureThatFailedToStartNamesItsErrorTypeInTheJournal()
    {
        using var listener = new TestEventListener(Provider);

        DecklePlaygroundSource.Log.ScreenCaptureStartFailed("ProbeException");

        EventWrittenEventArgs failed = Assert.Single(
            listener.Events, e => e.EventId == DecklePlaygroundSource.EvtScreenCaptureStartFailed);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Contains("ProbeException", Line(failed));
    }

    [Fact]
    public void EveryPayloadOfTheProviderAppearsInItsMessageTemplate()
    {
        foreach (var declared in ProviderManifest.EventsOf(typeof(DecklePlaygroundSource)))
        {
            System.Reflection.MethodInfo method = typeof(DecklePlaygroundSource).GetMethod(declared.Name)!;
            string template = method
                .GetCustomAttributes(typeof(EventAttribute), inherit: false)
                .Cast<EventAttribute>().Single().Message ?? "";
            for (int i = 0; i < method.GetParameters().Length; i++)
            {
                Assert.True(
                    template.Contains("{" + i + "}", StringComparison.Ordinal)
                        || template.Contains("{" + i + ":", StringComparison.Ordinal),
                    $"{declared.Name}: payload {method.GetParameters()[i].Name} is missing from its message template.");
            }
        }
    }
}