using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Hud.Tests;

// The two Hud events that fire many times a second are counted for the 60 s
// rollup. The count must not depend on a listener: with none attached the event
// is dropped, and a count that went with it could not tell a quiet HUD from an
// unwatched one.
// The class joins the Hud observability collection: it emits events of the
// process-wide Deckle-Hud provider, which a listener of a parallel test would hear.
[Collection("Hud operational observability")]
[Trait("Category", "observability")]
public sealed class HudHotLoopCountTests
{
    [Fact]
    public void ARevealGeometryEventIsCountedWithoutAListener()
    {
        long before = HotLoops.HudRevealGeometry.Total;

        DeckleHudSource.Log.RevealGeometry(1, 1, 0, 0, 100, 100);

        Assert.True(HotLoops.HudRevealGeometry.Total >= before + 1);
    }

    [Fact]
    public void AStateChangedEventIsCountedWithoutAListener()
    {
        long before = HotLoops.HudStateChanged.Total;

        DeckleHudSource.Log.StateChanged("Hidden", "Visible", "test", 255, 96);

        Assert.True(HotLoops.HudStateChanged.Total >= before + 1);
    }
}