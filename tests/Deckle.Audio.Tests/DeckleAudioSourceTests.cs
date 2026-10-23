using System.Diagnostics.Tracing;
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

    [Fact]
    public void RecordingStartAndEndShareOneWorkIdInTheJournal()
    {
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.RecordingStarted("dictation", "Mic", 0, true, 16000, 1, 16, "w1");
        DeckleAudioSource.Log.RecordingCompleted("dictation", "stop_requested", 2.5, "w1");

        Assert.Collection(
            listener.Events,
            e => AssertJournalLine(e, DeckleAudioSource.EvtRecordingStarted, "w1"),
            e => AssertJournalLine(e, DeckleAudioSource.EvtRecordingCompleted, "w1"));
    }

    [Fact]
    public void AnomalySummaryCarriesTheCountsOfARecordingThatEndedMidAnomaly()
    {
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.RecordingAnomaliesSummary(
            no_data_stalls: 0, no_data_longest_gap_ms: 0, no_data_recovered: false,
            empty_buffer_occurrences: 0, empty_buffer_recovered: false,
            lag_occurrences: 3, lag_recovered: false, lag_max_buffers_ready: 4,
            recording_ms: 1800, work_id: "w7");

        EventWrittenEventArgs line = Assert.Single(listener.Events);
        AssertJournalLine(line, DeckleAudioSource.EvtRecordingAnomaliesSummary, "w7");
        Assert.Equal(3, PayloadOf(line, "lag_occurrences"));
        Assert.Equal(false, PayloadOf(line, "lag_recovered"));
        Assert.Equal(4, PayloadOf(line, "lag_max_buffers_ready"));
    }

    [Fact]
    public void LagWarningNamesHowFarBehindTheLoopFell()
    {
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.CaptureLagDetected(buffers_ready: 3, behind_ms: 150);

        EventWrittenEventArgs line = Assert.Single(listener.Events);
        Assert.Equal(EventLevel.Warning, line.Level);
        Assert.Equal(3, PayloadOf(line, "buffers_ready"));
        Assert.Equal(150, PayloadOf(line, "behind_ms"));
    }

    // One play per user action: the device it opened and the outcome it ended with
    // both reach the journal, so a playback that never sounds is readable from the file.
    [Fact]
    public void APlaybackTravelsInTheJournalFromItsDeviceToItsOutcome()
    {
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.PlaybackStarted("Speakers", 0, 24000, 1500);
        DeckleAudioSource.Log.PlaybackCompleted("cancelled", 400);

        Assert.Collection(
            listener.Events,
            started =>
            {
                Assert.Equal(DeckleAudioSource.EvtPlaybackStarted, started.EventId);
                Assert.True(ObservationTags.IsJournal(started.Tags));
            },
            ended =>
            {
                Assert.Equal(DeckleAudioSource.EvtPlaybackCompleted, ended.EventId);
                Assert.True(ObservationTags.IsJournal(ended.Tags));
                Assert.Equal("cancelled", PayloadOf(ended, "outcome"));
            });
    }

    private static void AssertJournalLine(EventWrittenEventArgs e, int eventId, string workId)
    {
        Assert.Equal(eventId, e.EventId);
        Assert.True(ObservationTags.IsJournal(e.Tags));
        Assert.Equal(workId, PayloadOf(e, "work_id"));
    }

    private static object? PayloadOf(EventWrittenEventArgs e, string name)
        => e.Payload![e.PayloadNames!.IndexOf(name)];
}
