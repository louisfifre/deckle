using System.Text.Json;

namespace Deckle.Projects;

// package.json: the "version" of the root object. A "version" inside
// "dependencies" or any nested object is somebody else's and is never read.
// Comments and trailing commas are tolerated: tools that write package.json
// by hand leave them, and one bad character must not hide the version.
internal static class PackageJsonManifest
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Throws JsonException when the text is not JSON at all; the caller turns
    // that into "no source" and one Verbose event.
    public static string? Version(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text.TrimStart('﻿'), Options);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!document.RootElement.TryGetProperty("version", out JsonElement version)) return null;
        return version.ValueKind == JsonValueKind.String ? VersionText.Plain(version.GetString()) : null;
    }
}
