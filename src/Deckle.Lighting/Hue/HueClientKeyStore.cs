namespace Deckle.Lighting;

// Narrow public seam for consumers that own Hue pairing state but must
// not know how Deckle stores the Entertainment DTLS PSK. Ambient keeps
// bridge ip/id/username in its settings; Lighting owns the secret store.
public static class HueClientKeyStore
{
    public static void StoreClientKey(string bridgeId, string username, string clientKey)
        => HueCredentialVault.CreateDefault().StoreClientKey(bridgeId, username, clientKey);

    public static string? TryGetClientKey(string bridgeId, string username)
        => HueCredentialVault.CreateDefault().TryGetClientKey(bridgeId, username);

    public static bool RemoveClientKey(string bridgeId, string username)
        => HueCredentialVault.CreateDefault().RemoveClientKey(bridgeId, username);
}
