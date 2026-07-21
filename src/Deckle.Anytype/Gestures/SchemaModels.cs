namespace Deckle.Anytype;

internal sealed record SchemaPreview(
    string Id,
    string SpaceAlias,
    string SpaceId,
    SchemaManifest Manifest,
    SchemaSnapshot Snapshot,
    IReadOnlyList<SchemaAction> Actions,
    IReadOnlyList<string> Conflicts);

internal sealed record SchemaAction(string Kind, string Key, string Name);

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
    IReadOnlyList<SchemaPropertyLinkInfo> PropertyLinks);

public sealed record SchemaPropertyInfo(string Id, string Key, string Name, string Format);

public sealed record SchemaPropertyLinkInfo(string Id, string Key, string Name, string Format)
{
    public bool HasPayload => Key.Length > 0 && Name.Length > 0 && Format.Length > 0;
}

public sealed record SchemaTagInfo(string Id, string Key, string Name, string Color);
