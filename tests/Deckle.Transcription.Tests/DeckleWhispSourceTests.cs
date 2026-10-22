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

        DeckleWhispSource.Log.MicrophoneUnavailable("probe", 2);
        DeckleWhispSource.Log.MicrophoneRecovered();
        DeckleWhispSource.Log.ModelUnavailable("file_not_found", "", "");
        DeckleWhispSource.Log.ModelRecovered();

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneUnavailable, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneRecovered, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelUnavailable, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelRecovered, e.EventId));
    }

    [Fact]
    public void FilteredHallucinationMetricsRemainWhenDetailsAreDisabled()
    {
        // The metrics explain why text went missing from a take, so the
        // Transcription detail control does not hold them back.
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.TranscribeHallucinationFiltered();
        DeckleWhispSource.Log.TranscribeHallucinationFilteredMetrics(
            1, 3, 34, "Sous-titrage Société Radio-Canada");

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtTranscribeHallucinationFiltered, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtTranscribeHallucinationFilteredMetrics, e.EventId));
    }

    [Fact]
    public void FileBatchMilestonesRemainWhenDetailsAreDisabled()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.FileTranscriptionBatchStarted("w1", 3);
        DeckleWhispSource.Log.FileTranscriptionBatchStartedDetail(3, 1);
        DeckleWhispSource.Log.FileTranscriptionBatchCompleted(
            "w1",
            3,
            TranscriptionOutcome.SavedToFile.ToString());

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtFileTranscriptionBatchStarted, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtFileTranscriptionBatchCompleted, e.EventId));
    }
}
