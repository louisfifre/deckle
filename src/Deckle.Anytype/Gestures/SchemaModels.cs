namespace Deckle.Anytype;

internal sealed record SchemaPreview(
    string Id,
    string SpaceAlias,
    string SpaceId,
    SchemaManifest Manifest,
    SchemaSnapshot Snapshot,
    IReadOnlyList<SchemaAction> Actions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> SkippedConflicts,
    IReadOnlyDictionary<string, string> SectionCollections);

internal sealed record SchemaAction(string Kind, string Key, string Name);

// One live collection object (built-in type key "collection") as read for
// section planning. Kept off the public SchemaSnapshot: that record is a frozen
// provider boundary consumed by domain modules.
internal sealed record SchemaCollectionObjectInfo(string Id, string Name);

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
