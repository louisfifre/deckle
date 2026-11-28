namespace Deckle.Browser;

// How a page is named in the address field when the field is not being edited.
public static class SiteName
{
    // The host without a leading "www." for an http or https page, nothing
    // for anything else (a blank page, a file, no page yet). The public suffix
    // list comes in step 3 of the browser plan: until then this is the host,
    // not the registrable domain, so "mail.example.co.uk" shows whole.
    public static string Display(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return "";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";

        string host = uri.IdnHost;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Length > 4 ? host[4..] : host;
    }
}
