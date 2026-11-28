namespace Deckle.Browser;

// How a page is named in the address field when the field is not being edited:
// the registrable domain alone, always in its ASCII (punycode) form, so a
// look-alike host written with other alphabets is never shown as the host it
// imitates.
public static class SiteName
{
    /// <summary>The registrable domain of an http or https page, nothing for
    /// anything else (a blank page, a file, no page yet).</summary>
    public static string Display(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return "";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";
        return RegistrableDomain.Of(uri.IdnHost);
    }

    /// <summary>Whether the page is served over plain http, which the field says
    /// in words ("Not secure").</summary>
    public static bool IsNotSecure(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttp;
}
