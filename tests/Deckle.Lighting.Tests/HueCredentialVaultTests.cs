using Deckle.Lighting;
using Deckle.Security;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueCredentialVaultTests
{
    [Fact]
    public void StoreClientKeyKeepsKeysScopedByBridgeAndUsername()
    {
        var vault = new FakeSecretVault();
        var hueVault = new HueCredentialVault(vault);

        hueVault.StoreClientKey("bridge-a", "user-a", "key-a");
        hueVault.StoreClientKey("bridge-a", "user-b", "key-b");

        Assert.Equal("key-a", hueVault.TryGetClientKey("bridge-a", "user-a"));
        Assert.Equal("key-b", hueVault.TryGetClientKey("bridge-a", "user-b"));
        Assert.Null(hueVault.TryGetClientKey("bridge-b", "user-a"));
    }

    [Fact]
    public void RemoveClientKeyDeletesOnlyTheMatchingCredential()
    {
        var vault = new FakeSecretVault();
        var hueVault = new HueCredentialVault(vault);

        hueVault.StoreClientKey("bridge-a", "user-a", "key-a");
        hueVault.StoreClientKey("bridge-a", "user-b", "key-b");

        Assert.True(hueVault.RemoveClientKey("bridge-a", "user-a"));

        Assert.Null(hueVault.TryGetClientKey("bridge-a", "user-a"));
        Assert.Equal("key-b", hueVault.TryGetClientKey("bridge-a", "user-b"));
    }

    private sealed class FakeSecretVault : ISecretVault
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool TryGet(string name, out string? value)
            => _values.TryGetValue(name, out value);

        public bool Contains(string name)
            => _values.ContainsKey(name);

        public void Set(string name, string value)
            => _values[name] = value;

        public bool Remove(string name)
            => _values.Remove(name);
    }
}
