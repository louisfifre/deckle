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
    IReadOnlyList<string>? RemoveFromCollections = null,
    string? AppendText = null,
    HomeSectionEdit? Section = null);

public sealed record HomeSectionEdit(string Heading, string Text);

public sealed record HomeSearchFilter(
    string? Text,
    string? Type,
    string? Room,
    string? Circuit,
    string? Category,
    string? Condition,
    bool? Done = null,
    string? Worksite = null,
    string? State = null,
    string? System = null,
    string? Domain = null,
    string? EquipmentCategory = null,
    string? ConnectedTo = null,
    string? Panel = null,
    string? About = null,
    bool? Needed = null,
    int Limit = 50,
    int Offset = 0);

public sealed record HomeSearchPage(string Text, JsonObject Data);
