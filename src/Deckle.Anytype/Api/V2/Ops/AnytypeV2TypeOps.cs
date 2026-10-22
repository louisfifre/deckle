using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Where a property sits on a type: at the top of the object (featured), off it
// entirely (hidden), or in the plain field list.
public enum AnytypeV2PropertySection
{
    Plain,
    Featured,
    Hidden,
}

// The three ops that edit a type's field list, PATCH /types/{key} only. A
// batch plans and refuses everything before any write; then property ops are
// written first and view ops after, so a refused view op leaves the property
// changes in place.
public static class AnytypeV2TypeOps
{
    // An unknown name creates the property, which is what format is for; on
    // an existing one, format must match. Creating options of a select needs
    // AnytypeV2WriteOptions.CreateMissingOptions. section null leaves a
    // property already on the type where it is.
    public static AnytypeV2Op AddProperty(
        string property,
        AnytypeV2PropertyFormat? format = null,
        IEnumerable<string>? options = null,
        AnytypeV2PropertySection? section = null,
        AnytypeV2Placement? placement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        var op = new JsonObject { ["op"] = "add_property", ["property"] = property };
        if (format is { } known) op["format"] = known.ToWire();
        if (options is not null)
        {
            var array = new JsonArray();
            foreach (string name in options)
                array.Add(new JsonObject { ["name"] = name });
            op["options"] = array;
        }
        if (section is { } where)
        {
            // Plain is the schema's null: the field list, not a featured or hidden slot.
            op["section"] = where switch
            {
                AnytypeV2PropertySection.Featured => "featured",
                AnytypeV2PropertySection.Hidden => "hidden",
                _ => null,
            };
        }
        WritePlacement(op, placement);
        return ForType("add_property", op);
    }

    public static AnytypeV2Op RemoveProperty(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return ForType("remove_property", new JsonObject { ["op"] = "remove_property", ["property"] = property });
    }

    public static AnytypeV2Op MoveProperty(string property, AnytypeV2Placement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        ArgumentNullException.ThrowIfNull(placement);

        var op = new JsonObject { ["op"] = "move_property", ["property"] = property };
        WritePlacement(op, placement);
        return ForType("move_property", op);
    }

    private static void WritePlacement(JsonObject op, AnytypeV2Placement? placement)
    {
        if (placement is null) return;
        if (placement.IsInside)
            throw new ArgumentException("A property has no container to go inside; use After, Before, Start or End.", nameof(placement));
        placement.WriteTo(op);
    }

    private static AnytypeV2Op ForType(string kind, JsonObject json) =>
        new(kind, AnytypeV2OpScope.Type, json);
}
