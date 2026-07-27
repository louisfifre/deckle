using System.Text.Json.Nodes;

namespace Deckle.Travel;

public sealed record TravelCreateItem(
    string Name,
    JsonObject? Properties);

public sealed record TravelUpdateItem(
    string Object,
    string? Name,
    JsonObject? Properties);

public sealed record TravelSearchFilter(
    string? Text,
    string? Type,
    string? Stay,
    string? Category,
    string? Mode);
