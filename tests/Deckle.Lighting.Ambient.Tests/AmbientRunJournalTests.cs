using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// A run of Ambient lighting is one piece of lasting work: its journal lines
// share a work_id, its end carries the totals, and an incident it opened is
// closed when the run stops.
[Trait("Category", "observability")]
public sealed class AmbientRunJournalTests
{
    private sealed class Host : IAmbientEngineHost
    {
        public AmbientSettings Ambient { get; } = new();
    }

    public AmbientRunJournalTests()
        => OperationalLogAdmission.Configure(
            activity => activity == OperationalLogActivity.Ambient);

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    [Fact]
    public void EveryLifecycleMilestoneOfARunCarriesItsWorkId()
    {
        string[] milestones =
        [
            nameof(DeckleAmbientSource.PipelineStarting),
            nameof(DeckleAmbientSource.PipelineStarted),
            nameof(DeckleAmbientSource.PipelineStartFailed),
            nameof(DeckleAmbientSource.PipelineStartCancelled),
            nameof(DeckleAmbientSource.PipelineStopping),
            nameof(DeckleAmbientSource.PipelineStopped),
            nameof(DeckleAmbientSource.PipelineIncidentAbandoned),
        ];

        foreach (var declared in ProviderManifest.EventsOf(typeof(DeckleAmbientSource))
                     .Where(e => milestones.Contains(e.Name)))
        {
            Assert.True(ObservationTags.IsJournal(declared.Tags), $"{declared.Name} must travel in the journal.");
        }

        IReadOnlyList<string> payloads = ProviderManifest.PayloadNamesOf(typeof(DeckleAmbientSource));
        foreach (string milestone in milestones)
            Assert.Contains($"{milestone}.{WorkId.FieldName}", payloads);
    }

    [Fact]
    public void TheStopCarriesWhatTheRunPushedAndDropped()
    {
        using var listener = new TestEventListener("Deckle-Ambient");

        DeckleAmbientSource.Log.PipelineStopped(
            "w1", "user", "completed", 12.5, pushed: 640, dropped: 31, "none", "none");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleAmbientSource.EvtPipelineStopped);
        Assert.Equal("640", Field(ev, "pushed"));
        Assert.Equal("31", Field(ev, "dropped"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void StoppingWithAnOpenPushIncidentClosesItOnce()
    {
        using var listener = new TestEventListener("Deckle-Ambient");
        var engine = new AmbientEngine(new Host());

        for (int i = 0; i < 3; i++)
            engine.OnPushFailed(new HttpRequestException("bridge unreachable"));

        engine.AbandonOpenIncidents("w1");
        engine.AbandonOpenIncidents("w1");

        var closer = Assert.Single(
            listener.Events, e => e.EventId == DeckleAmbientSource.EvtPipelineIncidentAbandoned);
        Assert.Equal(AmbientIncidentKinds.PushFailures, Field(closer, "incident"));
        Assert.Equal("w1", Field(closer, WorkId.FieldName));
        Assert.Equal("3", Field(closer, "failures"));
    }

    [Fact]
    public void ARecoveredPushIncidentHasNothingLeftToClose()
    {
        using var listener = new TestEventListener("Deckle-Ambient");
        var engine = new AmbientEngine(new Host());

        for (int i = 0; i < 3; i++)
            engine.OnPushFailed(new HttpRequestException("bridge unreachable"));
        engine.OnPushSucceeded();
        engine.AbandonOpenIncidents("w1");

        Assert.Contains(listener.Events, e => e.EventId == DeckleAmbientSource.EvtPushRecovered);
        Assert.DoesNotContain(
            listener.Events, e => e.EventId == DeckleAmbientSource.EvtPipelineIncidentAbandoned);
    }

    [Fact]
    public void FailuresBelowTheIncidentThresholdHaveNothingToClose()
    {
        using var listener = new TestEventListener("Deckle-Ambient");
        var engine = new AmbientEngine(new Host());

        engine.OnPushFailed(new HttpRequestException("bridge unreachable"));
        engine.OnPushFailed(new HttpRequestException("bridge unreachable"));
        engine.AbandonOpenIncidents("w1");

        Assert.DoesNotContain(
            listener.Events, e => e.EventId == DeckleAmbientSource.EvtPipelineIncidentAbandoned);
    }
}