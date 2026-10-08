namespace Deckle.Orchestration;

// One key of a Valve KeyValues document. A key carries either a string Value
// or a block of Children, never both: a leaf has Children empty, a block has
// Value null.
public sealed class KeyValuesNode
{
    public KeyValuesNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    public string? Value { get; }

    public List<KeyValuesNode> Children { get; } = new();

    // The first child with this key. Valve's own reader matches keys without
    // case ("AppState" and "appstate" are the same key), so this does too.
    public KeyValuesNode? this[string key] =>
        Children.Find(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    // The value of the first child with this key; null when it is absent or a block.
    public string? GetValue(string key) => this[key]?.Value;
}
