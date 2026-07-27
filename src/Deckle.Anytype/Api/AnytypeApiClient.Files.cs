using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

public sealed partial class AnytypeApiClient
{
    // POST a file into the space as multipart/form-data. Wire facts read from
    // anytype-heart's UploadFileHandler (backwards-compatible addition of
    // v0.50.5, served under the 2025-11-08 umbrella this client already pins):
    // the form field is named "file", the stored name comes from the part's
    // filename, and the media type is derived from the bytes on the backend
    // side. The answer is a BARE {object_id, name, media, extension,
    // size_in_bytes} — no "object" wrapper, unlike every other create route.
    //
    // The returned object_id is what a property of format "files" references;
    // the file itself lives in the space like any other object.
    //
    // Content comes as a byte[] and not a Stream because the transport retries
    // once on a transient, and a retry has to send the same bytes again.
    public async Task<JsonObject> UploadFileAsync(
        string spaceId,
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        return await SendContentAsync(
            HttpMethod.Post,
            $"{SpacePath(spaceId)}/files",
            () =>
            {
                var part = new ByteArrayContent(content);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                // Set the disposition by hand rather than through Add(name,
                // fileName): HttpClient emits bare tokens there, and a filename
                // with a space or a comma would then split the parameter list.
                part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"file\"",
                    FileName = "\"" + fileName.Replace("\"", "") + "\"",
                };
                return new MultipartFormDataContent { part };
            },
            ct).ConfigureAwait(false);
    }
}
