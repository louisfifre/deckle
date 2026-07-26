using Deckle.Llm.Rewrite;
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

    [Theory]
    [InlineData("Ca marche", "Ca marche")]
    [InlineData("Ca marche", "Le système fonctionne")]
    public void IdentityAndRejectedRewriteStaySilent(string original, string rewritten)
    {
        using var coordinator = CoordinatorReturning(rewritten);
        using var offered = new ManualResetEventSlim();
        coordinator.OfferReady += _ => offered.Set();

        coordinator.Request(original);

        Assert.False(offered.Wait(
            TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void InputMutationSuppressesALateResult()
    {
        using var release = new ManualResetEventSlim();
        using var service = new BlockingRewriteService("Ça marche.", release);
        using var coordinator = new ParagraphRewriteCoordinator(service, () => "local");
        using var offered = new ManualResetEventSlim();
        coordinator.OfferReady += _ => offered.Set();

        coordinator.Request("Ca marche");
        Assert.True(service.Started.Wait(
            TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        coordinator.Invalidate();
        release.Set();

        Assert.False(offered.Wait(
            TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken));
    }

    private static ParagraphRewriteCoordinator CoordinatorReturning(string rewritten)
        => new(new ImmediateRewriteService(rewritten), () => "local");

    private sealed class ImmediateRewriteService(string rewritten) : IRewriteService
    {
        public RewriteResult Rewrite(string text, string endpoint, RewriteProfile profile)
            => throw new NotSupportedException();

        public RewriteResult RewriteParagraph(
            string paragraph,
            string endpoint,
            CancellationToken cancellationToken)
            => new(rewritten, 1, 0, 0, 0, 0, 0);

        public RewriteResult RewriteSentence(
            string sentence,
            string endpoint,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class BlockingRewriteService(
        string rewritten,
        ManualResetEventSlim release) : IRewriteService, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();

        public RewriteResult Rewrite(string text, string endpoint, RewriteProfile profile)
            => throw new NotSupportedException();

        public RewriteResult RewriteParagraph(
            string paragraph,
            string endpoint,
            CancellationToken cancellationToken)
        {
            Started.Set();
            release.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            return new(rewritten, 1, 0, 0, 0, 0, 0);
        }

        public RewriteResult RewriteSentence(
            string sentence,
            string endpoint,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Dispose() => Started.Dispose();
    }
}
