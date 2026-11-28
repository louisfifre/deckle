using Xunit;

namespace Deckle.Browser.Tests;

// WebView2's word for a request's purpose, in the blocker's.
[Trait("Category", "unit")]
public sealed class RequestKindTests
{
    [Theory]
    [InlineData("Script", "script")]
    [InlineData("Image", "image")]
    [InlineData("Stylesheet", "stylesheet")]
    [InlineData("Font", "font")]
    [InlineData("Media", "media")]
    [InlineData("XmlHttpRequest", "xmlhttprequest")]
    [InlineData("Fetch", "xmlhttprequest")]
    [InlineData("Websocket", "websocket")]
    [InlineData("Ping", "ping")]
    [InlineData("Manifest", "other")]
    [InlineData("TextTrack", "media")]
    [InlineData("EventSource", "other")]
    [InlineData("Other", "other")]
    [InlineData("Document", "document")]
    public void EachContextHasItsType(string context, string expected) =>
        Assert.Equal(expected, RequestKind.ToAdblockType(context));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingNew")]
    public void AnUnknownContextIsOther(string? context) =>
        Assert.Equal("other", RequestKind.ToAdblockType(context));

    [Fact]
    public void OnlyTheDocumentIsNeverBlocked()
    {
        Assert.True(RequestKind.IsNeverBlocked("Document"));
        Assert.False(RequestKind.IsNeverBlocked("Script"));
        Assert.False(RequestKind.IsNeverBlocked(null));
    }
}
