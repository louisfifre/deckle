using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Hud;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Hud.Tests;

// The admission reader is process-wide static state: the collection keeps
// these tests from racing on it.
[Collection("Hud operational observability")]
[Trait("Category", "observability")]
public sealed class DeckleHudSourceTests : IDisposable
{
    public DeckleHudSourceTests()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Windowing);
    }

    public void Dispose()
    {
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void ProximityRollupEmitsVerboseOnHeartbeatKeyword()
    {
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.ProximityRollup(
            duration_ms: 4200, samples: 525,
            min_alpha: 100, max_alpha: 240,
            p50_cursor_dist_dip: 64, p95_cursor_dist_dip: 12);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleHudSource.EvtProximityRollup, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Heartbeat));
        Assert.Equal(4200, ev.Payload?[0]);
        Assert.Equal(525, ev.Payload?[1]);
        Assert.Equal((byte)100, ev.Payload?[2]);
        Assert.Equal((byte)240, ev.Payload?[3]);
        Assert.Equal(64, ev.Payload?[4]);
        Assert.Equal(12, ev.Payload?[5]);
    }

    [Fact]
    public void TranscriptPanelVisibilityEmitsVerboseOnLifecycleKeyword()
    {
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.TranscriptPanelVisibilityChanged(shown: true, side: "Right", utterances: 3);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleHudSource.EvtTranscriptPanelVisibilityChanged, ev.EventId);
        Assert.Equal(12, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Lifecycle));
        Assert.Equal(true, ev.Payload?[0]);
        Assert.Equal("Right", ev.Payload?[1]);
        Assert.Equal(3, ev.Payload?[2]);
    }

    // Regression: HUD detail used the bare listener level, which the
    // always-on log listener satisfies, so per-show and per-frame detail
    // reached the log whatever the Windowing verbosity said.
    [Fact]
    public void HudDetailIsRejectedWhileWindowingDetailIsOff()
    {
        OperationalLogAdmission.Configure(static _ => false);
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.StateChanged("Hidden", "Recording", "show_recording", 0, 96);
        DeckleHudSource.Log.FadeInStarted("hud", 150, 0, 255);
        DeckleHudSource.Log.ProximityRollup(4200, 525, 100, 240, 64, 12);
        DeckleHudSource.Log.HideSyncTimedOut("ui_thread_unresponsive", 5000);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleHudSource.EvtHideSyncTimedOut, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
    }

    [Fact]
    public void RevealMaskDegradedCarriesItsCauseInTheWarning()
    {
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.RevealMaskDegraded(10, "COMException", "The parameter is incorrect.");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleHudSource.EvtRevealMaskDegraded, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Lifecycle));
        Assert.Equal(10, ev.Payload?[0]);
        Assert.Equal("COMException", ev.Payload?[1]);
        Assert.Equal("The parameter is incorrect.", ev.Payload?[2]);
    }

    // The settings store reports in free text. A damaged file at load is a
    // Warning (degraded, defaults in use) and a failed write an Error.
    [Fact]
    public void ADamagedSettingsFileSurfacesAsAWarningAndAFailedWriteAsAnError()
    {
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.SettingsLoadWarning("reason=parse_failed");
        DeckleHudSource.Log.SettingsLoadError("reason=write_failed");

        Assert.Collection(
            listener.Events,
            warning =>
            {
                Assert.Equal(DeckleHudSource.EvtSettingsLoadWarning, warning.EventId);
                Assert.Equal(EventLevel.Warning, warning.Level);
                Assert.Equal("reason=parse_failed", warning.Payload?[0]);
            },
            error =>
            {
                Assert.Equal(DeckleHudSource.EvtSettingsLoadError, error.EventId);
                Assert.Equal(EventLevel.Error, error.Level);
                Assert.Equal("reason=write_failed", error.Payload?[0]);
            });
    }
}
