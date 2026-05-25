using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Tests.Shared;
using Xunit;

namespace Deckle.Tests.Diagnostics;

// Sub-provider transverse — transitions d'état réseau. Test sentinelle qui
// vérifie que l'event canonique du provider sort avec ses quatre paramètres
// dans l'ordre déclaré (connected, profile, ipv4_count, ipv6_count) et au
// bon EventId / Level / Keyword. La présence du test à ce grain protège
// contre une régression silencieuse de schéma (réordering, retypage d'un
// paramètre) qui ne se verrait qu'au moment où un listener consommerait
// effectivement la valeur.
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
