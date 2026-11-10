using System.Text.Json;
using System.Text.RegularExpressions;

namespace Deckle.Atelier;

internal sealed record McpDeclaration(string Name, int Line, string? Summary);

// Reads the servers an MCP configuration file declares under its key. VS Code
// writes these files as JSON with comments, so comments and trailing commas
// are allowed. The summary is the command that starts a server, or its
// address without the query; arguments and environment stay out, since they
// often carry a key.
internal static class McpDeclarations
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Throws JsonException when the file is not JSON.
    public static IReadOnlyList<McpDeclaration> Read(string text, string serversKey)
    {
        using JsonDocument document = JsonDocument.Parse(text, Options);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(serversKey, out JsonElement servers)
            || servers.ValueKind != JsonValueKind.Object) return [];

        int keyAt = Math.Max(0, text.IndexOf("\"" + serversKey + "\"", StringComparison.Ordinal));
        var found = new List<McpDeclaration>();
        foreach (JsonProperty server in servers.EnumerateObject())
        {
            Match declared = Regex.Match(text[keyAt..], "\"" + Regex.Escape(server.Name) + "\"\\s*:");
            int line = TextLines.At(text, declared.Success ? keyAt + declared.Index : keyAt);
            found.Add(new McpDeclaration(server.Name, line, Summary(server.Value)));
        }
        return found;
    }

    private static string? Summary(JsonElement server)
    {
        if (server.ValueKind != JsonValueKind.Object) return null;
        if (server.TryGetProperty("command", out JsonElement command) && command.ValueKind == JsonValueKind.String)
            return command.GetString();
        foreach (string key in (string[])["url", "httpUrl", "serverUrl"])
        {
            if (server.TryGetProperty(key, out JsonElement url) && url.ValueKind == JsonValueKind.String
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? address))
                return address.GetLeftPart(UriPartial.Path);
        }
        return null;
    }
}
