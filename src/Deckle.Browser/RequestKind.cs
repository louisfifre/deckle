namespace Deckle.Browser;

// What a request is for, from WebView2's own word for it to the one the ad
// blocker's rules use. The domain holds no WebView2 type, so the context
// arrives as the name of the CoreWebView2WebResourceContext value.
public static class RequestKind
{
    /// <summary>The type name the blocker expects for a WebView2 resource context
    /// name. A name not known is "other", the type the rules treat as the rest.</summary>
    public static string ToAdblockType(string? webResourceContextName) => webResourceContextName switch
    {
        "Document" => "document",
        "Script" => "script",
        "Image" => "image",
        "Stylesheet" => "stylesheet",
        "Font" => "font",
        "Media" => "media",
        "TextTrack" => "media",
        "XmlHttpRequest" => "xmlhttprequest",
        "Fetch" => "xmlhttprequest",
        "Websocket" => "websocket",
        "Ping" => "ping",
        _ => "other", // Manifest, EventSource, Other, CspViolationReport, SignedExchange, All, unknown
    };

    /// <summary>The page itself (a top-level document) is never blocked: a rule
    /// that matches it would leave a blank window with no way to say why.</summary>
    public static bool IsNeverBlocked(string? webResourceContextName) => webResourceContextName == "Document";
}
