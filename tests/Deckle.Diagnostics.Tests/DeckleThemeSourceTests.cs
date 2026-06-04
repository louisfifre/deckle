using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Cross-cutting sub-provider: theme transitions (light / dark / accent). A
// single ThemeChanged event carries the four surface/from/to/source parameters.
// The test fixes parameter order and verifies that the probe
// statique ThemeRequestSourceProbe round-trip un push/consume correctement
// (the Push/Consume mechanism distinguishes a "user" switch from a "system"
// switch on the ActualThemeChanged handler side).
[Trait("Category", "observability")]
public class DeckleThemeSourceTests
{
    [Fact]
    public void ThemeChangedEmitsVerboseOnThemeKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Theme");

        DeckleThemeSource.Log.ThemeChanged(
            surface: "settings", from: "Light", to: "Dark", source: "user");

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleThemeSource.EvtThemeChanged, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Theme));
        Assert.Equal("settings", ev.Payload?[0]);
        Assert.Equal("Light", ev.Payload?[1]);
        Assert.Equal("Dark", ev.Payload?[2]);
        Assert.Equal("user", ev.Payload?[3]);
    }

    [Fact]
    public void ThemeRequestSourceProbeRoundtripsPushAndConsume()
    {
        // Reset in case another test left a pending value.
        ThemeRequestSourceProbe.Consume();

        ThemeRequestSourceProbe.Push("user");

        Assert.Equal("user", ThemeRequestSourceProbe.Consume());
        Assert.Null(ThemeRequestSourceProbe.Consume());
    }

    [Fact]
    public void ThemeRequestSourceProbeLatestPushOverwritesPrevious()
    {
        ThemeRequestSourceProbe.Consume();

        ThemeRequestSourceProbe.Push("app-init");
        ThemeRequestSourceProbe.Push("user");

        Assert.Equal("user", ThemeRequestSourceProbe.Consume());
    }
}
