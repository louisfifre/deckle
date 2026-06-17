using Xunit;

namespace Deckle.Autocorrect.Tests;

// Start/stop lifecycle: a host that fails to start leaves the engine fully
// detached (handlers unsubscribed, no observation), and Stop unwinds the host.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineLifecycleTests
{
    [Fact]
    public void AHostThatFailsToStartLeavesTheEngineDetached()
    {
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Host.StartResult = false;

        Assert.False(h.Start());
        Assert.Equal(1, h.Host.StartCount);

        // Detached: host signals reach nothing — no surface seeded, no tracking,
        // no correction.
        h.Host.RaiseFocusChanged();
        h.Type("ca ");

        Assert.Empty(h.SurfaceChanges);
        Assert.Empty(h.Injector.Calls);
        Assert.Equal("", h.Tracker.CurrentWord);
    }

    [Fact]
    public void StopUnwindsTheHostAndDetaches()
    {
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Engine.Stop();

        Assert.Equal(1, h.Host.StopCount);

        // After Stop the handlers are gone: a committed word triggers no correction.
        h.Type("ca ");
        Assert.Empty(h.Injector.Calls);
    }
}
