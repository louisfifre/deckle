using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// A rewrite of a take is lasting work: the log must hold its start and its
// end under one work_id, so a reader of the file alone can tell a rewrite that
// finished from one that never did. The paragraph and sentence rewrites run on
// every pause in typing and stay detail.
[Trait("Category", "observability")]
public sealed class RewriteLogTests
{
    [Fact]
    public void ACompletedTranscriptRewriteLogsItsStartAndItsEndUnderOneWorkId()
    {
        const string provider = "log-completed";
        var service = new RewriteService(new SingleEngine(provider, new AnsweringEngine()));
        using var listener = new TestEventListener("Deckle-Llm");

        service.Rewrite("euh bonjour", ProfileOf(provider));

        EventWrittenEventArgs started = Assert.Single(Of(listener, DeckleLlmSource.EvtTranscriptRewriteStarted, provider));
        EventWrittenEventArgs completed = Assert.Single(Of(listener, DeckleLlmSource.EvtTranscriptRewriteCompleted, provider));
        string workId = Field(started, WorkId.FieldName);
        Assert.NotEmpty(workId);
        Assert.Equal(workId, Field(completed, WorkId.FieldName));
        Assert.Equal(EventLevel.Informational, started.Level);
        Assert.Equal(EventLevel.Informational, completed.Level);
        Assert.True(IsMilestone(DeckleLlmSource.EvtTranscriptRewriteStarted));
        Assert.True(IsMilestone(DeckleLlmSource.EvtTranscriptRewriteCompleted));
    }

    [Fact]
    public void AFailedTranscriptRewriteEndsWithTheWarningThatCarriesTheStartsWorkId()
    {
        const string provider = "log-failed";
        var service = new RewriteService(new SingleEngine(provider, new ThrowingEngine()));
        using var listener = new TestEventListener("Deckle-Llm");

        service.Rewrite("euh bonjour", ProfileOf(provider));

        EventWrittenEventArgs started = Assert.Single(Of(listener, DeckleLlmSource.EvtTranscriptRewriteStarted, provider));
        EventWrittenEventArgs unavailable = Assert.Single(Of(listener, DeckleLlmSource.EvtRewriteUnavailable, provider));
        Assert.Equal(Field(started, WorkId.FieldName), Field(unavailable, WorkId.FieldName));
        Assert.Equal(EventLevel.Warning, unavailable.Level);
        Assert.Empty(Of(listener, DeckleLlmSource.EvtTranscriptRewriteCompleted, provider));
    }

    [Fact]
    public void AnInteractiveRewriteStaysDetail()
    {
        Assert.False(IsMilestone(DeckleLlmSource.EvtRewriteStarted));
        Assert.False(IsMilestone(DeckleLlmSource.EvtRewriteCompleted));
        Assert.False(IsMilestone(DeckleLlmSource.EvtRewriteCancelled));
        Assert.Equal(EventLevel.Verbose, LevelOf(DeckleLlmSource.EvtRewriteStarted));
        Assert.Equal(EventLevel.Verbose, LevelOf(DeckleLlmSource.EvtRewriteCompleted));
        Assert.Equal(EventLevel.Verbose, LevelOf(DeckleLlmSource.EvtRewriteCancelled));
    }

    // A dictation batch used to write "A dictation batch came back rewritten"
    // at the same millisecond as "Transcript rewrite complete", and "A
    // dictation batch left for rewriting" beside the rewrite's start: the
    // same moment told twice. The rewrite's own start and end are the run
    // story; the batch keeps only its Verbose details (index, sizes).
    [Fact]
    [Trait("Category", "regression")]
    public void ABatchRewriteTellsItsStartAndItsEndOnceInTheRunStory()
    {
        var events = ProviderManifest.EventsOf(typeof(DeckleLlmSource));
        Assert.DoesNotContain(events, d => d.Name is "BatchRewriteSent" or "BatchRewriteReturned");
        Assert.All(
            events.Where(d => d.Name.StartsWith("BatchRewrite", StringComparison.Ordinal)
                           && d.Name is not "BatchRewriteFellBack" and not "BatchRewriteProfileChanged"),
            d => Assert.Equal(EventLevel.Verbose, d.Level));
    }

    static RewriteProfile ProfileOf(string provider) => new()
    {
        Name = "Lissage", ProviderId = provider, Model = "test-model",
    };

    // The provider id is unique to each test: the Deckle-Llm provider is
    // process-wide, and a parallel test's rewrite lands in this listener too.
    static IEnumerable<EventWrittenEventArgs> Of(TestEventListener listener, int eventId, string provider) =>
        listener.Events.Where(e => e.EventId == eventId && Field(e, "provider") == provider);

    static string Field(EventWrittenEventArgs e, string name) =>
        (string)e.Payload![e.PayloadNames!.IndexOf(name)]!;

    static bool IsMilestone(int eventId) =>
        ObservationTags.IsMilestone(ProviderManifest.EventsOf(typeof(DeckleLlmSource)).Single(d => d.Id == eventId).Tags);

    static EventLevel LevelOf(int eventId) =>
        ProviderManifest.EventsOf(typeof(DeckleLlmSource)).Single(d => d.Id == eventId).Level;

    private sealed class SingleEngine(string providerId, IGenerationEngine engine) : IGenerationEngineResolver
    {
        public IGenerationEngine? Resolve(string id) => id == providerId ? engine : null;
    }

    private sealed class AnsweringEngine : IGenerationEngine
    {
        public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken) =>
            new("Bonjour.", 1, 0, 0, 0, 0, 0);
    }

    private sealed class ThrowingEngine : IGenerationEngine
    {
        public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}