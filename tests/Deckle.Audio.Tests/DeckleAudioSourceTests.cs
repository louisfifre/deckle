using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Audio.Tests;

[Collection("Audio operational observability")]
[Trait("Category", "observability")]
public sealed class DeckleAudioSourceTests : IDisposable
{
    public DeckleAudioSourceTests()
    {
        OperationalLogAdmission.Configure(static _ => false);
        OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, true);
    }

    public void Dispose()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, false);
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void PerTakeTailDetailIsRejectedBeforePayloadAdmission()
    {
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.RecordingTailSummary("headline");
        DeckleAudioSource.Log.RecordingTailSummaryDetail(600, -42);
        DeckleAudioSource.Log.MicrophoneTelemetryEmpty();

        Assert.Empty(listener.Events);
    }

    [Fact]
    public void PerTakeTailDetailIsAdmittedWithTranscriptionDetails()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.RecordingTailSummary("headline");
        DeckleAudioSource.Log.RecordingTailSummaryDetail(600, -42);
        DeckleAudioSource.Log.MicrophoneTelemetryEmpty();

        Assert.Collection(
            listener.Events,
            e => Assert.Equal(DeckleAudioSource.EvtRecordingTailSummary, e.EventId),
            e => Assert.Equal(DeckleAudioSource.EvtRecordingTailSummaryDetail, e.EventId),
            e => Assert.Equal(DeckleAudioSource.EvtMicrophoneTelemetryEmpty, e.EventId));
    }
}
