using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// How one view of a plan differs from the same view in a space, and the view ops
// that close the gap. Pure: no call is made here.
internal static class PlanViewsDiff
{
    // The members of `set` an op may change.
    private static readonly string[] SetMembers =
    [
        "type", "group_by", "filters", "sorts", "alternate_rows", "card_size", "colored_groups",
        "cover_fit", "cover_property", "end_property", "hide_icon", "list_size", "page_size", "wrap_content",
    ];

    // A plan view with the API's own defaults written out, so that a view the
    // plan leaves silent compares equal to the one the API stored.
    public static JsonObject Normalize(JsonObject view)
    {
        var copy = (JsonObject)view.DeepClone();
        copy["type"] ??= "table";
        var sorts = new JsonArray();
        foreach (JsonObject sort in (view["sorts"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var s = (JsonObject)sort.DeepClone();
            s["direction"] ??= "asc";
            sorts.Add(s);
        }
        copy["sorts"] = sorts;
        copy["filters"] ??= new JsonArray();
        copy["columns"] = new JsonArray(
            (view["columns"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(c => !PlanViews.IsUnreachableColumn(c["property"]?.GetValue<string>() ?? ""))
                .Select(c => c.DeepClone()).ToArray());
        return copy;
    }

    // What differs, view by view parts. Empty means the live view is the one the
    // plan states.
    public static List<string> Differences(JsonObject wanted, JsonObject live)
    {
        JsonObject normalized = Normalize(wanted);
        var differences = PlanJson.Differences(normalized, live);
        // The plan says nothing about grouping: the live view must not group.
        if (normalized["group_by"] is null && live["group_by"] is not null)
            differences.Add($"group_by : attendu aucun, vu {live["group_by"]}");
        return differences;
    }

    public static bool SetDiffers(JsonObject wanted, JsonObject live)
    {
        JsonObject normalized = Normalize(wanted);
        var shape = new JsonObject();
        foreach (string member in SetMembers)
            if (normalized[member] is { } value) shape[member] = value.DeepClone();
        if (PlanJson.Differences(shape, live).Count > 0) return true;
        return normalized["group_by"] is null && live["group_by"] is not null;
    }

    public static bool ColumnsDiffer(JsonObject wanted, JsonObject live) =>
        PlanJson.Differences(Normalize(wanted)["columns"], live["columns"]).Count > 0;

    // The `set` of an update_view op: every member the plan states, plus a null
    // for a grouping the plan leaves out (null clears a field to its default).
    public static JsonObject SetPayload(JsonObject wanted, JsonObject live, IPlanRefs refs, string? selfValue = null)
    {
        JsonObject wire = PlanViews.ToWire(Normalize(wanted), refs, selfValue);
        var set = new JsonObject();
        foreach (string member in SetMembers)
            if (wire[member] is { } value) set[member] = value.DeepClone();
        if (wire["group_by"] is null && live["group_by"] is not null) set["group_by"] = null;
        return set;
    }

    // The update_view ops that bring a view's columns to the plan's list. The
    // columns channel patches by property and a patch cannot place a column, so
    // order is made by appending: keep the longest prefix that already matches,
    // remove what follows, then add the wanted columns one op each, in order (a
    // map in one op would lose its order on the server).
    public static List<JsonObject> ColumnOps(string viewId, JsonObject wanted, JsonObject live)
    {
        var want = (Normalize(wanted)["columns"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var have = (live["columns"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(c => !PlanViews.IsUnreachableColumn(c["property"]?.GetValue<string>() ?? "")).ToList();

        int keep = 0;
        while (keep < want.Count && keep < have.Count
            && Property(want[keep]) == Property(have[keep])
            && Matches(want[keep], have[keep]))
            keep++;
        var ops = new List<JsonObject>();
        var removal = new JsonObject();
        foreach (JsonObject column in have.Skip(keep)) removal[Property(column)] = null;
        if (removal.Count > 0)
            ops.Add(new JsonObject { ["op"] = "update_view", ["view"] = viewId, ["columns"] = removal });
        foreach (JsonObject column in want.Skip(keep))
        {
            var patch = Patch(column);
            ops.Add(new JsonObject
            {
                ["op"] = "update_view",
                ["view"] = viewId,
                ["columns"] = new JsonObject { [Property(column)] = patch },
            });
        }
        return ops;
    }

    // A wanted column matches a live one when its hidden state agrees (absent is
    // visible) and, if the plan gives a width, when the width agrees.
    private static bool Matches(JsonObject want, JsonObject have)
    {
        bool wantHidden = want["hidden"]?.GetValue<bool>() == true;
        bool haveHidden = have["hidden"]?.GetValue<bool>() == true;
        if (wantHidden != haveHidden) return false;
        if (want["width"] is { } width && (have["width"] is null || width.GetValue<int>() != have["width"]!.GetValue<int>())) return false;
        return true;
    }
    private static string Property(JsonObject column) => column["property"]?.GetValue<string>() ?? "";

    private static JsonObject Patch(JsonObject column)
    {
        var patch = new JsonObject();
        foreach (string member in new[] { "hidden", "width", "aggregation", "align" })
            if (column[member] is { } value) patch[member] = value.DeepClone();
        return patch;
    }
}
