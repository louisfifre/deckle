using System.Net;
using System.Net.Sockets;

namespace Deckle.Browser;

// HTTPS first: a top-level navigation to http://host/... becomes
// https://host/... unless the host is local (localhost, a loopback or private
// address) or the person already chose, in this run, to go on over http.
public sealed class HttpsFirst
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The https form of an http address to navigate to instead, or
    /// null when the address is left as it is.</summary>
    public Uri? Upgrade(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttp) return null;
        if (IsExempt(uri.IdnHost)) return null;

        var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = uri.IsDefaultPort ? -1 : uri.Port };
        return builder.Uri;
    }

    /// <summary>The person chose to go on over http to this host, for the rest
    /// of the run.</summary>
    public void AllowHttp(string host) => _allowed.Add(host);

    public bool IsHttpAllowed(string host) => _allowed.Contains(host);

    private bool IsExempt(string host)
    {
        if (_allowed.Contains(host)) return true;
        string name = host.Trim('[', ']');
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(name, out IPAddress? ip) && IsLocalAddress(ip);
    }

    private static bool IsLocalAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        byte[] b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 10
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }
        return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
    }
}
