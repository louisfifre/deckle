using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The exhaustive read of a list the API serves page by page: every page, in
// order, until pagination.has_more turns false.
//
// The server chooses how many rows a page holds, and a limit asked above its
// ceiling is silently lowered. The next offset is therefore the rows the page
// carried, never the limit that was asked: advancing by the limit skips every
// row between the two, and the read looks complete.
//
// An empty page ends the read even if has_more still claims more, so a server
// that never stops claiming it cannot keep the loop alive.
internal static class PagedRead
{
    public static async IAsyncEnumerable<JsonObject> PagesAsync(
        Func<int, CancellationToken, Task<JsonObject>> readPage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        int offset = 0;
        while (true)
        {
            JsonObject root = await readPage(offset, ct).ConfigureAwait(false);
            yield return root;

            int returned = root["data"] is JsonArray data ? data.Count : 0;
            if (returned == 0 || root["pagination"]?["has_more"]?.GetValue<bool>() != true) yield break;
            offset += returned;
        }
    }
}
