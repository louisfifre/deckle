namespace Deckle.Anytype;

internal sealed record SchemaPreview(
    string Id,
    string SpaceAlias,
    string SpaceId,
    SchemaManifest Manifest,
    SchemaSnapshot Snapshot,
    IReadOnlyList<SchemaAction> Actions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> SkippedConflicts);

internal sealed record SchemaAction(string Kind, string Key, string Name);

// Public preview contract for non-domain adapters. It deliberately excludes the
// full live Anytype snapshot and repeated manifest: consumers need the reviewed
// plan and its deterministic handle, not the provider payload used to derive it.
public sealed record SchemaPreviewResult(
    string PreviewId,
    string SpaceAlias,
    IReadOnlyList<SchemaPreviewAction> Actions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> SkippedConflicts,
    string Digest);

public sealed record SchemaPreviewAction(string Kind, string Key, string Name);

public sealed record SchemaSnapshot(
    IReadOnlyDictionary<string, SchemaTypeInfo> Types,
    IReadOnlyDictionary<string, SchemaPropertyInfo> Properties,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, SchemaTagInfo>> TagsByProperty);

public sealed record SchemaTypeInfo(
    string Id,
    string Key,
    string Name,
    string PluralName,
    string Layout,
    SchemaTypeIconInfo? Icon,
    IReadOnlyList<SchemaPropertyLinkInfo> PropertyLinks);

public sealed record SchemaTypeIconInfo(
    string Format,
    string? Name,
    string? Color,
    string? Emoji,
    string? File)
{
    public string Display => Format switch
    {
        "icon" when Color is not null => $"icon:{Name}:{Color}",
        "icon" => $"icon:{Name}",
        "emoji" => $"emoji:{Emoji}",
        "file" => $"file:{File}",
        _ => Format,
    };
}

public sealed record SchemaPropertyInfo(string Id, string Key, string Name, string Format);

public sealed record SchemaPropertyLinkInfo(string Id, string Key, string Name, string Format)
{
    public bool HasPayload => Key.Length > 0 && Name.Length > 0 && Format.Length > 0;
}

public sealed record SchemaTagInfo(string Id, string Key, string Name, string Color);
