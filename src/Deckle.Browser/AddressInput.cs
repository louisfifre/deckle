namespace Deckle.Browser;

// What the user typed in the address field, turned into the page to open.
public static class AddressInput
{
    // The search the text goes to when it is not an address. One constant,
    // {0} being the escaped text, so changing the engine is one line.
    public const string SearchTemplate = "https://duckduckgo.com/?q={0}";

    private static readonly string[] OpenSchemes = ["http", "https", "file", "about"];

    /// <summary>The URI to open for what was typed, or null when nothing was
    /// typed. An address that names a scheme is kept as it is; a host
    /// (<c>example.com</c>, <c>localhost:5000/x</c>) opens over https; anything
    /// else is a search.</summary>
    public static Uri? Resolve(string typed)
    {
        string text = (typed ?? "").Trim();
        if (text.Length == 0) return null;

        if (HasOpenScheme(text, out Uri? explicitUri)) return explicitUri;
        if (LooksLikeHost(text) && Uri.TryCreate("https://" + text, UriKind.Absolute, out Uri? host)) return host;

        return new Uri(string.Format(SearchTemplate, Uri.EscapeDataString(text)));
    }

    private static bool HasOpenScheme(string text, out Uri? uri)
    {
        uri = null;
        int colon = text.IndexOf(':');
        if (colon <= 0) return false;
        string scheme = text[..colon];
        if (!OpenSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)) return false;
        return Uri.TryCreate(text, UriKind.Absolute, out uri);
    }

    // No space, a dot or "localhost", an optional port and path. The host
    // part is what comes before the first slash, question mark or hash.
    private static bool LooksLikeHost(string text)
    {
        if (text.Any(char.IsWhiteSpace)) return false;
        int end = text.IndexOfAny(['/', '?', '#']);
        string authority = end < 0 ? text : text[..end];
        if (authority.Length == 0) return false;

        string host = authority;
        int colon = authority.LastIndexOf(':');
        if (colon >= 0)
        {
            string port = authority[(colon + 1)..];
            if (port.Length == 0 || !port.All(char.IsAsciiDigit)) return false;
            host = authority[..colon];
        }
        if (host.Length == 0) return false;

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.'));
    }
}
