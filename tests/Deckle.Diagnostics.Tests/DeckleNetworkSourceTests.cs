using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Pins the parameter order and types: a reordered or retyped parameter would
// otherwise only show when a listener consumed it.
[Trait("Category", "observability")]
[Collection(DiagnosticsProviderCollection.Name)]
public class DeckleNetworkSourceTests
{
    [Fact]
    public void NetworkStatusChangedEmitsVerboseOnNetworkKeyword()
    {
        using var listener = new TestEventListener("Deckle-Network");

        DeckleNetworkSource.Log.NetworkStatusChanged(
            connected: true, medium: NetworkMedium.Wifi, ipv4_count: 2, ipv6_count: 1);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleNetworkSource.EvtNetworkStatusChanged, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Network));
    }

    [Fact]
    public void NetworkStatusChangedCarriesAllFourParametersInOrder()
    {
        using var listener = new TestEventListener("Deckle-Network");

        DeckleNetworkSource.Log.NetworkStatusChanged(
            connected: false, medium: NetworkMedium.None, ipv4_count: 0, ipv6_count: 0);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(false, ev.Payload?[0]);
        Assert.Equal("none", ev.Payload?[1]);
        Assert.Equal(0, ev.Payload?[2]);
        Assert.Equal(0, ev.Payload?[3]);
    }

    // A Wi-Fi name can identify a place, and the journal travels to another
    // machine: the snapshot says the kind of link, never the name of the network.
    [Fact]
    public void NetworkStatusChangedHasNoFieldThatCouldHoldANetworkName()
    {
        using var listener = new TestEventListener("Deckle-Network");

        DeckleNetworkSource.Log.NetworkStatusChanged(
            connected: true, medium: NetworkMedium.Ethernet, ipv4_count: 1, ipv6_count: 1);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(["connected", "medium", "ipv4_count", "ipv6_count"], ev.PayloadNames);
    }

    [Theory]
    [InlineData(6u, "ethernet")]
    [InlineData(71u, "wifi")]
    [InlineData(243u, "cellular")]
    [InlineData(244u, "cellular")]
    [InlineData(131u, "other")]
    [InlineData(24u, "other")]
    public void NetworkMediumNamesTheLinkFromItsInterfaceType(uint ianaInterfaceType, string expected)
    {
        Assert.Equal(expected, NetworkMedium.FromInterfaceType(ianaInterfaceType));
    }
}

// A WinRT query that throws is a degradation, reported once and closed by one
// recovery; it is never a connectivity state.
[Trait("Category", "observability")]
[Collection(DiagnosticsProviderCollection.Name)]
public class NetworkQueryIncidentsTests
{
    [Fact]
    public void A_stage_that_keeps_failing_is_one_warning()
    {
        using var listener = new TestEventListener("Deckle-Network");
        var incidents = new NetworkQueryIncidents();

        incidents.Failed("connection_profile", new InvalidOperationException("first"));
        incidents.Failed("connection_profile", new InvalidOperationException("second"));

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleNetworkSource.EvtNetworkQueryFailed, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("connection_profile", ev.Payload?[0]);
        Assert.Equal("InvalidOperationException", ev.Payload?[1]);
    }

    [Fact]
    public void The_recovery_of_a_failed_stage_is_one_journal_milestone()
    {
        using var listener = new TestEventListener("Deckle-Network");
        var incidents = new NetworkQueryIncidents();

        incidents.Failed("connectivity_level", new InvalidOperationException("gone"));
        incidents.Succeeded("connectivity_level");
        incidents.Succeeded("connectivity_level");

        Assert.Equal(2, listener.Events.Count);
        var recovered = listener.Events[1];
        Assert.Equal(DeckleNetworkSource.EvtNetworkQueryRecovered, recovered.EventId);
        Assert.Equal("connectivity_level", recovered.Payload?[0]);
        Assert.True((recovered.Tags & ObservationTags.Journal) != 0);
    }

    [Fact]
    public void A_stage_that_never_failed_says_nothing_when_it_succeeds()
    {
        using var listener = new TestEventListener("Deckle-Network");
        var incidents = new NetworkQueryIncidents();

        incidents.Succeeded("host_names");

        Assert.Empty(listener.Events);
    }

    [Fact]
    public void Each_stage_is_its_own_incident()
    {
        using var listener = new TestEventListener("Deckle-Network");
        var incidents = new NetworkQueryIncidents();

        incidents.Failed("medium", new InvalidOperationException("a"));
        incidents.Failed("host_names", new InvalidOperationException("b"));

        Assert.Equal(2, listener.Events.Count);
    }
}
