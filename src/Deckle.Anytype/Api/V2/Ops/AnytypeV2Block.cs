using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// A block to create, as the flat AnyBlock payload the insert and replace ops
// take. The payload is open — 35 block types and a long tail of optional fields
// (rows, columns, icon, language…) — so this only names the few fields nearly
// every block uses and returns a JsonObject the caller can add the rest to. The
// server validates the type and the fields; mirroring its enum here would only
// go stale. No id slot: the server mints every id and reports it in
// created_blocks.
public static class AnytypeV2Block
{
    public static JsonObject Create(
        string type,
        string? text = null,
        bool? @checked = null,
        int relativeIndent = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfNegative(relativeIndent);

        var block = new JsonObject { ["type"] = type };
        if (text is not null) block["text"] = text;
        if (@checked is { } isChecked) block["checked"] = isChecked;
        // 0 is the level where the run lands, so it is the default and stays off the wire.
        if (relativeIndent > 0) block["relative_indent"] = relativeIndent;
        return block;
    }
}
