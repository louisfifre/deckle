using System.Security.Cryptography;
using System.Text;

namespace Deckle.Anytype;

// What the gateway tells about the Anytype API key it holds, and what the
// supervisor compares it to: a short digest, never the key. The gateway reads
// the key once at start (ADR-0008), so a key minted afterwards reaches it only
// through a restart; the supervisor spots the difference by this value.
//
// Eight bytes of SHA-256 (16 hex characters) are enough to tell two keys
// apart, and too short to be worth attacking: the key is a long random token,
// so the digest does not narrow a guess in any useful way. The value is still
// treated as sensitive: no log line carries it.
public static class GatewayKeyFingerprint
{
    private const int Bytes = 8;

    // What a gateway holds from its start: the key, and with it the space, the
    // endpoint and the API version, all read once from the same credentials. A
    // gateway kept its first space after credentials.json named another one
    // (2026-11-11), so the digest covers every one of them: whichever changes,
    // the supervisor sees a gateway that is out of date.
    public static string Of(AnytypeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrEmpty(credentials.ApiKey);
        return Of(string.Join('\n',
            credentials.ApiKey, credentials.SpaceId, credentials.ApiUrl, credentials.ApiVersion));
    }

    public static string Of(string apiKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexStringLower(hash.AsSpan(0, Bytes));
    }
}