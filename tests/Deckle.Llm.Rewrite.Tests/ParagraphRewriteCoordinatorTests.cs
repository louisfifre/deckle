using System.Diagnostics.Tracing;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

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

        public GenerationResult RewriteSentence(
            string sentence,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
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

        public GenerationResult RewriteSentence(
            string sentence,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Dispose() => Started.Dispose();
    }
}
