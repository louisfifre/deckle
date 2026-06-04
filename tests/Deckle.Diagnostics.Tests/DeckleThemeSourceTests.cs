using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Sub-provider transverse — transitions de thème (light / dark / accent).
// Un seul event ThemeChanged porte les quatre paramètres surface/from/to/
// source. Le test fixe l'ordre des paramètres et vérifie que la probe
// statique ThemeRequestSourceProbe round-trip un push/consume correctement
// (le mécanisme Push/Consume sert à distinguer une bascule "user" d'une
// bascule "system" côté handler ActualThemeChanged).
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
        // Reset au cas où un autre test aurait laissé un pending.
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
