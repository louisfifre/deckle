using Deckle.Security;

namespace Deckle.Lighting;

// Stores Hue secrets owned by Deckle in the shared DPAPI-backed vault.
// AmbientSettings keeps only the non-secret coordinates (bridge IP/id,
// CLIP username, selected target); the Entertainment client key is a
// DTLS PSK and belongs in the secret store.
internal sealed class HueCredentialVault
{
    private const string ClientKeyPrefix = "hue.clientkey";

    private readonly ISecretVault _vault;

    public HueCredentialVault(ISecretVault vault)
    {
        _vault = vault;
    }

    public static HueCredentialVault CreateDefault()
        => new(SecretVault.CreateDefault());

    public void StoreClientKey(string bridgeId, string username, string clientKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        _vault.Set(ClientKeyName(bridgeId, username), clientKey);
    }

    public string? TryGetClientKey(string bridgeId, string username)
    {
        return _vault.TryGet(ClientKeyName(bridgeId, username), out var value)
            ? value
            : null;
    }

    public bool RemoveClientKey(string bridgeId, string username)
        => _vault.Remove(ClientKeyName(bridgeId, username));

    private static string ClientKeyName(string bridgeId, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return $"{ClientKeyPrefix}.{bridgeId}.{username}";
    }
}
