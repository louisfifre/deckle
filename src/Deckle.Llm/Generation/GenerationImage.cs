namespace Deckle.Llm;

// ─── An image a request carries ──────────────────────────────────────────────
//
// One picture handed to a model that reads images, beside the request's text.
// The caller prepares it (size, format): the engine sends the bytes as they
// are, base64-encoded the way its server's API asks.
//
// A request with an image goes only to a server on this machine. Every engine
// checks it before it builds the request (LocalEndpoint), and an engine bound
// to a hosted API refuses it outright: a picture is the person's own data,
// and nothing here sends it away.

/// <summary><paramref name="MediaType"/> is the image's MIME type, such as
/// <c>image/jpeg</c>; it names the data URI the chat-completions shape
/// carries.</summary>
public sealed record GenerationImage(byte[] Bytes, string MediaType)
{
    public string ToBase64() => Convert.ToBase64String(Bytes);
}
