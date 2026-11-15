using System.Net;
using System.Net.Http;

namespace Deckle.Llm;

// ─── How a request with an image travels ─────────────────────────────────────
//
// The engines check the address before they build the request
// (LocalEndpoint.RequireForImage). Two things could still carry the picture
// elsewhere after that check, and this client turns both off:
// - a redirect: a 307 or 308 from the local server would make HttpClient
//   send the same body to whatever address it names;
// - a proxy: the system's proxy settings would hand the connection to
//   another host.
// So the client that sends images follows no redirect and uses no proxy, and
// an answer that redirects is a failure like any other.
//
// The second half reads a refusal: a server that runs a text-only model says
// so in its error body, with its own words (Ollama: "does not support
// images" or "missing data required for image input"; llama.cpp's server:
// "image input is not supported", with a hint about --mmproj). The words are
// matched loosely, as the servers change them between versions; an error that
// names none of them stays an HttpRequestException with its status.
internal static class ImageRequestTransport
{
    // No deadline here: the caller's CancellationToken is the only one, as
    // for text (a vision model on a CPU can take minutes per picture).
    public static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly string[] ImageRefusalWords =
        ["image", "vision", "multimodal", "mmproj"];

    /// <summary>Throws when <paramref name="response"/> is not a success:
    /// <see cref="ImageInputNotSupportedException"/> when the server says
    /// the model cannot read images, otherwise an HttpRequestException that
    /// carries the status and the start of the body.</summary>
    public static void ThrowIfFailed(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string body = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        if (IsImageRefusal(response.StatusCode, body))
            throw new ImageInputNotSupportedException(Trim(body, 300));

        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {Trim(body, 300)}",
            inner: null,
            statusCode: response.StatusCode);
    }

    internal static bool IsImageRefusal(HttpStatusCode status, string body)
    {
        // A 404 is an unknown model, whatever its message says about it.
        if (status == HttpStatusCode.NotFound) return false;
        foreach (string word in ImageRefusalWords)
        {
            if (body.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
