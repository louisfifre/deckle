using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The seven ops that edit an object's body, all PATCH /objects/{id} only.
// Each factory builds one batch entry; AnytypeV2Writer applies a batch
// atomically (one refused op, nothing written) and error.OpIndex names the
// refused entry.
//
// Payloads the caller hands in are cloned: a JsonNode has a single parent, so
// reusing one block in two ops, or in a retry, would throw on the second
// attach.
public static class AnytypeV2BlockOps
{
    // Merge semantics: only the named fields change, null clears one. id and
    // indent are refused by the server (use MoveBlock to re-nest).
    public static AnytypeV2Op UpdateBlock(AnytypeV2BlockRef target, JsonObject set)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(set);
        if (set.Count == 0)
            throw new ArgumentException("An update_block with nothing to set changes nothing.", nameof(set));

        var op = new JsonObject { ["op"] = "update_block" };
        target.WriteTo(op);
        op["set"] = set.DeepClone();
        return ForObject("update_block", op);
    }

    // A checkbox is ticked through update_block, not through a property: the
    // outline does not even say whether it is ticked, a full read does.
    public static AnytypeV2Op SetChecked(AnytypeV2BlockRef target, bool isChecked) =>
        UpdateBlock(target, new JsonObject { ["checked"] = isChecked });

    public static AnytypeV2Op ReplaceSubtree(string id, IEnumerable<JsonObject> blocks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return ForObject("replace_subtree", new JsonObject
        {
            ["op"] = "replace_subtree",
            ["id"] = id,
            ["blocks"] = BlocksArray(blocks),
        });
    }

    public static AnytypeV2Op InsertBlocks(AnytypeV2Placement placement, IEnumerable<JsonObject> blocks)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var op = new JsonObject { ["op"] = "insert_blocks" };
        placement.WriteTo(op);
        op["blocks"] = BlocksArray(blocks);
        return ForObject("insert_blocks", op);
    }

    // The same op with markdown parsed server-side into flat blocks
    // (headings, lists, checkboxes, fences, quotes, dividers, tables).
    public static AnytypeV2Op InsertMarkdown(AnytypeV2Placement placement, string markdown)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentException.ThrowIfNullOrEmpty(markdown);
        var op = new JsonObject { ["op"] = "insert_blocks" };
        placement.WriteTo(op);
        op["markdown"] = markdown;
        return ForObject("insert_blocks", op);
    }

    // Needs a named block to move relative to; "the end of the document" is
    // not a place a move_block can go.
    public static AnytypeV2Op MoveBlock(string id, AnytypeV2Placement destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.IsDocumentLevel)
            throw new ArgumentException("move_block needs After, Before or Inside a named block.", nameof(destination));

        var op = new JsonObject { ["op"] = "move_block", ["id"] = id };
        destination.WriteTo(op);
        return ForObject("move_block", op);
    }

    // recursive also deletes the block's children; the server's default is
    // not asserted here, so it is only sent when asked for.
    public static AnytypeV2Op DeleteBlock(AnytypeV2BlockRef target, bool? recursive = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var op = new JsonObject { ["op"] = "delete_block" };
        target.WriteTo(op);
        if (recursive is { } isRecursive) op["recursive"] = isRecursive;
        return ForObject("delete_block", op);
    }

    // Replaces text inside one block, marks preserved. Without id, find must
    // appear in exactly one block of the document.
    public static AnytypeV2Op ReplaceText(
        string find,
        string replace,
        string? blockId = null,
        bool replaceAll = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(find);
        ArgumentNullException.ThrowIfNull(replace);

        var op = new JsonObject { ["op"] = "replace_text", ["find"] = find, ["replace"] = replace };
        if (blockId is not null) op["id"] = blockId;
        if (replaceAll) op["replace_all"] = true;
        return ForObject("replace_text", op);
    }

    // value: a string is the paragraph shorthand, null clears the cell, a
    // block object or an array of blocks fills it.
    public static AnytypeV2Op SetCell(string tableId, string row, string column, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        return ForObject("set_cell", new JsonObject
        {
            ["op"] = "set_cell",
            ["table_id"] = tableId,
            ["row"] = row,
            ["col"] = column,
            ["value"] = value?.DeepClone(),
        });
    }

    private static JsonArray BlocksArray(IEnumerable<JsonObject> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var array = new JsonArray();
        foreach (JsonObject block in blocks)
            array.Add(block.DeepClone());

        if (array.Count is < 1 or > 256)
            throw new ArgumentException("A block run holds 1 to 256 blocks.", nameof(blocks));
        return array;
    }

    private static AnytypeV2Op ForObject(string kind, JsonObject json) =>
        new(kind, AnytypeV2OpScope.Object, json);
}
