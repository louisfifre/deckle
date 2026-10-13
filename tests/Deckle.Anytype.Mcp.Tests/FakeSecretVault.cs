using Deckle.Security;

namespace Deckle.Anytype.Mcp.Tests;

// Stands in for the DPAPI-sealed store so a test mints and reads bearers without
// touching disk or the developer's machine. Not thread-safe: each test builds its own.
sealed class FakeSecretVault : ISecretVault
{
    private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

    public bool TryGet(string name, out string? value)
    {
        bool found = _store.TryGetValue(name, out string? stored);
        value = stored;
        return found;
    }

    public bool Contains(string name) => _store.ContainsKey(name);

    public void Set(string name, string value) => _store[name] = value;

    public bool Remove(string name) => _store.Remove(name);
}
