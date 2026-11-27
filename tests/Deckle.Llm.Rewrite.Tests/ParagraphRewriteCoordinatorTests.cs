using System.Diagnostics.Tracing;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Collection(LlmRewriteObservabilityCollection.Name)]
[Trait("Category", "unit")]
public sealed class ParagraphRewriteCoordinatorTests
{
    [Fact]
    public void AcceptedChangedRewriteBecomesAnOffer()
    {
        using var ready = new ManualResetEventSlim();
        using var coordinator = CoordinatorReturning("Ça marche.");
        ParagraphRewriteOffer? observed = null;
        coordinator.OfferReady += offer =>
        {
            observed = offer;
            ready.Set();
        };

        coordinator.Request("Ca marche");

        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.NotNull(observed);
        Assert.Equal("Ça marche.", observed.Rewritten);
    }

    // A silent outcome cannot be awaited on OfferReady, which never fires. The
    // coordinator logs one ParagraphOfferSettled line per request whatever
    // became of it, so the test waits for that line (a positive signal) and
    // only then checks that no offer went out, instead of sleeping and hoping.
    [Theory]
    [InlineData("Ca marche", "Ca marche", "identity")]
    [InlineData("Ca marche", "Le système fonctionne", "gate_rejected")]
    public void IdentityAndRejectedRewriteStaySilent(string original, string rewritten, string outcome)
    {
        using var listener = new TestEventListener("Deckle-Llm");
        using var coordinator = CoordinatorReturning(rewritten);
        using var offered = new ManualResetEventSlim();
        coordinator.OfferReady += _ => offered.Set();

        coordinator.Request(original);
        AwaitSettled(listener, outcome);

        Assert.False(offered.IsSet);
    }

    [Fact]
    public void InputMutationSuppressesALateResult()
    {
        using var listener = new TestEventListener("Deckle-Llm");
        using var release = new ManualResetEventSlim();
        using var service = new BlockingRewriteService("Ça marche.", release);
        using var coordinator = new ParagraphRewriteCoordinator(service);
        using var offered = new ManualResetEventSlim();
        coordinator.OfferReady += _ => offered.Set();

        coordinator.Request("Ca marche");
        Assert.True(service.Started.Wait(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        coordinator.Invalidate();
        release.Set();
        AwaitSettled(listener, "stale");

        Assert.False(offered.IsSet);
    }

    // Option B of the trim plan: a proposal has no time limit. The model here
    // answers after more than the old 3 s limit and the offer still shows.
    [Fact]
    public void ProposalArrivingAfterMoreThanThreeSecondsIsStillOffered()
    {
        using var ready = new ManualResetEventSlim();
        using var service = new DelayedRewriteService("Ça marche.", TimeSpan.FromSeconds(3.5));
        using var coordinator = new ParagraphRewriteCoordinator(service);
        ParagraphRewriteOffer? observed = null;
        coordinator.OfferReady += offer =>
        {
            observed = offer;
            ready.Set();
        };

        coordinator.Request("Ca marche");

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(service.Waited >= TimeSpan.FromSeconds(3));
        Assert.NotNull(observed);
        Assert.Equal("Ça marche.", observed.Rewritten);
    }

    // Typing again cancels the request in flight: the service sees its token
    // cancelled, and the answer it gives afterwards is never offered.
    [Fact]
    public void TypingAgainCancelsTheRequestAndNothingIsOffered()
    {
        using var listener = new TestEventListener("Deckle-Llm");
        using var service = new DelayedRewriteService("Ça marche.", TimeSpan.FromSeconds(30));
        using var coordinator = new ParagraphRewriteCoordinator(service);
        using var offered = new ManualResetEventSlim();
        coordinator.OfferReady += _ => offered.Set();

        coordinator.Request("Ca marche");
        Assert.True(service.Started.Wait(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        coordinator.Invalidate();

        Assert.True(service.Cancelled.Wait(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        AwaitSettled(listener, "cancelled");
        Assert.False(offered.IsSet);
    }

    static void AwaitSettled(TestEventListener listener, string outcome)
    {
        bool Settled() => listener.Events.Any(e =>
            e.EventId == DeckleLlmSource.EvtParagraphOfferSettled
            && e.Payload![e.PayloadNames!.IndexOf("outcome")] as string == outcome);

        Assert.True(
            SpinWait.SpinUntil(Settled, TimeSpan.FromSeconds(5)),
            $"No ParagraphOfferSettled with outcome '{outcome}' was logged.");
    }

    private static ParagraphRewriteCoordinator CoordinatorReturning(string rewritten)
        => new(new ImmediateRewriteService(rewritten));

    private sealed class ImmediateRewriteService(string rewritten) : IRewriteService
    {
        public GenerationResult Rewrite(string text, RewriteProfile profile)
            => throw new NotSupportedException();

        public GenerationResult RewriteParagraph(
            string paragraph,
            CancellationToken cancellationToken)
            => new(rewritten, 1, 0, 0, 0, 0, 0);
    }

    // Answers only after a delay, and stops waiting early when its token is
    // cancelled, as a real engine does: a cancelled call returns no text.
    private sealed class DelayedRewriteService(string rewritten, TimeSpan delay)
        : IRewriteService, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Cancelled { get; } = new();
        public TimeSpan Waited { get; private set; }

        public GenerationResult Rewrite(string text, RewriteProfile profile)
            => throw new NotSupportedException();

        public GenerationResult RewriteParagraph(
            string paragraph,
            CancellationToken cancellationToken)
        {
            Started.Set();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool cancelled = cancellationToken.WaitHandle.WaitOne(delay);
            Waited = sw.Elapsed;
            if (cancelled)
            {
                Cancelled.Set();
                return default;
            }
            return new(rewritten, 1, 0, 0, 0, 0, 0);
        }

        public void Dispose()
        {
            Started.Dispose();
            Cancelled.Dispose();
        }
    }

    private sealed class BlockingRewriteService(
        string rewritten,
        ManualResetEventSlim release) : IRewriteService, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();

        public GenerationResult Rewrite(string text, RewriteProfile profile)
            => throw new NotSupportedException();

        public GenerationResult RewriteParagraph(
            string paragraph,
            CancellationToken cancellationToken)
        {
            Started.Set();
            release.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            return new(rewritten, 1, 0, 0, 0, 0, 0);
        }

        public void Dispose() => Started.Dispose();
    }
}
