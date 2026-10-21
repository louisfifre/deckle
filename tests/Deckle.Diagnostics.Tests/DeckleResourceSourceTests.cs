using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "observability")]
public class DeckleResourceSourceTests
{
    [Fact]
    public void ResourceAcquiredEmitsVerboseOnResourceKeyword()
    {
        using var listener = new TestEventListener("Deckle-Resource");

        DeckleResourceSource.Log.ResourceAcquired(
            kind: "d3d11-texture", handle: 0xDEADBEEF, size_bytes: 1024, owner: "capture-loop");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleResourceSource.EvtResourceAcquired, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Resource));
        Assert.Equal("d3d11-texture", ev.Payload?[0]);
        Assert.Equal(0xDEADBEEFL, ev.Payload?[1]);
        Assert.Equal(1024, ev.Payload?[2]);
        Assert.Equal("capture-loop", ev.Payload?[3]);
    }

    [Fact]
    public void ResourceReleasedEmitsVerboseOnResourceKeyword()
    {
        using var listener = new TestEventListener("Deckle-Resource");

        DeckleResourceSource.Log.ResourceReleased(
            kind: "composition-visual", handle: 0xCAFEL, age_ms: 250, owner: "hud-message");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleResourceSource.EvtResourceReleased, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Resource));
    }
}
