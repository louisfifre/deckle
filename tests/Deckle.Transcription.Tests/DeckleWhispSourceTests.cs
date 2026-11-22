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

        DeckleWhispSource.Log.TranscribeDecodeUsed(0, 0, false, true, 0, 0, 3, 4.2, 4.5, false);
        DeckleWhispSource.Log.SegmentRecognized(1, 0, 1, 1, 0.1, 0.8, 0.6, 3, 5, 12);

        Assert.Empty(listener.Events);
    }

    [Fact]
    public void OperationalDetailContainsMetricsButNoUserContent()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.TranscribeDecodeUsed(0, 0, false, true, 0, 0, 3, 4.2, 4.5, false);
        DeckleWhispSource.Log.SegmentRecognized(1, 0, 1, 1, 0.1, 0.8, 0.6, 3, 5, 12);

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtTranscribeDecodeUsed, e.EventId),
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

        DeckleWhispSource.Log.MicrophoneUnavailable("probe", "0x80070002");
        DeckleWhispSource.Log.MicrophoneRecovered();
        DeckleWhispSource.Log.ModelUnavailable("w1", "file_not_found", "", false, "", "");
        DeckleWhispSource.Log.ModelRecovered("w2", 1200, "CPU");

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneUnavailable, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtMicrophoneRecovered, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelUnavailable, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelRecovered, e.EventId));
    }

    // Version 2 passed the code as a uint: no typed WriteEvent overload takes
    // it, and the listener read its bytes as text ("�￿").
    [Theory]
    [InlineData(0x88890004u, "0x88890004")]
    [InlineData(0xFFFFD8F0u, "-10000")]
    public void TheMicrophoneIncidentCarriesItsCodeAsAPersonQuotesIt(uint code, string expected)
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.MicrophoneUnavailable("probe", Deckle.Audio.MicrophoneCapture.FormatErrorCode(code));

        var e = Assert.Single(listener.Events);
        Assert.Equal(expected, e.Payload![e.PayloadNames!.IndexOf("error_code")]);
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
