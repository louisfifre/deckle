using System.Text.Json.Serialization;

namespace Deckle.Lighting;

// Passive description of a Philips Hue bridge on the local network.
// Identification data only — no credentials. The pairing exchange
// returns a separate HueCredentials record so the IP can be logged
// freely while the username / clientkey stay quarantined.
//
// JSON shape matches the response from `discovery.meethue.com` :
//   [{"id":"001788FFFE3A2C18","internalipaddress":"192.168.1.5","port":443}]
// The key is all-lowercase `internalipaddress`, not snake_case.
public sealed record HueBridge(
    [property: JsonPropertyName("id")]                string Id,
    [property: JsonPropertyName("internalipaddress")] string InternalIpAddress,
    [property: JsonPropertyName("port")]              int Port);

// `Username` is the CLIP application key, sent in the URL path on CLIP v1
// and in the `hue-application-key` header on v2. Sensitive but revocable
// from the Hue app.
//
// `ClientKey` is the PSK for the Entertainment v2 DTLS handshake; the
// REST path never uses it. A true secret: never logged, stored only in
// the DPAPI vault (HueCredentialVault), never in settings.json.
public sealed record HueCredentials(string Username, string ClientKey)
{
    /// <summary>First 8 characters of the username followed by `...`,
    /// for the diagnostic UI only. It is never written to a log: even a
    /// prefix of the application key is part of a credential.</summary>
    public string UsernameHead => Username.Length <= 8
        ? Username
        : string.Concat(Username.AsSpan(0, 8), "...");
}
