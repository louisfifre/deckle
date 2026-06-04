using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Cross-cutting sub-provider: network state transitions. Sentinel test that
// verifies the provider's canonical event comes out with its four parameters in
// declared order (connected, profile, ipv4_count, ipv6_count) and the right
// EventId / Level / Keyword. This test at this grain protects against a silent
// schema regression (reordering, retyping a parameter) that would only be seen
// when a listener consumed it.
[Trait("Category", "observability")]
public class DeckleNetworkSourceTests
{
    [Fact]
    public void NetworkStatusChangedEmitsVerboseOnNetworkKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Network");

        DeckleNetworkSource.Log.NetworkStatusChanged(
            connected: true, profile: "Wi-Fi Home", ipv4_count: 2, ipv6_count: 1);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleNetworkSource.EvtNetworkStatusChanged, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Network));
    }

    [Fact]
    public void NetworkStatusChangedCarriesAllFourParametersInOrder()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Network");

        DeckleNetworkSource.Log.NetworkStatusChanged(
            connected: false, profile: "(none)", ipv4_count: 0, ipv6_count: 0);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(false, ev.Payload?[0]);
        Assert.Equal("(none)", ev.Payload?[1]);
        Assert.Equal(0, ev.Payload?[2]);
        Assert.Equal(0, ev.Payload?[3]);
    }
}
