using System.Diagnostics.Tracing;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Paragraph rewriting runs only where Deckle finds Ollama and the pinned
// model: the probe says so, the monitor holds the answer, and the host plan
// turns it into starting or stopping the global input host.
[Trait("Category", "unit")]
public sealed class ParagraphRewriteAvailabilityTests
{
    static LlmProvider Ollama(bool enabled = true) => new()
    {
        Id = ParagraphRewrite.ProviderId,
        Name = "Ollama",
        Kind = LlmProviderKind.Ollama,
        Endpoint = "http://localhost:11434/api/generate",
        Enabled = enabled,
    };

    static OllamaParagraphRewriteProbe ProbeOf(
        IReadOnlyList<LlmProvider> providers, OllamaParagraphRewriteProbe.ListModelNames list) =>
        new(() => providers, list);

    static OllamaParagraphRewriteProbe.ListModelNames Serving(params string[] names) =>
        (_, _) => Task.FromResult<IReadOnlyList<string>>(names);

    // ── The probe ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheModelListedByOllamaMakesParagraphRewritingAvailable()
    {
        var probe = ProbeOf([Ollama()], Serving("qwen3:8b", ParagraphRewrite.Model));

        Assert.Equal(ParagraphRewriteAvailability.Available, await probe.ProbeAsync(default));
    }

    [Fact]
    public async Task AnOllamaWithoutThePinnedModelIsReportedAsMissingTheModel()
    {
        // The 3B sibling is deliberately not a fallback.
        var probe = ProbeOf([Ollama()], Serving("ministral-3:3b", "qwen3:8b"));

        Assert.Equal(ParagraphRewriteAvailability.ModelMissing, await probe.ProbeAsync(default));
    }

    [Fact]
    public async Task AnEmptyOllamaIsReportedAsMissingTheModel()
    {
        var probe = ProbeOf([Ollama()], Serving());

        Assert.Equal(ParagraphRewriteAvailability.ModelMissing, await probe.ProbeAsync(default));
    }

    [Fact]
    public async Task AnOllamaThatDoesNotAnswerIsReportedAsUnreachableWithoutThrowing()
    {
        var probe = ProbeOf([Ollama()], (_, _) => throw new HttpRequestException("refused"));

        Assert.Equal(ParagraphRewriteAvailability.OllamaUnreachable, await probe.ProbeAsync(default));
    }

    [Fact]
    public async Task AnOllamaThatNeverAnswersIsReportedAsUnreachableOnceTheTimeoutRuns()
    {
        // The fake honours its token, as the HTTP call does: the probe's own
        // timeout is what ends the wait, not the caller's.
        var probe = ProbeOf([Ollama()], async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Array.Empty<string>();
        });

        Assert.Equal(ParagraphRewriteAvailability.OllamaUnreachable, await probe.ProbeAsync(default));
    }

    [Fact]
    public async Task TheCallersOwnCancellationStillCancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var probe = ProbeOf([Ollama()], (_, token) => Task.FromCanceled<IReadOnlyList<string>>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.ProbeAsync(cts.Token));
    }

    [Fact]
    public async Task ASwitchedOffOrMissingOllamaProviderAsksNothingOfTheNetwork()
    {
        bool asked = false;
        OllamaParagraphRewriteProbe.ListModelNames list = (_, _) =>
        {
            asked = true;
            return Task.FromResult<IReadOnlyList<string>>([ParagraphRewrite.Model]);
        };

        Assert.Equal(ParagraphRewriteAvailability.ProviderOff,
            await ProbeOf([Ollama(enabled: false)], list).ProbeAsync(default));
        Assert.Equal(ParagraphRewriteAvailability.ProviderOff,
            await ProbeOf([], list).ProbeAsync(default));
        Assert.False(asked);
    }

    [Fact]
    public async Task TheProbeAsksTheProvidersOwnAddress()
    {
        string? asked = null;
        var providers = new[] { Ollama() };
        providers[0].Endpoint = "http://localhost:9999/api/generate";
        var probe = ProbeOf(providers, (endpoint, _) =>
        {
            asked = endpoint;
            return Task.FromResult<IReadOnlyList<string>>([ParagraphRewrite.Model]);
        });

        await probe.ProbeAsync(default);

        Assert.Equal("http://localhost:9999/api/generate", asked);
    }

    // ── The host plan ──────────────────────────────────────────────────

    [Theory]
    [InlineData(true, ParagraphRewriteAvailability.Available, true)]
    [InlineData(false, ParagraphRewriteAvailability.Available, false)]
    [InlineData(true, ParagraphRewriteAvailability.Unknown, false)]
    [InlineData(true, ParagraphRewriteAvailability.OllamaUnreachable, false)]
    [InlineData(true, ParagraphRewriteAvailability.ModelMissing, false)]
    [InlineData(true, ParagraphRewriteAvailability.ProviderOff, false)]
    public void TheHostRunsOnlyWhileEnabledAndAvailable(
        bool enabled, ParagraphRewriteAvailability availability, bool expected)
    {
        Assert.Equal(expected, ParagraphRewriteHostPlan.ShouldRun(enabled, availability));
    }

    [Theory]
    [InlineData(false, true, ParagraphRewriteAvailability.Available, ParagraphRewriteHostAction.Start)]
    [InlineData(true, true, ParagraphRewriteAvailability.Available, ParagraphRewriteHostAction.None)]
    [InlineData(true, false, ParagraphRewriteAvailability.Available, ParagraphRewriteHostAction.Stop)]
    [InlineData(true, true, ParagraphRewriteAvailability.OllamaUnreachable, ParagraphRewriteHostAction.Stop)]
    [InlineData(false, true, ParagraphRewriteAvailability.ModelMissing, ParagraphRewriteHostAction.None)]
    [InlineData(false, false, ParagraphRewriteAvailability.Unknown, ParagraphRewriteHostAction.None)]
    public void AReconcileOnlyActsWhenTheHostIsNotWhereItShouldBe(
        bool running, bool enabled, ParagraphRewriteAvailability availability, ParagraphRewriteHostAction expected)
    {
        Assert.Equal(expected, ParagraphRewriteHostPlan.Next(running, enabled, availability));
    }

    // ── The notice ─────────────────────────────────────────────────────

    // The Autocorrect page's bar: the Rewriting switch is no longer part of it.
    [Theory]
    [InlineData(ParagraphRewriteAvailability.OllamaUnreachable, true)]
    [InlineData(ParagraphRewriteAvailability.ModelMissing, true)]
    [InlineData(ParagraphRewriteAvailability.ProviderOff, true)]
    [InlineData(ParagraphRewriteAvailability.Available, false)]
    [InlineData(ParagraphRewriteAvailability.Unknown, false)]
    public void ThePageAdvisesOnlyWhenOllamaOrTheModelIsMissing(
        ParagraphRewriteAvailability availability, bool expected)
    {
        Assert.Equal(expected, ParagraphRewriteNotices.NeedsSetup(availability));
    }

    // ── The monitor ────────────────────────────────────────────────────

    sealed class FakeProbe(params ParagraphRewriteAvailability[] answers) : IParagraphRewriteProbe
    {
        int _next;
        public Task<ParagraphRewriteAvailability> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(answers[Math.Min(_next++, answers.Length - 1)]);
    }

    [Fact]
    public async Task TheMonitorStartsUnknownAndTakesTheProbesAnswer()
    {
        var monitor = new ParagraphRewriteAvailabilityMonitor(
            new FakeProbe(ParagraphRewriteAvailability.Available));
        Assert.Equal(ParagraphRewriteAvailability.Unknown, monitor.Current);

        await monitor.RefreshAsync();

        Assert.Equal(ParagraphRewriteAvailability.Available, monitor.Current);
    }

    [Fact]
    public async Task ChangedFiresOnlyWhenTheStateChanges()
    {
        var monitor = new ParagraphRewriteAvailabilityMonitor(new FakeProbe(
            ParagraphRewriteAvailability.OllamaUnreachable,
            ParagraphRewriteAvailability.OllamaUnreachable,
            ParagraphRewriteAvailability.Available));
        int changes = 0;
        monitor.Changed += () => changes++;

        await monitor.RefreshAsync();
        await monitor.RefreshAsync();
        Assert.Equal(1, changes);

        await monitor.RefreshAsync();
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task AProbeThatFaultsCountsAsUnreachableAndDoesNotThrow()
    {
        var monitor = new ParagraphRewriteAvailabilityMonitor(new FaultingProbe());

        await monitor.RefreshAsync();

        Assert.Equal(ParagraphRewriteAvailability.OllamaUnreachable, monitor.Current);
    }

    sealed class FaultingProbe : IParagraphRewriteProbe
    {
        public Task<ParagraphRewriteAvailability> ProbeAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task AnOlderProbeAnsweringLastCannotOverwriteTheNewerOne()
    {
        var first = new TaskCompletionSource<ParagraphRewriteAvailability>();
        var monitor = new ParagraphRewriteAvailabilityMonitor(new SequencedProbe(
            first.Task, Task.FromResult(ParagraphRewriteAvailability.Available)));

        Task older = monitor.RefreshAsync();
        await monitor.RefreshAsync();
        first.SetResult(ParagraphRewriteAvailability.OllamaUnreachable);
        await older;

        Assert.Equal(ParagraphRewriteAvailability.Available, monitor.Current);
    }

    sealed class SequencedProbe(params Task<ParagraphRewriteAvailability>[] answers) : IParagraphRewriteProbe
    {
        int _next;
        public Task<ParagraphRewriteAvailability> ProbeAsync(CancellationToken cancellationToken) =>
            answers[_next++];
    }
}

// A degradation that repeats is one incident (deckle-logging): a PC without
// Ollama is probed every thirty seconds and says so once. In the observability
// collection: the monitors of ParagraphRewriteAvailabilityTests write the same
// event, and a run beside them counted theirs too.
[Collection(LlmRewriteObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class ParagraphRewriteAvailabilityLogTests
{
    sealed class Repeating(ParagraphRewriteAvailability answer) : IParagraphRewriteProbe
    {
        public Task<ParagraphRewriteAvailability> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    [Fact]
    public async Task AStateIsLoggedOncePerChangeNotOncePerProbe()
    {
        var monitor = new ParagraphRewriteAvailabilityMonitor(
            new Repeating(ParagraphRewriteAvailability.ModelMissing));
        using var listener = new TestEventListener("Deckle-Llm");

        await monitor.RefreshAsync();
        await monitor.RefreshAsync();
        await monitor.RefreshAsync();

        EventWrittenEventArgs logged = Assert.Single(listener.Events, e => e.EventId == DeckleLlmSource.EvtParagraphRewriteAvailabilityChanged);
        Assert.Equal(EventLevel.Informational, logged.Level);
        Assert.Contains("model_missing", logged.Payload!.Select(p => p?.ToString()));
    }
}
