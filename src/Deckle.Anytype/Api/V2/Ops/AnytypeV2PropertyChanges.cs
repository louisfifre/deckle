using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// What one set_properties op does to an object's property values. The four
// verbs mirror the schema:
//
//   • Set    — property key → value; a list-shaped key (select, multi_select,
//              objects, files) takes an array of option NAMES or object ids,
//              a date takes "2026-08-01" or an ISO timestamp, not unix seconds.
//   • Add    — append entries to a list-shaped key without rewriting it.
//   • Remove — delete entries from a list-shaped key.
//   • Unset  — drop the key.
//
// Select options are addressed by name, case-sensitive; an unknown name in Set
// or Add creates the option. A key may appear in only one verb — the server
// refuses the op otherwise, and this builder refuses it first so the mistake
// shows up where it was made.
public sealed class AnytypeV2PropertyChanges
{
    private readonly JsonObject _set = new();
    private readonly JsonObject _add = new();
    private readonly JsonObject _remove = new();
    private readonly JsonArray _unset = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public AnytypeV2PropertyChanges Set(string key, JsonNode? value)
    {
        Claim(key);
        _set[key] = value?.DeepClone();
        return this;
    }

    public AnytypeV2PropertyChanges Set(string key, string value) => Set(key, JsonValue.Create(value));

    public AnytypeV2PropertyChanges Set(string key, bool value) => Set(key, JsonValue.Create(value));

    public AnytypeV2PropertyChanges Set(string key, double value) => Set(key, JsonValue.Create(value));

    // The list form of Set: one or more option names or object ids.
    public AnytypeV2PropertyChanges SetList(string key, IEnumerable<string> entries)
    {
        Claim(key);
        _set[key] = StringArray(entries, allowEmpty: true);
        return this;
    }

    public AnytypeV2PropertyChanges Add(string key, IEnumerable<string> entries)
    {
        Claim(key);
        _add[key] = StringArray(entries, allowEmpty: false);
        return this;
    }

    public AnytypeV2PropertyChanges Remove(string key, IEnumerable<string> entries)
    {
        Claim(key);
        _remove[key] = StringArray(entries, allowEmpty: false);
        return this;
    }

    public AnytypeV2PropertyChanges Unset(string key)
    {
        Claim(key);
        _unset.Add(key);
        return this;
    }

    internal bool IsEmpty => _keys.Count == 0;

    internal JsonObject ToJson()
    {
        if (IsEmpty)
            throw new InvalidOperationException("A set_properties op with no change does nothing.");

        var op = new JsonObject { ["op"] = "set_properties" };
        if (_set.Count > 0) op["set"] = _set.DeepClone();
        if (_add.Count > 0) op["add"] = _add.DeepClone();
        if (_remove.Count > 0) op["remove"] = _remove.DeepClone();
        if (_unset.Count > 0) op["unset"] = _unset.DeepClone();
        return op;
    }

    private void Claim(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_keys.Add(key))
            throw new InvalidOperationException(
                $"Property '{key}' is already named in this op; a key may appear in only one of set, add, remove and unset.");
    }

    private static JsonArray StringArray(IEnumerable<string> entries, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var array = new JsonArray();
        foreach (string entry in entries)
            array.Add(entry);

        // An empty array in Set means present-but-empty, which is meaningful;
        // an empty Add or Remove is a no-op that would only spend the budget.
        if (!allowEmpty && array.Count == 0)
            throw new ArgumentException("Give at least one entry.", nameof(entries));
        return array;
    }
}
