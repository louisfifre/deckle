using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

public sealed partial class AnytypeApiClient
{
    // Wire facts from anytype-heart's UploadFileHandler: the multipart form
    // field is named "file", the stored name comes from the part's filename,
    // and the backend derives the media type from the bytes. The answer is a
    // BARE {object_id, name, media, extension, size_in_bytes} — no "object"
    // wrapper, unlike every other create route.
    //
    // The returned object_id is what a property of format "files" references;
    // the file itself lives in the space like any other object.
    //
    // Content comes as a byte[] and not a Stream so each request owns a stable
    // replayable payload. Upload itself is not replayed after a 5xx: the file may
    // already exist even when its object id did not reach the caller.
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
