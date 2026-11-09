using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// What a record from the DLL becomes in Deckle's log: the event of its level,
// with the DLL's own event name and its fields in the payload.
[Trait("Category", "observability")]
[Collection(TaskbarObservabilityCollection.Name)]
public sealed class NativeLogRelayTests
{
    private static NativeLogRecord Record(NativeLogLevel level) => new(
        level, "advise_failed", "AdviseVisualTreeChange failed",
        new Dictionary<string, string> { ["hr"] = "0x8000FFFF", ["tid"] = "7" });

    private static EventWrittenEventArgs Relayed(NativeLogLevel level)
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        NativeLogRelay.Relay(Record(level));
        return Assert.Single(listener.Events);
    }

    [Theory]
    [InlineData("Verbose", DeckleShellTaskbarSource.EvtNativeEngineDetail, EventLevel.Verbose)]
    [InlineData("Info", DeckleShellTaskbarSource.EvtNativeEngineNote, EventLevel.Informational)]
    [InlineData("Warning", DeckleShellTaskbarSource.EvtNativeEngineWarning, EventLevel.Warning)]
    [InlineData("Error", DeckleShellTaskbarSource.EvtNativeEngineError, EventLevel.Error)]
    public void EachLevelOfTheDllReachesTheEventOfTheSameLevel(string level, int eventId, EventLevel expected)
    {
        EventWrittenEventArgs written = Relayed(Enum.Parse<NativeLogLevel>(level));

        Assert.Equal(eventId, written.EventId);
        Assert.Equal(expected, written.Level);
    }

    [Fact]
    public void TheDllsEventNameAndFieldsTravelInThePayload()
    {
        EventWrittenEventArgs written = Relayed(NativeLogLevel.Warning);

        Assert.Equal(
            new[] { "advise_failed", "AdviseVisualTreeChange failed", "hr=0x8000FFFF | tid=7" },
            written.Payload!.Select(p => p?.ToString()).ToArray());
    }

    [Fact]
    public void AnInfoRecordCarriesItsNameAndFieldsOnly()
    {
        EventWrittenEventArgs written = Relayed(NativeLogLevel.Info);

        Assert.Equal(new[] { "advise_failed", "hr=0x8000FFFF | tid=7" }, written.Payload!.Select(p => p?.ToString()).ToArray());
    }
}

