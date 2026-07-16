using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class DeckleWhispSourceTests : IDisposable
{
    public DeckleWhispSourceTests()
        => OperationalLogAdmission.Configure(static _ => false);

    public void Dispose()
        => OperationalLogAdmission.Configure(static _ => false);

    [Fact]
    public void DisabledAdmissionRejectsOperationalDetail()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.TranscribePromptConfigured(42, carry: true);
        DeckleWhispSource.Log.TranscribeRepetitionLoopMetrics(4, 2);
        DeckleWhispSource.Log.SegmentRecognized(1, 0, 1, 1, 0.1, 0.8, 0.6, 3, 5, 12);

        Assert.Empty(listener.Events);
    }

    [Fact]
    public void OperationalDetailContainsMetricsButNoUserContent()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.TranscribePromptConfigured(42, carry: true);
        DeckleWhispSource.Log.TranscribeRepetitionLoopMetrics(4, 2);
        DeckleWhispSource.Log.SegmentRecognized(1, 0, 1, 1, 0.1, 0.8, 0.6, 3, 5, 12);

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtTranscribePromptConfigured, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtTranscribeRepetitionLoopMetrics, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtSegmentRecognized, e.EventId));

        Assert.All(listener.Events, e =>
        {
            Assert.NotNull(e.Payload);
            Assert.DoesNotContain(e.Payload!, value => value is string);
        });
    }

    [Fact]
    public void DependencyIncidentsAndRecoveriesRemainWhenDetailsAreDisabled()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.MicrophoneUnavailable();
        DeckleWhispSource.Log.MicrophoneUnavailableDetail("probe", 2);
        DeckleWhispSource.Log.MicrophoneRecovered();
        DeckleWhispSource.Log.ModelUnavailable();
        DeckleWhispSource.Log.ModelUnavailableDetail("file_not_found");
        DeckleWhispSource.Log.ModelRecovered();

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneUnavailable, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneUnavailableDetail, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneRecovered, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelUnavailable, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelUnavailableDetail, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelRecovered, e.EventId));
    }
}
