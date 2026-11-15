using System.Net;

namespace Deckle.Llm;

// ─── Is an endpoint on this machine? ─────────────────────────────────────────
//
// The rule a request carrying an image obeys: it goes only to a loopback
// address, the name "localhost", an IPv4 address of 127.0.0.0/8 or the IPv6
// address ::1. Anything else is refused before a byte leaves, whatever the
// person typed: a LAN address, a host name that merely starts with
// "localhost", an endpoint that does not parse.
//
// Uri.IsLoopback answers exactly that set (Microsoft Learn: "true if this
// Uri references the local host"), and a parsed address is checked again
// with IPAddress.IsLoopback so an IPv4-mapped IPv6 form is read the same way.
// The text path keeps its own softer rule (OllamaService warns once and lets
// a remote Ollama serve text).
public static class LocalEndpoint
{
    public static bool IsLoopback(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        if (string.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(uri.IdnHost, out IPAddress? address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return IPAddress.IsLoopback(address);
        }
        return false;
    }

    /// <summary>Throws <see cref="LocalEndpointRequiredException"/> when a
    /// request with an image would go to <paramref name="url"/> and that
    /// address is not on this machine.</summary>
    public static void RequireForImage(GenerationRequest request, string url)
    {
        if (request.Image is not null && !IsLoopback(url))
            throw new LocalEndpointRequiredException();
    }
}

/// <summary>A request carrying an image was bound to an endpoint that is not
/// on this machine, or to a hosted engine. Thrown before any byte is
/// sent.</summary>
public sealed class LocalEndpointRequiredException()
    : InvalidOperationException("A request carrying an image is sent only to a server on this machine.");

/// <summary>The server answered that the model it runs cannot read images
/// (no vision projector, a text-only model).</summary>
public sealed class ImageInputNotSupportedException(string serverMessage)
    : InvalidOperationException("The model cannot read images: " + serverMessage);
