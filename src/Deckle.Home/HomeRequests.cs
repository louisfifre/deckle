using System.Text.Json.Nodes;

namespace Deckle.Home;

// Force passes the near-name guard for a true twin. Category and Room serve a
// point alone: given instead of Code, the gesture mints the lowest free code
// of that room and category.
public sealed record HomeCreateItem(
    string? Code,
    string? Name,
    JsonObject? Properties,
    IReadOnlyList<string>? Collections = null,
    string? Text = null,
    string? Template = null,
    bool Force = false,
    string? Category = null,
    string? Room = null);

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
    int Offset = 0,
    // null leaves archived objects out, as false does; true keeps only them.
    bool? Archived = null);

public sealed record HomeSearchPage(string Text, JsonObject Data);

// One errand of product_need. Force creates a new product even though one
// with a near name exists.
public sealed record HomeProductNeed(
    string Name,
    string? ProductCategory = null,
    double? Quantity = null,
    bool Force = false);

public sealed record HomeProductList(string Text, JsonObject Data);
