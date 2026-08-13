using System.Text.Json.Nodes;

namespace Deckle.Home;

public sealed record HomeCreateItem(
    string? Code,
    string? Name,
    JsonObject? Properties,
    IReadOnlyList<string>? Collections = null,
    string? Text = null,
    string? Template = null);

public sealed record HomeUpdateItem(
    string Object,
    string? Name,
    JsonObject? Properties,
    IReadOnlyList<string>? AddToCollections = null,
    IReadOnlyList<string>? RemoveFromCollections = null);

public sealed record HomeSearchFilter(
    string? Text,
    string? Type,
    string? Room,
    string? Circuit,
    string? Category,
    string? Existence,
    string? Condition,
    bool? Done = null,
    string? Worksite = null,
    string? State = null,
    string? System = null);
