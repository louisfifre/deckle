using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "observability")]
public class DeckleThemeSourceTests
{
    [Fact]
    public void ThemeChangedEmitsVerboseOnThemeKeyword()
    {
        using var listener = new TestEventListener("Deckle-Theme");

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

    // One theme request re-themes every open window; when their handlers ran
    // after the loop of writes, the read-once probe labelled the first window
    // and left the others as "system".
    [Fact]
    [Trait("Category", "regression")]
    public void ThemeRequestSourceProbeLabelsEveryWindowOfOneRequest()
    {
        ThemeRequestSourceProbe.Push("user", nowMs: 10_000);

        Assert.Equal("user", ThemeRequestSourceProbe.Consume(nowMs: 10_050));
        Assert.Equal("user", ThemeRequestSourceProbe.Consume(nowMs: 10_060));
        Assert.Equal("user", ThemeRequestSourceProbe.Consume(nowMs: 10_070));
    }

    [Fact]
    public void ThemeRequestSourceProbeForgetsARequestPastItsSpan()
    {
        ThemeRequestSourceProbe.Push("user", nowMs: 10_000);

        long later = 10_000 + ThemeRequestSourceProbe.RequestWindowMs + 1;
        Assert.Null(ThemeRequestSourceProbe.Consume(later));
    }

    [Fact]
    public void ThemeRequestSourceProbeLatestPushOverwritesPrevious()
    {
        ThemeRequestSourceProbe.Push("app-init", nowMs: 10_000);
        ThemeRequestSourceProbe.Push("user", nowMs: 10_010);

        Assert.Equal("user", ThemeRequestSourceProbe.Consume(nowMs: 10_020));
    }
}
