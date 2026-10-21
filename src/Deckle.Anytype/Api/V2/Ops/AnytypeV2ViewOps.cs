using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The four ops that edit a dataview's views. They are the only ops both
// PATCH /objects/{id} and PATCH /types/{key} accept; on a type, the property
// ops are written first and a view op refused afterwards leaves them written.
//
// view and block are optional on the wire when there is exactly one view (or
// one dataview — types, queries and collections have one): a null here omits
// the field and lets the server resolve it.
//
// set is a merge: only the named fields change, null clears one to its default,
// sorts and filters replace whole. columns is a per-column patch keyed by
// property key, each merging into that column (null removes it); never resend
// the whole column list.
public static class AnytypeV2ViewOps
{
    public static AnytypeV2Op UpdateView(
        string? view = null,
        string? block = null,
        JsonObject? set = null,
        JsonObject? columns = null)
    {
        if (set is not { Count: > 0 } && columns is not { Count: > 0 })
            throw new ArgumentException("An update_view with no set and no columns changes nothing.");

        var op = new JsonObject { ["op"] = "update_view" };
        if (view is not null) op["view"] = view;
        if (block is not null) op["block"] = block;
        if (set is { Count: > 0 }) op["set"] = set.DeepClone();
        if (columns is { Count: > 0 }) op["columns"] = columns.DeepClone();
        return Both("update_view", op);
    }

    // copyFrom duplicates another view (all but id and name), then set and
    // columns apply on top. Without it the view starts from defaults. The first
    // view of a dataview is the client's default tab.
    public static AnytypeV2Op InsertView(
        string name,
        AnytypeV2Placement? placement = null,
        string? block = null,
        string? copyFrom = null,
        JsonObject? set = null,
        JsonObject? columns = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var op = new JsonObject { ["op"] = "insert_view", ["name"] = name };
        WritePlacement(op, placement);
        if (block is not null) op["block"] = block;
        if (copyFrom is not null) op["copy_from"] = copyFrom;
        if (set is { Count: > 0 }) op["set"] = set.DeepClone();
        if (columns is { Count: > 0 }) op["columns"] = columns.DeepClone();
        return Both("insert_view", op);
    }

    public static AnytypeV2Op MoveView(string view, AnytypeV2Placement placement, string? block = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        ArgumentNullException.ThrowIfNull(placement);

        var op = new JsonObject { ["op"] = "move_view", ["view"] = view };
        WritePlacement(op, placement);
        if (block is not null) op["block"] = block;
        return Both("move_view", op);
    }

    public static AnytypeV2Op DeleteView(string view, string? block = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);

        var op = new JsonObject { ["op"] = "delete_view", ["view"] = view };
        if (block is not null) op["block"] = block;
        return Both("delete_view", op);
    }

    private static void WritePlacement(JsonObject op, AnytypeV2Placement? placement)
    {
        if (placement is null) return;
        if (placement.IsInside)
            throw new ArgumentException("A view has no container to go inside; use After, Before, Start or End.", nameof(placement));
        placement.WriteTo(op);
    }

    private static AnytypeV2Op Both(string kind, JsonObject json) =>
        new(kind, AnytypeV2OpScope.Object | AnytypeV2OpScope.Type, json);
}
