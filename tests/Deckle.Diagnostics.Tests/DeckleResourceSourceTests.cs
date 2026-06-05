using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Cross-cutting sub-provider: native resource lifecycle.
// `Acquired` and `Released` are Verbose on the Resource keyword;
// `LeakSuspect` is Warning on the same keyword (anomaly that must surface even
// when Verbose is not listened to). Exercise all three to freeze the contract,
// including the Warning that no site emits today but whose signature is frozen.
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

    [Fact]
    public void ResourceLeakSuspectEmitsWarningOnResourceKeyword()
    {
        using var listener = new TestEventListener("Deckle-Resource");

        DeckleResourceSource.Log.ResourceLeakSuspect(
            kind: "dxgi-resource", handle: 0x1234L, age_ms: 60000,
            owner: "capture-loop", symptom: "finalizer-called");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleResourceSource.EvtResourceLeakSuspect, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Resource));
    }
}
