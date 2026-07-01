using Deckle.Security;

namespace Deckle.Anytype.Mcp.Tests;

// A Dictionary-backed ISecretVault for the token and HTTP-host tests: it stands
// in for the real DPAPI-sealed store so a unit test mints and reads bearers
// without touching disk or the developer's machine. Shared by McpClientTokensTests
// and McpHttpHostTests so both drive the same in-memory truth. Not thread-safe —
// each test builds its own, so no concurrent writer exists.
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
