using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.Tests;

// A recording is written under the telemetry folder, which usually sits in the
// profile, so the line names it under %USERPROFILE% and not under the account.
[Trait("Category", "observability")]
[Collection(InputObservabilityCollection.Name)]
public sealed class InputPathRedactionTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void ARecordingStartNamesItsFileUnderTheProfileVariable()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.RecordingStartedDetail(Path.Combine(Profile, "corpus", "frames.bin"));

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleInputSource.EvtRecordingStartedDetail);
        Assert.Equal(@"%USERPROFILE%\corpus\frames.bin", ev.Payload![0]);
    }

    [Fact]
    public void ARecordingStopNamesItsFileUnderTheProfileVariable()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.RecordingStoppedDetail(Path.Combine(Profile, "corpus", "frames.bin"), 12.5, 4096);

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleInputSource.EvtRecordingStoppedDetail);
        Assert.Equal(@"%USERPROFILE%\corpus\frames.bin", ev.Payload![0]);
    }

    [Fact]
    public void AFailedRecordingHidesAPathQuotedByTheFailureText()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.RecordingFailed("IOException", $"Access to '{Profile}\\corpus\\frames.bin' is denied.");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleInputSource.EvtRecordingFailed);
        Assert.Equal(@"Access to '%USERPROFILE%\corpus\frames.bin' is denied.", ev.Payload![1]);
    }
}
