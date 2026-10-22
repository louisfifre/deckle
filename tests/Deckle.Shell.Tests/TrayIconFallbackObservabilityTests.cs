using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.Tests;

// The tray icon falls back to the generic application icon when Deckle's own is
// not next to the exe. The test project's output has no Assets/Icons folder,
// which is exactly that case, so the real loader runs without a window.
[Trait("Category", "observability")]
public sealed class TrayIconFallbackObservabilityTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void ALoaderThatCannotFindTheIconFallsBackWithAWarningNamingTheStateAndTheReason()
    {
        using var listener = new TestEventListener("Deckle-Shell");

        (IntPtr hIcon, bool owned) = TrayIconManager.LoadIconFromFile(active: true);

        Assert.NotEqual(IntPtr.Zero, hIcon);
        Assert.False(owned);
        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleShellSource.EvtTrayIconFallback);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("recording", ev.Payload?[0]);
        Assert.Equal("asset_missing", ev.Payload?[1]);
        Assert.Equal(0, ev.Payload?[2]);
    }
}