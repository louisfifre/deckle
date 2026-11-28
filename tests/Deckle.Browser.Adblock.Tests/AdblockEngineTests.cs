using System.Text;
using Xunit;

namespace Deckle.Browser.Adblock.Tests;

// The native engine through its managed wrapper, with local rules only.
[Trait("Category", "integration")]
public sealed class AdblockEngineTests
{
    private const string Page = "https://browser.test/";

    [Fact]
    public void ARuleBlocksItsHostAndAnExceptionLetsOneThrough()
    {
        using var engine = AdblockEngine.Create("||ads.browser.test^\n@@||ads.browser.test/ok.js^\n");
        Assert.True(engine.ShouldBlock("https://ads.browser.test/ad.png", Page, "image"));
        Assert.False(engine.ShouldBlock("https://ads.browser.test/ok.js", Page, "script"));
        Assert.False(engine.ShouldBlock("https://browser.test/app.js", Page, "script"));
    }

    [Fact]
    public void ARuleCanBeLimitedToAType()
    {
        using var engine = AdblockEngine.Create("||cdn.browser.test^$script\n");
        Assert.True(engine.ShouldBlock("https://cdn.browser.test/a.js", Page, "script"));
        Assert.False(engine.ShouldBlock("https://cdn.browser.test/a.png", Page, "image"));
    }

    [Fact]
    public void CosmeticSelectorsComeBackForTheirSiteOnly()
    {
        using var engine = AdblockEngine.Create("browser.test##.ad-banner\n");
        Assert.Contains(".ad-banner", engine.Cosmetic("https://browser.test/page").HideSelectors);
        Assert.DoesNotContain(".ad-banner", engine.Cosmetic("https://other.test/page").HideSelectors);
    }

    [Fact]
    public void AScriptletRuleYieldsAnInjectedScriptOnceResourcesAreLoaded()
    {
        using var engine = AdblockEngine.Create("browser.test##+js(tiny-marker, hello)\n");
        string content = Convert.ToBase64String(Encoding.UTF8.GetBytes("window.__marker = '{{1}}';"));
        string json = "[{\"name\":\"tiny-marker.js\",\"aliases\":[],\"kind\":\"template\",\"content\":\"" + content + "\"}]";
        Assert.True(engine.UseResources(json));
        string script = engine.Cosmetic("https://browser.test/page").InjectedScript;
        Assert.Contains("__marker", script);
        Assert.Contains("hello", script);
    }

    [Fact]
    public void ResourcesThatAreNotTheJsonAreRefused()
    {
        using var engine = AdblockEngine.Create("||ads.browser.test^\n");
        Assert.False(engine.UseResources("{ not json"));
        Assert.False(engine.UseResources("{}"));
        Assert.True(engine.ShouldBlock("https://ads.browser.test/x", Page, "image"));
    }

    [Fact]
    public void BadInputDoesNotCrash()
    {
        using var engine = AdblockEngine.Create("||ads.browser.test^\n");
        Assert.False(engine.ShouldBlock("not a url", "", "script"));
        Assert.False(engine.ShouldBlock("", "", ""));
        Assert.False(engine.ShouldBlock("https://browser.test/\ud800", Page, "script"));
        Assert.Empty(engine.Cosmetic("not a url").HideSelectors);
        using var empty = AdblockEngine.Create("");
        Assert.False(empty.ShouldBlock("https://ads.browser.test/x", Page, "image"));
    }

    [Fact]
    public void AnEngineAfterDisposeBlocksNothingAndDisposeTwiceIsFine()
    {
        var engine = AdblockEngine.Create("||ads.browser.test^\n");
        engine.Dispose();
        engine.Dispose();
        Assert.False(engine.ShouldBlock("https://ads.browser.test/x", Page, "image"));
        Assert.False(engine.UseResources("[]"));
        Assert.Empty(engine.Cosmetic(Page).HideSelectors);
    }

    [Fact]
    public void ManyThreadsAskAtOnce()
    {
        using var engine = AdblockEngine.Create("||ads.browser.test^\n");
        int blocked = 0;
        Parallel.For(0, 2000, i =>
        {
            if (engine.ShouldBlock("https://ads.browser.test/" + i, Page, "image")) Interlocked.Increment(ref blocked);
            engine.Cosmetic(Page);
        });
        Assert.Equal(2000, blocked);
    }
}
