using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Tests.Shared;
using Xunit;

namespace Deckle.Tests.Diagnostics;

// Sub-provider transverse — cycle de vie des ressources natives.
// `Acquired` et `Released` sont Verbose sur le keyword Resource ;
// `LeakSuspect` est Warning sur le même keyword (anomalie qui doit remonter
// même quand le Verbose n'est pas écouté). On exerce les trois pour figer
// le contrat, y compris le Warning qu'aucun site n'émet aujourd'hui mais
// dont la signature est gelée.
[Trait("Category", "observability")]
public class DeckleResourceSourceTests
{
    [Fact]
    public void ResourceAcquiredEmitsVerboseOnResourceKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Resource");

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
        using var listener = new TestEventListener("Deckle.Diagnostics.Resource");

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
        using var listener = new TestEventListener("Deckle.Diagnostics.Resource");

        DeckleResourceSource.Log.ResourceLeakSuspect(
            kind: "dxgi-resource", handle: 0x1234L, age_ms: 60000,
            owner: "capture-loop", symptom: "finalizer-called");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleResourceSource.EvtResourceLeakSuspect, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Resource));
    }
}
