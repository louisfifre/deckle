using Deckle.Diagnostics;
using Deckle.TestSupport;
using Deckle.Vad;
using Xunit;

namespace Deckle.Transcription.Tests;

// The engine runs the loaded VAD itself and reports each trim's outcome to the
// service; these reports need no model, so the service is driven directly.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class VadServiceIncidentTests : IDisposable
{
    public VadServiceIncidentTests()
        => OperationalLogAdmission.Configure(static _ => false);

    public void Dispose()
        => OperationalLogAdmission.Configure(static _ => false);

    [Fact]
    public void RepeatedTrimFailuresOpenOneIncidentThatTheNextTrimCloses()
    {
        using var vadService = new VadService(static () => Path.GetTempPath());
        using var listener = new TestEventListener("Deckle-Vad");

        vadService.ReportTrimFailure(1, new InvalidOperationException("first take"));
        vadService.ReportTrimFailure(1, new InvalidOperationException("second take"));
        vadService.ReportTrimSucceeded();

        var logged = listener.Events
            .Where(static e => e.EventId is DeckleVadSource.EvtSpeechTrimUnavailable
                                         or DeckleVadSource.EvtSpeechTrimRecovered)
            .ToList();
        Assert.Collection(
            logged,
            e => Assert.Equal(DeckleVadSource.EvtSpeechTrimUnavailable, e.EventId),
            e => Assert.Equal(DeckleVadSource.EvtSpeechTrimRecovered, e.EventId));

        // The recovery names the cause the opening reported.
        Assert.Equal(logged[0].Payload![0], logged[1].Payload![0]);
    }
}
