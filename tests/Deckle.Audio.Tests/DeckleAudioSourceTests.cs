using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
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

    private sealed class TestEventListener : EventListener
    {
        private readonly string _providerName;
        private readonly ConcurrentQueue<EventWrittenEventArgs> _events = new();

        public TestEventListener(string providerName)
        {
            _providerName = providerName;
            foreach (EventSource source in EventSource.GetSources())
                OnEventSourceCreated(source);
        }

        public IReadOnlyCollection<EventWrittenEventArgs> Events => _events.ToArray();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == _providerName)
                EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
            => _events.Enqueue(eventData);
    }
}
